using Microsoft.Extensions.DependencyInjection;
using Starfish.Services;
using Starfish.Windows;

namespace Starfish.GraphWidget;

/// <summary>
/// Public API surface for the Starfish Graph Widget library.
/// </summary>
public static class GraphWidgetFactory
{
    /// <summary>
    /// Registers all graph widget services into the DI container.
    /// </summary>
    public static IServiceCollection AddStarfishGraphWidget(this IServiceCollection services)
    {
        services.AddTransient<WebWindow>();
        services.AddSingleton<IPackageTraversalService, PackageTraversalService>();
        services.AddTransient<IUnprivilegedOperationService, UnprivilegedOperationService>();
        return services;
    }

    /// <summary>
    /// Creates the graph widget as a Gtk.Widget ready to be appended to any container.
    /// Optionally starts loading a root package at the given depth.
    /// </summary>
    public static Gtk.Widget CreateGraphWidget(IServiceProvider serviceProvider, string? rootPackage = null, int depth = 3)
    {
        var webWindow = serviceProvider.GetRequiredService<WebWindow>();
        var widget = webWindow.CreateWindow();
        widget.SetVexpand(true);

        if (!string.IsNullOrWhiteSpace(rootPackage))
        {
            Task.Run(async () => await webWindow.InitializeAsync(rootPackage, depth));
        }

        return widget;
    }

    /// <summary>
    /// Creates a display-only graph widget with no UI controls (no search, depth spinner, or toggle buttons).
    /// Takes pre-calculated dependency data and renders the graph immediately.
    /// Supports pan (right-click drag), zoom (scroll), hover highlighting, and click selection.
    /// </summary>
    public static Gtk.Widget CreateDisplayOnlyGraphWidget(
        string rootPackage,
        Dictionary<string, List<string>> dependencyMap)
    {
        var graphWidget = new GskGraphWidget();
        graphWidget.SetHexpand(true);
        graphWidget.SetVexpand(true);
        graphWidget.SetSizeRequest(300, 300);

        var labelOverlay = Gtk.DrawingArea.New();
        labelOverlay.CanTarget = false;
        labelOverlay.SetDrawFunc((_, cr, w, h) =>
        {
            graphWidget.DrawLabels(cr, w, h);
        });

        var overlay = Gtk.Overlay.New();
        overlay.SetHexpand(true);
        overlay.SetVexpand(true);
        overlay.SetChild(graphWidget);
        overlay.AddOverlay(labelOverlay);

        graphWidget.SetLabelOverlay(labelOverlay);

        double zoom = 1.0;
        double panX = 0, panY = 0;
        double panStartX = 0, panStartY = 0;

        var scroll = Gtk.EventControllerScroll.New(Gtk.EventControllerScrollFlags.Vertical);
        scroll.OnScroll += (sender, args) =>
        {
            zoom = Math.Clamp(zoom - args.Dy * 0.1, 0.1, 10.0);
            graphWidget.SetTransform(zoom, panX, panY);
            return true;
        };
        graphWidget.AddController(scroll);

        var drag = Gtk.GestureDrag.New();
        drag.Button = 3;
        drag.OnDragBegin += (_, args) =>
        {
            panStartX = panX;
            panStartY = panY;
        };
        drag.OnDragUpdate += (_, args) =>
        {
            panX = panStartX + args.OffsetX;
            panY = panStartY + args.OffsetY;
            graphWidget.SetTransform(zoom, panX, panY);
        };
        graphWidget.AddController(drag);

        var click = Gtk.GestureClick.New();
        click.Button = 1;
        click.OnPressed += (_, args) =>
        {
            var pkg = graphWidget.GetPackageAt(args.X, args.Y);
            graphWidget.SetSelectedNode(pkg);
        };
        graphWidget.AddController(click);

        var motion = Gtk.EventControllerMotion.New();
        motion.OnMotion += (_, args) =>
        {
            var pkg = graphWidget.GetPackageAt(args.X, args.Y);
            graphWidget.SetHoverNode(pkg);
        };
        motion.OnLeave += (_, _) =>
        {
            graphWidget.SetHoverNode(null);
        };
        graphWidget.AddController(motion);

        graphWidget.UpdateData(rootPackage, dependencyMap);

        return overlay;
    }
}
