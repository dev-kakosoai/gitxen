using System.Diagnostics;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitExtensions.Extensibility.Plugins;
using GitUIPluginInterfaces;
using Microsoft;

namespace GitUI;

public static class PluginRegistry
{
    private const int _initialPluginCapacity = 20;
    public static List<IGitPlugin> Plugins { get; } = new(_initialPluginCapacity);

    public static List<IRepositoryHostPlugin> GitHosters { get; } = [];

    private static readonly HashSet<IGitUICommands> _registeredCommands = [];

    private static bool _isLoaded = false;

    public static void InitializeGitHostersOnly()
    {
        LoadPlugins<IRepositoryHostPlugin>();
    }

    /// <summary>
    /// Initialises all available plugins on the background thread.
    /// </summary>
    public static void InitializeAll()
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;
        LoadPlugins<IGitPlugin>();
    }

    public static void InitializeForCommitForm()
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;
        LoadPlugins<IGitPluginForCommit>();
    }

    private static void LoadPlugins<T>() where T : IGitPlugin
    {
        try
        {
            IGitPlugin[] plugins = [.. ManagedExtensibility.GetExports<T>()
                .Select(lazy =>
                    {
                        try
                        {
                            return (IGitPlugin)lazy.Value;
                        }
                        catch (Exception ex)
                        {
                            FailedPluginWrapper wrapper = new(ex);
                            DebugHelpers.Fail($"{wrapper.Name}. Error: {ex.Demystify()}");
                            return wrapper;
                        }
                    })];

            lock (Plugins)
            {
                foreach (IGitPlugin plugin in plugins)
                {
                    Validates.NotNull(plugin.Description);

                    // Description for old plugin setting processing as key
                    plugin.SettingsContainer = new GitPluginSettingsContainer(plugin.Id, plugin.Description);

                    if (plugin is IRepositoryHostPlugin repositoryHostPlugin && !GitHosters.Contains(repositoryHostPlugin))
                    {
                        GitHosters.Add(repositoryHostPlugin);
                    }

                    if (!Plugins.Contains(plugin))
                    {
                        Plugins.Add(plugin);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            DebugHelpers.Fail($"Fail to load plugins. Error: {ex.Demystify()}");
        }
    }

    public static IRepositoryHostPlugin? TryGetGitHosterForModule(IGitModule module)
    {
        if (!module.IsValidGitWorkingDir())
        {
            return null;
        }

        return GitHosters.FirstOrDefault(gitHoster => gitHoster.GitModuleIsRelevantToMe());
    }

    public static bool IsRegistered(IGitUICommands gitUiCommands)
    {
        lock (Plugins)
        {
            return _registeredCommands.Contains(gitUiCommands);
        }
    }

    public static void Register(IGitUICommands gitUiCommands)
    {
        lock (Plugins)
        {
            if (!_registeredCommands.Add(gitUiCommands))
            {
                return;
            }

            Plugins.ForEach(p => p.Register(gitUiCommands));
        }
    }

    public static void Unregister(IGitUICommands gitUiCommands)
    {
        lock (Plugins)
        {
            if (!_registeredCommands.Remove(gitUiCommands))
            {
                return;
            }

            Plugins.ForEach(p => p.Unregister(gitUiCommands));
        }
    }
}
