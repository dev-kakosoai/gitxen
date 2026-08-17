using GitExtensions.WinUI.Models;
using GitExtensions.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;

namespace GitExtensions.WinUI.Views;

/// <summary>Remotes, their URLs, and per-remote fetching.</summary>
public sealed partial class RemotesView : RepositoryPage
{
    public RemotesView()
    {
        InitializeComponent();
    }

    public override async Task ActivateAsync()
    {
        if (Tab is RepositoryTabViewModel tab)
        {
            await tab.LoadRemotesAsync();
        }
    }

    private static RemoteInfo? RemoteFrom(object sender) =>
        (sender as FrameworkElement)?.DataContext as RemoteInfo;

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ActivateAsync();

    private async void AddRemote_Click(object sender, RoutedEventArgs e)
    {
        if (await PromptAsync("Add remote", "Name", "Next", "origin") is not string name || name.Length == 0)
        {
            return;
        }

        if (await PromptAsync("Add remote", "URL", "Add") is string url && url.Length > 0)
        {
            await ReportAsync(tab => tab.AddRemoteAsync(name, url));
            await ActivateAsync();
        }
    }

    private async void Fetch_Click(object sender, RoutedEventArgs e)
    {
        if (RemoteFrom(sender) is RemoteInfo remote)
        {
            await ReportAsync(tab => tab.FetchRemoteAsync(remote.Name, prune: false));
        }
    }

    private async void FetchPrune_Click(object sender, RoutedEventArgs e)
    {
        if (RemoteFrom(sender) is not RemoteInfo remote)
        {
            return;
        }

        // Pruning deletes local remote-tracking refs, so it is worth saying so rather than just doing it.
        if (await ConfirmAsync(
            "Fetch and prune?",
            $"Fetches '{remote.Name}' and deletes remote-tracking branches that no longer exist on it.",
            "Fetch and prune"))
        {
            await ReportAsync(tab => tab.FetchRemoteAsync(remote.Name, prune: true));
        }
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (RemoteFrom(sender) is not RemoteInfo remote)
        {
            return;
        }

        if (await ConfirmAsync(
            "Remove remote?",
            $"Removes '{remote.Name}' and its remote-tracking branches from this repository. Nothing on the server changes.",
            "Remove"))
        {
            await ReportAsync(tab => tab.RemoveRemoteAsync(remote.Name));
            await ActivateAsync();
        }
    }

    private void CopyUrl_Click(object sender, RoutedEventArgs e)
    {
        if (RemoteFrom(sender) is not RemoteInfo remote || Tab is not RepositoryTabViewModel tab)
        {
            return;
        }

        DataPackage package = new();
        package.SetText(remote.FetchUrl);
        Clipboard.SetContent(package);

        tab.ReportInformation("Copied", $"{remote.FetchUrl} copied to the clipboard.");
    }
}
