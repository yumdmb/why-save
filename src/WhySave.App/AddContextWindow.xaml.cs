using System.Windows;
using System.Windows.Controls;
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

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateEditBindings();

        if (DataContext is AddContextViewModel viewModel && viewModel.SaveCommand.CanExecute(null))
            viewModel.SaveCommand.Execute(null);
    }

    private void UpdateEditBindings()
    {
        ReasonTextBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        ProjectComboBox.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();
        SourceUrlTextBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        NotesTextBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        SavedDatePicker.GetBindingExpression(DatePicker.SelectedDateProperty)?.UpdateSource();
    }
}
