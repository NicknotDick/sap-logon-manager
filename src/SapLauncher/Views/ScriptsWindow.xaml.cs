using System.Windows;
using System.Windows.Controls;
using SapLauncher.Models;
using SapLauncher.Services;

namespace SapLauncher.Views;

public partial class ScriptsWindow : Window
{
    private readonly SapSystem? _system;
    private ScriptDef? _current;

    public ScriptsWindow(SapSystem? system)
    {
        InitializeComponent();
        WindowTheme.Apply(this);
        _system = system;
        RefreshList();
        UpdateBoundText();
    }

    private void RefreshList(string? selectId = null)
    {
        var items = AppServices.Scripts.Scripts.ToList();
        ScriptList.ItemsSource = null;
        ScriptList.ItemsSource = items;

        if (items.Count > 0)
        {
            var target = selectId is null ? items[0] : items.FirstOrDefault(s => s.Id == selectId) ?? items[0];
            ScriptList.SelectedItem = target;
        }
    }

    private void OnSelectScript(object sender, SelectionChangedEventArgs e)
    {
        _current = ScriptList.SelectedItem as ScriptDef;
        if (_current is null)
        {
            Editor.Text = "";
            NameBox.Text = "";
            return;
        }
        NameBox.Text = _current.Name;
        Editor.Text = AppServices.Scripts.Read(_current);
        UpdateBoundText();
    }

    private void OnNew(object sender, RoutedEventArgs e)
    {
        var dlg = new InputDialog(Localizer.Get("Script.New"), Localizer.Get("Script.Name"), "新脚本") { Owner = this };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Value)) return;
        var def = AppServices.Scripts.Create(dlg.Value);
        AppServices.Manifest.Save();
        RefreshList(def.Id);
    }

    private void OnImport(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "VBScript (*.vbs)|*.vbs" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var def = AppServices.Scripts.Import(dlg.FileName);
            AppServices.Manifest.Save();
            RefreshList(def.Id);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Localizer.Get("App.Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        if (MessageBox.Show(this, Localizer.Get("Script.DeleteConfirm"), Localizer.Get("App.Title"),
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;

        foreach (var s in AppServices.Manifest.Data.Systems) s.Scripts.Remove(_current.Id);
        AppServices.Scripts.Remove(_current);
        AppServices.Manifest.Save();
        _current = null;
        RefreshList();
        UpdateBoundText();
    }

    private void SaveCurrent()
    {
        if (_current is null) return;
        _current.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? _current.Name : NameBox.Text.Trim();
        AppServices.Scripts.Write(_current, Editor.Text);
        AppServices.Manifest.Save();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_current is null) { MessageBox.Show(this, Localizer.Get("Script.Select")); return; }
        SaveCurrent();
        RefreshList(_current.Id);
    }

    private async void OnRun(object sender, RoutedEventArgs e)
    {
        if (_current is null) { MessageBox.Show(this, Localizer.Get("Script.Select")); return; }
        SaveCurrent();
        var (ok, output) = await AppServices.Scripts.RunAsync(_current);
        MessageBox.Show(this, output, _current.Name, MessageBoxButton.OK,
            ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void OnBind(object sender, RoutedEventArgs e)
    {
        if (_system is null)
        {
            MessageBox.Show(this, Localizer.Get("Msg.SelectSystem"), Localizer.Get("App.Title"));
            return;
        }
        if (_current is null)
        {
            MessageBox.Show(this, Localizer.Get("Script.Select"), Localizer.Get("App.Title"));
            return;
        }
        if (!_system.Scripts.Contains(_current.Id)) _system.Scripts.Add(_current.Id);
        AppServices.Manifest.Save();
        UpdateBoundText();
    }

    private void OnUnbind(object sender, RoutedEventArgs e)
    {
        if (_system is null || _current is null) return;
        _system.Scripts.Remove(_current.Id);
        AppServices.Manifest.Save();
        UpdateBoundText();
    }

    private void UpdateBoundText()
    {
        if (_system is null)
        {
            BoundText.Text = Localizer.Get("Script.Hint");
            return;
        }

        var names = _system.Scripts
            .Select(id => AppServices.Scripts.Find(id)?.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();

        BoundText.Text = names.Count == 0
            ? $"{Localizer.Get("Script.Bound")}：—"
            : $"{Localizer.Get("Script.Bound")}：{string.Join("、", names)}";
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        AppServices.Manifest.Save();
        base.OnClosed(e);
    }
}
