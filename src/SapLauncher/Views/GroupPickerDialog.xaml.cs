using System.Windows;
using SapLauncher.Services;

namespace SapLauncher.Views;

public partial class GroupPickerDialog : Window
{
    public string? SelectedGroup { get; private set; }

    public GroupPickerDialog(IEnumerable<string> groups, string? current)
    {
        InitializeComponent();
        WindowTheme.Apply(this);

        GroupCombo.Items.Add(Localizer.Get("Editor.GroupNone"));
        foreach (var g in groups) GroupCombo.Items.Add(g);
        GroupCombo.Text = string.IsNullOrWhiteSpace(current) ? Localizer.Get("Editor.GroupNone") : current;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var text = GroupCombo.Text.Trim();
        SelectedGroup = string.IsNullOrWhiteSpace(text) || text == Localizer.Get("Editor.GroupNone") ? null : text;

        if (SelectedGroup is not null &&
            !AppServices.Manifest.Data.Groups.Any(g => string.Equals(g.Name, SelectedGroup, StringComparison.CurrentCultureIgnoreCase)))
        {
            AppServices.Manifest.Data.Groups.Add(new Models.GroupDef
            {
                Name = SelectedGroup,
                SortOrder = AppServices.Manifest.Data.Groups.Count
            });
        }

        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
