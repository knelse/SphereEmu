using System.Windows;

namespace SpherePacketVisualEditor;

public partial class SaveNewPacketDefinitionDialog
{
    public SaveNewPacketDefinitionDialog ()
    {
        InitializeComponent();
        NewPacketDefinitionName.Focus();
    }

    public string Name => NewPacketDefinitionName.Text;

    private void SaveButton_OnClick (object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NewPacketDefinitionName.Text))
        {
            MessageBox.Show("Please input name");
            return;
        }

        DialogResult = true;
    }
}