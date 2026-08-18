using System.ComponentModel.Design;
using System.IO.Abstractions;
using GitCommands.Git;
using GitExtUtils;

namespace GitExtensions.WinUI.Bootstrap;

internal static class ServiceContainerRegistry
{
    public static void RegisterServices(ServiceContainer serviceContainer)
    {
        GitExtUtils.ServiceContainerRegistry.RegisterServices(serviceContainer);

        FileSystem fileSystem = new();
        GitDirectoryResolver gitDirectoryResolver = new(fileSystem);
        serviceContainer.AddService<IFileSystem>(fileSystem);
        serviceContainer.AddService<IGitDirectoryResolver>(gitDirectoryResolver);

        GitCommands.ServiceContainerRegistry.RegisterServices(serviceContainer);
    }
}
