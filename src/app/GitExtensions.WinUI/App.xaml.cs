using System.ComponentModel.Design;
using GitCommands;
using GitUI;
using Microsoft.UI.Xaml;
using Microsoft.VisualStudio.Threading;

namespace GitExtensions.WinUI;

public partial class App : Application
{
    private readonly ServiceContainer _serviceContainer = new();
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // WinUI3's Application.Start bootstrap has already installed a DispatcherQueue-backed
        // SynchronizationContext on this thread by the time OnLaunched runs, so this capture
        // works the same way Program.cs's throwaway-Form trick did for the WinForms app.
        ThreadHelper.JoinableTaskContext = new JoinableTaskContext();

        Bootstrap.ServiceContainerRegistry.RegisterServices(_serviceContainer);

        AppSettings.LoadSettings();

        MainWindow window = new(_serviceContainer);
        _window = window;
        window.Activate();

        _ = window.RestoreSessionAsync();
    }
}
