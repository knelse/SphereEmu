using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SphServer.Helpers.Enums;

namespace SpherePacketVisualEditor;

public partial class CreatePacketPartDefinitionDialog
{
    private const string NoEnumSelected = "(NONE)";
    public string? EnumName;
    public PacketPartType? PacketPartType;

    public CreatePacketPartDefinitionDialog (Color color, List<string> definedEnums)
    {
        InitializeComponent();
        ColorPicker.SetColor(color);
        var partTypeNames = Enum.GetNames(typeof (PacketPartType)).Select(x => new ComboBoxItemWithName
        {
            Name = x
        });
        if (definedEnums.All(x => x != NoEnumSelected))
        {
            definedEnums.Insert(0, NoEnumSelected);
        }

        var enumNames = definedEnums.Select(x => new ComboBoxItemWithName { Name = x });
        EnumNameComboBox.ItemsSource = enumNames;
        EnumNameComboBox.SelectedIndex = 0;
        PacketPartTypeComboBox.ItemsSource = partTypeNames;
        PacketPartTypeComboBox.SelectedIndex = 3;

        PacketPartName.Focus();
    }

    public bool LengthFromPreviousField => LengthFromPreviousFieldCheckBox.IsChecked ?? false;

    public string Name => PacketPartName.Text;
    public Color Color => ColorPicker.Color;

    private void DialogOkButton_OnClick (object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PacketPartName.Text))
        {
            MessageBox.Show("Please input name");
            return;
        }

        DialogResult = true;
    }

    private void PacketPartTypeComboBox_OnSelectionChanged (object sender, SelectionChangedEventArgs e)
    {
        var selected = (ComboBoxItemWithName) PacketPartTypeComboBox.SelectedItem;
        if (selected is null)
        {
            return;
        }

        PacketPartType = Enum.Parse<PacketPartType>(selected.Name);
    }

    private void EnumNameComboBox_OnSelectionChanged (object sender, SelectionChangedEventArgs e)
    {
        var selected = (ComboBoxItemWithName) EnumNameComboBox.SelectedItem;
        if (selected is null)
        {
            return;
        }

        EnumName = selected.Name == NoEnumSelected ? null : selected.Name;
    }
}

public class ComboBoxItemWithName
{
    public string Name { get; set; }
}