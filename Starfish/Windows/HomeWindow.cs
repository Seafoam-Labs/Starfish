using Gtk;

namespace Starfish.Windows;

public class HomeWindow(WebWindow webWindow)
{
    public Widget CreateWindow()
    {
       var box = Box.New(Orientation.Vertical, 0);
     
       var webWidget = webWindow.CreateWindow();
       webWidget.SetVexpand(true);
       box.Append(webWidget);
       
       Task.Run(async () => await webWindow.InitializeAsync("pacman", 3));

       return box;
    }
}