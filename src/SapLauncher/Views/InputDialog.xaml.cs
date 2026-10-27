using System.Windows;
using SapLauncher.Services;

namespace SapLauncher.Views;

public partial class InputDialog : Window
{
    private readonly bool _secret;

    /// <summary>密码模式不 Trim（避免吞掉有意义的空格）；普通输入保留 Trim。</summary>
    public string Value => _secret ? SecretBox.Password : ValueBox.Text.Trim();

    public InputDialog(string title, string label, string initial, bool secret = false)
    {
        InitializeComponent();
        WindowTheme.Apply(this);
        _secret = secret;
        Title = title;
        TitleText.Text = title;
        LabelText.Text = label;

        if (secret)
        {
            ValueBox.Visibility = Visibility.Collapsed;
            SecretBox.Visibility = Visibility.Visible;
            SecretBox.Password = initial;
        }
        else
        {
            ValueBox.Text = initial;
        }

        Loaded += (_, _) =>
        {
            if (secret)
            {
                SecretBox.FocusInput();
            }
            else
            {
                ValueBox.Focus();
                ValueBox.SelectAll();
            }
        };
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
