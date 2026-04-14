using System.Windows;

namespace SpherePacketVisualEditor;

public partial class ExportSubpacketDialog
{
    public int EndBit;
    public int EndOffset;
    public int StartBit;
    public int StartOffset;

    public ExportSubpacketDialog ()
    {
        InitializeComponent();
        SubpacketName.Focus();
    }

    public string Name => SubpacketName.Text;

    private void ButtonBase_OnClick (object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SubpacketName.Text) || string.IsNullOrWhiteSpace(StartOffsetText.Text) ||
            string.IsNullOrWhiteSpace(StartBitText.Text) || string.IsNullOrWhiteSpace(EndOffsetText.Text) ||
            string.IsNullOrWhiteSpace(EndBitText.Text))
        {
            MessageBox.Show("Please input name, offset and bit");
            return;
        }

        if (!int.TryParse(StartOffsetText.Text, out var startOffset) ||
            !int.TryParse(StartBitText.Text, out var startBit) || startOffset < 0 || startBit < 0 ||
            !int.TryParse(EndOffsetText.Text, out var endOffset) ||
            !int.TryParse(EndBitText.Text, out var endBit) || endOffset < 0 || endBit < 0)
        {
            MessageBox.Show("Offset and bit should be integers >= 0");
            return;
        }

        StartOffset = startOffset;
        StartBit = startBit;
        EndOffset = endOffset;
        EndBit = endBit;
        DialogResult = true;
    }
}