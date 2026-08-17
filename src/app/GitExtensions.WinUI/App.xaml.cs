using System.ComponentModel.Design;
using GitCommands;
using GitUI;
using Microsoft.UI.Xaml;
using Microsoft.VisualStudio.Threading;

namespace GitExtensions.WinUI;

public partial class App : Application
{
    private readonly ServiceContainer _serviceContainer = new();

    public App()
    {
        InitializeComponent();
    }

    /// <summary>
    ///  The shell window.
    /// </summary>
    /// <remarks>
    ///  Static because the file and folder pickers need an owning window handle, and a desktop WinUI
    ///  app has no ambient one to fall back on — the pages that show a picker are several levels below
    ///  the window and have no other route to it. This app only ever creates one window.
    /// </remarks>
    public static Window? Shell { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // WinUI3's Application.Start bootstrap has already installed a DispatcherQueue-backed
        // SynchronizationContext on this thread by the time OnLaunched runs, so this capture
        // works the same way Program.cs's throwaway-Form trick did for the WinForms app.
        ThreadHelper.JoinableTaskContext = new JoinableTaskContext();

        Bootstrap.ServiceContainerRegistry.RegisterServices(_serviceContainer);

        AppSettings.LoadSettings();

        MainWindow window = new(_serviceContainer);
        Shell = window;
        window.Activate();

        _ = window.RestoreSessionAsync();
    }
}
