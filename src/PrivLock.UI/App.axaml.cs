using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PrivLock.UI.ViewModels;
using PrivLock.UI.Views;

namespace PrivLock.UI;

public partial class App : Avalonia.Application
{
    private readonly MainViewModel? _mainViewModel;

    // Default constructor for designer
    public App()
    {
    }

    public App(MainViewModel mainViewModel)
    {
        _mainViewModel = mainViewModel;
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = _mainViewModel
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
