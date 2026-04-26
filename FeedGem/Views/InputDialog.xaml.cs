using System.Windows;

namespace FeedGem.Views
{
    public partial class InputDialog : Window
    {
        public string InputText => InputBox.Text;

        public InputDialog(string message, string defaultValue = "")
        {
            InitializeComponent();

            MessageText.Text = message;
            InputBox.Text = defaultValue;
            InputBox.Focus();
            InputBox.SelectAll();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                DialogResult = true;
            }
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                DialogResult = false;
            }
        }
    }
}