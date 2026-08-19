using System.Diagnostics;
using GitExtensions.WinUI.Services;
using GitExtensions.WinUI.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GitExtensions.WinUI.Views;

/// <summary>
///  Application settings, as a page.
/// </summary>
/// <remarks>
///  <para>
///   Previously a modal dialog built by hand in code. As a page each setting can carry a sentence
///   explaining what it costs, which matters for the two limits — they exist for a reason and the
///   number alone does not say what it is.
///  </para>
///  <para>
///   Derives from <see cref="RepositoryPage"/> for uniformity with the other sections even though it
///   ignores <see cref="RepositoryPage.Tab"/>: these settings are process-wide, not per repository.
///  </para>
/// </remarks>
public sealed partial class SettingsView : RepositoryPage
{
    /// <summary>
    ///  Suppresses the change handlers while the controls are being populated from current state, so
    ///  loading the page does not look like the user editing it.
    /// </summary>
    private bool _isLoading;

    public SettingsView()
    {
        InitializeComponent();
    }

    public override Task ActivateAsync()
    {
        _isLoading = true;

        IReadOnlyList<ThemeOption> themes = ThemeOption.Build(ThemeService.IsSystemDark);
        ThemeGallery.ItemsSource = themes;
        ThemeGallery.SelectedItem = themes.FirstOrDefault(option => option.Theme == AppOptions.Theme)
            ?? themes[0];
        CommitsBox.Value = AppOptions.MaxCommits;
        DiffLinesBox.Value = AppOptions.MaxDiffLines;
        DensityBox.SelectedIndex = AppOptions.Density == UiDensity.Compact ? 1 : 0;

        _isLoading = false;
        return Task.CompletedTask;
    }

    private void Density_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        AppOptions.Density = DensityBox.SelectedIndex == 1 ? UiDensity.Compact : UiDensity.Comfortable;

        // Replaces the shared row style for pages parsed from here on; the commit graph picks the
        // new row height up as each list refreshes.
        DensityService.Apply();
    }

    private void Theme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Also raised while the gallery is being populated, and when a selection is cleared.
        if (_isLoading || ThemeGallery.SelectedItem is not ThemeOption option)
        {
            return;
        }

        AppOptions.Theme = option.Theme;
        ThemeService.Apply();
    }

    private void Commits_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        // NaN arrives when the box is cleared mid-edit; leave the stored value alone until it is valid.
        if (!_isLoading && !double.IsNaN(args.NewValue))
        {
            AppOptions.MaxCommits = (int)args.NewValue;
        }
    }

    private void DiffLines_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_isLoading && !double.IsNaN(args.NewValue))
        {
            AppOptions.MaxDiffLines = (int)args.NewValue;
        }
    }

    /// <summary>
    ///  Resets the application to a first run: confirm, wipe the stored state, relaunch, close.
    /// </summary>
    /// <remarks>
    ///  The new process is started before the window closes. It launches into an already-emptied
    ///  state directory, and <see cref="SessionStore.Reset"/> has disarmed <c>Save</c>, so the closing
    ///  window's final save cannot write the old session back in between.
    /// </remarks>
    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        ContentDialog dialog = new()
        {
            Title = "Reset Gitxen?",
            Content = new TextBlock
            {
                Text = "This closes every tab and clears projects, recent repositories, layout and "
                    + "all settings. Gitxen restarts as a first run.\n\n"
                    + "Your repositories on disk and your git configuration are not touched.",
                TextWrapping = TextWrapping.Wrap
            },
            PrimaryButtonText = "Reset and restart",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        SessionStore.Reset();

        if (Environment.ProcessPath is string executable)
        {
            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
        }

        // Through the window rather than Application.Exit, so the Closed handler still runs — it is
        // what shuts the terminal shells down.
        App.Shell?.Close();
    }
}
