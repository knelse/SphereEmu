// TODO: probably needs refactor and splitting into multiple files

using System;
using System.Collections.Generic;
using Godot;
using SphServer.Packets;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.GameData.Enums;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking.WorldObject.Serializers;
using SphServer.Shared.WorldState;
using SphServer.Sphere.Game.IngameToEmulatorTypeConverters;
using SphServer.Sphere.Game.NpcTrade.ItemsOnSale;

namespace SphServer.Sphere.Game.WorldObject;

[Tool]
public partial class NpcInteractable : WorldObject
{
    private const string PlaceholderMeshNodeName = "MeshInstance3D";
    /// <summary>Short name + unique scene id so the tree shows e.g. <c>…#Glb</c> instead of a long duplicate name.</summary>
    private const string GlbModelChildName = "Glb";
    private const string GlbModelMetaKey = "_npc_interactable_glb";
    private const string PlaceholderCheckerDdsPath = "res://Godot/Textures/npc_placeholder_checker.dds";

    [Export] public int NameID { get; set; } = 4016;

    private string _modelName = string.Empty;

    [Export]
    public string ModelName
    {
        get => _modelName;
        set
        {
            if (_modelName == value)
            {
                return;
            }

            _modelName = value;
            if (IsInsideTree())
            {
                CallDeferred (nameof (RefreshModelVisual));
            }
        }
    }

    public string ModelNameSph => ModelName + "\0";
    [Export] public string IconName { get; set; } = string.Empty;
    public string IconNameSph => IconName + "\0";
    public int IconNameLength => IconNameSph.Length;

    [Export] public NpcType NpcType { get; set; }
    [Export] public int VendorItemTierMin { get; set; }
    [Export] public int VendorItemTierMax { get; set; }
    [Export] public VendorLocation VendorLocation { get; set; }
    public readonly List<ItemDbEntry> ItemsOnSale = [];

    private NpcInteractableSerializer? serializer;

    public override void _Ready ()
    {
        RefreshModelVisual ();
        if (Engine.IsEditorHint ())
        {
            return;
        }

        base._Ready ();
        ObjectType = NpcType switch
        {
            NpcType.Banker => ObjectType.NpcBanker,
            NpcType.Guilder => ObjectType.NpcGuilder,
            NpcType.QuestTitle => ObjectType.NpcQuestTitle,
            NpcType.QuestKarma => ObjectType.NpcQuestKarma,
            NpcType.QuestDegree => ObjectType.NpcQuestDegree,
            NpcType.Tournament => ObjectType.NpcGuilder,
            _ => ObjectType.NpcTrade
        };
        
        if (VendorItemTierMax == 0 || VendorItemTierMin == 0)
        {
            SphLogger.Warning($"Vendor [{ID}] ({NpcType}) has no item tiers set");
        }
        else
        {
            GenerateItemsForSale();
        }

        serializer = new NpcInteractableSerializer (this);
    }

    /// <summary>
    /// Loads <see cref="ModelName"/> from <c>res://Godot/Models/{name}.glb</c>, or a 1×1 placeholder cube with a tileable pink checker (DDS).
    /// Runs in the editor (<see cref="Tool"/>) when exports change.
    /// </summary>
    private void RefreshModelVisual ()
    {
        var trimmed = ModelName?.Trim () ?? string.Empty;
        RemoveGlbModelChild ();

        if (string.IsNullOrEmpty (trimmed))
        {
            ShowPlaceholderCube ();
            return;
        }

        RemovePlaceholderMeshChild ();
        var glbPath = $"res://Godot/Models/{trimmed}.glb";
        if (!ResourceLoader.Exists (glbPath))
        {
            GD.PushWarning ($"NpcInteractable: GLB not found: {glbPath}");
            ShowPlaceholderCube ();
            return;
        }

        var packed = ResourceLoader.Load<PackedScene> (glbPath);
        if (packed is null)
        {
            GD.PushWarning ($"NpcInteractable: failed to load scene: {glbPath}");
            ShowPlaceholderCube ();
            return;
        }

        var root = packed.Instantiate<Node3D> ();
        root.Name = GlbModelChildName;
        root.UniqueNameInOwner = true;
        root.SetMeta (GlbModelMetaKey, true);
        AddChild (root);
        SetOwnerForEditedScene (root);
    }

