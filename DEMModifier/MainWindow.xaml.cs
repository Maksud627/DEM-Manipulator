using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace DEMModifier;

public class LayerItem : INotifyPropertyChanged
{
    public string Path { get; set; }
    public string FileName => System.IO.Path.GetFileName(Path);

    public ObservableCollection<string> AvailableAttributes { get; set; } = new ObservableCollection<string>();
    public string SelectedAttribute { get; set; }

    private string _filterColumn;
    public string FilterColumn
    {
        get => _filterColumn;
        set
        {
            if (_filterColumn != value)
            {
                _filterColumn = value;
                SelectedFilterValues.Clear();
                OnPropertyChanged(nameof(FilterColumn));
                OnPropertyChanged(nameof(FilterSummary));
            }
        }
    }

    public List<string> SelectedFilterValues { get; set; } = new List<string>();

    public string FilterSummary
    {
        get
        {
            if (string.IsNullOrEmpty(FilterColumn)) return "No Filter Selected";
            if (SelectedFilterValues == null || SelectedFilterValues.Count == 0) return "Select Values...";
            return $"{SelectedFilterValues.Count} values included";
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class MainWindow : Window
{
    public ObservableCollection<LayerItem> StandardLayers { get; set; }
    public ObservableCollection<LayerItem> FilteredLayers { get; set; }

    public MainWindow()
    {
        InitializeComponent();
        try { DemProcessor.ConfigureGdal(); }
        catch (Exception ex) { MessageBox.Show($"GDAL Init Error: {ex.Message}"); }

        StandardLayers = new ObservableCollection<LayerItem>();
        FilteredLayers = new ObservableCollection<LayerItem>();

        GridStandardLayers.ItemsSource = StandardLayers;
        GridFilteredLayers.ItemsSource = FilteredLayers;
    }

    private LayerItem CreateLayerItemFromFile(string filePath, bool attemptAutoFilter)
    {
        var processor = new DemProcessor();
        var columns = processor.GetShapefileAttributes(filePath);

        var newItem = new LayerItem
        {
            Path = filePath,
            AvailableAttributes = new ObservableCollection<string>(columns)
        };

        newItem.SelectedAttribute = columns.FirstOrDefault(c => c.ToUpper().Contains("ELEV"))
                                 ?? columns.FirstOrDefault(c => c.ToUpper().Contains("HEIGHT"))
                                 ?? columns.FirstOrDefault();

        if (attemptAutoFilter)
        {
            newItem.FilterColumn = columns.FirstOrDefault(c => c.ToLower() == "type");
        }

        return newItem;
    }

    private void BtnBrowseDem_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog ofd = new OpenFileDialog { Filter = "GeoTIFF Files|*.tif;*.tiff" };
        if (ofd.ShowDialog() == true) TxtDemPath.Text = ofd.FileName;
    }

    private void BtnAddStandardLayer_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog ofd = new OpenFileDialog { Filter = "Shapefiles|*.shp", Multiselect = true };
        if (ofd.ShowDialog() == true)
        {
            foreach (string file in ofd.FileNames)
            {
                try { StandardLayers.Add(CreateLayerItemFromFile(file, false)); }
                catch (Exception ex) { MessageBox.Show($"Error loading {file}: {ex.Message}"); }
            }
        }
    }

    private void BtnAddFilteredLayer_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog ofd = new OpenFileDialog { Filter = "Shapefiles|*.shp", Multiselect = true };
        if (ofd.ShowDialog() == true)
        {
            foreach (string file in ofd.FileNames)
            {
                try { FilteredLayers.Add(CreateLayerItemFromFile(file, true)); }
                catch (Exception ex) { MessageBox.Show($"Error loading {file}: {ex.Message}"); }
            }
        }
    }

    private void BtnRemoveStandardLayer_Click(object sender, RoutedEventArgs e)
    {
        if (GridStandardLayers.SelectedItem is LayerItem selected) StandardLayers.Remove(selected);
    }

    private void BtnRemoveFilteredLayer_Click(object sender, RoutedEventArgs e)
    {
        if (GridFilteredLayers.SelectedItem is LayerItem selected) FilteredLayers.Remove(selected);
    }

    private void BtnFilterValues_Click(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        var layerItem = button.DataContext as LayerItem;

        if (layerItem == null || string.IsNullOrEmpty(layerItem.FilterColumn))
        {
            MessageBox.Show("Please select a 'Filter Column' first.");
            return;
        }

        try
        {
            var processor = new DemProcessor();
            List<string> uniqueValues = processor.GetUniqueColumnValues(layerItem.Path, layerItem.FilterColumn);

            var dialog = new FilterDialog(uniqueValues, layerItem.SelectedFilterValues);
            if (dialog.ShowDialog() == true)
            {
                layerItem.SelectedFilterValues = dialog.SelectedValues;
                layerItem.FilterColumn = layerItem.FilterColumn;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error reading values: {ex.Message}");
        }
    }

    private async void BtnProcess_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtDemPath.Text))
        {
            MessageBox.Show("Please select a Base DEM.");
            return;
        }

        if (StandardLayers.Count == 0 && FilteredLayers.Count == 0)
        {
            MessageBox.Show("Please add at least one layer.");
            return;
        }

        SaveFileDialog sfd = new SaveFileDialog { Filter = "GeoTIFF|*.tif", FileName = "modified_dem.tif" };
        if (sfd.ShowDialog() == true)
        {
            string outPath = sfd.FileName;
            string demPath = TxtDemPath.Text;

            var configLayers = new List<DemProcessor.LayerConfig>();

            foreach (var layer in StandardLayers)
            {
                if (string.IsNullOrWhiteSpace(layer.SelectedAttribute))
                {
                    MessageBox.Show($"Missing height attribute for {layer.FileName}"); return;
                }
                configLayers.Add(new DemProcessor.LayerConfig
                {
                    ShapefilePath = layer.Path,
                    AttributeColumn = layer.SelectedAttribute
                });
            }

            foreach (var layer in FilteredLayers)
            {
                if (string.IsNullOrWhiteSpace(layer.SelectedAttribute))
                {
                    MessageBox.Show($"Missing height attribute for {layer.FileName}"); return;
                }
                if (string.IsNullOrWhiteSpace(layer.FilterColumn) || layer.SelectedFilterValues.Count == 0)
                {
                    MessageBox.Show($"Incomplete filter configuration for {layer.FileName}"); return;
                }

                configLayers.Add(new DemProcessor.LayerConfig
                {
                    ShapefilePath = layer.Path,
                    AttributeColumn = layer.SelectedAttribute,
                    FilterColumn = layer.FilterColumn,
                    FilterValues = layer.SelectedFilterValues
                });
            }

            ProgressBar.Visibility = Visibility.Visible;
            TxtStatus.Text = "Processing...";
            IsEnabled = false;

            try
            {
                await Task.Run(() => new DemProcessor().ProcessDem(demPath, configLayers, outPath));
                MessageBox.Show("Done!", "Success");
                TxtStatus.Text = $"Saved to: {outPath}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}");
                TxtStatus.Text = "Error.";
            }
            finally
            {
                ProgressBar.Visibility = Visibility.Hidden;
                IsEnabled = true;
            }
        }
    }
}