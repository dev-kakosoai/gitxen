using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace GitExtensions.WinUI.Terminal;

/// <summary>
///  One shell process attached to a Windows pseudoconsole (ConPTY).
/// </summary>
/// <remarks>
///  <para>
///   ConPTY rather than plain stdio redirection because a shell behind redirected pipes knows it is
///   not interactive: PowerShell stops printing its prompt, bash drops its aliases, and anything that
///   asks "is this a terminal?" answers no. Behind a pseudoconsole the shell behaves exactly as it
///   does in Windows Terminal, and the cost is that the output arrives as a VT stream —
///   <see cref="TerminalOutputBuffer"/> is the piece that turns that back into lines.
///  </para>
///  <para>
///   Raw P/Invoke rather than a terminal-control package: the WindowsAppSDK assembly resolution in
///   this project is fragile enough already (see the csproj comments), and the ConPTY surface is five
///   functions.
///  </para>
///  <para>
///   <see cref="OutputReceived"/> and <see cref="Exited"/> fire on the reader thread; the caller
///   marshals to the UI.
///  </para>
/// </remarks>
public sealed class ConPtySession : IDisposable
{
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint Infinite = 0xFFFFFFFF;
    private static readonly IntPtr _pseudoConsoleAttribute = new(0x00020016);

    private readonly IntPtr _console;
    private readonly IntPtr _processHandle;
    private readonly IntPtr _threadHandle;
    private readonly FileStream _input;
    private readonly SafeFileHandle _outputRead;
    private readonly Lock _writeLock = new();
    private bool _disposed;

    /// <summary>Raw VT output as it arrives. Reader thread.</summary>
    public event Action<string>? OutputReceived;

    /// <summary>The shell exited, with its exit code. Reader thread.</summary>
    public event Action<int>? Exited;

    /// <exception cref="Win32Exception">The pseudoconsole or the process could not be created.</exception>
    public ConPtySession(string executablePath, string arguments, string workingDirectory, short columns, short rows)
    {
        if (!CreatePipe(out SafeFileHandle inputRead, out SafeFileHandle inputWrite, IntPtr.Zero, 0)
            || !CreatePipe(out SafeFileHandle outputRead, out SafeFileHandle outputWrite, IntPtr.Zero, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the console pipes.");
        }

        int result = CreatePseudoConsole(
            new COORD { X = columns, Y = rows }, inputRead, outputWrite, 0, out _console);
        if (result != 0)
        {
            throw new Win32Exception(result, "Could not create the pseudoconsole.");
        }

        // The pseudoconsole holds its own duplicates of these ends; closing ours is what lets the
        // reader see EOF when the console goes away.
        inputRead.Dispose();
        outputWrite.Dispose();

        _outputRead = outputRead;
        _input = new FileStream(inputWrite, FileAccess.Write);

        IntPtr attributeListSize = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attributeListSize);
        IntPtr attributeList = Marshal.AllocHGlobal(attributeListSize);

        try
        {
            if (!InitializeProcThreadAttributeList(attributeList, 1, 0, ref attributeListSize)
                || !UpdateProcThreadAttribute(
                    attributeList, 0, _pseudoConsoleAttribute, _console, IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not attach the pseudoconsole.");
            }

            STARTUPINFOEX startup = new()
            {
                StartupInfo = new STARTUPINFO { SizeInBytes = Marshal.SizeOf<STARTUPINFOEX>() },
                AttributeList = attributeList
            };

            // Quoted because shell paths routinely contain spaces; StringBuilder because
            // CreateProcessW writes into the command-line buffer.
            StringBuilder commandLine = new($"\"{executablePath}\"");
            if (arguments.Length > 0)
            {
                commandLine.Append(' ').Append(arguments);
            }

            if (!CreateProcess(
                null, commandLine, IntPtr.Zero, IntPtr.Zero, inheritHandles: false,
                ExtendedStartupInfoPresent, IntPtr.Zero, workingDirectory,
                ref startup, out PROCESS_INFORMATION process))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not start {executablePath}.");
            }

            _processHandle = process.Process;
            _threadHandle = process.Thread;
        }
        catch
        {
            ClosePseudoConsole(_console);
            _input.Dispose();
            _outputRead.Dispose();
            throw;
        }
        finally
        {
            DeleteProcThreadAttributeList(attributeList);
            Marshal.FreeHGlobal(attributeList);
        }

        Thread reader = new(ReadOutput) { IsBackground = true, Name = "ConPTY reader" };
        reader.Start();

        // The output pipe never reports EOF while the pseudoconsole is open — conhost keeps the
        // write end — so the shell exiting is observed on its process handle, not on the pipe.
        Thread exitWatcher = new(WatchForExit) { IsBackground = true, Name = "ConPTY exit watcher" };
        exitWatcher.Start();
    }

