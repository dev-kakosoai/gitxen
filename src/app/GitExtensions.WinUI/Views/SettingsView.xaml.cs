using GitExtensions.WinUI.Services;
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

        ThemeBox.SelectedIndex = (int)AppOptions.Theme;
        CommitsBox.Value = AppOptions.MaxCommits;
        DiffLinesBox.Value = AppOptions.MaxDiffLines;

        _isLoading = false;
        return Task.CompletedTask;
    }

    private void Theme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || ThemeBox.SelectedIndex < 0)
        {
            return;
        }

        AppOptions.Theme = (AppTheme)ThemeBox.SelectedIndex;
        ApplyTheme();
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
    ///  Applied to the window's root element rather than the application: Application.RequestedTheme
    ///  can only be set before the first window exists.
    /// </summary>
    private void ApplyTheme()
    {
        if (XamlRoot?.Content is FrameworkElement root)
        {
            root.RequestedTheme = AppOptions.Theme switch
            {
                AppTheme.Light => ElementTheme.Light,
                AppTheme.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
        }

        // Diff colours are chosen per theme and cached, so they have to be recomputed.
        DiffLineViewModel.InvalidatePalette();
    }
}
