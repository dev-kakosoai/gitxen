using GitExtensions.WinUI.Services;
using GitExtensions.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GitExtensions.WinUI.Dialogs;

/// <summary>
///  Stage/unstage individual files and commit what is staged — the partial-commit path the
///  toolbar's all-or-nothing commit doesn't offer.
/// </summary>
public sealed partial class CommitDialog : ContentDialog
{
    private readonly RepositoryTabViewModel _tab;

    public CommitDialog(RepositoryTabViewModel tab, XamlRoot xamlRoot)
    {
        _tab = tab;
        ViewModel = new CommitDialogViewModel();
        InitializeComponent();
        XamlRoot = xamlRoot;
        Title = $"Commit to {tab.Branch}";
    }

    public CommitDialogViewModel ViewModel { get; }

    /// <summary>Shows the dialog and commits if confirmed. Returns null if cancelled.</summary>
    public async Task<GitOperationResult?> ShowAndCommitAsync()
    {
        await RefreshAsync();

        while (await ShowAsync() == ContentDialogResult.Primary)
        {
            string message = MessageBox.Text;
            bool amend = AmendCheck.IsChecked == true;

            if (string.IsNullOrWhiteSpace(message))
            {
                ViewModel.Error = "A commit message is required.";
                continue;
            }

            // Amending intentionally allows an empty staging area — it can just reword HEAD.
            if (ViewModel.Staged.Count == 0 && !amend)
            {
                ViewModel.Error = "Nothing is staged.";
                continue;
            }

            return await _tab.CommitStagedAsync(message, amend, SignOffCheck.IsChecked == true);
        }

        return null;
    }

    private async void Stage_Click(object sender, RoutedEventArgs e) =>
        await MoveAsync(UnstagedList.SelectedItems.OfType<ChangedFileViewModel>().ToList(), staged: true);

    private async void Unstage_Click(object sender, RoutedEventArgs e) =>
        await MoveAsync(StagedList.SelectedItems.OfType<ChangedFileViewModel>().ToList(), staged: false);

    private async void Amend_Changed(object sender, RoutedEventArgs e)
    {
        // Pre-fill with HEAD's message the first time amend is ticked, so rewording is one edit away.
        if (AmendCheck.IsChecked == true && string.IsNullOrWhiteSpace(MessageBox.Text))
        {
            MessageBox.Text = await _tab.GetLastCommitMessageAsync();
        }
    }

    private async Task MoveAsync(IReadOnlyList<ChangedFileViewModel> files, bool staged)
    {
        foreach (ChangedFileViewModel file in files)
        {
            GitOperationResult result = await _tab.SetStagedAsync(file.Name, staged);

            if (!result.Succeeded)
            {
                ViewModel.Error = result.Output;
                break;
            }
        }

        await RefreshAsync();
    }

    private async Task RefreshAsync() => ViewModel.Load(await _tab.GetWorkingDirectoryFilesAsync());
}
