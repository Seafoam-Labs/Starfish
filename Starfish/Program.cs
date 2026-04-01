using System.Runtime;
using System.Reflection;
using Gtk;
using Microsoft.Extensions.DependencyInjection;
using Starfish;
using Starfish.Constants;
using Starfish.Helpers;
using Starfish.Windows;

sealed class Program
{
    public static int Main(string[] args)
    {
        ServiceCollection serviceCollection = new();
        var serviceProvider = ServiceBuilder.CreateDependencyInjection(serviceCollection);

        var application = Application.New(StarfishConstants.Service, Gio.ApplicationFlags.DefaultFlags);


        application.OnActivate += (sender, _) =>
        {
           
            var existingWindow = application.GetActiveWindow();
            if (existingWindow != null)
            {
                existingWindow.Present();
                return;
            }
            
            var iconTheme = IconTheme.GetForDisplay(Gdk.Display.GetDefault()!);
            iconTheme.AddSearchPath("Assets/svg");

            var mainBuilder = Builder.NewFromString(ResourceHelper.LoadUiFile("UiFiles/MainWindow.ui"), -1);
            var window = (ApplicationWindow)mainBuilder.GetObject("MainWindow")!;

            window.SetIconName("starfish");
            window.Application = application;

            var menuBuilder = Builder.NewFromString(ResourceHelper.LoadUiFile("UiFiles/MainMenu.ui"), -1);
            var appMenu = (Gio.Menu)menuBuilder.GetObject("AppMenu")!;
            application.Menubar = appMenu;

            var quitAction = Gio.SimpleAction.New("quit", null);
            quitAction.OnActivate += (_, _) => application.Quit();
            application.AddAction(quitAction);

            var aboutAction = Gio.SimpleAction.New("about", null);
            aboutAction.OnActivate += (_, _) => Console.WriteLine("About clicked");
            application.AddAction(aboutAction);

            var contentArea = (Box)mainBuilder.GetObject("ContentArea")!;
            
            var homeWindow = serviceProvider.GetRequiredService<HomeWindow>();
            contentArea.Append(homeWindow.CreateWindow());
            
            window.Show();
            
            
        };

        return application.Run(args);
    }
}