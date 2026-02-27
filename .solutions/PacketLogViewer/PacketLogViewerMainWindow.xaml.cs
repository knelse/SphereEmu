using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BitStreams;
using LiteDB;
using PacketLogViewer.Models;
using PacketLogViewer.Models.PacketAnalyzeData;
using SphereHelpers.Extensions;
using SpherePacketVisualEditor;
using SphServer.Helpers;
using SphServer.Helpers.Enums;
using Microsoft.Extensions.Configuration;

namespace PacketLogViewer;

public partial class PacketLogViewerMainWindow
{
    private static readonly string PacketDefinitionPath;
    private const string PacketDefinitionExtension = ".spd";
    private const string ExportedPartExtension = ".spdp";
    private const string EnumExtension = ".sphenum";
    public static Encoding Win1251 = null!;
    internal static IConfigurationRoot AppConfig;

    public static readonly LiteDatabase PacketDatabase;

    public static readonly ILiteCollection<StoredPacket> PacketCollection;

    private static Bit[] PacketContentBits = null!;

    private static BitStream? CurrentContentBitStream;

    private static SolidColorBrush SelectionBrush = null!;

    public static readonly Dictionary<string, Dictionary<int, string>> DefinedEnums = new ();
    private readonly List<string> DefinedEnumNames = new ();

    public readonly PacketCapture PacketCapture;
    public static readonly ObservableCollection<PacketDefinition> PacketDefinitions = new ();

    public static readonly ObservableCollection<PacketPart> PacketParts = new ();
    public readonly DispatcherTimer SphereTimeUpdateTimer;
    public static readonly ObservableCollection<Subpacket> Subpackets = new ();
    public static readonly ObservableCollection<PacketAnalyzeData> CurrentClientState = new ();

    private TextPointer? EndTextPointer;
    private int? LastCaretOffset;
    private double LastVerticalOffset;
    private ScrollViewer? PacketDisplayScrollViewer;
    private TextPointer? StartTextPointer;

    static PacketLogViewerMainWindow ()
    {
        AppConfig = new ConfigurationBuilder().AddJsonFile("appconfig.json").AddEnvironmentVariables().Build();
        PacketDatabase = new LiteDatabase(AppConfig.GetConnectionString("LiteDbPacketCollection"));
        PacketDefinitionPath = AppConfig.GetSection("Settings").GetValue<string>("PacketDefinitionPath");
        Directory.CreateDirectory(PacketDefinitionPath);
        PacketCollection = PacketDatabase.GetCollection<StoredPacket>("Packets");
    }

