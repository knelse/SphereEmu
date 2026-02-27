using System;
using System.Collections.Generic;
using LiteDB;
using SpherePacketVisualEditor;

namespace PacketLogViewer.Models;

public class StoredPacket
{
    [BsonId] public int Id { get; set; }
    public byte[] ContentBytes { get; set; }
    public PacketSource Source { get; set; }
    public DateTime Timestamp { get; set; }
    public bool HiddenByDefault { get; set; }
    public bool Favorite { get; set; }
    public PacketTypes? PacketType { get; set; }
    public ushort TargetId { get; set; }
    public ObjectType? ObjectType { get; set; }
    public PacketAnalyzeState AnalyzeState { get; set; } = PacketAnalyzeState.UNDEF;
    public List<PacketPart> PacketParts { get; set; } = new ();
    public int NumberInSequence { get; set; }

    [BsonIgnore] public string SourceStr => new (Source.ToString()[0], 1);
    [BsonIgnore] public string ContentString => Convert.ToHexString(ContentBytes);

    [BsonIgnore] public List<PacketAnalyzeData.PacketAnalyzeData> AnalyzeResult { get; set; } = new ();
}