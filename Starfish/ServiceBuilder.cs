using Microsoft.Extensions.DependencyInjection;
using Starfish.Services;
using Starfish.Windows;

namespace Starfish;

public static class ServiceBuilder
{
    public static ServiceProvider CreateDependencyInjection(ServiceCollection collection)
    {
        collection.AddTransient<HomeWindow>();
        collection.AddTransient<WebWindow>();
        collection.AddTransient<IUnprivilegedOperationService, UnprivilegedOperationService>();
        collection.AddSingleton<IPackageTraversalService, PackageTraversalService>();
        return collection.BuildServiceProvider();
    }
}