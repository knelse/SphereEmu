using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BitStreams;
using LiteDB;
using PacketLogViewer.Dialogs;
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

    public static readonly Dictionary<string, Dictionary<int, string>> DefinedEnums = new();
    private readonly List<string> DefinedEnumNames = new();

    public PacketCapture? PacketCapture;

    private SphereMitmProxy? _mitmProxy;
    public static readonly ObservableCollection<PacketDefinition> PacketDefinitions = new();

    public static readonly ObservableCollection<PacketPart> PacketParts = new();
    public DispatcherTimer? SphereTimeUpdateTimer;
    private DispatcherTimer? _entityRadarRefreshTimer;
    public static readonly ObservableCollection<Subpacket> Subpackets = new();
    public static readonly ObservableCollection<PacketAnalyzeData> CurrentClientState = new();
    private HashSet<ObjectType>? ClientStateObjectTypeFilter;

    private double _radarClientX;
    private double _radarClientZ;
    private double _radarClientTurn;
    private bool _radarHasClientPosition;

    private TextPointer? EndTextPointer;
    private int? LastCaretOffset;
    private double LastVerticalOffset;
    private ScrollViewer? PacketDisplayScrollViewer;
    private TextPointer? StartTextPointer;

    private bool _initialized;

    static PacketLogViewerMainWindow()
    {
        AppConfig = new ConfigurationBuilder().AddJsonFile("appconfig.json").AddEnvironmentVariables().Build();
        PacketDatabase = new LiteDatabase(AppConfig.GetConnectionString("LiteDbPacketCollection"));
        PacketDefinitionPath = Path.Combine(AppConfig.GetSection("Settings").GetValue<string>("ClonedRepoPath"), "Sphere.PacketDefinitions");
        Directory.CreateDirectory(PacketDefinitionPath);
        PacketCollection = PacketDatabase.GetCollection<StoredPacket>("Packets");
    }

    public PacketLogViewerMainWindow()
    {
        InitializeComponent();
        RegisterBsonMapperForBrush();
        ApplyStartWindowDimensionsFromConfig();
        ApplyStartWindowPosition();

        var scrollViewerProperty =
            typeof(RichTextBox).GetProperty("ScrollViewer", BindingFlags.NonPublic | BindingFlags.Instance)!;

        Loaded += (_, _) =>
        {
            // Resolve internal RichTextBox ScrollViewer after visual tree is ready.
            PacketDisplayScrollViewer = (ScrollViewer)scrollViewerProperty.GetValue(MainView.PacketVisualizerPanel.PacketVisualizerControl)!;

            PacketDisplayScrollViewer!.ScrollChanged += (sender, _) => { SynchronizeScrollValues(sender); };
            MainView.PacketVisualizerPanel.PacketVisualizerDefinedPacketValuesScrollViewer.ScrollChanged += (sender, _) =>
            {
                SynchronizeScrollValues(sender);
            };
            MainView.PacketVisualizerPanel.PacketVisualizerLineNumbersAndValuesScrollViewer.ScrollChanged +=
                (sender, _) => { SynchronizeScrollValues(sender); };

            RefreshEntityRadar();

            // Run the rest of initialization after the window is shown,
            // so dialogs can safely set Owner = this.
            InitializeAfterWindowShown();
        };
    }

    private void ApplyStartWindowDimensionsFromConfig()
    {
        var settings = AppConfig.GetSection("Settings");
        var startWidth = settings.GetValue<double?>("StartWidth");
        var startHeight = settings.GetValue<double?>("StartHeight");

        if (startWidth is > 0)
        {
            Width = startWidth.Value;
        }

        if (startHeight is > 0)
        {
            Height = startHeight.Value;
        }
    }

    private void ApplyStartWindowPosition()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = Math.Max(workArea.Top, workArea.Bottom - Height);
    }

    private void InitializeAfterWindowShown()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Win1251 = Encoding.GetEncoding(1251);

            EnsureCaptureAdapterSelectedAndInitialize();

            UpdateGameTime();

            // prewarm
            _ = SphObjectDb.GameObjectDataDb;

            MainView.PacketLogList.LogListFullPackets.ItemsSource = LogRecords;
            MainView.PacketLogList.LogListFullPackets.ContextMenu = new ContextMenu();
            var menuItem = new MenuItem { Header = "Copy" };
            menuItem.Click += FullPacketsLog_MenuItem_OnClick;
            MainView.PacketLogList.LogListFullPackets.ContextMenu.Items.Add(menuItem);

            MainView.PacketLogList.LogListFullPackets.SelectionChanged += LogListOnSelectionChanged;
            MainView.ClientStatePanel.CurrentEntityStateForClient.ItemsSource = CurrentClientState;
            InitializeClientStateFilter();

            MainView.PacketLogList.LogListFullPackets.KeyDown += (_, args) =>
            {
                if (args.KeyboardDevice.Modifiers != ModifierKeys.Control || args.Key != Key.C)
                {
                    return;
                }

                CopySelectedRowContent(MainView.PacketLogList.LogListFullPackets);
            };

            LoadPacketDefinitions();
            LoadEnums();
            LoadContent();

            var fullPacketView = CollectionViewSource.GetDefaultView(MainView.PacketLogList.LogListFullPackets.ItemsSource);
            var filterFunc = new Predicate<object>(o =>
            {
                if (ShowFavoritesOnly)
                {
                    return (o as StoredPacket)?.Favorite ?? false;
                }

                var p = o as StoredPacket;
                if (p is null)
                {
                    return true;
                }

                if (HideClientPackets && p.Source == PacketSource.CLIENT)
                {
                    return false;
                }

                if (HideServerJunk && p.Source == PacketSource.SERVER && p.HiddenByDefaultServer)
                {
                    return false;
                }

                return true;
            });
            fullPacketView.Filter = filterFunc;

            MainView.PacketVisualizerPanel.PacketVisualizerControl.KeyDown += PacketVisualizerControlAddPacketPart;
            MainView.PacketVisualizerPanel.PacketVisualizerControl.KeyDown += PacketVisualizerControlHandlePartSelection;
            MainView.PacketVisualizerPanel.PacketVisualizerControl.AddHandler(Keyboard.PreviewKeyDownEvent,
                new KeyEventHandler(PacketVisualizerControlShiftSelectionOnArrowKeys), true);
            SelectionBrush = new SolidColorBrush
            {
                Color = ((SolidColorBrush)MainView.PacketVisualizerPanel.PacketVisualizerControl.SelectionBrush).Color,
                Opacity = MainView.PacketVisualizerPanel.PacketVisualizerControl.SelectionOpacity
            };
            MainView.PacketVisualizerPanel.PacketVisualizerControl.SelectionBrush = Brushes.Transparent;
            MainView.PacketVisualizerPanel.PacketVisualizerControl.PreviewMouseWheel += (_, _) => { };

            KeyUp += (_, e) =>
            {
                if (e.KeyboardDevice.Modifiers != ModifierKeys.Control || e.Key != Key.S)
                {
                    return;
                }

                if (MainView.PacketVisualizerPanel.DefinitionsPanel.DefinedPacketsListBox.SelectedItem is null)
                {
                    return;
                }

                SaveSelectedPacketDefinition();
            };

            CreateFlowDocumentWithHighlights(false, true);

            MainView.PacketVisualizerPanel.DefinitionsPanel.PacketPartsInDefinitionListBox.ItemsSource = PacketParts;

            // Wire UI events (moved into UserControls)
            MainView.FilterToggles.ShowBookmarkedOnly.Checked += ShowFavoritesOnlyToggleButton_OnChecked;
            MainView.FilterToggles.ShowBookmarkedOnly.Unchecked += ShowFavoritesOnlyToggleButton_OnUnchecked;
            MainView.FilterToggles.HideClientPackets.Checked += HideClientPackets_OnChecked;
            MainView.FilterToggles.HideClientPackets.Unchecked += HideClientPackets_OnUnchecked;
            MainView.FilterToggles.HideServerJunk.Checked += HideServerJunk_OnChecked;
            MainView.FilterToggles.HideServerJunk.Unchecked += HideServerJunk_OnUnchecked;
            MainView.FilterToggles.EnableListener.Checked += ListenerEnabled_OnChecked;
            MainView.FilterToggles.EnableListener.Unchecked += ListenerEnabled_OnUnchecked;
            MainView.FilterToggles.ShowNewInUi.Checked += ShowInUI_OnChecked;
            MainView.FilterToggles.ShowNewInUi.Unchecked += ShowInUI_OnUnchecked;

            MainView.PacketActionsBar.IsFavorite.Checked += FavoriteToggleButton_OnChecked;
            MainView.PacketActionsBar.IsFavorite.Unchecked += FavoriteToggleButton_OnUnchecked;
            MainView.PacketActionsBar.SearchInPacketTextBox.TextChanged += SearchInPacketTextBox_OnTextChanged;
            MainView.PacketActionsBar.SearchInPacketTextBox.KeyUp += SearchInPacketTextBox_OnKeyUp;
            MainView.PacketActionsBar.SearchAllVisibleButton.Click += SearchAllVisibleButton_OnClick;
            MainView.PacketActionsBar.AddPacketButton.Click += AddPacketButton_OnClick;
            MainView.PacketActionsBar.TeleportGoButton.Click += TeleportGoButton_OnClick;
            MainView.PacketActionsBar.SettingsButton.Click += SettingsButton_OnClick;

            MainView.PacketVisualizerPanel.DefinitionsPanel.DefinedPacketsListBox.SelectionChanged += DefinedPacketsListBox_OnSelectionChanged;
            MainView.PacketVisualizerPanel.DefinitionsPanel.SavePacketDefinitionButton.Click += SavePacketDefinition_OnClick;
            MainView.PacketVisualizerPanel.DefinitionsPanel.CreateNewPacketDefinitionButton.Click += CreateNewPacketDefinitionButton_OnClick;
            MainView.PacketVisualizerPanel.DefinitionsPanel.DeletePacketDefinitionButton.Click += DeletePacketDefinition_OnClick;

            MainView.PacketVisualizerPanel.DefinitionsPanel.PacketPartsInDefinitionListBox.SelectionChanged +=
                PacketPartsInDefinitionListBox_OnSelectionChanged;
            MainView.PacketVisualizerPanel.DefinitionsPanel.EditPacketPartButton.Click += EditPacketPart_OnClick;
            MainView.PacketVisualizerPanel.DefinitionsPanel.DeletePacketPartButton.Click += DeletePacketPartInCurrentDefinition_OnClick;

            MainView.PacketVisualizerPanel.DefinitionsPanel.ImportFromSubpacketButton.Click += ImportFromSubpacket_OnClick;
            MainView.PacketVisualizerPanel.DefinitionsPanel.ExportSubpacketButton.Click += ExportSubpacket_OnClick;

            MainView.PacketVisualizerPanel.PacketVisualizerControl.SelectionChanged += PacketVisualizerControl_OnSelectionChanged;
            MainView.ClientStatePanel.ClearClientStateButton.Click += ClearClientState_OnClick;
            MainView.ClientStatePanel.FilterClientStateButton.Click += FilterClientState_OnClick;
            MainView.GameState.TrackXpButton.Click += TrackXpButton_OnClick;

            SphereTimeUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1.0 / 24)
            };
            SphereTimeUpdateTimer.Tick += (_, _) => UpdateGameTime();
            SphereTimeUpdateTimer.Start();

            _entityRadarRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _entityRadarRefreshTimer.Tick += (_, _) => RefreshEntityRadar();
            _entityRadarRefreshTimer.Start();
            Closed += (_, _) => _entityRadarRefreshTimer?.Stop();
            Closed += (_, _) =>
            {
                _mitmProxy?.Dispose();
                _mitmProxy = null;
            };
            Closed += (_, _) => PacketCapture?.Dispose();

            TryStartMitmProxy();

            ScrollIntoViewIfSelectionExists();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Exception: {ex.Message}");
        }
    }

    private void TryStartMitmProxy()
    {
        var settings = AppConfig.GetSection("Settings");
        if (!settings.GetValue("MitmProxyEnabled", false))
        {
            return;
        }

        try
        {
            var listenAddr = settings.GetValue<string>("MitmProxyListenAddress") ?? "127.0.0.1";
            var listenPort = settings.GetValue<int?>("MitmProxyListenPort") ?? 25861;
            var upstreamHost = settings.GetValue<string>("MitmProxyUpstreamHost") ?? "77.223.107.68";
            var upstreamPort = settings.GetValue<int?>("MitmProxyUpstreamPort") ?? 25860;

            _mitmProxy = new SphereMitmProxy(listenAddr, listenPort, upstreamHost, upstreamPort);
            _mitmProxy.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"MITM proxy could not start: {ex.Message}\n\nDisable MitmProxyEnabled or fix MitmProxyListenAddress / MitmProxyListenPort in appconfig.json.",
                "MITM proxy",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            _mitmProxy?.Dispose();
            _mitmProxy = null;
        }
    }

    private void EnsureCaptureAdapterSelectedAndInitialize()
    {
        var mac = AppConfig.GetSection("Settings").GetValue<string>("MacAddress");
        if (string.IsNullOrWhiteSpace(mac))
        {
            var dialog = new SelectCaptureAdapterDialog(mac) { Owner = this };
            var result = dialog.ShowDialog();
            if (result != true || string.IsNullOrWhiteSpace(dialog.SelectedMacAddress))
            {
                throw new InvalidOperationException("No capture adapter selected. Please select an adapter to continue.");
            }

            mac = dialog.SelectedMacAddress;
            SaveMacAddressToAppConfig(mac);
            ReloadAppConfig();
        }

        try
        {
            InstallNewPacketCapture(mac);
        }
        catch (ArgumentException ex)
        {
            // appconfig may contain a stale/nonexistent MAC; route into adapter selection instead of crashing startup
            MessageBox.Show(this,
                $"Configured network adapter was not found.\n\n{ex.Message}\n\nPlease select a valid adapter to continue.",
                "Select network adapter",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            var dialog = new SelectCaptureAdapterDialog(mac) { Owner = this };
            var result = dialog.ShowDialog();
            if (result != true || string.IsNullOrWhiteSpace(dialog.SelectedMacAddress))
            {
                throw new InvalidOperationException("No capture adapter selected. Please select an adapter to continue.");
            }

            mac = dialog.SelectedMacAddress;
            SaveMacAddressToAppConfig(mac);
            ReloadAppConfig();
            InstallNewPacketCapture(mac);
        }
    }

    private void InstallNewPacketCapture(string macAddress)
    {
        var old = PacketCapture;
        try
        {
            old?.Dispose();
        }
        catch
        {
            // ignore dispose errors
        }

        PacketCapture = new PacketCapture(macAddress)
        {
            OnPacketProcessed = OnPacketProcessed
        };
    }

    private static void ReloadAppConfig()
    {
        AppConfig = new ConfigurationBuilder().AddJsonFile("appconfig.json").AddEnvironmentVariables().Build();
    }

    private static void SaveMacAddressToAppConfig(string macAddress)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appconfig.json");
        if (!File.Exists(path))
        {
            path = "appconfig.json";
        }

        var jsonText = File.ReadAllText(path);
        var node = JsonNode.Parse(jsonText) as JsonObject ?? new JsonObject();
        var settings = node["Settings"] as JsonObject ?? new JsonObject();
        settings["MacAddress"] = macAddress;
        node["Settings"] = settings;

        File.WriteAllText(path, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private void InitializeClientStateFilter()
    {
        ClientStateObjectTypeFilter = Enum.GetValues<ObjectType>()
            .Where(x => x != ObjectType.Unknown)
            .ToHashSet();

        ClientStateObjectTypeFilter.Remove(ObjectType.Monster);
        ClientStateObjectTypeFilter.Remove(ObjectType.MonsterFlyer);

        var view = CollectionViewSource.GetDefaultView(MainView.ClientStatePanel.CurrentEntityStateForClient.ItemsSource);
        view.Filter = o =>
        {
            if (o is not PacketAnalyzeData pad)
            {
                return true;
            }

            return ClientStateObjectTypeFilter is null ||
                   ClientStateObjectTypeFilter.Count == 0 ||
                   ClientStateObjectTypeFilter.Contains(pad.ObjectType);
        };
    }

    private void RefreshClientStateFilter()
    {
        CollectionViewSource.GetDefaultView(MainView.ClientStatePanel.CurrentEntityStateForClient.ItemsSource).Refresh();
        RefreshEntityRadar();
    }

    private void RefreshEntityRadar()
    {
        var view = CollectionViewSource.GetDefaultView(CurrentClientState);
        var items = view.Cast<PacketAnalyzeData>().ToList();
        MainView.EntityRadar.SetEntities(items, _radarClientX, _radarClientZ, _radarClientTurn, _radarHasClientPosition);
    }

    public byte[]? CurrentContentBytes { get; set; }
    public ObservableCollection<StoredPacket> LogRecords { get; } = new();
    public bool ShowFavoritesOnly { get; set; }
    public bool ListenerEnabled { get; set; } = true;
    public bool HideClientPackets { get; set; } = true;
    public bool HideServerJunk { get; set; } = true;
    public bool ShowNewInUI { get; set; } = true;

    private TrackXpSnapshot? _trackXpSnapshot;
    private bool _trackXpEnabled;

    private void OnPacketProcessed(List<StoredPacket> storedPackets, bool forceProcess)
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
            var objectType = Enum.IsDefined(typeof(ObjectType), objectTypeVal)
                ? (ObjectType)objectTypeVal
                : ObjectType.Unknown;
            if (objectType is ObjectType.Other)
            {
                continue;
            }

            currentStream.ReadByte(1);
            var actionTypeVal = (int)currentStream.ReadByte();
            var actionType = Enum.IsDefined(typeof(EntityActionType), actionTypeVal)
                ? (EntityActionType)actionTypeVal
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
            newCurrentContent[1] = (byte)(newCurrentContent.Count / 256);
            newCurrentContent[0] = (byte)(newCurrentContent.Count % 256);
            storedPacket.ContentBytes = newCurrentContent.ToArray();
            previousStream.Seek(previousStream.Length, 0);
            // this is a hack, probably bit count varies
            previousStream.SeekBack(3);
            previousStream.AutoIncreaseStream = true;
            previousStream.WriteBits(remainderBits[16..]);
            previousStream.SeekBitOffset(0);
            // last one is the divider, first 2 are something random?
            var previousContentBytes = previousStream.GetStreamDataFromCurrentOffsetAndBit()[..^1];
            previousContentBytes[1] = (byte)(previousContentBytes.Length / 256);
            previousContentBytes[0] = (byte)(previousContentBytes.Length % 256);
            storedPackets[i - 1].ContentBytes = previousContentBytes;
        }

        for (var i = 0; i < storedPackets.Count; i++)
        {
            var storedPacket = storedPackets[i];

            storedPacket.UpdatePacketPartsForContent();

            if (_trackXpEnabled && storedPacket.Source == PacketSource.SERVER)
            {
                TryTrackDegreeXpFromServerPacket(storedPacket);
            }

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
                    MainView.PacketLogList.LogListFullPackets.UpdateLayout();
                }
            });
        }
    }

    public void UpdateGameTime()
    {
        var time = TimeHelper.GetCurrentSphereDateTime().AddYears(7800);
        MainView.GameState.GameTime.Text = time.ToString("dd/MM/yyyy HH:mm");
        // TODO
        MainView.GameState.GameTimeBits.Text = "0";
    }

    public void UpdateClientCoordsAndId(StoredPacket storedPacket)
    {
        try
        {
            var coords = CoordsHelper.GetCoordsFromPingBytes(storedPacket.ContentBytes);
            MainView.GameState.CoordsX.Text = $"{coords.x:F4}";
            MainView.GameState.CoordsY.Text = $"{coords.y:F4}";
            MainView.GameState.CoordsZ.Text = $"{coords.z:F4}";
            MainView.GameState.CoordsT.Text = $"{coords.turn:F4}";

            var xBytes = CoordsHelper.EncodeServerCoordinate(coords.x);
            var yBytes = CoordsHelper.EncodeServerCoordinate(coords.y);
            var zBytes = CoordsHelper.EncodeServerCoordinate(coords.z);
            var tBytes = CoordsHelper.EncodeServerCoordinate(coords.turn);

            MainView.GameState.CoordsXBits.Text = StringConvertHelpers.ByteArrayToBinaryString(xBytes, false, true);
            MainView.GameState.CoordsYBits.Text = StringConvertHelpers.ByteArrayToBinaryString(yBytes, false, true);
            MainView.GameState.CoordsZBits.Text = StringConvertHelpers.ByteArrayToBinaryString(zBytes, false, true);
            MainView.GameState.CoordsTBits.Text = StringConvertHelpers.ByteArrayToBinaryString(tBytes, false, true);

            var id = (storedPacket.ContentBytes[16] >> 5) + (storedPacket.ContentBytes[17] << 3) +
                     ((storedPacket.ContentBytes[18] & 0b11111) << 11);
            MainView.GameState.ClientId.Text = $"{id:X4}";
            PacketCapture?.SetClientId((short)id);

            _radarClientX = coords.x;
            _radarClientZ = coords.z;
            _radarClientTurn = coords.turn;
            _radarHasClientPosition = true;
            RefreshEntityRadar();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }

    public void LoadContent()
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
            else
            {
                PacketAnalyzer.RefreshHiddenByDefaultFlags(packet);
            }

            LogRecords.Add(packet);
        }

        MainView.PacketLogList.LogListFullPackets.UpdateLayout();
    }

    public void UpdateContentPreview(StoredPacket selected)
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
                .Where(x => x.GetType() != typeof(DespawnPacket)
                         && x.GetType() != typeof(PacketAnalyzeData))
                .ToList();
            if (knownAnalyzedParts.Any())
            {
                packetContents = string.Join('\n', knownAnalyzedParts.Select(x => x.DisplayValue));
                packetContents +=
                    "\n----------------------------------------------------------------------------------";
            }

            MainView.ClientStatePanel.ContentPreview.Text = packetContents + "\n" + PacketAnalyzer.GetTextOutputForPacket(bytes) +
                                  "----------------------------------------------------------------------------------\n";
            var sphObjects = ObjectPacketTools.GetObjectsFromPacket(bytes);
            MainView.ClientStatePanel.ContentPreview.Text += sphObjects.Count > 0 ? ObjectPacketTools.GetTextOutput(sphObjects) : "";
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            MainView.ClientStatePanel.ContentPreview.Text = "Not an item packet";
        }
    }

    public void UpdateClientState(StoredPacket storedPacket)
    {
        foreach (var result in storedPacket.AnalyzeResult)
        {
            if (result.GetType() == typeof(DespawnPacket))
            {
                var entsToDespawn = CurrentClientState.Where(x => x.Id == result.Id).ToList();
                foreach (var ent in entsToDespawn)
                {
                    CurrentClientState.Remove(ent);
                }
            }
            else if (result.GetType() == typeof(MobPacket))
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
            else if (result.GetType() == typeof(NpcTradePacket))
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

                else if (npc.ActionType == EntityActionType.FULL_SPAWN)
                {
                    CurrentClientState.Insert(0, result);
                }
            }
            else if (result.GetType() == typeof(CharacterPacket))
            {
                var character = result as CharacterPacket;

                if (CurrentClientState.FirstOrDefault(x => x.Id == result.Id) is CharacterPacket previousState)
                {
                    var previousIndex = CurrentClientState.IndexOf(previousState);
                    if (character.ActionType == EntityActionType.SET_POSITION)
                    {
                        previousState.X = character.X;
                        previousState.Y = character.Y;
                        previousState.Z = character.Z;
                        previousState.Angle = character.Angle;
                    }
                    else if (character.ActionType == EntityActionType.FULL_SPAWN)
                    {
                        CurrentClientState.Remove(previousState);
                        CurrentClientState.Insert(previousIndex, result);
                    }
                    else if (character is
                    {
                        ActionType: EntityActionType.INTERACT, InteractionType: EntityInteractionType.DEATH
                    })
                    {
                        CurrentClientState.Remove(previousState);
                    }
                }

                else if (character.ActionType == EntityActionType.FULL_SPAWN)
                {
                    CurrentClientState.Insert(0, result);
                }

            }
            else if (result.GetType() == typeof(DoorEntrancePacket))
            {
                var door = result as DoorEntrancePacket;
                if (door.ActionType == EntityActionType.FULL_SPAWN)
                {
                    CurrentClientState.Insert(0, result);
                }
            }
            else if (result.GetType() == typeof(DoorExitPacket))
            {
                var door = result as DoorExitPacket;
                if (door.ActionType == EntityActionType.FULL_SPAWN)
                {
                    CurrentClientState.Insert(0, result);
                }
            }
            else if (result.GetType() == typeof(DoorEntranceWithKey))
            {
                var door = result as DoorEntranceWithKey;
                if (door.ActionType == EntityActionType.FULL_SPAWN)
                {
                    CurrentClientState.Insert(0, result);
                }
            }
            else if (result.GetType() == typeof(TeleportWithTargetPacket))
            {
                var teleportWithTarget = result as TeleportWithTargetPacket;
                if (teleportWithTarget.ActionType == EntityActionType.FULL_SPAWN)
                {
                    CurrentClientState.Insert(0, teleportWithTarget);
                }
            }
            else if (result.GetType() == typeof(CastleTablet))
            {
                var castleTablet = result as CastleTablet;
                if (castleTablet.ActionType == EntityActionType.FULL_SPAWN)
                {
                    CurrentClientState.Insert(0, castleTablet);
                }
            }
            else if (result.GetType() == typeof(CastleEntrance))
            {
                var castleEntrance = result as CastleEntrance;
                if (castleEntrance.ActionType == EntityActionType.FULL_SPAWN)
                {
                    CurrentClientState.Insert(0, castleEntrance);
                }
            }
            else if (result.GetType() == typeof(CastleGate))
            {
                var castleGates = result as CastleGate;
                if (castleGates.ActionType == EntityActionType.FULL_SPAWN)
                {
                    CurrentClientState.Insert(0, castleGates);
                }
            }
            else if (result.GetType() == typeof(CastleChest))
            {
                var castleChest = result as CastleChest;
                if (castleChest.ActionType == EntityActionType.FULL_SPAWN)
                {
                    CurrentClientState.Insert(0, castleChest);
                }
            }
            else if (result.GetType() == typeof(WorldObject))
            {
                var worldObject = result as WorldObject;
                if (worldObject.ActionType == EntityActionType.FULL_SPAWN)
                {
                    CurrentClientState.Insert(0, worldObject);
                }
            }
        }

        MainView.ClientStatePanel.CurrentEntityStateForClient.UpdateLayout();
    }

    private void LogListOnSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        try
        {
            if (args.AddedItems.Count < 1)
            {
                return;
            }

            var selected = args.AddedItems[0] as StoredPacket;
            CurrentContentBytes = selected.ContentBytes;

            MainView.PacketActionsBar.IsFavorite.IsChecked = selected.Favorite;
            MainView.PacketVisualizerPanel.DefinitionsPanel.DefinedPacketsListBox.SelectedItem = null;
            PacketParts.Clear();
            MainView.PacketLogList.LogListFullPackets.ScrollIntoView(selected);
            UpdateContentPreview(selected);
        }
        catch
        {
            MainView.PacketActionsBar.IsFavorite.IsChecked = false;
        }
    }

    private void FullPacketsLog_MenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        CopySelectedRowContent(MainView.PacketLogList.LogListFullPackets);
    }

    private void CopySelectedRowContent(ListView listView)
    {
        var selectedRow = (StoredPacket)listView.SelectedItem;
        var text =
            $"{Convert.ToHexString(selectedRow.ContentBytes)}";
        Clipboard.SetText(text);
    }

    private void FavoriteToggleButton_OnChecked(object sender, RoutedEventArgs e)
    {
        var logList = MainView.PacketLogList.LogListFullPackets;
        if (logList.SelectedItem is null)
        {
            return;
        }

        var item = (StoredPacket)logList.SelectedItem;
        item.Favorite = true;
        UpdateStoredPacket(item);
    }

    private void FavoriteToggleButton_OnUnchecked(object sender, RoutedEventArgs e)
    {
        var logList = MainView.PacketLogList.LogListFullPackets;
        if (logList.SelectedItem is null)
        {
            return;
        }

        var item = (StoredPacket)logList.SelectedItem;
        item.Favorite = false;
        UpdateStoredPacket(item);
    }

    private void ShowFavoritesOnlyToggleButton_OnChecked(object sender, RoutedEventArgs e)
    {
        if (ShowFavoritesOnly)
        {
            return;
        }

        ShowFavoritesOnly = true;
        ScrollIntoViewIfSelectionExists();
    }

    private void UpdateStoredPacket(StoredPacket storedPacket)
    {
        PacketCollection.Update(storedPacket);
    }

    private void ShowFavoritesOnlyToggleButton_OnUnchecked(object sender, RoutedEventArgs e)
    {
        ShowFavoritesOnly = false;
        ScrollIntoViewIfSelectionExists();
    }

    private void HideClientPackets_OnChecked(object sender, RoutedEventArgs e)
    {
        if (HideClientPackets)
        {
            return;
        }

        HideClientPackets = true;
        ScrollIntoViewIfSelectionExists();
    }

    private void HideClientPackets_OnUnchecked(object sender, RoutedEventArgs e)
    {
        HideClientPackets = false;
        ScrollIntoViewIfSelectionExists();
    }

    private void HideServerJunk_OnChecked(object sender, RoutedEventArgs e)
    {
        if (HideServerJunk)
        {
            return;
        }

        HideServerJunk = true;
        ScrollIntoViewIfSelectionExists();
    }

    private void HideServerJunk_OnUnchecked(object sender, RoutedEventArgs e)
    {
        HideServerJunk = false;
        ScrollIntoViewIfSelectionExists();
    }

    private void ListenerEnabled_OnChecked(object sender, RoutedEventArgs e)
    {
        if (ListenerEnabled)
        {
            return;
        }

        ListenerEnabled = true;
    }

    private void ListenerEnabled_OnUnchecked(object sender, RoutedEventArgs e)
    {
        ListenerEnabled = false;
    }

    private void ScrollIntoViewIfSelectionExists()
    {
        CollectionViewSource.GetDefaultView(MainView.PacketLogList.LogListFullPackets.ItemsSource).Refresh();
        if (MainView.PacketLogList.LogListFullPackets.Items.Count < 1)
        {
            return;
        }

        var selected = MainView.PacketLogList.LogListFullPackets.SelectedItem ?? MainView.PacketLogList.LogListFullPackets.Items[^1];
        if (!MainView.PacketLogList.LogListFullPackets.Items.PassesFilter(selected))
        {
            // should only happen when switching to a more restricted view with filtered out item selected
            selected = MainView.PacketLogList.LogListFullPackets.Items[^1];
        }

        MainView.PacketLogList.LogListFullPackets.SelectedItem = selected;
        MainView.PacketLogList.LogListFullPackets.ScrollIntoView(selected);
    }

    private void LoadEnums()
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

    private void LoadPacketDefinitions()
    {
        MainView.PacketVisualizerPanel.DefinitionsPanel.DefinedPacketsListBox.ItemsSource = PacketDefinitions;
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

        MainView.PacketVisualizerPanel.DefinitionsPanel.SubpacketsListBox.ItemsSource = Subpackets;

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
            MainView.PacketVisualizerPanel.DefinitionsPanel.SubpacketsListBox.SelectedItem = Subpackets.First();
        }
    }

    private void SynchronizeScrollValues(object source)
    {
        var scrollViewer = (ScrollViewer)source;
        if (scrollViewer != MainView.PacketVisualizerPanel.PacketVisualizerLineNumbersAndValuesScrollViewer &&
            Math.Abs(MainView.PacketVisualizerPanel.PacketVisualizerLineNumbersAndValuesScrollViewer.VerticalOffset - scrollViewer.VerticalOffset) >
            double.Epsilon)
        {
            MainView.PacketVisualizerPanel.PacketVisualizerLineNumbersAndValuesScrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset);
        }

        if (scrollViewer != MainView.PacketVisualizerPanel.PacketVisualizerDefinedPacketValuesScrollViewer &&
            Math.Abs(MainView.PacketVisualizerPanel.PacketVisualizerDefinedPacketValuesScrollViewer.VerticalOffset - scrollViewer.VerticalOffset) >
            double.Epsilon)
        {
            MainView.PacketVisualizerPanel.PacketVisualizerDefinedPacketValuesScrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset);
        }

        if (scrollViewer != PacketDisplayScrollViewer &&
            Math.Abs(PacketDisplayScrollViewer!.VerticalOffset - scrollViewer.VerticalOffset) > double.Epsilon)
        {
            PacketDisplayScrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset);
        }
    }

    private void PacketVisualizerControlHandlePartSelection(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.S && e.Key != Key.E && e.Key != Key.Escape)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            ClearSelection();
        }

        var caretPosition = MainView.PacketVisualizerPanel.PacketVisualizerControl.CaretPosition;
        if (caretPosition is null)
        {
            ClearSelection();
            LastVerticalOffset = 0;
            return;
        }

        LastCaretOffset = MainView.PacketVisualizerPanel.PacketVisualizerControl.Document.ContentStart.GetOffsetToPosition(caretPosition);
        LastVerticalOffset = MainView.PacketVisualizerPanel.PacketVisualizerControl.VerticalOffset;

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

    private void PacketVisualizerControlShiftSelectionOnArrowKeys(object sender, KeyEventArgs e)
    {
        var isShiftDown = (e.KeyboardDevice.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        if (!isShiftDown)
        {
            return;
        }

        if (e.Key != Key.Left && e.Key != Key.Right && e.Key != Key.Up && e.Key != Key.Down)
        {
            return;
        }

        if (StartTextPointer is null || EndTextPointer is null)
        {
            return;
        }

        var deltaBits = e.Key switch
        {
            Key.Left => -1,
            Key.Right => 1,
            Key.Up => -8,
            Key.Down => 8,
            _ => 0
        };
        if (deltaBits == 0)
        {
            return;
        }

        var maxOffset = PacketContentBits?.Length ?? (CurrentContentBytes?.Length ?? 0) * 8;
        if (maxOffset <= 0)
        {
            return;
        }

        var startOffset = StartTextPointer.GetCharOffset();
        var endOffset = EndTextPointer.GetCharOffset();

        var newStart = startOffset + deltaBits;
        var newEnd = endOffset + deltaBits;

        // Keep selection size; clamp shift at boundaries.
        var min = Math.Min(newStart, newEnd);
        if (min < 0)
        {
            newStart -= min;
            newEnd -= min;
        }

        var max = Math.Max(newStart, newEnd);
        if (max > maxOffset)
        {
            var overshoot = max - maxOffset;
            newStart -= overshoot;
            newEnd -= overshoot;
        }

        newStart = Math.Clamp(newStart, 0, maxOffset);
        newEnd = Math.Clamp(newEnd, 0, maxOffset);

        var docStart = MainView.PacketVisualizerPanel.PacketVisualizerControl.Document.ContentStart;
        StartTextPointer = MoveByCharOffset(docStart, newStart);
        EndTextPointer = MoveByCharOffset(docStart, newEnd);

        e.Handled = true;
        CreateFlowDocumentWithHighlights();
    }

    private void ClearSelection()
    {
        StartTextPointer = null;
        EndTextPointer = null;
        LastCaretOffset = null;
    }

    private void UpdateScrolling()
    {
        if (LastCaretOffset.HasValue)
        {
            var newCaretPosition = LastCaretOffset.Value <= 2
                ? MainView.PacketVisualizerPanel.PacketVisualizerControl.Document.ContentStart.GetLineStartPosition(0)
                : MainView.PacketVisualizerPanel.PacketVisualizerControl.Document.ContentStart.GetPositionAtOffset(LastCaretOffset.Value);
            if (newCaretPosition is not null)
            {
                MainView.PacketVisualizerPanel.PacketVisualizerControl.CaretPosition = newCaretPosition;
            }
        }

        MainView.PacketVisualizerPanel.PacketVisualizerControl.ScrollToVerticalOffset(LastVerticalOffset);
        MainView.PacketVisualizerPanel.PacketVisualizerLineNumbersAndValuesScrollViewer.ScrollToVerticalOffset(LastVerticalOffset);
        MainView.PacketVisualizerPanel.PacketVisualizerDefinedPacketValuesScrollViewer.ScrollToVerticalOffset(LastVerticalOffset);
    }

    private void PacketVisualizerControlAddPacketPart(object sender, KeyEventArgs e)
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
            R = (byte)Random.Shared.Next(0, 255),
            G = (byte)Random.Shared.Next(0, 255),
            B = (byte)Random.Shared.Next(0, 255)
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

    public void CreateFlowDocumentWithHighlights(bool keepSelection = true, bool firstUpdateOnLoad = false)
    {
        MainView.PacketVisualizerPanel.PacketVisualizerLineNumbersAndValues.Inlines.Clear();
        MainView.PacketVisualizerPanel.PacketVisualizerDefinedPacketValues.Inlines.Clear();
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
                lineByte = (int)((((ulong)lineByte * 0x0202020202UL) & 0x010884422010UL) % 1023);
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

        MainView.PacketVisualizerPanel.PacketVisualizerLineNumbersAndValues.Text = linesSb.ToString();
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
                MainView.PacketVisualizerPanel.PacketVisualizerDefinedPacketValues.Inlines.Add(new string('\n',
                    lineToReach - previousLineBreakLineIndex));
            }

            previousLineBreakLineIndex = lineToReach;

            AddPacketPartInlines(MainView.PacketVisualizerPanel.PacketVisualizerDefinedPacketValues.Inlines, part);
        }

        CurrentContentBitStream.Seek(0, 0);
        if (previousLineBreakLineIndex < PacketContentBits.Length / 8 - 1)
        {
            var remainingNewlines = PacketContentBits.Length / 8 - previousLineBreakLineIndex - 2;
            if (remainingNewlines > 0)
            {
                MainView.PacketVisualizerPanel.PacketVisualizerDefinedPacketValues.Inlines.Add(new string('\n', remainingNewlines));
            }
        }

        document.Blocks.Add(paragraph);

        if (firstUpdateOnLoad)
        {
            MainView.PacketVisualizerPanel.PacketReadableDisplayText.Inlines.Clear();
            MainView.PacketVisualizerPanel.PacketReadableDisplayText.Inlines.Add(Convert.ToHexString(BitStream.BitArrayToBytes(PacketContentBits)) +
                                                  "\n");
            var toShift = PacketContentBits.ToList();
            for (var i = 0; i < 8; i++)
            {
                var shiftedBytes = BitStream.BitArrayToBytes(toShift.ToArray());
                var shiftedChars = Win1251.GetString(shiftedBytes).ToCharArray();
                var shiftedString = new string(shiftedChars.Select(GetVisibleChar).ToArray());
                MainView.PacketVisualizerPanel.PacketReadableDisplayText.Inlines.Add(new Run($"\n[{i}] {shiftedString}")
                {
                    FontSize = 14
                });
                toShift.RemoveAt(0);
            }
        }

        MainView.PacketVisualizerPanel.PacketVisualizerControl.Document = document;
        UpdateSelectedValueDisplay(selectionBits);
        UpdateScrolling();
    }

    private void PacketVisualizerControl_OnSelectionChanged(object o, RoutedEventArgs e)
    {
        e.Handled = true;
    }

    private PacketPart CreatePacketPart(string name, string? enumName, PacketPartType packetPartType,
        bool lengthFromPrevious, TextPointer start, TextPointer end, Brush highlightColor)
    {
        var bitOffsetStart = start.GetCharOffset();
        var bitOffsetEnd = end.GetCharOffset();
        var actualStart = Math.Min(bitOffsetStart, bitOffsetEnd);

        var bitLength = Math.Abs(bitOffsetEnd - bitOffsetStart);
        var color = ((SolidColorBrush)highlightColor).Color;
        var part = new PacketPart(bitLength, name, enumName, lengthFromPrevious, packetPartType,
            actualStart, Array.Empty<Bit>(), color.R, color.G, color.B, color.A);
        PacketPart.UpdatePacketPartValues(new List<PacketPart> { part }, CurrentContentBitStream, actualStart);
        return part;
    }

    private void UpdateDefinedPackets()
    {
        MainView.PacketVisualizerPanel.DefinedPacketPartsControl.Document.Blocks.Clear();
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
                var lineWidth = MainView.PacketVisualizerPanel.DefinedPacketPartsControl.ActualWidth < 50
                    ? 120
                    : (int)(MainView.PacketVisualizerPanel.DefinedPacketPartsControl.ActualWidth / 9);
                var comment = $" {part.Comment} ";
                var paddingLength = Math.Max(0, (lineWidth - comment.Length) / 2);
                var padding = paddingLength == 0 ? string.Empty : new string('=', paddingLength);
                var commentColor = part.Comment == "NEXT PACKET" ? Brushes.SlateGray : Brushes.Honeydew;
                paragraph.Inlines.Add(new Run(
                    $"{padding}{comment}{padding}\n\n")
                {
                    Background = commentColor
                });
            }

            AddPacketPartInlines(paragraph.Inlines, part);

            MainView.PacketVisualizerPanel.DefinedPacketPartsControl.Document.Blocks.Add(paragraph);
        }
    }

    private void AddPacketPartInlines(InlineCollection inlineCollection, PacketPart part)
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
                Foreground = Brushes.Gray,
                FontSize = 12
            });
    }

    private void AddNewDefinedPacketPartBulk(List<PacketPart> packetParts, bool updateLayout = true)
    {
        packetParts.ForEach(x => AddNewDefinedPacketPart(x, true));

        UpdateDefinedPackets();
        if (updateLayout)
        {
            CreateFlowDocumentWithHighlights(false);
            ClearSelection();
        }
    }

    private void AddNewDefinedPacketPart(PacketPart packetPart, bool isBulk = false)
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

    private void UpdateSelectedValueDisplay(List<Bit> bits)
    {
        if (!bits.Any())
        {
            MainView.PacketVisualizerPanel.PacketSelectedValueDisplay.Text = "Select bits to show value preview\n\n" +
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

        MainView.PacketVisualizerPanel.PacketSelectedValueDisplay.Text = sb.ToString();
    }

    public static char GetVisibleChar(char c)
    {
        return (c >= 0x20 && c <= 0x7E) || c is >= 'А' and <= 'я' ? c : '·';
    }

    private void CreateNewPacketDefinitionButton_OnClick(object sender, RoutedEventArgs e)
    {
        CreatePacketDefinition();
    }

    private void CreatePacketDefinition()
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
            MainView.PacketVisualizerPanel.DefinitionsPanel.DefinedPacketsListBox.SelectedItem = definition;
        }
    }

    private void SavePacketDefinition_OnClick(object sender, RoutedEventArgs e)
    {
        SaveSelectedPacketDefinition();
    }

    private void SaveSelectedPacketDefinition()
    {
        if (MainView.PacketVisualizerPanel.DefinitionsPanel.DefinedPacketsListBox.SelectedItem is not PacketDefinition selectedDefinition)
        {
            CreatePacketDefinition();
            selectedDefinition = (PacketDefinition?)MainView.PacketVisualizerPanel.DefinitionsPanel.DefinedPacketsListBox.SelectedItem;
        }

        if (selectedDefinition is null)
        {
            return;
        }

        SavePacketDefinition(selectedDefinition.Name, 0, PacketContentBits.Length);
    }

    private void SavePacketDefinition(string definitionName, int startBitOffset, int endBitOffset,
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

    private void DefinedPacketsListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MainView.PacketVisualizerPanel.DefinitionsPanel.DefinedPacketsListBox.SelectedItem is not PacketDefinition packetDefinition)
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

    private void ExportSubpacket_OnClick(object sender, RoutedEventArgs e)
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

    private void DeletePacketPartInCurrentDefinition_OnClick(object sender, RoutedEventArgs e)
    {
        if (MainView.PacketVisualizerPanel.DefinitionsPanel.PacketPartsInDefinitionListBox.SelectedItems.Count == 0)
        {
            return;
        }

        if (MessageBox.Show("Delete selected parts?", "Delete selected parts?",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) !=
            MessageBoxResult.Yes)
        {
            return;
        }

        var listToRemove = MainView.PacketVisualizerPanel.DefinitionsPanel.PacketPartsInDefinitionListBox.SelectedItems.Cast<PacketPart>().ToList();
        MainView.PacketVisualizerPanel.DefinitionsPanel.PacketPartsInDefinitionListBox.UnselectAll();

        foreach (var selectedItem in listToRemove)
        {
            PacketParts.Remove(selectedItem);
        }

        UpdateDefinedPackets();
        CreateFlowDocumentWithHighlights();
    }

    private void ImportFromSubpacket_OnClick(object sender, RoutedEventArgs e)
    {
        if (MainView.PacketVisualizerPanel.DefinitionsPanel.SubpacketsListBox.SelectedItem is not Subpacket subpacket)
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

    private void DeletePacketDefinition_OnClick(object sender, RoutedEventArgs e)
    {
        if (MainView.PacketVisualizerPanel.DefinitionsPanel.DefinedPacketsListBox.SelectedItem is not PacketDefinition packetDefinition)
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

    private void DeletePacketDefinition(PacketDefinition packetDefinition)
    {
        PacketDefinitions.Remove(packetDefinition);
        File.Delete(packetDefinition.FilePath);
        if (PacketDefinitions.Any())
        {
            MainView.PacketVisualizerPanel.DefinitionsPanel.DefinedPacketsListBox.SelectedItem = PacketDefinitions.First();
        }
    }

    public static string SnakeCaseToCamelCase(string input)
    {
        return input
            .Split(new[] { "_" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => char.ToUpperInvariant(s[0]) + s.Substring(1, s.Length - 1))
            .Aggregate(string.Empty, (s1, s2) => s1 + s2);
    }

    private void PacketPartsInDefinitionListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
    }

    private void EditPacketPart_OnClick(object sender, RoutedEventArgs e)
    {
    }

    private void SearchInPacketTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        StartTextPointer = null;
        EndTextPointer = null;
        SearchText();
    }

    private static TextPointer MoveByCharOffset(TextPointer textPointer, int countChars)
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

    private void SearchText()
    {
        var text = MainView.PacketActionsBar.SearchInPacketTextBox.Text;
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
                        startOffset = (int)CurrentContentBitStream.Offset;
                        startBit = CurrentContentBitStream.Bit;
                        break;
                    }

                    CurrentContentBitStream.ReadBit();
                }

                if (startOffset != -1)
                {
                    // found something
                    var range = new TextRange(MainView.PacketVisualizerPanel.PacketVisualizerControl.Document.ContentStart,
                        MainView.PacketVisualizerPanel.PacketVisualizerControl.Document.ContentEnd);

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
                        startOffset = (int)CurrentContentBitStream.Offset;
                        startBit = CurrentContentBitStream.Bit;
                        break;
                    }

                    CurrentContentBitStream.ReadBit();
                }

                if (startOffset != -1)
                {
                    // found something
                    var range = new TextRange(MainView.PacketVisualizerPanel.PacketVisualizerControl.Document.ContentStart,
                        MainView.PacketVisualizerPanel.PacketVisualizerControl.Document.ContentEnd);
                    var endOffset = startOffset + bytesToFind.Length;
                    var endBit = startBit;

                    var startOffsetPointer = MoveByCharOffset(range.Start, startOffset * 8 + startBit);

                    var endOffsetPointer = MoveByCharOffset(startOffsetPointer, bitLength);
                    StartTextPointer = endOffsetPointer;
                    EndTextPointer = startOffsetPointer;

                    CreateFlowDocumentWithHighlights();
                    MainView.PacketVisualizerPanel.PacketVisualizerControl.ScrollToVerticalOffset(16 * startOffset);
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

    private int GetMinimumBitsToEncodeValue(long value)
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

    private void SearchInPacketTextBox_OnKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        SearchText();
    }

    private async void SearchAllVisibleButton_OnClick(object sender, RoutedEventArgs e)
    {
        var query = MainView.PacketActionsBar.SearchInPacketTextBox.Text;
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        var list = MainView.PacketLogList.LogListFullPackets;
        var visible = list.Items.Cast<object>().OfType<StoredPacket>().ToList();
        if (visible.Count == 0)
        {
            return;
        }

        bool SearchInCurrentPacket(bool resetToStart)
        {
            if (resetToStart)
            {
                StartTextPointer = null;
                EndTextPointer = null;
            }

            SearchText();
            return StartTextPointer is not null && EndTextPointer is not null;
        }

        // 1) Continue search within the currently selected (visible) packet first
        if (SearchInCurrentPacket(resetToStart: false))
        {
            return;
        }

        var selected = list.SelectedItem as StoredPacket;
        var startIndex = 0;
        if (selected is not null)
        {
            var currentIndex = visible.IndexOf(selected);
            startIndex = currentIndex >= 0 ? currentIndex + 1 : 0;
        }

        // 2) If not found, search all other visible packets concurrently,
        //    then jump to the earliest match in list order (wrapping once).
        var searchOrder = new List<(int OrderIndex, int VisibleIndex, StoredPacket Packet)>(visible.Count);
        var order = 0;
        for (var i = startIndex; i < visible.Count; i++)
        {
            searchOrder.Add((order++, i, visible[i]));
        }
        for (var i = 0; i < Math.Min(startIndex, visible.Count); i++)
        {
            searchOrder.Add((order++, i, visible[i]));
        }

        var btn = sender as Button;
        if (btn is not null)
        {
            btn.IsEnabled = false;
        }

        try
        {
            var bestOrderIndex = await Task.Run(() =>
            {
                var best = int.MaxValue;

                Parallel.ForEach(searchOrder, item =>
                {
                    // Skip work if we already found an earlier match
                    if (Volatile.Read(ref best) <= item.OrderIndex)
                    {
                        return;
                    }

                    if (!PacketHasContentMatch(item.Packet.ContentBytes, query))
                    {
                        return;
                    }

                    var current = Volatile.Read(ref best);
                    while (item.OrderIndex < current)
                    {
                        var prev = Interlocked.CompareExchange(ref best, item.OrderIndex, current);
                        if (prev == current)
                        {
                            break;
                        }

                        current = prev;
                    }
                });

                return best == int.MaxValue ? (int?)null : best;
            });

            if (bestOrderIndex is null)
            {
                return;
            }

            var target = searchOrder.First(x => x.OrderIndex == bestOrderIndex.Value).Packet;
            list.SelectedItem = target; // triggers LogListOnSelectionChanged -> UpdateContentPreview -> updates bitstream
            list.ScrollIntoView(target);

            // run the real highlighter search inside the newly selected packet
            if (SearchInCurrentPacket(resetToStart: true))
            {
                list.Focus();
            }
        }
        finally
        {
            if (btn is not null)
            {
                btn.IsEnabled = true;
            }
        }
    }

    private bool PacketHasContentMatch(byte[] contentBytes, string query)
    {
        if (contentBytes.Length == 0)
        {
            return false;
        }

        if (query.StartsWith("0"))
        {
            // integers, 0x 0d 0b (same validation as SearchText())
            if (query.Length < 3)
            {
                return false;
            }

            var intBase = query[1] == 'x' ? 16 : query[1] == 'd' ? 10 : query[1] == 'b' ? 2 : 0;
            if (intBase == 0 || query[2..].Any(x => !char.IsAsciiHexDigit(x)))
            {
                return false;
            }

            try
            {
                var value = Convert.ToInt64(query[2..], intBase);
                var bitsToRead = GetMinimumBitsToEncodeValue(value);
                var bs = new BitStream(contentBytes);
                bs.Seek(0, 0);
                while (bs.ValidPosition)
                {
                    var test = bs.ReadInt64(bitsToRead);
                    if (!bs.ValidPosition)
                    {
                        break;
                    }

                    bs.SeekBack(bitsToRead);
                    if (test == value)
                    {
                        return true;
                    }

                    bs.ReadBit();
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        // assuming win1251 string
        var bytesToFind = Win1251.GetBytes(query);
        var bitLength = bytesToFind.Length * 8;
        if (bitLength <= 0)
        {
            return false;
        }

        try
        {
            var bs = new BitStream(contentBytes);
            bs.Seek(0, 0);
            while (bs.ValidPosition)
            {
                var test = bs.ReadBytes(bitLength);
                if (!bs.ValidPosition)
                {
                    break;
                }

                bs.SeekBack(bitLength);
                if (test.HasEqualElementsAs(bytesToFind))
                {
                    return true;
                }

                bs.ReadBit();
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    public static void RegisterBsonMapperForBrush()
    {
        BsonMapper.Global.RegisterType<SolidColorBrush>(
            brush => Dispatcher.CurrentDispatcher.Invoke(() =>
                $"{brush.Color.R},{brush.Color.G},{brush.Color.B},{brush.Color.A}"),
            bson =>
            {
                var colors = ((string)bson).Split(',').Select(byte.Parse).ToArray();
                return new SolidColorBrush
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

    private void AddPacketButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new AddPacketManuallyDialog
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
                PacketCapture?.ProcessPacketRawDataForce(rawData, true);
            }
        }
    }

    private void TeleportGoButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryResolveTeleportClientIndex(out var clientIndex))
        {
            MessageBox.Show(this,
                "No client ID available. Capture traffic until the Client ID field is filled, or ensure it shows a non-zero hex value.",
                "Teleport",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        static bool TryParseCoord(string? s, out double v)
        {
            v = 0;
            return !string.IsNullOrWhiteSpace(s) &&
                   double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        var bar = MainView.PacketActionsBar;
        if (!TryParseCoord(bar.TeleportXTextBox.Text, out var cx) ||
            !TryParseCoord(bar.TeleportYTextBox.Text, out var cy) ||
            !TryParseCoord(bar.TeleportZTextBox.Text, out var cz) ||
            !TryParseCoord(bar.TeleportTTextBox.Text, out var ct))
        {
            MessageBox.Show(this, "Enter valid floating-point values for X, Y, Z, and T (use \".\" as decimal separator).",
                "Teleport",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var settings = AppConfig.GetSection("Settings");
        var coords = new WorldCoords(cx, cy, cz, ct);
        var packet = TeleportPacketBuilder.BuildTeleportPacket(clientIndex, coords);

        if (settings.GetValue("MitmProxyEnabled", false))
        {
            if (_mitmProxy is null)
            {
                MessageBox.Show(this,
                    "MitmProxyEnabled is true but the proxy did not start. Check appconfig.json listen settings and restart PacketLogViewer.",
                    "Teleport",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (_mitmProxy.TryInjectTowardClient(packet))
            {
                return;
            }

            MessageBox.Show(this,
                $"No active connection through the MITM proxy. Configure the game client to connect to {_mitmProxy.ListenEndPoint} (see MitmProxyListenAddress / MitmProxyListenPort), then log in.",
                "Teleport",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var configuredListenPort = settings.GetValue<int?>("ClientInjectionPort");
        var observedGamePort = PacketCapture?.ObservedLocalClientTcpPort ?? 0;

        var injectionPort = configuredListenPort is > 0
            ? configuredListenPort.Value
            : observedGamePort;
        if (injectionPort == 0)
        {
            MessageBox.Show(this,
                "Set Settings.ClientInjectionPort in appconfig.json to the TCP port your injector listens on, " +
                "or capture traffic so the game client local port is observed (used only if ClientInjectionPort is unset). " +
                "Or enable MitmProxyEnabled and route the client through the built-in proxy.",
                "Teleport",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!TrySendInjectionPacketToLoopback(injectionPort, packet, out var lastEx))
        {
            var src = configuredListenPort is > 0
                ? "appconfig ClientInjectionPort (injector listen port)"
                : "captured game client local port";

            var refusedHint = lastEx is SocketException { SocketErrorCode: SocketError.ConnectionRefused }
                ? "\n\nNothing accepted an inbound TCP connection on that port. " +
                  "The port shown for the game in Task Manager is usually an outbound connection to the server, " +
                  "not a listen socket—another process cannot connect to it. " +
                  "Configure your injector to listen on a dedicated port, set ClientInjectionPort to that value, " +
                  "and keep using capture only if your tool really expects that behavior.\n\n" +
                  "This attempt tried both 127.0.0.1 and ::1."
                : string.Empty;

            MessageBox.Show(this,
                $"Could not send teleport packet to loopback:{injectionPort} ({src}).\n{lastEx?.Message ?? "Unknown error"}{refusedHint}",
                "Teleport",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static bool TrySendInjectionPacketToLoopback(int port, byte[] packet, out Exception? lastEx)
    {
        lastEx = null;
        foreach (var address in new[] { IPAddress.Loopback, IPAddress.IPv6Loopback })
        {
            try
            {
                using var tcp = new TcpClient();
                tcp.NoDelay = true;
                tcp.Connect(address, port);
                var stream = tcp.GetStream();
                stream.Write(packet, 0, packet.Length);
                stream.Flush();
                return true;
            }
            catch (Exception ex)
            {
                lastEx = ex;
            }
        }

        return false;
    }

    private bool TryResolveTeleportClientIndex(out ushort clientIndex)
    {
        clientIndex = 0;
        var fromCapture = PacketCapture?.ClientId ?? 0;
        if (fromCapture != 0)
        {
            clientIndex = unchecked((ushort)fromCapture);
            return true;
        }

        var text = MainView.GameState.ClientId?.Text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        return ushort.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out clientIndex) &&
               clientIndex != 0;
    }

    private void SettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var currentMac = AppConfig.GetSection("Settings").GetValue<string>("MacAddress");
        var dialog = new SelectCaptureAdapterDialog(currentMac) { Owner = this };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.SelectedMacAddress))
        {
            return;
        }

        var newMac = dialog.SelectedMacAddress;
        if (string.Equals(newMac, currentMac, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SaveMacAddressToAppConfig(newMac);
        ReloadAppConfig();
        InstallNewPacketCapture(newMac);
    }

    private void ShowInUI_OnChecked(object sender, RoutedEventArgs e)
    {
        ShowNewInUI = true;
    }

    private void ShowInUI_OnUnchecked(object sender, RoutedEventArgs e)
    {
        ShowNewInUI = false;
    }

    private void ClearClientState_OnClick(object sender, RoutedEventArgs e)
    {
        CurrentClientState.Clear();
    }

    private void FilterClientState_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ClientStateObjectTypeFilterDialog(ClientStateObjectTypeFilter)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            ClientStateObjectTypeFilter = dialog.SelectedObjectTypes.ToHashSet();
            RefreshClientStateFilter();
        }
    }

    private void TrackXpButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new TrackXpDialog(_trackXpSnapshot) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _trackXpSnapshot = new TrackXpSnapshot(
            dialog.TitleLevel,
            dialog.TitleRebirth,
            dialog.TitleXp,
            dialog.DegreeLevel,
            dialog.DegreeRebirth,
            dialog.DegreeXp,
            dialog.DegreeXpUseNextPacketAsBase,
            dialog.TitleXpUseNextPacketAsBase,
            dialog.PillActive,
            dialog.ServerBonus,
            dialog.MissionArea
        );
        _trackXpEnabled = true;
    }

    private void TryTrackDegreeXpFromServerPacket(StoredPacket storedPacket)
    {
        if (_trackXpSnapshot is null)
        {
            return;
        }

        var clientId = PacketCapture?.ClientId ?? 0;
        if (clientId == 0)
        {
            return;
        }

        var (successDegree, successTitle, newDegreeXp, newTitleXp)
            = XpExtractor.TryExtractAllXpFromPacket(storedPacket.ContentBytes, clientId);

        if (!successDegree && !successTitle)
        {
            return;
        }

        if (successTitle && _trackXpSnapshot.TitleXpUseNextPacketAsBase)
        {
            _trackXpSnapshot = _trackXpSnapshot with
            {
                TitleXp = newTitleXp,
                TitleXpUseNextPacketAsBase = false
            };
        }

        if (successDegree && _trackXpSnapshot.DegreeXpUseNextPacketAsBase)
        {
            _trackXpSnapshot = _trackXpSnapshot with
            {
                DegreeXp = newDegreeXp,
                DegreeXpUseNextPacketAsBase = false
            };
        }

        if ((successTitle && _trackXpSnapshot.TitleXpUseNextPacketAsBase)
            || (successDegree && _trackXpSnapshot.DegreeXpUseNextPacketAsBase))
        {
            return;
        }

        var oldDegreeXp = _trackXpSnapshot.DegreeXp;
        var oldTitleXp = _trackXpSnapshot.TitleXp;
        long earnedDegreeXp = -1;
        long earnedTitleXp = -1;
        var nextDegreeLevel = _trackXpSnapshot.DegreeLevel;
        var nextTitleLevel = _trackXpSnapshot.TitleLevel;
        var nextDegreeRebirth = _trackXpSnapshot.DegreeRebirth;
        var nextTitleRebirth = _trackXpSnapshot.TitleRebirth;

        if (successDegree && newDegreeXp >= oldDegreeXp)
        {
            earnedDegreeXp = newDegreeXp - oldDegreeXp;
        }
        else if (successDegree)
        {
            var titleMinusOne = _trackXpSnapshot.TitleLevel - 1;
            var degreeMinusOne = _trackXpSnapshot.DegreeLevel - 1;
            var xpToLevelUp = GetXpToLevelUp(titleMinusOne, degreeMinusOne);
            earnedDegreeXp = (long)(xpToLevelUp - (ulong)oldDegreeXp + (ulong)newDegreeXp);

            nextDegreeLevel += 1;
            if (nextDegreeLevel > 60)
            {
                nextDegreeLevel = 1;
                nextDegreeRebirth = Math.Min(nextDegreeRebirth + 1, 3);
            }
        }
        else if (successTitle && newTitleXp >= oldTitleXp)
        {
            earnedTitleXp = newTitleXp - oldTitleXp;
        }
        else if (successTitle)
        {
            var titleMinusOne = _trackXpSnapshot.TitleLevel - 1;
            var degreeMinusOne = _trackXpSnapshot.DegreeLevel - 1;
            var xpToLevelUp = GetXpToLevelUp(titleMinusOne, degreeMinusOne);
            earnedTitleXp = (long)(xpToLevelUp - (ulong)oldTitleXp + (ulong)newTitleXp);

            nextTitleLevel += 1;
            if (nextTitleLevel > 60)
            {
                nextTitleLevel = 1;
                nextTitleRebirth = Math.Min(nextTitleRebirth + 1, 3);
            }
        }

        if (earnedDegreeXp <= 0 && earnedTitleXp <= 0)
        {
            return;
        }

        int? mobType = null;
        int? mobLevel = null;
        if (XpExtractor.TryFindMobKilledByClient(storedPacket.PacketParts, clientId, out var killedMobEntityId))
        {
            if (CurrentClientState.FirstOrDefault(x => x.Id == killedMobEntityId) is MobPacket mob)
            {
                mobType = mob.Type;
                mobLevel = mob.Level;
            }
        }

        if (earnedDegreeXp > 0)
        {
            WriteDegreeXpLogLine(_trackXpSnapshot, (uint)earnedDegreeXp, mobType, mobLevel);
        }

        if (earnedTitleXp > 0)
        {
            WriteTitleXpLogLine(_trackXpSnapshot, (uint)earnedTitleXp, mobType, mobLevel);
        }

        _trackXpSnapshot = _trackXpSnapshot with
        {
            DegreeXp = newDegreeXp,
            TitleXp = newTitleXp,
            DegreeLevel = nextDegreeLevel,
            DegreeRebirth = nextDegreeRebirth,
            TitleLevel = nextTitleLevel,
            TitleRebirth = nextTitleRebirth
        };
    }

    private static ulong GetXpToLevelUp(int titleMinusOne, int degreeMinusOne)
    {
        if (titleMinusOne % 60 == 59 && degreeMinusOne % 60 == 59)
        {
            return 1;
        }

        var minLevel = Math.Min(titleMinusOne, degreeMinusOne);
        var maxLevel = Math.Max(titleMinusOne, degreeMinusOne);
        return (ulong)(CharacterDataHelper.XpPerLevelBase[maxLevel] + CharacterDataHelper.XpPerLevelDelta[maxLevel] * minLevel);
    }

    private static void WriteDegreeXpLogLine(TrackXpSnapshot snapshot, uint earnedXp, int? mobType, int? mobLevel)
    {
        var outputPath = AppConfig.GetSection("Settings").GetValue<string>("OutputFolder");
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            outputPath = AppContext.BaseDirectory;
        }

        var filePath = Path.Combine(outputPath, "degree_xp.txt");
        var pill = snapshot.PillActive ? 1 : 0;
        var line = string.Join('\t', new object[]
        {
            snapshot.TitleLevel,
            snapshot.TitleRebirth,
            snapshot.DegreeLevel,
            snapshot.DegreeRebirth,
            pill,
            snapshot.ServerBonus,
            snapshot.MissionArea,
            earnedXp
        });

        if (mobType.HasValue && mobLevel.HasValue)
        {
            line += "\t" + mobType.Value + "\t" + mobLevel.Value;
        }
        File.AppendAllText(filePath, line + Environment.NewLine);
    }

    private static void WriteTitleXpLogLine(TrackXpSnapshot snapshot, uint earnedXp, int? mobType, int? mobLevel)
    {
        var outputPath = AppConfig.GetSection("Settings").GetValue<string>("OutputFolder");
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            outputPath = AppContext.BaseDirectory;
        }

        var filePath = Path.Combine(outputPath, "title_xp.txt");
        var pill = snapshot.PillActive ? 1 : 0;
        var line = string.Join('\t', new object[]
        {
            snapshot.TitleLevel,
            snapshot.TitleRebirth,
            snapshot.DegreeLevel,
            snapshot.DegreeRebirth,
            pill,
            snapshot.ServerBonus,
            snapshot.MissionArea,
            earnedXp
        });

        if (mobType.HasValue && mobLevel.HasValue)
        {
            line += "\t" + mobType.Value + "\t" + mobLevel.Value;
        }
        File.AppendAllText(filePath, line + Environment.NewLine);
    }

}