using System.Windows;
using Poc.Net472.ViewModels;

namespace Poc.Net472.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