    /// <summary>
    /// Removes prior GLB roots (meta-tagged or name <see cref="GlbModelChildName"/>), including Godot-renamed duplicates.
    /// </summary>
    private void RemoveGlbModelChild ()
    {
        var toRemove = new List<Node> ();
        foreach (Node child in GetChildren ())
        {
            if (child.HasMeta (GlbModelMetaKey))
            {
                toRemove.Add (child);
                continue;
            }

            var nm = child.Name.ToString ();
            if (nm == GlbModelChildName || nm.StartsWith ("NpcGlbModel", StringComparison.Ordinal))
            {
                toRemove.Add (child);
            }
        }

        foreach (var n in toRemove)
        {
            n.Free ();
        }
    }

    private void RemovePlaceholderMeshChild ()
    {
        var n = GetNodeOrNull (PlaceholderMeshNodeName);
        n?.Free ();
    }

    private void ShowPlaceholderCube ()
    {
        RemoveGlbModelChild ();

        MeshInstance3D meshInst;
        if (GetNodeOrNull (PlaceholderMeshNodeName) is MeshInstance3D existing)
        {
            meshInst = existing;
        }
        else
        {
            meshInst = new MeshInstance3D ();
            meshInst.Name = PlaceholderMeshNodeName;
            AddChild (meshInst);
            SetOwnerForEditedScene (meshInst);
        }

        var box = new BoxMesh { Size = Vector3.One };
        meshInst.Mesh = box;
        var mat = new StandardMaterial3D ();
        mat.AlbedoTexture = LoadPlaceholderCheckerTexture ();
        meshInst.MaterialOverride = mat;
    }

    private static Texture2D LoadPlaceholderCheckerTexture ()
    {
        if (ResourceLoader.Exists (PlaceholderCheckerDdsPath))
        {
            var tex = ResourceLoader.Load<Texture2D> (PlaceholderCheckerDdsPath);
            if (tex is not null)
            {
                return tex;
            }
        }

        return CreateFallbackPinkCheckerTexture ();
    }

