using Microsoft.Extensions.DependencyInjection;
using Starfish.Windows;

namespace Starfish;

public static class ServiceBuilder
{
    public static ServiceProvider CreateDependencyInjection(ServiceCollection collection)
    {
        collection.AddTransient<HomeWindow>();
        return collection.BuildServiceProvider();
    }
}