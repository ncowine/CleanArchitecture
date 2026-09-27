using System.Windows;
using Poc1.PrismRabbitMq.ViewModels;

namespace Poc1.PrismRabbitMq.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
