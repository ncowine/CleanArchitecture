using System.Windows;
using Poc.Net8.ViewModels;

namespace Poc.Net8.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
