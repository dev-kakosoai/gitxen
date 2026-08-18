using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace GitExtensions.WinUI;

internal static class AssemblyResolveFallback
{
    [ModuleInitializer]
    public static void Register()
    {
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            string path = Path.Combine(AppContext.BaseDirectory, name.Name + ".dll");
            return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
        };
    }
}
