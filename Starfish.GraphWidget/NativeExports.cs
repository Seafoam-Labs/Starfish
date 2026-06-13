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
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Starfish] init exception: {ex}");
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
    /// Creates a display-only graph widget from a JSON payload containing
    /// rootPackage and dependencyMap. Returns the GObject handle, or 0 on failure.
    /// The JSON string must be a UTF-8 null-terminated C string.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "starfish_graph_widget_create_display_only")]
    public static nint CreateDisplayOnlyWidget(nint jsonUtf8Ptr)
    {
        try
        {
            var json = Marshal.PtrToStringUTF8(jsonUtf8Ptr);
            if (string.IsNullOrEmpty(json))
            {
                Console.Error.WriteLine("[Starfish] create_display_only: json is null or empty");
                return 0;
            }

            var request = System.Text.Json.JsonSerializer.Deserialize(json,
                StarfishGraphWidgetJsonContext.Default.DisplayOnlyRequest);
            if (request == null)
            {
                Console.Error.WriteLine("[Starfish] create_display_only: deserialization returned null");
                return 0;
            }

            var widget = GraphWidgetFactory.CreateDisplayOnlyGraphWidget(
                request.RootPackage, request.DependencyMap);
            return widget.Handle.DangerousGetHandle();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Starfish] create_display_only exception: {ex}");
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