    public PacketLogViewerMainWindow ()
    {
        InitializeComponent();
        RegisterBsonMapperForBrush();

        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Win1251 = Encoding.GetEncoding(1251);

            PacketCapture = new PacketCapture(AppConfig.GetSection("Settings").GetValue<string>("MacAddress"))
            {
                OnPacketProcessed = OnPacketProcessed
            };

            UpdateGameTime();

            // prewarm
            _ = SphObjectDb.GameObjectDataDb;

            LogListFullPackets.ItemsSource = LogRecords;
            LogListFullPackets.ContextMenu = new ContextMenu();
            var menuItem = new MenuItem { Header = "Copy" };
            menuItem.Click += FullPacketsLog_MenuItem_OnClick;
            LogListFullPackets.ContextMenu.Items.Add(menuItem);

            LogListFullPackets.SelectionChanged += LogListOnSelectionChanged;
            CurrentEntityStateForClient.ItemsSource = CurrentClientState;

            LogListFullPackets.KeyDown += (_, args) =>
            {
                if (args.KeyboardDevice.Modifiers != ModifierKeys.Control || args.Key != Key.C)
                {
                    return;
                }

                CopySelectedRowContent(LogListFullPackets);
            };

            LoadPacketDefinitions();
            LoadEnums();
            LoadContent();

            var fullPacketView = CollectionViewSource.GetDefaultView(LogListFullPackets.ItemsSource);
            var filterFunc = new Predicate<object>(o =>
            {
                if (ShowFavoritesOnly)
                {
                    return (o as StoredPacket)?.Favorite ?? false;
                }

                if (!HideUninteresting)
                {
                    return true;
                }

                return !((o as StoredPacket)?.HiddenByDefault ?? false);
            });
            fullPacketView.Filter = filterFunc;

            PacketVisualizerControl.KeyDown += PacketVisualizerControlAddPacketPart;
            PacketVisualizerControl.KeyDown += PacketVisualizerControlHandlePartSelection;
            SelectionBrush = new SolidColorBrush
            {
                Color = ((SolidColorBrush) PacketVisualizerControl.SelectionBrush).Color,
                Opacity = PacketVisualizerControl.SelectionOpacity
            };
            PacketVisualizerControl.SelectionBrush = Brushes.Transparent;
            PacketVisualizerControl.PreviewMouseWheel += (_, _) => { };
            var scrollViewerProperty =
                typeof (RichTextBox).GetProperty("ScrollViewer", BindingFlags.NonPublic | BindingFlags.Instance)!;

            Loaded += (_, _) =>
            {
                PacketDisplayScrollViewer = (ScrollViewer) scrollViewerProperty.GetValue(PacketVisualizerControl)!;

                PacketDisplayScrollViewer!.ScrollChanged += (sender, _) => { SynchronizeScrollValues(sender); };

                PacketVisualizerDefinedPacketValuesScrollViewer.ScrollChanged += (sender, _) =>
                {
                    SynchronizeScrollValues(sender);
                };

                PacketVisualizerLineNumbersAndValuesScrollViewer.ScrollChanged +=
                    (sender, _) => { SynchronizeScrollValues(sender); };
            };

            KeyUp += (_, e) =>
            {
                if (e.KeyboardDevice.Modifiers != ModifierKeys.Control || e.Key != Key.S)
                {
                    return;
                }

                if (DefinedPacketsListBox.SelectedItem is null)
                {
                    return;
                }

                SaveSelectedPacketDefinition();
            };

            CreateFlowDocumentWithHighlights(false, true);

            PacketPartsInDefinitionListBox.ItemsSource = PacketParts;

            SphereTimeUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1.0 / 24)
            };
            SphereTimeUpdateTimer.Tick += (_, _) => UpdateGameTime();
            SphereTimeUpdateTimer.Start();

            ScrollIntoViewIfSelectionExists();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Exception: {ex.Message}");
        }
    }

    public byte[]? CurrentContentBytes { get; set; }
    public ObservableCollection<StoredPacket> LogRecords { get; } = new ();
    public bool ShowFavoritesOnly { get; set; }
    public bool ListenerEnabled { get; set; } = true;
    public bool HideUninteresting { get; set; } = true;
    public bool ShowNewInUI { get; set; } = true;

    private void OnPacketProcessed (List<StoredPacket> storedPackets, bool forceProcess)
    {
        if (!ListenerEnabled && !forceProcess)
        {
            return;
        }

        storedPackets.Sort((a, b) => a.NumberInSequence.CompareTo(b.NumberInSequence));

        for (var i = 1; i < storedPackets.Count; i++)
        {
            // try fixing split packets
            var storedPacket = storedPackets[i];

            var currentStream = new BitStream(storedPacket.ContentBytes);
            // header
            currentStream.ReadBytes(7, true);
            var entityId = currentStream.ReadUInt16();
            currentStream.ReadByte(2);
            var objectTypeVal = currentStream.ReadUInt16(10);
            var objectType = Enum.IsDefined(typeof (ObjectType), objectTypeVal)
                ? (ObjectType) objectTypeVal
                : ObjectType.Unknown;
            if (objectType is ObjectType.UpdateState)
            {
                continue;
            }

            currentStream.ReadByte(1);
            var actionTypeVal = (int) currentStream.ReadByte();
            var actionType = Enum.IsDefined(typeof (EntityActionType), actionTypeVal)
                ? (EntityActionType) actionTypeVal
                : EntityActionType.UNDEF;
            if (actionType is EntityActionType.FULL_SPAWN or EntityActionType.FULL_SPAWN_2)
            {
                continue;
            }

            var previousStream = new BitStream(storedPackets[i - 1].ContentBytes);
            previousStream.Seek(previousStream.Length, 0);
            previousStream.SeekBack(16);
            var entityIdTest = previousStream.ReadUInt16();
            while (entityIdTest != entityId && previousStream.BitOffsetFromStart > 72)
            {
                previousStream.SeekBack(17);
                entityIdTest = previousStream.ReadUInt16();
            }

            var shouldStitchPackets = false;

            if (entityIdTest != entityId)
            {
                continue;
            }

            if (previousStream.BitOffsetFromStart == 72)
            {
                shouldStitchPackets = true;
            }
            else
            {
                previousStream.SeekBack(24);
                var dividerTest = previousStream.ReadByte();
                shouldStitchPackets = dividerTest is 0x7F or 0x7E;
            }

            if (!shouldStitchPackets)
            {
                continue;
            }

            while (currentStream.ValidPosition)
            {
                var splitTest = currentStream.ReadByte();
                if (!currentStream.ValidPosition || splitTest == 0x7E || splitTest == 0x7F)
                {
                    break;
                }

                currentStream.SeekBack(7);
            }

            if (!currentStream.ValidPosition)
            {
                continue;
            }

            var positionAfterDelimiter = currentStream.BitOffsetFromStart;
            var entityRemainderLength = positionAfterDelimiter - 72; // header (56) and ent id (16)
            currentStream.SeekBitOffset(0);
            var header = currentStream.ReadBytes(7, true);
            // should be 9, 0 but skipping 2 more to align for sacks
            currentStream.Seek(9, 0);
            var remainderBits = currentStream.ReadBits(entityRemainderLength);
            var newCurrentContent = new List<byte>();
            newCurrentContent.AddRange(header);
            newCurrentContent.AddRange(currentStream.GetStreamDataFromCurrentOffsetAndBit());
            newCurrentContent[1] = (byte) (newCurrentContent.Count / 256);
            newCurrentContent[0] = (byte) (newCurrentContent.Count % 256);
            storedPacket.ContentBytes = newCurrentContent.ToArray();
            previousStream.Seek(previousStream.Length, 0);
            // this is a hack, probably bit count varies
            previousStream.SeekBack(3);
            previousStream.AutoIncreaseStream = true;
            previousStream.WriteBits(remainderBits[16..]);
            previousStream.SeekBitOffset(0);
            // last one is the divider, first 2 are something random?
            var previousContentBytes = previousStream.GetStreamDataFromCurrentOffsetAndBit()[..^1];
            previousContentBytes[1] = (byte) (previousContentBytes.Length / 256);
            previousContentBytes[0] = (byte) (previousContentBytes.Length % 256);
            storedPackets[i - 1].ContentBytes = previousContentBytes;
        }

        for (var i = 0; i < storedPackets.Count; i++)
        {
            var storedPacket = storedPackets[i];

            storedPacket.UpdatePacketPartsForContent();

            // if (i >= 1)
            // {
            //     var firstEntityIdThisPacket = storedPacket.PacketParts.FirstOrDefault(x => x.Name == PacketPartNames.ID)
            //         ?.ActualLongValue;
            //     var lastStoredPacket = storedPackets[i - 1];
            //     var lastEntityIdLastPacket = lastStoredPacket.PacketParts
            //         .LastOrDefault(x => x.Name == PacketPartNames.ID)
            //         ?.ActualLongValue;
            //     if (firstEntityIdThisPacket == lastEntityIdLastPacket && firstEntityIdThisPacket != null)
            //     {
            //         // packet got split in 2 and entity continues in the next packet, so we gotta fix
            //         var stream = new BitStream(storedPacket.ContentBytes);
            //         var initialOffset = storedPacket.PacketParts.FirstOrDefault(x => x.Name == PacketPartNames.ID)
            //             .BitOffset;
            //         stream.SeekBitOffset(initialOffset);
            //         while (stream.ValidPosition)
            //         {
            //             var splitTest = stream.ReadByte();
            //             if (!stream.ValidPosition || splitTest == 0x7E || splitTest == 0x7F)
            //             {
            //                 break;
            //             }
            //
            //             stream.SeekBack(7);
            //         }
            //
            //         if (stream.ValidPosition)
            //         {
            //             var newOffset = stream.BitOffsetFromStart;
            //             // entity_id = 16, skip = 2, entity_type = 10, skip = 1, action_type = 8
            //             stream.SeekBitOffset(initialOffset + 37);
            //             var oldBufferChunk = stream.ReadBytes(newOffset - initialOffset - 8 - 37) ?? [];
            //             stream.SeekBitOffset(newOffset);
            //             var newStreamBuffer = stream.GetStreamDataFromCurrentOffsetAndBit();
            //             stream.SeekBitOffset(0);
            //             var newStreamHeader = stream.ReadBytes(7, true);
            //             var newContentBytes = new List<byte>(newStreamHeader);
            //             newContentBytes.AddRange(newStreamBuffer);
            //             storedPacket.ContentBytes = newContentBytes.ToArray();
            //             var newLength = storedPacket.ContentBytes.Length;
            //             storedPacket.ContentBytes[0] = (byte) (newLength & 0xFF);
            //             storedPacket.ContentBytes[1] = (byte) ((newLength >> 8) & 0xFF);
            //             storedPacket.UpdatePacketPartsForContent();
            //             var lastContentBytes = new List<byte>(lastStoredPacket.ContentBytes);
            //             lastContentBytes.AddRange(oldBufferChunk);
            //             lastStoredPacket.ContentBytes = lastContentBytes.ToArray();
            //             var lastLength = lastStoredPacket.ContentBytes.Length;
            //             lastStoredPacket.ContentBytes[0] = (byte) (lastLength & 0xFF);
            //             lastStoredPacket.ContentBytes[1] = (byte) ((lastLength >> 8) & 0xFF);
            //             lastStoredPacket.UpdatePacketPartsForContent();
            //         }
            //     }
            // }

            storedPacket.Id = PacketCollection.Insert(storedPacket);

            Dispatcher.Invoke(() =>
            {
                if (PacketAnalyzer.IsClientPingPacket(storedPacket))
                {
                    UpdateClientCoordsAndId(storedPacket);
                }

                UpdateClientState(storedPacket);
                if (ShowNewInUI)
                {
                    LogRecords.Add(storedPacket);
                    LogListFullPackets.UpdateLayout();
                }
            });
        }
    }

    public void UpdateGameTime ()
    {
        var time = TimeHelper.GetCurrentSphereDateTime().AddYears(7800);
        GameTime.Text = time.ToString("dd/MM/yyyy HH:mm");
        // TODO
        GameTimeBits.Text = "0";
    }

    public void UpdateClientCoordsAndId (StoredPacket storedPacket)
    {
        try
        {
            var coords = CoordsHelper.GetCoordsFromPingBytes(storedPacket.ContentBytes);
            CoordsX.Text = $"{coords.x:F4}";
            CoordsY.Text = $"{coords.y:F4}";
            CoordsZ.Text = $"{coords.z:F4}";
            CoordsT.Text = $"{coords.turn:F4}";

            var xBytes = CoordsHelper.EncodeServerCoordinate(coords.x);
            var yBytes = CoordsHelper.EncodeServerCoordinate(coords.y);
            var zBytes = CoordsHelper.EncodeServerCoordinate(coords.z);
            var tBytes = CoordsHelper.EncodeServerCoordinate(coords.turn);

            CoordsXBits.Text = StringConvertHelpers.ByteArrayToBinaryString(xBytes, false, true);
            CoordsYBits.Text = StringConvertHelpers.ByteArrayToBinaryString(yBytes, false, true);
            CoordsZBits.Text = StringConvertHelpers.ByteArrayToBinaryString(zBytes, false, true);
            CoordsTBits.Text = StringConvertHelpers.ByteArrayToBinaryString(tBytes, false, true);

            var id = (storedPacket.ContentBytes[16] >> 5) + (storedPacket.ContentBytes[17] << 3) +
                     ((storedPacket.ContentBytes[18] & 0b11111) << 11);
            ClientId.Text = $"{id:X4}";
            PacketCapture.SetClientId((short) id);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }

    public void LoadContent ()
    {
        PacketCollection.EnsureIndex(x => x.Source);
        // might not be great
        PacketCollection.EnsureIndex(x => x.Timestamp);

        var packets = PacketCollection.Query().Where(x => x.Favorite).OrderByDescending(x => x.Timestamp)
            .Limit(100).ToList();
        if (packets is null)
        {
            MessageBox.Show("Packets to load (full) are null");
            return;
        }

        packets.AddRange(PacketCollection.Query().OrderByDescending(x => x.Timestamp)
            .Limit(100).ToList());

        if (!packets.Any())
        {
            MessageBox.Show("No full packets to load");
        }

        packets.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

        for (var i = 0; i < packets.Count; i++)
        {
            var packet = packets[i];
            if (packet.AnalyzeState != PacketAnalyzeState.FULL)
            {
                packet = packet.UpdatePacketPartsForContent();
                UpdateStoredPacket(packet);
            }

            LogRecords.Add(packet);
        }

        LogListFullPackets.UpdateLayout();
    }

    public void UpdateContentPreview (StoredPacket selected)
    {
        try
        {
            var bytes = selected.ContentBytes;
            CurrentContentBytes = bytes;
            CurrentContentBitStream = new BitStream(CurrentContentBytes);
            // TODO: remove
            selected.UpdatePacketPartsForContent();
            PacketParts.Clear();
            selected.PacketParts.ForEach(x => PacketParts.Add(x));

            CreateFlowDocumentWithHighlights(false, true);
            UpdateDefinedPackets();
            ClearSelection();
            var packetContents = string.Empty;
            var knownAnalyzedParts = selected.AnalyzeResult
                .Where(x => x is ItemPacket or MobPacket or NpcTradePacket or WorldObject or DoorPacket
                    or TeleportWithTargetPacket)
                .ToList();
            if (knownAnalyzedParts.Any())
            {
                packetContents = string.Join('\n', knownAnalyzedParts.Select(x => x.DisplayValue));
                packetContents +=
                    "\n----------------------------------------------------------------------------------";
            }

            ContentPreview.Text = packetContents + "\n" + PacketAnalyzer.GetTextOutputForPacket(bytes) +
                                  "----------------------------------------------------------------------------------\n";
            var sphObjects = ObjectPacketTools.GetObjectsFromPacket(bytes);
            ContentPreview.Text += sphObjects.Count > 0 ? ObjectPacketTools.GetTextOutput(sphObjects) : "";
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            ContentPreview.Text = "Not an item packet";
        }
    }

    public void UpdateClientState (StoredPacket storedPacket)
    {
        foreach (var result in storedPacket.AnalyzeResult)
        {
            if (result.GetType() == typeof (DespawnPacket))
            {
                var entsToDespawn = CurrentClientState.Where(x => x.Id == result.Id).ToList();
                foreach (var ent in entsToDespawn)
                {
                    CurrentClientState.Remove(ent);
                }
            }
            else if (result.GetType() == typeof (MobPacket))
            {
                var mob = result as MobPacket;
                if (CurrentClientState.FirstOrDefault(x => x.Id == result.Id) is MobPacket previousState)
                {
                    var previousIndex = CurrentClientState.IndexOf(previousState);
                    if (mob.ActionType == EntityActionType.SET_POSITION)
                    {
                        previousState.X = mob.X;
                        previousState.Y = mob.Y;
                        previousState.Z = mob.Z;
                        previousState.Angle = mob.Angle;
                    }
                    else if (mob.ActionType == EntityActionType.FULL_SPAWN)
                    {
                        CurrentClientState.Remove(previousState);
                        CurrentClientState.Insert(previousIndex, result);
                    }
                    else if (mob is
                             {
                                 ActionType: EntityActionType.INTERACT, InteractionType: EntityInteractionType.DEATH
                             })
                    {
                        CurrentClientState.Remove(previousState);
                    }
                }

                if (mob.ActionType == EntityActionType.FULL_SPAWN)
                {
                    CurrentClientState.Insert(0, result);
                }
            }
            else if (result.GetType() == typeof (NpcTradePacket))
            {
                var npc = result as NpcTradePacket;
                if (CurrentClientState.FirstOrDefault(x => x.Id == result.Id) is NpcTradePacket previousState)
                {
                    var previousIndex = CurrentClientState.IndexOf(previousState);
                    if (npc.ActionType == EntityActionType.SET_POSITION)
                    {
                        previousState.X = npc.X;
                        previousState.Y = npc.Y;
                        previousState.Z = npc.Z;
                        previousState.Angle = npc.Angle;
                    }
                    else if (npc.ActionType == EntityActionType.FULL_SPAWN)
                    {
                        CurrentClientState.Remove(previousState);
                        CurrentClientState.Insert(previousIndex, result);
                    }
                    else if (npc is
                             {
                                 ActionType: EntityActionType.INTERACT, InteractionType: EntityInteractionType.DEATH
                             })
                    {
                        CurrentClientState.Remove(previousState);
                    }
                }

                if (npc.ActionType == EntityActionType.FULL_SPAWN)
                {
                    CurrentClientState.Insert(0, result);
                }
            }
        }

        CurrentEntityStateForClient.UpdateLayout();
    }

    private void LogListOnSelectionChanged (object sender, SelectionChangedEventArgs args)
    {
        try
        {
            if (args.AddedItems.Count < 1)
            {
                return;
            }

            var selected = args.AddedItems[0] as StoredPacket;
            CurrentContentBytes = selected.ContentBytes;

            IsFavorite.IsChecked = selected.Favorite;
            DefinedPacketsListBox.SelectedItem = null;
            PacketParts.Clear();
            LogListFullPackets.ScrollIntoView(selected);
            UpdateContentPreview(selected);
        }
        catch
        {
            IsFavorite.IsChecked = false;
        }
    }

    private void FullPacketsLog_MenuItem_OnClick (object sender, RoutedEventArgs e)
    {
        CopySelectedRowContent(LogListFullPackets);
    }

    private void CopySelectedRowContent (ListView listView)
    {
        var selectedRow = (StoredPacket) listView.SelectedItem;
        var text =
            $"{Convert.ToHexString(selectedRow.ContentBytes)}";
        Clipboard.SetText(text);
    }

    private void FavoriteToggleButton_OnChecked (object sender, RoutedEventArgs e)
    {
        var logList = LogListFullPackets;
        if (logList.SelectedItem is null)
        {
            return;
        }

        var item = (StoredPacket) logList.SelectedItem;
        item.Favorite = true;
        UpdateStoredPacket(item);
    }

    private void FavoriteToggleButton_OnUnchecked (object sender, RoutedEventArgs e)
    {
        var logList = LogListFullPackets;
        if (logList.SelectedItem is null)
        {
            return;
        }

        var item = (StoredPacket) logList.SelectedItem;
        item.Favorite = false;
        UpdateStoredPacket(item);
    }

    private void ShowFavoritesOnlyToggleButton_OnChecked (object sender, RoutedEventArgs e)
    {
        if (ShowFavoritesOnly)
        {
            return;
        }

        ShowFavoritesOnly = true;
        ScrollIntoViewIfSelectionExists();
    }

    private void UpdateStoredPacket (StoredPacket storedPacket)
    {
        PacketCollection.Update(storedPacket);
    }

    private void ShowFavoritesOnlyToggleButton_OnUnchecked (object sender, RoutedEventArgs e)
    {
        ShowFavoritesOnly = false;
        ScrollIntoViewIfSelectionExists();
    }

    private void HideUninteresting_OnChecked (object sender, RoutedEventArgs e)
    {
        if (HideUninteresting)
        {
            return;
        }

        HideUninteresting = true;
        ScrollIntoViewIfSelectionExists();
    }

    private void HideUninteresting_OnUnchecked (object sender, RoutedEventArgs e)
    {
        HideUninteresting = false;
        ScrollIntoViewIfSelectionExists();
    }

    private void ListenerEnabled_OnChecked (object sender, RoutedEventArgs e)
    {
        if (ListenerEnabled)
        {
            return;
        }

        ListenerEnabled = true;
    }

    private void ListenerEnabled_OnUnchecked (object sender, RoutedEventArgs e)
    {
        ListenerEnabled = false;
    }

    private void ScrollIntoViewIfSelectionExists ()
    {
        CollectionViewSource.GetDefaultView(LogListFullPackets.ItemsSource).Refresh();
        if (LogListFullPackets.Items.Count < 1)
        {
            return;
        }

        var selected = LogListFullPackets.SelectedItem ?? LogListFullPackets.Items[^1];
        if (!LogListFullPackets.Items.PassesFilter(selected))
        {
            // should only happen when switching to a more restricted view with filtered out item selected
            selected = LogListFullPackets.Items[^1];
        }

        LogListFullPackets.SelectedItem = selected;
        LogListFullPackets.ScrollIntoView(selected);
    }

    private void LoadEnums ()
    {
        var enumFiles = Directory.EnumerateFiles(PacketDefinitionPath, $"*{EnumExtension}");
        foreach (var enumFile in enumFiles)
        {
            var enumName = Path.GetFileNameWithoutExtension(enumFile);
            DefinedEnums.Add(enumName, new Dictionary<int, string>());
            DefinedEnumNames.Add(enumName);
            var enumEntryLines = File.ReadAllLines(enumFile).Select(x =>
                x.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToList();
            foreach (var enumEntryLine in enumEntryLines)
            {
                var id = int.Parse(enumEntryLine[0]);
                var name = enumEntryLine[1];
                DefinedEnums[enumName].Add(id, name);
            }
        }
    }

    private void LoadPacketDefinitions ()
    {
        DefinedPacketsListBox.ItemsSource = PacketDefinitions;
        if (!Path.Exists(PacketDefinitionPath))
        {
            MessageBox.Show($"Cannot load packet definitions.\nDirectory not found: {PacketDefinitionPath}");
            return;
        }

        var definitionFiles = Directory.EnumerateFiles(PacketDefinitionPath, $"*{PacketDefinitionExtension}");
        foreach (var definitionFile in definitionFiles)
        {
            PacketDefinitions.Add(new PacketDefinition
            {
                Name = Path.GetFileNameWithoutExtension(definitionFile),
                FilePath = definitionFile
            });
        }

        SubpacketsListBox.ItemsSource = Subpackets;

        var subpacketFiles = Directory.EnumerateFiles(PacketDefinitionPath, $"*{ExportedPartExtension}");
        foreach (var subpacketFile in subpacketFiles)
        {
            Subpackets.Add(new Subpacket
            {
                Name = Path.GetFileNameWithoutExtension(subpacketFile),
                FilePath = subpacketFile
            });
        }

        if (Subpackets.Any())
        {
            SubpacketsListBox.SelectedItem = Subpackets.First();
        }
    }

    private void SynchronizeScrollValues (object source)
    {
        var scrollViewer = (ScrollViewer) source;
        if (scrollViewer != PacketVisualizerLineNumbersAndValuesScrollViewer &&
            Math.Abs(PacketVisualizerLineNumbersAndValuesScrollViewer.VerticalOffset - scrollViewer.VerticalOffset) >
            double.Epsilon)
        {
            PacketVisualizerLineNumbersAndValuesScrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset);
        }

        if (scrollViewer != PacketVisualizerDefinedPacketValuesScrollViewer &&
            Math.Abs(PacketVisualizerDefinedPacketValuesScrollViewer.VerticalOffset - scrollViewer.VerticalOffset) >
            double.Epsilon)
        {
            PacketVisualizerDefinedPacketValuesScrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset);
        }

        if (scrollViewer != PacketDisplayScrollViewer &&
            Math.Abs(PacketDisplayScrollViewer!.VerticalOffset - scrollViewer.VerticalOffset) > double.Epsilon)
        {
            PacketDisplayScrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset);
        }
    }

    private void PacketVisualizerControlHandlePartSelection (object sender, KeyEventArgs e)
    {
        if (e.Key != Key.S && e.Key != Key.E && e.Key != Key.Escape)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            ClearSelection();
        }

        var caretPosition = PacketVisualizerControl.CaretPosition;
        if (caretPosition is null)
        {
            ClearSelection();
            LastVerticalOffset = 0;
            return;
        }

        LastCaretOffset = PacketVisualizerControl.Document.ContentStart.GetOffsetToPosition(caretPosition);
        LastVerticalOffset = PacketVisualizerControl.VerticalOffset;

        if (e.Key == Key.S)
        {
            StartTextPointer = caretPosition;
        }

        if (e.Key == Key.E)
        {
            EndTextPointer = caretPosition;
        }

        CreateFlowDocumentWithHighlights();
    }

    private void ClearSelection ()
    {
        StartTextPointer = null;
        EndTextPointer = null;
        LastCaretOffset = null;
    }

    private void UpdateScrolling ()
    {
        if (LastCaretOffset.HasValue)
        {
            var newCaretPosition = LastCaretOffset.Value <= 2
                ? PacketVisualizerControl.Document.ContentStart.GetLineStartPosition(0)
                : PacketVisualizerControl.Document.ContentStart.GetPositionAtOffset(LastCaretOffset.Value);
            if (newCaretPosition is not null)
            {
                PacketVisualizerControl.CaretPosition = newCaretPosition;
            }
        }

        PacketVisualizerControl.ScrollToVerticalOffset(LastVerticalOffset);
        PacketVisualizerLineNumbersAndValuesScrollViewer.ScrollToVerticalOffset(LastVerticalOffset);
        PacketVisualizerDefinedPacketValuesScrollViewer.ScrollToVerticalOffset(LastVerticalOffset);
    }

    private void PacketVisualizerControlAddPacketPart (object sender, KeyEventArgs e)
    {
        if (e.KeyboardDevice.Modifiers != ModifierKeys.Control || e.Key != Key.D)
        {
            return;
        }

        if (StartTextPointer is null || EndTextPointer is null)
        {
            return;
        }

        var color = new Color
        {
            A = 150,
            R = (byte) Random.Shared.Next(0, 255),
            G = (byte) Random.Shared.Next(0, 255),
            B = (byte) Random.Shared.Next(0, 255)
        };

        var dialog = new CreatePacketPartDefinitionDialog(color, DefinedEnumNames)
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true)
        {
            var name = dialog.Name;
            color = dialog.Color;
            var type = dialog.PacketPartType ?? PacketPartType.BITS;
            var start = StartTextPointer;
            var end = EndTextPointer;
            var enumName = dialog.EnumName;
            var lengthFromPrevious = dialog.PacketPartType == PacketPartType.STRING && dialog.LengthFromPreviousField;
            AddNewDefinedPacketPart(CreatePacketPart(name, enumName, type, lengthFromPrevious, start, end,
                new SolidColorBrush(color)));
        }
    }

    public void CreateFlowDocumentWithHighlights (bool keepSelection = true, bool firstUpdateOnLoad = false)
    {
        PacketVisualizerLineNumbersAndValues.Inlines.Clear();
        PacketVisualizerDefinedPacketValues.Inlines.Clear();
        if (CurrentContentBytes is null)
        {
            return;
        }

        CurrentContentBitStream = new BitStream(CurrentContentBytes);
        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Hack"),
            FontSize = 14,
            LineHeight = 16,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            PageWidth = 80,
            TextAlignment = TextAlignment.Right,
            PagePadding = new Thickness(10, 4, 0, 4)
        };
        var paragraph = new Paragraph
        {
            Margin = new Thickness(0)
        };
        var selectionBits = new List<Bit>();
        var sb = new StringBuilder();
        var selectionStartOffset = StartTextPointer?.GetCharOffset();
        var selectionEndOffset = EndTextPointer?.GetCharOffset();

        var actualStart = selectionStartOffset;
        var actualEnd = selectionEndOffset;
        if (actualStart > actualEnd)
        {
            actualEnd = selectionStartOffset;
            actualStart = selectionEndOffset;
        }

        PacketContentBits = CurrentContentBitStream.ReadBits(int.MaxValue);
        var wasInSelection = false;
        PacketPart? previousPacketPart = null;
        Brush? textBrush = null;

        var linesSb = new StringBuilder();
        var valueDisplayDict = new Dictionary<int, PacketPart>();
        var lineByte = 0;

        for (var i = 0; i < PacketContentBits.Length; i++)
        {
            var currentPacketPart = PacketParts.FirstOrDefault(x => x.BitOffset <= i && x.BitOffsetEnd > i);
            var inSelection = keepSelection && actualStart <= i && actualEnd > i;

            var textBlockChanged = (inSelection && !wasInSelection) || (wasInSelection && !inSelection) ||
                                   (currentPacketPart != null && currentPacketPart != previousPacketPart) ||
                                   (currentPacketPart == null && previousPacketPart != null);
            if (inSelection)
            {
                selectionBits.Add(PacketContentBits[i]);
            }

            if (textBlockChanged)
            {
                var newTextBrush = inSelection ? SelectionBrush :
                    currentPacketPart is null ? null : new SolidColorBrush
                    {
                        Color = new Color
                        {
                            R = currentPacketPart.HighlightColorR,
                            G = currentPacketPart.HighlightColorG,
                            B = currentPacketPart.HighlightColorB,
                            A = currentPacketPart.HighlightColorA
                        }
                    };
                if (textBrush is null)
                {
                    if (sb.Length > 0)
                    {
                        paragraph.Inlines.Add(sb.ToString());
                    }
                }
                else
                {
                    paragraph.Inlines.Add(new Run(sb.ToString())
                    {
                        Background = textBrush
                    });
                }

                sb.Clear();
                textBrush = newTextBrush;

                if (currentPacketPart is not null && previousPacketPart != currentPacketPart)
                {
                    valueDisplayDict.Add(i, currentPacketPart);
                }

                previousPacketPart = currentPacketPart;
            }

            wasInSelection = inSelection;
            var bit = PacketContentBits[i].AsInt();
            sb.Append(bit);
            lineByte <<= 1;
            lineByte += bit;

            if (i % 8 == 7)
            {
                // flip bits
                lineByte = (int) ((((ulong) lineByte * 0x0202020202UL) & 0x010884422010UL) % 1023);
                linesSb.Append($"[{lineByte:X2} ")
                    .Append($"{lineByte}".PadLeft(3, ' ')).Append("] ");
                if (i < PacketContentBits.Length - 1)
                {
                    linesSb.AppendLine($"{i / 8} ".PadLeft(5, ' '));
                }
                else
                {
                    linesSb.Append($"{i / 8} ".PadLeft(5, ' '));
                }

                lineByte = 0;
            }
        }

        if (sb.Length > 0)
        {
            if (textBrush is null)
            {
                paragraph.Inlines.Add(sb.ToString());
            }
            else
            {
                paragraph.Inlines.Add(new Run(sb.ToString())
                {
                    Background = textBrush
                });
            }
        }

        PacketVisualizerLineNumbersAndValues.Text = linesSb.ToString();
        var previousLineBreakLineIndex = 0;

        for (var i = 0; i < PacketContentBits.Length; i++)
        {
            if (!valueDisplayDict.TryGetValue(i, out var part))
            {
                continue;
            }

            var lineToReach = part.BitOffset / 8;

            if (lineToReach > previousLineBreakLineIndex)
            {
                PacketVisualizerDefinedPacketValues.Inlines.Add(new string('\n',
                    lineToReach - previousLineBreakLineIndex));
            }

            previousLineBreakLineIndex = lineToReach;

            AddPacketPartInlines(PacketVisualizerDefinedPacketValues.Inlines, part);
        }

        CurrentContentBitStream.Seek(0, 0);
        if (previousLineBreakLineIndex < PacketContentBits.Length / 8 - 1)
        {
            PacketVisualizerDefinedPacketValues.Inlines.Add(new string('\n',
                PacketContentBits.Length / 8 - previousLineBreakLineIndex - 2));
        }

        document.Blocks.Add(paragraph);

        if (firstUpdateOnLoad)
        {
            PacketReadableDisplayText.Inlines.Clear();
            PacketReadableDisplayText.Inlines.Add(Convert.ToHexString(BitStream.BitArrayToBytes(PacketContentBits)) +
                                                  "\n");
            var toShift = PacketContentBits.ToList();
            for (var i = 0; i < 8; i++)
            {
                var shiftedBytes = BitStream.BitArrayToBytes(toShift.ToArray());
                var shiftedChars = Win1251.GetString(shiftedBytes).ToCharArray();
                var shiftedString = new string(shiftedChars.Select(GetVisibleChar).ToArray());
                PacketReadableDisplayText.Inlines.Add(new Run($"\n[{i}] {shiftedString}")
                {
                    FontSize = 14
                });
                toShift.RemoveAt(0);
            }
        }

        PacketVisualizerControl.Document = document;
        UpdateSelectedValueDisplay(selectionBits);
        UpdateScrolling();
    }

    private void PacketVisualizerControl_OnSelectionChanged (object o, RoutedEventArgs e)
    {
        e.Handled = true;
    }

    private PacketPart CreatePacketPart (string name, string? enumName, PacketPartType packetPartType,
        bool lengthFromPrevious, TextPointer start, TextPointer end, Brush highlightColor)
    {
        var bitOffsetStart = start.GetCharOffset();
        var bitOffsetEnd = end.GetCharOffset();
        var actualStart = Math.Min(bitOffsetStart, bitOffsetEnd);

        var bitLength = Math.Abs(bitOffsetEnd - bitOffsetStart);
        var color = ((SolidColorBrush) highlightColor).Color;
        var part = new PacketPart(bitLength, name, enumName, lengthFromPrevious, packetPartType,
            actualStart, Array.Empty<Bit>(), color.R, color.G, color.B, color.A);
        PacketPart.UpdatePacketPartValues(new List<PacketPart> { part }, CurrentContentBitStream, actualStart);
        return part;
    }

    private void UpdateDefinedPackets ()
    {
        DefinedPacketPartsControl.Document.Blocks.Clear();
        var toSort = PacketParts.ToList();
        toSort.Sort((a, b) => a.BitOffset.CompareTo(b.BitOffset));
        PacketParts.Clear();
        toSort.ForEach(x => PacketParts.Add(x));
        foreach (var part in PacketParts)
        {
            var paragraph = new Paragraph
            {
                BreakPageBefore = false,
                Margin = new Thickness(4)
            };
            if (!string.IsNullOrEmpty(part.Comment) && part.Comment != PacketPart.UndefinedFieldValue)
            {
                var lineWidth = DefinedPacketPartsControl.ActualWidth < 50
                    ? 120
                    : (int) (DefinedPacketPartsControl.ActualWidth / 9);
                var comment = $" {part.Comment} ";
                var paddingLength = (lineWidth - comment.Length) / 2;
                var padding = new string('=', paddingLength);
                var commentColor = part.Comment == "NEXT PACKET" ? Brushes.SlateGray : Brushes.Honeydew;
                paragraph.Inlines.Add(new Run(
                    $"{padding}{comment}{padding}\n\n")
                {
                    Background = commentColor
                });
            }

            AddPacketPartInlines(paragraph.Inlines, part);

            DefinedPacketPartsControl.Document.Blocks.Add(paragraph);
        }
    }

    private void AddPacketPartInlines (InlineCollection inlineCollection, PacketPart part)
    {
        var color = new Color
        {
            R = part.HighlightColorR,
            G = part.HighlightColorG,
            B = part.HighlightColorB,
            A = part.HighlightColorA
        };
        inlineCollection.Add(new Run($"{part.Name}")
        {
            Background = new SolidColorBrush { Color = color }
        });
        inlineCollection.Add(": ");
        var valueStr = part.GetDisplayTextForValueType();

        if (part.EnumName is not null)
        {
            var enumValue = part.DisplayText.EnumValue?.ToUpper() ?? string.Empty;
            inlineCollection.Add(new Run(enumValue) { FontWeight = FontWeights.Bold });
            var enumName = SnakeCaseToCamelCase(part.EnumName);
            inlineCollection.Add(new Run($" ({enumName}::{enumValue} = {valueStr})")
                { Foreground = Brushes.Gray, FontSize = 12 });
        }
        else
        {
            var valueStrSplit = valueStr
                .Split('=', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
            if (part.ActualLongValue is not null)
            {
                if (part.Name is PacketPartNames.Level or PacketPartNames.CurrentHP or PacketPartNames.MaxHP)
                {
                    inlineCollection.Add(new Run($"{part.ActualLongValue.ToString()}")
                        { FontWeight = FontWeights.Bold });
                }
                else
                {
                    var actualValueStr = part.ActualLongValue.ToString();
                    var actualValueStrHex = $"{part.ActualLongValue:X}";
                    var hexPaddingLength = actualValueStrHex.Length + actualValueStrHex.Length % 2;
                    inlineCollection.Add(new Run($"0x{actualValueStrHex.PadLeft(hexPaddingLength, '0')}")
                        { FontWeight = FontWeights.Bold });
                    inlineCollection.Add(new Run($" = {actualValueStr}") { Foreground = Brushes.Gray, FontSize = 12 });
                }
            }
            else if (valueStrSplit.Count > 1)
            {
                // hex = dec, like 0x17B0 = 6064
                inlineCollection.Add(new Run(valueStrSplit[0]) { FontWeight = FontWeights.Bold });
                inlineCollection.Add(new Run($" = {valueStrSplit[1]}") { Foreground = Brushes.Gray, FontSize = 12 });
            }

            else
            {
                var valueTypeStr = part.PacketPartType == PacketPartType.BITS ? "0b" :
                    part.PacketPartType == PacketPartType.BYTES ? "0x" : string.Empty;
                inlineCollection.Add(new Run(valueTypeStr + valueStr) { FontWeight = FontWeights.Bold });
            }
        }

        inlineCollection.Add(
            new Run(
                $" [{Enum.GetName(part.PacketPartType) ?? string.Empty}] [({part.BitOffset / 8}, {part.BitOffset % 8}) to ({part.BitOffsetEnd / 8}, {part.BitOffsetEnd % 8}), {part.BitLength} bits] ")
            {
                Foreground = Brushes.Gray, FontSize = 12
            });
    }

    private void AddNewDefinedPacketPartBulk (List<PacketPart> packetParts, bool updateLayout = true)
    {
        packetParts.ForEach(x => AddNewDefinedPacketPart(x, true));

        UpdateDefinedPackets();
        if (updateLayout)
        {
            CreateFlowDocumentWithHighlights(false);
            ClearSelection();
        }
    }

    private void AddNewDefinedPacketPart (PacketPart packetPart, bool isBulk = false)
    {
        var newPacketParts = new List<PacketPart>();
        foreach (var definedPacketPart in PacketParts)
        {
            if (packetPart.Overlaps(definedPacketPart))
            {
                // remove old one
                continue;
            }

            if (packetPart.ContainedWithin(definedPacketPart))
            {
                var newLengthStart = packetPart.BitOffset - definedPacketPart.BitOffset;
                // split old and make it: old_start new old_end
                var oldStart = definedPacketPart.GetPiece(definedPacketPart.BitOffset, newLengthStart,
                    definedPacketPart.Name + "_1");
                var newLengthEnd = packetPart.BitOffsetEnd - definedPacketPart.BitOffsetEnd;
                var oldEnd = definedPacketPart.GetPiece(packetPart.BitOffsetEnd - newLengthEnd,
                    newLengthEnd, definedPacketPart.Name + "_2");
                newPacketParts.Add(oldStart);
                newPacketParts.Add(oldEnd);
                continue;
            }

            if (packetPart.BitOffset <= definedPacketPart.BitOffset &&
                packetPart.BitLength < definedPacketPart.BitLength)
            {
                // leave a chunk of old when new part intersects the beginning of old
                var oldChunk = definedPacketPart.GetPiece(packetPart.BitOffsetEnd,
                    definedPacketPart.BitOffsetEnd - packetPart.BitOffsetEnd);
                newPacketParts.Add(oldChunk);
                continue;
            }

            if (packetPart.BitOffset > definedPacketPart.BitOffset &&
                packetPart.BitOffset < definedPacketPart.BitOffsetEnd &&
                packetPart.BitOffsetEnd >= definedPacketPart.BitOffsetEnd)
            {
                // leave a chunk of old when new part intersects the end of old
                var oldChunk = definedPacketPart.GetPiece(definedPacketPart.BitOffset,
                    packetPart.BitOffset - definedPacketPart.BitOffset);
                newPacketParts.Add(oldChunk);
                continue;
            }

            newPacketParts.Add(definedPacketPart);
        }

        newPacketParts.Add(packetPart);
        newPacketParts.Sort((a, b) => a.BitOffset.CompareTo(b.BitOffset));
        PacketParts.Clear();
        newPacketParts.ForEach(x => PacketParts.Add(x));
        if (!isBulk)
        {
            UpdateDefinedPackets();
            CreateFlowDocumentWithHighlights(false);
            ClearSelection();
        }
    }

    private void UpdateSelectedValueDisplay (List<Bit> bits)
    {
        if (!bits.Any())
        {
            PacketSelectedValueDisplay.Text = "Select bits to show value preview\n\n" +
                                              "Key mappings for binary view:\n\n" +
                                              "S\t - begin selection\n" +
                                              "E\t - end selection\n" +
                                              "Esc\t - clear selection\n" +
                                              "Ctrl-D\t - define new packet part\n\n" +
                                              "To search, input string as is or a number with corresponding basis (0xAB, 0d13, 0b101)";
            return;
        }

        var displayText = PacketPart.GetValueDisplayText(bits, null);
        var sb = new StringBuilder();
        sb.AppendLine($"Bits:\t {displayText.Bits} ({displayText.Bits.Length})");
        sb.AppendLine($"Bytes:\t {displayText.Bytes}");
        sb.AppendLine($"Text:\t {displayText.Text}");
        sb.AppendLine($"Int64:\t {displayText.Long}");
        sb.AppendLine($"UInt64: {displayText.Ulong}");
        if (displayText.CoordsClient is not null)
        {
            sb.AppendLine($"CLI coords:\t {displayText.CoordsClient}");
        }

        if (displayText.CoordsServer is not null)
        {
            sb.AppendLine($"SRV coords:\t {displayText.CoordsServer}");
        }

        PacketSelectedValueDisplay.Text = sb.ToString();
    }

    public static char GetVisibleChar (char c)
    {
        return (c >= 0x20 && c <= 0x7E) || c is >= 'А' and <= 'я' ? c : '·';
    }

    private void CreateNewPacketDefinitionButton_OnClick (object sender, RoutedEventArgs e)
    {
        CreatePacketDefinition();
    }

    private void CreatePacketDefinition ()
    {
        var dialog = new SaveNewPacketDefinitionDialog
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true)
        {
            var path = Path.Combine(PacketDefinitionPath, dialog.Name + PacketDefinitionExtension);
            var definition = new PacketDefinition
            {
                Name = dialog.Name,
                FilePath = path
            };

            PacketDefinitions.Add(definition);
            SavePacketDefinition(dialog.Name, 0, 0);
            DefinedPacketsListBox.SelectedItem = definition;
        }
    }

    private void SavePacketDefinition_OnClick (object sender, RoutedEventArgs e)
    {
        SaveSelectedPacketDefinition();
    }

    private void SaveSelectedPacketDefinition ()
    {
        if (DefinedPacketsListBox.SelectedItem is not PacketDefinition selectedDefinition)
        {
            CreatePacketDefinition();
            selectedDefinition = (PacketDefinition?) DefinedPacketsListBox.SelectedItem;
        }

        if (selectedDefinition is null)
        {
            return;
        }

        SavePacketDefinition(selectedDefinition.Name, 0, PacketContentBits.Length);
    }

    private void SavePacketDefinition (string definitionName, int startBitOffset, int endBitOffset,
        bool exportedPart = false)
    {
        endBitOffset = Math.Min(endBitOffset, PacketContentBits.Length);
        var fileContentsSb = new StringBuilder();
        var currentIndex = startBitOffset;
        var nextPacketPartIndex =
            PacketParts.ToList().FindIndex(x => x.BitOffset >= startBitOffset);
        var bitOffsetChangeFromVariableStrings = 0;
        while (currentIndex < endBitOffset)
        {
            var nextPacketPart = nextPacketPartIndex == -1
                ? null
                : PacketParts.Count > nextPacketPartIndex
                    ? PacketParts[nextPacketPartIndex]
                    : null;

            var name = PacketPart.UndefinedFieldValue;
            var partType = PacketPartType.BITS;
            var enumName = PacketPart.UndefinedFieldValue;
            var colorR = 100;
            var colorG = 100;
            var colorB = 100;
            var colorA = 100;
            Bit[] bits;
            var startPosition = currentIndex - bitOffsetChangeFromVariableStrings;
            var lengthFromPrevious = false;
            if (nextPacketPart is null)
            {
                // only undef until end of packet
                bits = PacketContentBits[currentIndex..endBitOffset];
            }
            else
            {
                var nextPartStartIndex = nextPacketPart.BitOffset;
                nextPartStartIndex = Math.Min(nextPartStartIndex, endBitOffset);
                if (currentIndex < nextPartStartIndex)
                {
                    // undef between packet parts
                    bits = PacketContentBits[currentIndex..nextPartStartIndex];
                    currentIndex = nextPartStartIndex;
                }
                else
                {
                    var nextPartEndIndex = nextPacketPart.BitOffsetEnd;
                    if (lengthFromPrevious)
                    {
                        // previous part was a variable string, we treat everything after it as it its length was 0
                    }

                    nextPartEndIndex = Math.Min(nextPartEndIndex, endBitOffset);
                    if (nextPartStartIndex > nextPartEndIndex)
                    {
                        (nextPartEndIndex, nextPartStartIndex) = (nextPartStartIndex, nextPartEndIndex);
                    }

                    bits = PacketContentBits[nextPartStartIndex..nextPartEndIndex];
                    partType = nextPacketPart.PacketPartType;
                    name = nextPacketPart.Name;
                    enumName = nextPacketPart.EnumName ?? PacketPart.UndefinedFieldValue;
                    colorR = nextPacketPart.HighlightColorR;
                    colorG = nextPacketPart.HighlightColorG;
                    colorB = nextPacketPart.HighlightColorB;
                    colorA = nextPacketPart.HighlightColorA;
                    lengthFromPrevious = nextPacketPart.LengthFromPreviousField;
                    currentIndex = nextPartEndIndex;
                    nextPacketPartIndex++;
                }
            }

            if (exportedPart)
            {
                startPosition -= startBitOffset;
            }

            string lengthText;

            if (lengthFromPrevious)
            {
                lengthText = PacketPart.LengthFromPreviousFieldValue;
                bitOffsetChangeFromVariableStrings += bits.Length;
            }
            else
            {
                lengthText = bits.Length.ToString();
            }

            fileContentsSb.AppendLine(
                $"{name}\t{Enum.GetName(partType)}\t{startPosition}\t{lengthText}\t{enumName}\t{colorR}\t{colorG}" +
                $"\t{colorB}\t{colorA}\t{string.Join(null, bits.Reverse().Select(x => x.AsInt()))}");

            if (nextPacketPart is null)
            {
                break;
            }
        }

        var fileName = Path.Combine(PacketDefinitionPath,
            definitionName + (exportedPart ? ExportedPartExtension : PacketDefinitionExtension));

        File.WriteAllText(fileName, fileContentsSb.ToString());
    }

    private void DefinedPacketsListBox_OnSelectionChanged (object sender, SelectionChangedEventArgs e)
    {
        if (DefinedPacketsListBox.SelectedItem is not PacketDefinition packetDefinition)
        {
            return;
        }

        var parts = packetDefinition.LoadFromFile(CurrentContentBitStream, 0);
        PacketParts.Clear();
        parts.ForEach(x => PacketParts.Add(x));
        LastVerticalOffset = PacketDisplayScrollViewer?.VerticalOffset ?? 0;
        UpdateDefinedPackets();
        CreateFlowDocumentWithHighlights();
    }

    private void ExportSubpacket_OnClick (object sender, RoutedEventArgs e)
    {
        var dialog = new ExportSubpacketDialog
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var name = dialog.Name;
        var startOffset = dialog.StartOffset;
        var startBit = dialog.StartBit;
        var endOffset = dialog.EndOffset;
        var endBit = dialog.EndBit;

        SavePacketDefinition(name, startOffset * 8 + startBit, endOffset * 8 + endBit, true);
        if (Subpackets.All(x => x.Name != name))
        {
            Subpackets.Add(new Subpacket
            {
                Name = name,
                FilePath = Path.Combine(PacketDefinitionPath, name + ExportedPartExtension)
            });
        }
    }

    private void DeletePacketPartInCurrentDefinition_OnClick (object sender, RoutedEventArgs e)
    {
        if (PacketPartsInDefinitionListBox.SelectedItems.Count == 0)
        {
            return;
        }

        if (MessageBox.Show("Delete selected parts?", "Delete selected parts?",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) !=
            MessageBoxResult.Yes)
        {
            return;
        }

        var listToRemove = PacketPartsInDefinitionListBox.SelectedItems.Cast<PacketPart>().ToList();
        PacketPartsInDefinitionListBox.UnselectAll();

        foreach (var selectedItem in listToRemove)
        {
            PacketParts.Remove(selectedItem);
        }

        UpdateDefinedPackets();
        CreateFlowDocumentWithHighlights();
    }

    private void ImportFromSubpacket_OnClick (object sender, RoutedEventArgs e)
    {
        if (SubpacketsListBox.SelectedItem is not Subpacket subpacket)
        {
            return;
        }

        var dialog = new ImportFromSubpacketDialog
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var startOffset = dialog.StartOffset;
        var startBit = dialog.StartBit;

        var parts = subpacket.LoadFromFile(CurrentContentBitStream, startOffset * 8 + startBit);

        AddNewDefinedPacketPartBulk(parts);
    }

    private void DeletePacketDefinition_OnClick (object sender, RoutedEventArgs e)
    {
        if (DefinedPacketsListBox.SelectedItem is not PacketDefinition packetDefinition)
        {
            return;
        }

        if (MessageBox.Show("Delete selected definition?", "Delete selected definition?",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) ==
            MessageBoxResult.Yes)
        {
            DeletePacketDefinition(packetDefinition);
        }
    }

    private void DeletePacketDefinition (PacketDefinition packetDefinition)
    {
        PacketDefinitions.Remove(packetDefinition);
        File.Delete(packetDefinition.FilePath);
        if (PacketDefinitions.Any())
        {
            DefinedPacketsListBox.SelectedItem = PacketDefinitions.First();
        }
    }

    public static string SnakeCaseToCamelCase (string input)
    {
        return input
            .Split(new[] { "_" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => char.ToUpperInvariant(s[0]) + s.Substring(1, s.Length - 1))
            .Aggregate(string.Empty, (s1, s2) => s1 + s2);
    }

    private void PacketPartsInDefinitionListBox_OnSelectionChanged (object sender, SelectionChangedEventArgs e)
    {
    }

    private void EditPacketPart_OnClick (object sender, RoutedEventArgs e)
    {
    }

    private void SearchInPacketTextBox_OnTextChanged (object sender, TextChangedEventArgs e)
    {
        StartTextPointer = null;
        EndTextPointer = null;
        SearchText();
    }

    private static TextPointer MoveByCharOffset (TextPointer textPointer, int countChars)
    {
        var targetOffset = textPointer.GetCharOffset() + countChars;
        while (textPointer.GetCharOffset() != targetOffset)
        {
            if (countChars > 0)
            {
                textPointer = textPointer.GetPositionAtOffset(1);
            }
            else if (countChars < 0)
            {
                textPointer = textPointer.GetPositionAtOffset(-1);
            }
            else
            {
                return textPointer;
            }
        }

        return textPointer;
    }

    private void SearchText ()
    {
        var text = SearchInPacketTextBox.Text;
        if (text.Length == 0)
        {
            return;
        }

        if (text.StartsWith("0"))
        {
            // integers, 0x 0d 0b
            if (text.Length < 3)
            {
                return;
            }

            var intBase = text[1] == 'x' ? 16 : text[1] == 'd' ? 10 : text[1] == 'b' ? 2 : 0;
            if (intBase == 0 || text[2..].Any(x => !char.IsAsciiHexDigit(x)))
            {
                return;
            }

            try
            {
                var value = Convert.ToInt64(text[2..], intBase);
                var charPosition = 0;
                if (EndTextPointer is not null)
                {
                    charPosition = EndTextPointer.GetCharOffset() + 1;
                }

                CurrentContentBitStream.Seek(charPosition / 8, charPosition % 8);
                var bitsToRead = GetMinimumBitsToEncodeValue(value);
                var startOffset = -1;
                var startBit = 0;
                while (CurrentContentBitStream.ValidPosition)
                {
                    var test = CurrentContentBitStream.ReadInt64(bitsToRead);
                    if (!CurrentContentBitStream.ValidPosition)
                    {
                        break;
                    }

                    CurrentContentBitStream.SeekBack(bitsToRead);

                    if (test == value)
                    {
                        startOffset = (int) CurrentContentBitStream.Offset;
                        startBit = CurrentContentBitStream.Bit;
                        break;
                    }

                    CurrentContentBitStream.ReadBit();
                }

                if (startOffset != -1)
                {
                    // found something
                    var range = new TextRange(PacketVisualizerControl.Document.ContentStart,
                        PacketVisualizerControl.Document.ContentEnd);

                    var startOffsetPointer = MoveByCharOffset(range.Start, startOffset * 8 + startBit);

                    var endOffsetPointer = MoveByCharOffset(startOffsetPointer, bitsToRead);
                    StartTextPointer = endOffsetPointer;
                    EndTextPointer = startOffsetPointer;

                    CreateFlowDocumentWithHighlights();
                }
                else
                {
                    StartTextPointer = null;
                    EndTextPointer = null;
                    CreateFlowDocumentWithHighlights();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
        else
        {
            // assuming win1251 string
            var bytesToFind = Win1251.GetBytes(text);
            var bitLength = bytesToFind.Length * 8;
            try
            {
                var charPosition = 0;
                if (EndTextPointer is not null)
                {
                    charPosition = EndTextPointer.GetCharOffset() + 1;
                }

                CurrentContentBitStream.Seek(charPosition / 8, charPosition % 8);
                var startOffset = -1;
                var startBit = 0;
                while (CurrentContentBitStream.ValidPosition)
                {
                    var test = CurrentContentBitStream.ReadBytes(bitLength);
                    if (!CurrentContentBitStream.ValidPosition)
                    {
                        break;
                    }

                    CurrentContentBitStream.SeekBack(bitLength);

                    if (test.HasEqualElementsAs(bytesToFind))
                    {
                        startOffset = (int) CurrentContentBitStream.Offset;
                        startBit = CurrentContentBitStream.Bit;
                        break;
                    }

                    CurrentContentBitStream.ReadBit();
                }

                if (startOffset != -1)
                {
                    // found something
                    var range = new TextRange(PacketVisualizerControl.Document.ContentStart,
                        PacketVisualizerControl.Document.ContentEnd);
                    var endOffset = startOffset + bytesToFind.Length;
                    var endBit = startBit;

                    var startOffsetPointer = MoveByCharOffset(range.Start, startOffset * 8 + startBit);

                    var endOffsetPointer = MoveByCharOffset(startOffsetPointer, bitLength);
                    StartTextPointer = endOffsetPointer;
                    EndTextPointer = startOffsetPointer;

                    CreateFlowDocumentWithHighlights();
                    PacketVisualizerControl.ScrollToVerticalOffset(16 * startOffset);
                }
                else
                {
                    StartTextPointer = null;
                    EndTextPointer = null;
                    CreateFlowDocumentWithHighlights();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
    }

    private int GetMinimumBitsToEncodeValue (long value)
    {
        if (value == 0)
        {
            return 1;
        }

        var test = value;
        var bitCount = 0;
        while (test > 0)
        {
            test >>= 1;
            bitCount += 1;
        }

        return bitCount;
    }

    private void SearchInPacketTextBox_OnKeyUp (object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        SearchText();
    }

    public static void RegisterBsonMapperForBrush ()
    {
        BsonMapper.Global.RegisterType<SolidColorBrush>(
            brush => Dispatcher.CurrentDispatcher.Invoke(() =>
                $"{brush.Color.R},{brush.Color.G},{brush.Color.B},{brush.Color.A}"),
            bson =>
            {
                var colors = ((string) bson).Split(',').Select(byte.Parse).ToArray();
                return new SolidColorBrush()
                {
                    Color = new Color
                    {
                        R = colors[0],
                        G = colors[1],
                        B = colors[2],
                        A = colors[3]
                    }
                };
            });
    }

    private void AddPacketButton_OnClick (object sender, RoutedEventArgs e)
    {
        var dialog = new AddPacketManuallyDialog()
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            var packets = dialog.ProcessedPackets;
            foreach (var packet in packets)
            {
                var rawData = new CapturedPacketRawData
                {
                    ArrivalTime = DateTime.Now,
                    DecodedBuffer = packet,
                    Buffer = packet,
                    WasProcessed = false,
                    Source = PacketSource.SERVER
                };
                PacketCapture.ProcessPacketRawDataForce(rawData, true);
            }
        }
    }

    private void ShowInUI_OnChecked (object sender, RoutedEventArgs e)
    {
        ShowNewInUI = true;
    }

    private void ShowInUI_OnUnchecked (object sender, RoutedEventArgs e)
    {
        ShowNewInUI = false;
    }

    private void ClearClientState_OnClick (object sender, RoutedEventArgs e)
    {
        CurrentClientState.Clear();
    }
}