using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Documents;

namespace SpherePacketVisualEditor;

public partial class AddPacketManuallyDialog
{
    public readonly List<byte[]> ProcessedPackets = new ();

    public AddPacketManuallyDialog ()
    {
        InitializeComponent();
        PacketsTextBox.Document = new FlowDocument();
        PacketsTextBox.Focus();
    }

    private void ButtonBase_OnClick (object sender, RoutedEventArgs e)
    {
        var range = new TextRange(PacketsTextBox.Document.ContentStart,
            PacketsTextBox.Document.ContentEnd);
        var text = range.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            MessageBox.Show("Please input packets text (hex)");
            return;
        }

        var split = text.Split(Environment.NewLine,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var packetCandidate in split)
        {
            try
            {
                var packetBytes = Convert.FromHexString(packetCandidate);
                ProcessedPackets.Add(packetBytes);
            }
            catch
            {
                MessageBox.Show("Packets should be in hex format, 1 per line");
                return;
            }
        }

        DialogResult = true;
    }
}