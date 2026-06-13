using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;

namespace Starfish.GraphWidget;

/// <summary>
/// Native C-callable exports for non-.NET consumers of the shared library.
/// These functions can be called via dlopen/dlsym from native GTK applications.
/// </summary>
public static class NativeExports
{
    private static ServiceProvider? _serviceProvider;

    /// <summary>
    /// Initializes the graph widget library. Must be called before creating widgets.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "starfish_graph_widget_init")]
    public static int Initialize()
    {
        try
        {
            var services = new ServiceCollection();
            services.AddStarfishGraphWidget();
            _serviceProvider = services.BuildServiceProvider();
            return 0;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// Creates a graph widget and returns its GObject handle.
    /// Call starfish_graph_widget_init() first.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "starfish_graph_widget_create")]
    public static nint CreateWidget()
    {
        try
        {
            if (_serviceProvider == null) return 0;
            var widget = GraphWidgetFactory.CreateGraphWidget(_serviceProvider);
            return widget.Handle.DangerousGetHandle();
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Cleans up resources. Call when done with the library.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "starfish_graph_widget_shutdown")]
    public static void Shutdown()
    {
        _serviceProvider?.Dispose();
        _serviceProvider = null;
    }
}
