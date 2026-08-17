using GitExtensions.WinUI.Models;
using GitExtensions.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GitExtensions.WinUI.Views;

/// <summary>
///  The stash stack, as a list you can act on.
/// </summary>
/// <remarks>
///  The previous UI could only stash, pop the top entry, or dump <c>git stash list</c> into a dialog —
///  so applying anything other than the most recent stash was not reachable at all. Every entry here
///  can be applied, popped or dropped by name.
/// </remarks>
public sealed partial class StashesView : RepositoryPage
{
    public StashesView()
    {
        InitializeComponent();
    }

    public override async Task ActivateAsync()
    {
        if (Tab is RepositoryTabViewModel tab)
        {
            await tab.LoadStashesAsync();
        }
    }

    private static StashInfo? StashFrom(object sender) =>
        (sender as FrameworkElement)?.DataContext as StashInfo;

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ActivateAsync();

    private async void StashChanges_Click(object sender, RoutedEventArgs e)
    {
        TextBox message = new() { Header = "Description (optional)", PlaceholderText = "git will generate one" };

        // Untracked files are excluded by default, which surprises people: a stash then appears to
        // have "lost" the new files it never took. Offering it explicitly is the fix.
        CheckBox untracked = new() { Content = "Include untracked files" };
        CheckBox keepIndex = new() { Content = "Keep staged changes in the working directory" };

        StackPanel panel = new() { Spacing = 12, Width = 400 };
        panel.Children.Add(message);
        panel.Children.Add(untracked);
        panel.Children.Add(keepIndex);

        ContentDialog dialog = new()
        {
            Title = "Stash changes",
            Content = panel,
            PrimaryButtonText = "Stash",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ReportAsync(tab => tab.StashSaveAsync(
                message.Text.Trim(), untracked.IsChecked == true, keepIndex.IsChecked == true));

            await ActivateAsync();
        }
    }

    /// <summary>Shows what a stash would apply, so it can be read before it is applied.</summary>
    private async void ShowDiff_Click(object sender, RoutedEventArgs e)
    {
        if (StashFrom(sender) is not StashInfo stash || Tab is not RepositoryTabViewModel tab)
        {
            return;
        }

        string diff = await tab.GetStashDiffAsync(stash.Reference);

        await ShowTextAsync(
            $"{stash.Reference} — {stash.Subject}",
            string.IsNullOrWhiteSpace(diff) ? "This stash has no textual changes." : diff);
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (StashFrom(sender) is StashInfo stash)
        {
            await ReportAsync(tab => tab.StashApplyAsync(stash.Reference));
            await ActivateAsync();
        }
    }

    private async void Pop_Click(object sender, RoutedEventArgs e)
    {
        if (StashFrom(sender) is StashInfo stash)
        {
            await ReportAsync(tab => tab.StashPopAsync(stash.Reference));
            await ActivateAsync();
        }
    }

    private async void Drop_Click(object sender, RoutedEventArgs e)
    {
        if (StashFrom(sender) is not StashInfo stash)
        {
            return;
        }

        if (await ConfirmAsync(
            "Drop stash?",
            $"Discards {stash.Reference} — \"{stash.Subject}\" — without applying it. This cannot be undone.",
            "Drop"))
        {
            await ReportAsync(tab => tab.StashDropAsync(stash.Reference));
            await ActivateAsync();
        }
    }
}
