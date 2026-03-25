using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;

namespace EasyEyes;

public partial class PauseForDialog : Window
{
    public int Minutes { get; private set; }

    public PauseForDialog()
    {
        InitializeComponent();
        MinutesBox.Focus();
        MinutesBox.SelectAll();
    }

    private void MinutesBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !Regex.IsMatch(e.Text, @"^\d+$");
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(MinutesBox.Text, out var minutes) && minutes > 0)
        {
            Minutes = minutes;
            DialogResult = true;
        }
        else
        {
            MessageBox.Show("Please enter a positive number.", "Invalid input",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
