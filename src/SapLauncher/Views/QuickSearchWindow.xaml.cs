using System.Windows;
using System.Windows.Input;
using SapLauncher.Models;
using SapLauncher.Services;

namespace SapLauncher.Views;

public partial class QuickSearchWindow : Window
{
    private readonly List<SapSystem> _all;
    private readonly Func<SapSystem, Task> _onLogin;

    public QuickSearchWindow(IReadOnlyList<SapSystem> systems, Func<SapSystem, Task> onLogin)
    {
        InitializeComponent();
        WindowTheme.Apply(this);
        _all = systems.ToList();
        _onLogin = onLogin;

        Loaded += (_, _) =>
        {
            SearchInput.Text = "";
            Apply("");
            SearchInput.Focus();
            Keyboard.Focus(SearchInput);
        };
        Deactivated += (_, _) => Close();
        SearchInput.PreviewKeyDown += OnPreviewKeyDown;
    }

    private void Apply(string key)
    {
        var list = string.IsNullOrWhiteSpace(key)
            ? _all.OrderByDescending(s => s.Favorite).ThenBy(s => s.SortOrder).ToList()
            : _all.Where(s => s.DisplayName.Contains(key, StringComparison.CurrentCultureIgnoreCase)
                              || s.Sid.Contains(key, StringComparison.OrdinalIgnoreCase)
                              || (s.Alias?.Contains(key, StringComparison.CurrentCultureIgnoreCase) ?? false))
                .OrderByDescending(s => s.Favorite).ThenBy(s => s.SortOrder).ToList();

        Results.ItemsSource = list;
        if (list.Count > 0) Results.SelectedIndex = 0;
        EmptyHint.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSearchChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        Apply(SearchInput.Text);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;
            case Key.Down:
                if (Results.Items.Count > 0)
                {
                    Results.SelectedIndex = Math.Min(Results.Items.Count - 1, Results.SelectedIndex + 1);
                    Results.ScrollIntoView(Results.SelectedItem);
                }
                e.Handled = true;
                break;
            case Key.Up:
                if (Results.Items.Count > 0)
                {
                    Results.SelectedIndex = Math.Max(0, Results.SelectedIndex - 1);
                    Results.ScrollIntoView(Results.SelectedItem);
                }
                e.Handled = true;
                break;
            case Key.Enter:
                Login();
                e.Handled = true;
                break;
        }
    }

    private void OnDoubleClick(object sender, MouseButtonEventArgs e) => Login();

    private void Login()
    {
        if (Results.SelectedItem is not SapSystem system) return;
        Close();
        _ = _onLogin(system);
    }
}
