using System.Windows;
using Poc.Net10.ViewModels;

namespace Poc.Net10.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
