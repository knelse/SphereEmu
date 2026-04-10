using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using SphServer.Helpers;

namespace PacketLogViewer.Dialogs;

public partial class ClientStateObjectTypeFilterDialog : Window
{
    public sealed class ObjectTypeFilterItem
    {
        public ObjectType ObjectType { get; init; }
        public string Label { get; init; } = string.Empty;
        public bool IsSelected { get; set; }
    }

    public ObservableCollection<ObjectTypeFilterItem> Items { get; } = new();

    public ObjectType[] SelectedObjectTypes =>
        Items.Where(x => x.IsSelected).Select(x => x.ObjectType).ToArray();

    public ClientStateObjectTypeFilterDialog(HashSet<ObjectType>? initiallySelected)
    {
        InitializeComponent();
        DataContext = this;

        var selected = initiallySelected ??
                       Enum.GetValues<ObjectType>().Where(x => x != ObjectType.Unknown).ToHashSet();
        if (initiallySelected is null)
        {
            selected.Remove(ObjectType.MobSpawner);
            selected.Remove(ObjectType.Monster);
            selected.Remove(ObjectType.MonsterFlyer);
        }

        foreach (var ot in Enum.GetValues<ObjectType>().Where(x => x != ObjectType.Unknown).OrderBy(x => (ushort)x))
        {
            Items.Add(new ObjectTypeFilterItem
            {
                ObjectType = ot,
                Label = $"{(ushort)ot}  {ot}",
                IsSelected = selected.Contains(ot)
            });
        }
    }

    private void Ok_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}

