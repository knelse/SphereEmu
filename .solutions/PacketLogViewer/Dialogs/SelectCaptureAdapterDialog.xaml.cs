using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using SharpPcap;

namespace PacketLogViewer.Dialogs;

public partial class SelectCaptureAdapterDialog : INotifyPropertyChanged
{
    public sealed class CaptureAdapterInfo
    {
        public string MacAddress { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string DeviceType { get; init; } = string.Empty;
    }

    public ObservableCollection<CaptureAdapterInfo> Adapters { get; } = new();

    private CaptureAdapterInfo? _selectedAdapter;
    public CaptureAdapterInfo? SelectedAdapter
    {
        get => _selectedAdapter;
        set
        {
            if (Equals(value, _selectedAdapter))
            {
                return;
            }

            _selectedAdapter = value;
            OnPropertyChanged();
        }
    }

    public string? SelectedMacAddress => SelectedAdapter?.MacAddress;

    public event PropertyChangedEventHandler? PropertyChanged;

    public SelectCaptureAdapterDialog(string? currentMacAddress)
    {
        InitializeComponent();
        DataContext = this;

        var devices = CaptureDeviceList.Instance
            .Where(d => d.MacAddress is not null)
            .Select(d => new CaptureAdapterInfo
            {
                MacAddress = d.MacAddress!.ToString(),
                Name = d.Name ?? string.Empty,
                Description = d.Description ?? string.Empty,
                DeviceType = d.GetType().Name
            })
            .OrderBy(d => d.Description)
            .ToList();

        foreach (var dev in devices)
        {
            Adapters.Add(dev);
        }

        if (!string.IsNullOrWhiteSpace(currentMacAddress))
        {
            SelectedAdapter = Adapters.FirstOrDefault(a =>
                string.Equals(a.MacAddress, currentMacAddress, StringComparison.OrdinalIgnoreCase));
        }

        SelectedAdapter ??= Adapters.FirstOrDefault();
    }

    private void Ok_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedAdapter is null)
        {
            MessageBox.Show(this, "Please select an adapter.", "Select adapter", MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

