using System.Text.RegularExpressions;

namespace GitExtensions.WinUI.Services;

/// <summary>
///  A repository's identity on its hosting service, worked out from a remote URL.
/// </summary>
/// <param name="Host">The host, e.g. <c>github.com</c>.</param>
/// <param name="Owner">The user or organisation.</param>
/// <param name="Name">The repository name, without the <c>.git</c> suffix.</param>
public sealed record HostedRepository(string Host, string Owner, string Name)
{
    public string BrowseUrl => $"https://{Host}/{Owner}/{Name}";

    public bool IsGitHub => Host.Equals("github.com", StringComparison.OrdinalIgnoreCase);

    public string CommitUrl(string hash) => $"{BrowseUrl}/commit/{hash}";

    public string BranchUrl(string branch) => $"{BrowseUrl}/tree/{Uri.EscapeDataString(branch)}";

    public string IssuesUrl => $"{BrowseUrl}/issues";

    public string PullRequestsUrl => $"{BrowseUrl}/pulls";

    /// <summary>
    ///  The page GitHub shows for opening a pull request from a branch.
    /// </summary>
    /// <remarks>
    ///  Opening the compare page rather than calling the API is deliberate: creating a pull request
    ///  through the API would mean holding a credential, and this front-end has nowhere safe to keep
    ///  one. The browser is already signed in, and GitHub pre-fills the form from the branch.
    /// </remarks>
    public string CreatePullRequestUrl(string branch) =>
        $"{BrowseUrl}/compare/{Uri.EscapeDataString(branch)}?expand=1";

    /// <summary>Blame for one file on a branch, which is the view worth leaving the app for.</summary>
    public string BlameUrl(string branch, string path) =>
        $"{BrowseUrl}/blame/{Uri.EscapeDataString(branch)}/{string.Join('/', path.Split('/', '\\').Select(Uri.EscapeDataString))}";
}

/// <summary>
///  Turns a git remote URL into a browsable repository.
/// </summary>
/// <remarks>
///  Handles the three forms a remote takes in practice: HTTPS, the SSH shorthand
///  (<c>git@host:owner/repo.git</c>), and a full <c>ssh://</c> URL. Anything else yields null and the
///  hosting features simply do not appear, which is the right outcome for a repository that has no
///  remote or lives somewhere unrecognised.
/// </remarks>
public static partial class GitHostLinks
{
    public static HostedRepository? Parse(string remoteUrl)
    {
        if (string.IsNullOrWhiteSpace(remoteUrl))
        {
            return null;
        }

        string url = remoteUrl.Trim();

        // The SCP form is only attempted when there is no scheme. Otherwise it happily matches
        // "https://host/owner/repo" with "https" as the host — the colon and the rest of the string
        // both fit its pattern — and every generated link points at a host that does not exist.
        if (!url.Contains("://", StringComparison.Ordinal))
        {
            Match scp = ScpLikeUrl().Match(url);

            return scp.Success
                ? Build(scp.Groups["host"].Value, scp.Groups["path"].Value)
                : null;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed))
        {
            return null;
        }

        // Only web-browsable schemes; a file:// or a local path has nothing to open.
        if (parsed.Scheme is not ("http" or "https" or "ssh" or "git"))
        {
            return null;
        }

        return Build(parsed.Host, parsed.AbsolutePath);
    }

    private static HostedRepository? Build(string host, string path)
    {
        string[] segments = path
            .Trim('/', ':')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (host.Length == 0 || segments.Length < 2)
        {
            return null;
        }

        string name = segments[^1];

        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        // Everything between the host and the repository is the owner, which keeps self-hosted
        // instances with nested groups working rather than only the two-segment GitHub shape.
        string owner = string.Join('/', segments[..^1]);

        return name.Length == 0 ? null : new HostedRepository(host, owner, name);
    }

    [GeneratedRegex(@"^(?:[\w.\-]+@)?(?<host>[\w.\-]+):(?<path>[\w.\-/~]+)$")]
    private static partial Regex ScpLikeUrl();
}
