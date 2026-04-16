using System;
using System.Globalization;
using System.Windows;

namespace PacketLogViewer.Dialogs;

public partial class TrackXpDialog : Window
{
    private bool _isInitializing;
    private bool _degreeXpEdited;
    private bool _titleXpEdited;
    private readonly bool _hadInitialSnapshot;

    public int TitleLevel { get; private set; }
    public int TitleRebirth { get; private set; }
    public int TitleXp { get; private set; }

    public int DegreeLevel { get; private set; }
    public int DegreeRebirth { get; private set; }
    public int DegreeXp { get; private set; }
    public bool DegreeXpUseNextPacketAsBase { get; private set; }
    public bool TitleXpUseNextPacketAsBase { get; private set; }

    public bool PillActive { get; private set; }
    public int ServerBonus { get; private set; } // 1..3
    public string MissionArea { get; private set; } = "Гип";

    public TrackXpDialog(TrackXpSnapshot? initial)
    {
        _isInitializing = true;
        _hadInitialSnapshot = initial is not null;
        InitializeComponent();

        TitleRebirthComboBox.ItemsSource = new[] { "0", "в", "вв", "л" };
        DegreeRebirthComboBox.ItemsSource = new[] { "-", "в", "вв", "л" };
        ServerBonusComboBox.ItemsSource = new[] { "x1", "x2", "x3" };
        MissionAreaComboBox.ItemsSource = new[] { "Гип", "Харон", "Феб", "Родос" };

        TitleRebirthComboBox.SelectedIndex = 0;
        DegreeRebirthComboBox.SelectedIndex = 0;
        ServerBonusComboBox.SelectedIndex = 0;
        MissionAreaComboBox.SelectedIndex = 0;

        if (initial is not null)
        {
            TitleLevelTextBox.Text = initial.TitleLevel.ToString(CultureInfo.InvariantCulture);
            TitleRebirthComboBox.SelectedIndex = Math.Clamp(initial.TitleRebirth, 0, 3);
            TitleXpTextBox.Text = initial.TitleXp.ToString(CultureInfo.InvariantCulture);

            DegreeLevelTextBox.Text = initial.DegreeLevel.ToString(CultureInfo.InvariantCulture);
            DegreeRebirthComboBox.SelectedIndex = Math.Clamp(initial.DegreeRebirth, 0, 3);
            DegreeXpTextBox.Text = initial.DegreeXp.ToString(CultureInfo.InvariantCulture);

            PillActiveCheckBox.IsChecked = initial.PillActive;
            ServerBonusComboBox.SelectedIndex = Math.Clamp(initial.ServerBonus - 1, 0, 2);
            MissionAreaComboBox.SelectedItem = initial.MissionArea;
        }
        else
        {
            TitleLevelTextBox.Text = "1";
            TitleXpTextBox.Text = "0";
            DegreeLevelTextBox.Text = "1";
            DegreeXpTextBox.Text = "0";
        }

        DegreeXpTextBox.TextChanged += (_, _) =>
        {
            if (_isInitializing)
            {
                return;
            }

            _degreeXpEdited = true;
        };

        _isInitializing = false;
    }

    private void Ok_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryParseIntRange(TitleLevelTextBox.Text, 1, 60, out var titleLevel))
        {
            MessageBox.Show(this, "Title Level must be an integer between 1 and 60.", "Track XP",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryParseUInt(TitleXpTextBox.Text, out var titleXp))
        {
            MessageBox.Show(this, "Title XP must be an unsigned integer.", "Track XP",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryParseIntRange(DegreeLevelTextBox.Text, 1, 60, out var degreeLevel))
        {
            MessageBox.Show(this, "Degree Level must be an integer between 1 and 60.", "Track XP",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryParseUInt(DegreeXpTextBox.Text, out var degreeXp))
        {
            MessageBox.Show(this, "Degree XP must be an unsigned integer.", "Track XP",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TitleLevel = titleLevel;
        TitleRebirth = TitleRebirthComboBox.SelectedIndex;
        TitleXp = (int)titleXp;

        DegreeLevel = degreeLevel;
        DegreeRebirth = DegreeRebirthComboBox.SelectedIndex;
        DegreeXp = (int)degreeXp;
        // Auto-baseline only when first configuring tracking (no prior snapshot)
        DegreeXpUseNextPacketAsBase = !_hadInitialSnapshot && !_degreeXpEdited;
        TitleXpUseNextPacketAsBase = !_hadInitialSnapshot && !_titleXpEdited;

        PillActive = PillActiveCheckBox.IsChecked == true;
        ServerBonus = (ServerBonusComboBox.SelectedIndex < 0 ? 0 : ServerBonusComboBox.SelectedIndex) + 1;
        MissionArea = (MissionAreaComboBox.SelectedItem as string) ?? "Гип";

        DialogResult = true;
        Close();
    }

    private static bool TryParseUInt(string? text, out uint value)
    {
        text = (text ?? string.Empty).Trim();
        return uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseIntRange(string? text, int min, int max, out int value)
    {
        text = (text ?? string.Empty).Trim();
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        return value >= min && value <= max;
    }
}

public sealed record TrackXpSnapshot(
    int TitleLevel,
    int TitleRebirth,
    int TitleXp,
    int DegreeLevel,
    int DegreeRebirth,
    int DegreeXp,
    bool DegreeXpUseNextPacketAsBase,
    bool TitleXpUseNextPacketAsBase,
    bool PillActive,
    int ServerBonus,
    string MissionArea
);

