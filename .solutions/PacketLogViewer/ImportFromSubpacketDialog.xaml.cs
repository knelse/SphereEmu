using System.Windows;

namespace SpherePacketVisualEditor;

public partial class ImportFromSubpacketDialog
{
    public int StartBit;
    public int StartOffset;

    public ImportFromSubpacketDialog ()
    {
        InitializeComponent();
        StartOffsetText.Focus();
    }

    private void ButtonBase_OnClick (object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(StartOffsetText.Text) ||
            string.IsNullOrWhiteSpace(StartBitText.Text))
        {
            MessageBox.Show("Please input offset and bit");
            return;
        }

        if (!int.TryParse(StartOffsetText.Text, out var startOffset) ||
            !int.TryParse(StartBitText.Text, out var startBit) || startOffset < 0 || startBit < 0)
        {
            MessageBox.Show("Offset and bit should be integers >= 0");
            return;
        }

        StartOffset = startOffset;
        StartBit = startBit;
        DialogResult = true;
    }
}