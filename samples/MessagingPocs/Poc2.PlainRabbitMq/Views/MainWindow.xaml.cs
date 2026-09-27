using System.Windows;
using Poc2.PlainRabbitMq.ViewModels;

namespace Poc2.PlainRabbitMq.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
