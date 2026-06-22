using System.Windows;
using WhySave.App.ViewModels;

namespace WhySave.App;

public partial class AddContextWindow : Window
{
    public AddContextWindow(AddContextViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += (_, _) => Close();
    }
}
