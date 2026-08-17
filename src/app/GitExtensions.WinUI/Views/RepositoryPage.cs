using GitExtensions.WinUI.Services;
using GitExtensions.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GitExtensions.WinUI.Views;

/// <summary>
///  Base for the navigation sections (History, Changes, Branches, …).
/// </summary>
/// <remarks>
///  <para>
///   Each section is its own control rather than another region of one enormous window: the sections
///   have nothing to say to each other, and keeping them apart is what stops the shell turning back
///   into a single screen with everything on it.
///  </para>
///  <para>
///   The repository comes in through <see cref="Tab"/>, set by the shell. It is a dependency property
///   so <c>x:Bind</c> in the derived XAML can follow it — a plain CLR property would bind once, before
///   the shell had assigned anything, and never update.
///  </para>
/// </remarks>
public abstract partial class RepositoryPage : UserControl
{
    protected RepositoryPage()
    {
        // UserControl hosts its content in a ContentPresenter aligned by HorizontalContentAlignment,
        // which does not default to Stretch. Left unset, the page is measured with an unbounded width:
        // the diff list then reports the width of its longest line as its desired size, and anything
        // right-aligned in the page ends up positioned off the screen.
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
    }

    public static readonly DependencyProperty TabProperty = DependencyProperty.Register(
        nameof(Tab),
        typeof(RepositoryTabViewModel),
        typeof(RepositoryPage),
        new PropertyMetadata(null, OnTabPropertyChanged));

    /// <summary>The repository this section is showing. Null until the shell assigns one.</summary>
    public RepositoryTabViewModel? Tab
    {
        get => (RepositoryTabViewModel?)GetValue(TabProperty);
        set => SetValue(TabProperty, value);
    }

    /// <summary>
    ///  Called when the section is navigated to. Pages that list something load it here, so a tab only
    ///  pays for the listings actually looked at.
    /// </summary>
    public virtual Task ActivateAsync() => Task.CompletedTask;

    /// <summary>Runs an operation and puts its result in the repository's InfoBar.</summary>
    protected async Task ReportAsync(Func<RepositoryTabViewModel, Task<GitOperationResult>> operation)
    {
        if (Tab is not RepositoryTabViewModel tab)
        {
            return;
        }

        tab.Report(await operation(tab));
    }

    /// <summary>Modal yes/no. Defaults to Cancel so an accidental Enter cannot confirm a destructive action.</summary>
    protected async Task<bool> ConfirmAsync(string title, string message, string acceptText)
    {
        ContentDialog dialog = new()
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = acceptText,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>
    ///  Single-line text prompt. Returns null when cancelled, so callers can distinguish that from an
    ///  empty answer.
    /// </summary>
    protected async Task<string?> PromptAsync(string title, string label, string acceptText, string initialValue = "")
    {
        TextBox input = new()
        {
            Header = label,
            Text = initialValue,
            MinWidth = 320
        };

        ContentDialog dialog = new()
        {
            Title = title,
            Content = input,
            PrimaryButtonText = acceptText,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary ? input.Text : null;
    }

    /// <summary>
    ///  Shows a block of git output. Some things really are just text — <c>git blame</c>, a conflict
    ///  list — and they get a scrollable monospace dialog rather than being squeezed into the InfoBar.
    /// </summary>
    protected async Task ShowTextAsync(string title, string text)
    {
        ContentDialog dialog = new()
        {
            Title = title,
            Content = new ScrollViewer
            {
                MaxHeight = 420,
                HorizontalScrollMode = ScrollMode.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new TextBlock
                {
                    Text = text,
                    FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                    FontSize = 12,
                    IsTextSelectionEnabled = true
                }
            },
            CloseButtonText = "Close",
            XamlRoot = XamlRoot
        };

        await dialog.ShowAsync();
    }

    /// <summary>Hook for derived pages that need to react to a different repository being assigned.</summary>
    protected virtual void OnTabChanged()
    {
    }

    private static void OnTabPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((RepositoryPage)sender).OnTabChanged();
}