    /// <summary>Sends keystrokes to the shell. UTF-8, as ConPTY expects.</summary>
    public void Write(string text)
    {
        lock (_writeLock)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                _input.Write(bytes, 0, bytes.Length);
                _input.Flush();
            }
            catch (IOException)
            {
                // The shell has gone; Exited is what reports that.
            }
        }
    }

    /// <summary>
    ///  Tells the pseudoconsole its new size, so the shell reflows and wraps output at the pane's
    ///  actual width rather than at whatever the session started with.
    /// </summary>
    public void Resize(short columns, short rows)
    {
        if (!_disposed && columns > 0 && rows > 0)
        {
            ResizePseudoConsole(_console, new COORD { X = columns, Y = rows });
        }
    }

    /// <summary>
    ///  Ends the session: the shell is terminated, the console torn down, the handles closed.
    /// </summary>
    /// <remarks>
    ///  TerminateProcess before ClosePseudoConsole: closing the console does end well-behaved
    ///  clients, but a shell mid-command can outlive it, and an orphaned shell after the app closes
    ///  is exactly the leak this method exists to prevent.
    /// </remarks>
    public void Dispose()
    {
        lock (_writeLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        TerminateProcess(_processHandle, 1);
        ClosePseudoConsole(_console);
        _input.Dispose();
        _outputRead.Dispose();
        CloseHandle(_processHandle);
        CloseHandle(_threadHandle);
    }

    private void WatchForExit()
    {
        WaitForSingleObject(_processHandle, Infinite);

        int exitCode;
        lock (_writeLock)
        {
            // Disposed means the wait ended because the handle went away, not because the shell
            // chose to exit; nobody is listening any more.
            if (_disposed)
            {
                return;
            }

            GetExitCodeProcess(_processHandle, out uint code);
            exitCode = unchecked((int)code);
        }

        Exited?.Invoke(exitCode);
    }

    private void ReadOutput()
    {
        // A stateful decoder, because a UTF-8 sequence can be split across two pipe reads.
        Decoder decoder = Encoding.UTF8.GetDecoder();
        byte[] bytes = new byte[4096];
        char[] chars = new char[8192];

        using (FileStream output = new(_outputRead, FileAccess.Read))
        {
            while (true)
            {
                int read;
                try
                {
                    read = output.Read(bytes, 0, bytes.Length);
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException)
                {
                    break;
                }

                if (read == 0)
                {
                    break;
                }

                int decoded = decoder.GetChars(bytes, 0, read, chars, 0);
                if (decoded > 0)
                {
                    OutputReceived?.Invoke(new string(chars, 0, decoded));
                }
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct COORD
    {
        public short X;
        public short Y;
    }

    // The struct layouts and parameter order are what Windows reads; the names are for this file,
    // spelled without the Win32 headers' Hungarian prefixes to satisfy StyleCop.
    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFO
    {
        public int SizeInBytes;
        public IntPtr Reserved1;
        public IntPtr Desktop;
        public IntPtr Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2Length;
        public IntPtr Reserved2;
        public IntPtr StandardInput;
        public IntPtr StandardOutput;
        public IntPtr StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public IntPtr AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr Process;
        public IntPtr Thread;
        public int ProcessId;
        public int ThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(
        out SafeFileHandle readPipe, out SafeFileHandle writePipe, IntPtr pipeAttributes, int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int CreatePseudoConsole(
        COORD size, SafeFileHandle input, SafeFileHandle output, uint flags, out IntPtr console);

    [DllImport("kernel32.dll")]
    private static extern int ResizePseudoConsole(IntPtr console, COORD size);

    [DllImport("kernel32.dll")]
    private static extern void ClosePseudoConsole(IntPtr console);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(
        IntPtr attributeList, int attributeCount, int flags, ref IntPtr size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(
        IntPtr attributeList, uint flags, IntPtr attribute, IntPtr value, nint size,
        IntPtr previousValue, IntPtr returnSize);

    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr attributeList);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcess(
        string? applicationName, StringBuilder commandLine, IntPtr processAttributes,
        IntPtr threadAttributes, bool inheritHandles, uint creationFlags, IntPtr environment,
        string? currentDirectory, ref STARTUPINFOEX startupInfo,
        out PROCESS_INFORMATION processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
}