    /// <summary>Procedural tileable fallback if the DDS resource is missing.</summary>
    private static Texture2D CreateFallbackPinkCheckerTexture ()
    {
        const int w = 64;
        const int h = 64;
        const int tile = 8;
        var img = Image.CreateEmpty (w, h, false, Image.Format.Rgba8);
        var light = new Color (1f, 0.6f, 0.8f);
        var dark = new Color (1f, 0.25f, 0.55f);
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var c = (((x / tile) + (y / tile)) & 1) == 0 ? light : dark;
                img.SetPixel (x, y, c);
            }
        }

        return ImageTexture.CreateFromImage (img);
    }

    /// <summary>
    /// Owns editor-created visuals under this instance root only. Do not use <see cref="SceneTree.EditedSceneRoot"/>;
    /// assigning the main scene root as owner can persist those nodes as direct children of the main node in the .tscn.
    /// </summary>
    private void SetOwnerForEditedScene (Node node)
    {
        if (!Engine.IsEditorHint ())
        {
            return;
        }

        node.Owner = this;
    }

    protected override List<PacketPart> GetPacketParts ()
    {
        return PacketPart.LoadDefinedPartsFromFile(NpcType);
    }

    protected override List<PacketPart> ModifyPacketParts (List<PacketPart> packetParts)
    {
        PacketPart.UpdateValue(packetParts, "name_id", NameID - 4000, 11);
        var modelName = ModelNameSph;
        if (NpcType is NpcType.Guilder)
        {
            modelName = modelName.PadRight(16, '\0');
        }

        PacketPart.UpdateValue(packetParts, "entity_type_name_length", modelName.Length, 8);
        PacketPart.UpdateValue(packetParts, "entity_type_name", modelName);
        PacketPart.UpdateValue(packetParts, "icon_name_length", IconNameLength, 8);
        PacketPart.UpdateValue(packetParts, "icon_name", IconNameSph);
        var tradeType = NpcTypeToNpcTradeTypeSph.Convert(NpcType);
        PacketPart.UpdateValue(packetParts, "npc_trade_type", tradeType, 4);
        return packetParts;
    }

    protected override byte[] PostprocessPacketBytes (byte[] packet)
    {
        packet[^1] = 0;
        return packet;
    }

    public void ClientInteraction (ushort clientID,
        ClientInteractionType interactionType = ClientInteractionType.Unknown)
    {
        ClientInteract(clientID, interactionType);
    }

    protected override void ClientInteract (ushort clientID,
        ClientInteractionType interactionType = ClientInteractionType.Unknown)
    {
        SphLogger.Info($"FROM NPC: Client [{clientID:X4}] interacts with [{ID}] {ObjectType} -- {interactionType}");
        switch (interactionType)
        {
            case ClientInteractionType.OpenTrade:
                ShowItemList(clientID);
                ShowItemContents(clientID);
                break;
            default:
                break;
        }
    }

    private void GenerateItemsForSale ()
    {
        List<ItemDbEntry> itemsOnSale;

        switch (NpcType)
        {
            case NpcType.TradeJewelry:
                itemsOnSale = ItemsOnSaleGenerator.Jewelry(VendorItemTierMin, VendorItemTierMax);
                break;
            case NpcType.TradeTravelGeneric:
                itemsOnSale = ItemsOnSaleGenerator.TravelGeneric(VendorItemTierMin, VendorItemTierMax);
                break;
            case NpcType.TradeWeapon:
                itemsOnSale = ItemsOnSaleGenerator.Weapons(VendorItemTierMin, VendorItemTierMax);
                break;
            case NpcType.TradeArmor:
                itemsOnSale = ItemsOnSaleGenerator.Armor(VendorItemTierMin, VendorItemTierMax);
                break;
            case NpcType.TradeAlchemy:
                itemsOnSale = ItemsOnSaleGenerator.Alchemy(VendorItemTierMin, VendorItemTierMax);
                break;
            case NpcType.TradeMagic:
                itemsOnSale = ItemsOnSaleGenerator.Magic(VendorItemTierMin, VendorItemTierMax);
                break;
            default:
                itemsOnSale = [];
                break;
        }

        if (itemsOnSale.Count == 0)
        {
            for (var i = 0; i < 20; i++)
            {
                var item = ItemDbEntry.CreateFromGameObject(SphObjectDb.GameObjectDataDb[3400 + i]);
                ItemsOnSale.Add(item);
            }
        }

        foreach (var item in itemsOnSale)
        {
            item.ParentContainerId = ID;
            item.Id = WorldObjectIndex.New();
            ItemsOnSale.Add(item);
        }
    }

    public int GetMaxItemsOnSale ()
    {
        return Math.Min(ItemsOnSale.Count, 74);
    }

    private void ShowItemList (ushort clientId)
    {
        var output = serializer!.ShowItemList(clientId);
        FindClientAndScheduleSend(output, clientId);
    }

    private void FindClientAndScheduleSend (byte[] packet, ushort clientId)
    {
        var client = ActiveClients.Get(clientId);
        if (client is null)
        {
            SphLogger.Warning($"Unable to find client with ID: {clientId:X4} when trading with NPC ID: {ID:X4}");
            return;
        }

        client.MaybeQueueNetworkPacketSend(packet);
    }

    private void ShowItemContents (ushort clientId)
    {
        var output = serializer!.ShowItemContents(clientId);
        FindClientAndScheduleSend(output, clientId);
    }
}