using GitExtensions.WinUI.Models;
using GitExtensions.WinUI.Services;
using GitExtensions.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GitExtensions.WinUI.Views;

/// <summary>
///  The settings git itself keeps: who you commit as, and how it treats line endings and pulls.
/// </summary>
/// <remarks>
///  Both scopes are shown side by side because the interesting question is usually which one is in
///  force. A repository value overrides the global one, and an empty repository field means "fall
///  through to global" rather than "empty name" — which is why saving an empty box unsets the key
///  instead of storing a blank.
/// </remarks>
public sealed partial class GitConfigView : RepositoryPage
{
    private string _status = "";

    public GitConfigView()
    {
        InitializeComponent();
    }

    /// <summary>Result of the last save, shown beside the button.</summary>
    public string Status
    {
        get => _status;
        private set
        {
            _status = value;
            Bindings.Update();
        }
    }

    public override async Task ActivateAsync()
    {
        if (Tab is not RepositoryTabViewModel tab)
        {
            return;
        }

        await tab.LoadLocalConfigAsync();
        await tab.LoadGlobalConfigAsync();

        LocalName.Text = await tab.GetConfigValueAsync(GitConfigScope.Local, "user.name");
        LocalEmail.Text = await tab.GetConfigValueAsync(GitConfigScope.Local, "user.email");
        GlobalName.Text = await tab.GetConfigValueAsync(GitConfigScope.Global, "user.name");
        GlobalEmail.Text = await tab.GetConfigValueAsync(GitConfigScope.Global, "user.email");

        SelectOption(AutoCrlfBox, await tab.GetConfigValueAsync(GitConfigScope.Global, "core.autocrlf"));
        SelectPullStrategy(await tab.GetConfigValueAsync(GitConfigScope.Global, "pull.rebase"));
        DefaultBranchBox.Text = await tab.GetConfigValueAsync(GitConfigScope.Global, "init.defaultBranch");

        Status = "";
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ActivateAsync();

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (Tab is not RepositoryTabViewModel tab)
        {
            return;
        }

        List<GitOperationResult> results =
        [
            await tab.SetConfigValueAsync(GitConfigScope.Local, "user.name", LocalName.Text.Trim()),
            await tab.SetConfigValueAsync(GitConfigScope.Local, "user.email", LocalEmail.Text.Trim()),
            await tab.SetConfigValueAsync(GitConfigScope.Global, "user.name", GlobalName.Text.Trim()),
            await tab.SetConfigValueAsync(GitConfigScope.Global, "user.email", GlobalEmail.Text.Trim()),
            await tab.SetConfigValueAsync(GitConfigScope.Global, "core.autocrlf", SelectedOption(AutoCrlfBox)),
            await tab.SetConfigValueAsync(GitConfigScope.Global, "pull.rebase", SelectedPullStrategy()),
            await tab.SetConfigValueAsync(GitConfigScope.Global, "init.defaultBranch", DefaultBranchBox.Text.Trim())
        ];

        GitOperationResult? failure = results.FirstOrDefault(result => !result.Succeeded);

        if (failure is not null)
        {
            tab.Report(failure);
            Status = "";
            return;
        }

        Status = "Saved.";
        await tab.LoadLocalConfigAsync();
        await tab.LoadGlobalConfigAsync();
    }

    /// <summary>"(not set)" is the first item, and maps to an empty value, which unsets the key.</summary>
    private static string SelectedOption(ComboBox box) =>
        box.SelectedIndex <= 0 ? "" : (box.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";

    private static void SelectOption(ComboBox box, string value)
    {
        for (int i = 1; i < box.Items.Count; i++)
        {
            if ((box.Items[i] as ComboBoxItem)?.Content?.ToString() == value)
            {
                box.SelectedIndex = i;
                return;
            }
        }

        box.SelectedIndex = 0;
    }

    /// <summary>
    ///  pull.rebase is a boolean in git but a choice of two words here, since "false" reads as
    ///  nothing at all when the question is "merge or rebase".
    /// </summary>
    private string SelectedPullStrategy() => PullRebaseBox.SelectedIndex switch
    {
        1 => "false",
        2 => "true",
        _ => ""
    };

    private void SelectPullStrategy(string value) => PullRebaseBox.SelectedIndex = value switch
    {
        "true" => 2,
        "false" => 1,
        _ => 0
    };
}
