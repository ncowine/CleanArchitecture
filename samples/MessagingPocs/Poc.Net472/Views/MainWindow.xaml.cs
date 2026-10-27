using System.Windows;
using Poc.Net472.ViewModels;

namespace Poc.Net472.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel, SelectionStatusViewModel status)
    {
        InitializeComponent();
        DataContext = viewModel;
        StatusBar.DataContext = status;   // its own view model: the two only meet through the local event
    }
}
