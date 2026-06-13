using Gtk;
using Starfish.GraphWidget;

namespace Starfish.Windows;

public class HomeWindow(IServiceProvider serviceProvider)
{
    public Widget CreateWindow()
    {
       var box = Box.New(Orientation.Vertical, 0);
     
       var graphWidget = GraphWidgetFactory.CreateGraphWidget(serviceProvider, "shelly", 3);
       box.Append(graphWidget);
       
       return box;
    }
}
