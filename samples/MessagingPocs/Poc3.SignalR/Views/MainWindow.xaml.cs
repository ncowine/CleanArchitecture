using System.Windows;
using Poc3.SignalR.ViewModels;

namespace Poc3.SignalR.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
