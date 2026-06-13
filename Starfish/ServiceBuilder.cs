using Microsoft.Extensions.DependencyInjection;
using Starfish.GraphWidget;
using Starfish.Windows;

namespace Starfish;

public static class ServiceBuilder
{
    public static ServiceProvider CreateDependencyInjection(ServiceCollection collection)
    {
        collection.AddStarfishGraphWidget();
        collection.AddTransient<HomeWindow>();
        return collection.BuildServiceProvider();
    }
}
