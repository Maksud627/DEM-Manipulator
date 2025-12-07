using System.Windows;

namespace DEMModifier;

public class CheckableItem
{
    public string Value { get; set; }
    public bool IsSelected { get; set; }
}

public partial class FilterDialog : Window
{
    public List<string> SelectedValues { get; private set; } = new List<string>();

    public FilterDialog(List<string> allValues, List<string> previouslySelected)
    {
        InitializeComponent();

        var items = allValues.Select(v => new CheckableItem
        {
            Value = v,
            IsSelected = previouslySelected != null && previouslySelected.Contains(v)
        }).ToList();

        LstValues.ItemsSource = items;
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        var items = LstValues.ItemsSource as List<CheckableItem>;
        SelectedValues = items.Where(i => i.IsSelected).Select(i => i.Value).ToList();
        DialogResult = true;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
