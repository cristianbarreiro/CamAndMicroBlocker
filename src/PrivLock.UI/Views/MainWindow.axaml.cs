using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace PrivLock.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Hide();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Cancel close and hide to tray if available
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }
}
