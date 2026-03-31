using Gtk;

namespace Starfish.Windows;

public class HomeWindow
{
    public Widget CreateWindow()
    {
       var box = Box.New(Orientation.Vertical, 0);
       box.Append(Label.New("Welcome to Starfish"));
       return box;
    }
}