using System.Globalization;
using System.Text;
using Godot;
using SphServer.Shared.GameData.Enums;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Godot.Scripts.Objects;

/// <summary>
/// Editor tool: rebuilds NPC nodes under the <c>NPCs</c> parent from the TSV file
/// (<c>Sphere.Game/SpawnData/NPC/npc.txt</c>) and name list (<c>_rnms.txt</c>).
/// Mirrors the old <c>NpcSpawnTscnWriter</c> flow but runs from a button on the NPCs node.
/// </summary>
[Tool]
public partial class NpcsFill : Node3D
{
	[Export]
	public string InputTsvPath { get; set; } = @"d:\SphereDev\SphereSource\SphereEmu\Sphere.Game\SpawnData\NPC\npc.txt";

	[Export]
	public string RnmsPath { get; set; } = @"d:\SphereDev\SphereSource\SphereEmu\Sphere.GameDataDecode\language\_rnms.txt";

	[Export]
	public string NpcInteractableScenePath { get; set; } = "res://Godot/Scenes/npc_interactable.tscn";

	[ExportToolButton("Rebuild NPCs")]
	public Callable RebuildNpcsButton => Callable.From(RebuildNpcs);

	public void RebuildNpcs()
	{
		foreach (var child in GetChildren())
		{
			child.Free();
		}

		if (!WorldObjectDumpFillCommon.TryLoadPackedScene(NpcInteractableScenePath, "NpcsFill", out var npcScene) || npcScene is null)
		{
			return;
		}

		if (!WorldObjectDumpFillCommon.TryReadTextFile(InputTsvPath, "NpcsFill", out var inputText))
		{
			return;
		}

		Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
		string[] rnmsLines;
		try
		{
			rnmsLines = File.ReadAllLines(RnmsPath, Encoding.GetEncoding(1251));
		}
		catch (Exception ex)
		{
			GD.PushError($"NpcsFill: failed to read rnms file '{RnmsPath}': {ex.Message}");
			return;
		}

		var usedNames = new HashSet<string>(StringComparer.Ordinal);
		var stats = new Stats();

		foreach (var (lineNumber, parts) in WorldObjectDumpFillCommon.EnumerateDataLines(inputText))
		{
			stats.RowsConsidered++;

			if (parts.Length < 13)
			{
				stats.ParseErrors++;
				GD.PushWarning($"NpcsFill: npc.txt line {lineNumber}: expected ≥13 columns, got {parts.Length}. Skipping.");
				continue;
			}

			if (!TryParseHexInt32(parts[0], out var id) || id <= 0)
			{
				stats.ParseErrors++;
				GD.PushWarning($"NpcsFill: npc.txt line {lineNumber}: bad ID '{parts[0]}'. Skipping.");
				continue;
			}

			if (!Enum.TryParse<ObjectType>(parts[1].Trim(), ignoreCase: true, out var objectType))
			{
				stats.ParseErrors++;
				GD.PushWarning($"NpcsFill: npc.txt line {lineNumber}: bad ObjectType '{parts[1]}'. Skipping.");
				continue;
			}

			if (!WorldObjectDumpFillCommon.TryParseDouble(parts[3], out var x)
				|| !WorldObjectDumpFillCommon.TryParseDouble(parts[4], out var y)
				|| !WorldObjectDumpFillCommon.TryParseDouble(parts[5], out var z))
			{
				stats.ParseErrors++;
				GD.PushWarning($"NpcsFill: npc.txt line {lineNumber}: bad X/Y/Z. Skipping.");
				continue;
			}

			if (!WorldObjectDumpFillCommon.TryParseAngle(parts[6], out var angleEncoded))
			{
				stats.ParseErrors++;
				GD.PushWarning($"NpcsFill: npc.txt line {lineNumber}: bad Angle. Skipping.");
				continue;
			}

			if (!int.TryParse(parts[7].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var nameId))
			{
				stats.ParseErrors++;
				GD.PushWarning($"NpcsFill: npc.txt line {lineNumber}: bad NameId '{parts[7]}'. Skipping.");
				continue;
			}

			if (!int.TryParse(parts[12].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var npcTypeRaw))
			{
				stats.ParseErrors++;
				GD.PushWarning($"NpcsFill: npc.txt line {lineNumber}: bad NpcType '{parts[12]}'. Skipping.");
				continue;
			}

			var displayName = ResolveDisplayName(rnmsLines, nameId);
			var nodeNameBase = $"NPC_{id.ToString(CultureInfo.InvariantCulture)}_{StripLeadingNpcPrefixes(displayName)}";
			nodeNameBase = CollapseDuplicateNpcPrefix(nodeNameBase);
			var nodeName = MakeUniqueNodeName(SanitizeNodeName(nodeNameBase), usedNames);

			var modelName = (parts[9] ?? string.Empty).Trim();
			var iconName = (parts[11] ?? string.Empty).Trim();
			var npcType = (NpcType)npcTypeRaw;

			ApplyNpcTypeFixups(objectType, ref modelName, ref iconName, ref npcType);

			var instance = npcScene.Instantiate<Node3D>();
			if (instance is not NpcInteractable npc)
			{
				stats.ParseErrors++;
				GD.PushError($"NpcsFill: scene root is not NpcInteractable ({npcScene.ResourcePath}).");
				return;
			}

			npc.Name = nodeName;
			npc.Position = new Vector3((float)x, -(float)y, -(float)z);
			npc.Angle = angleEncoded;

			if (id is >= 0 and <= ushort.MaxValue)
			{
				npc.ID = (ushort)id;
			}

			npc.ObjectType = objectType;
			npc.NameID = 4000 + nameId;
			npc.ModelName = modelName;
			npc.IconName = iconName;
			npc.NpcType = npcType;
			npc.VendorItemTierMax = 15;

			AddChild(npc);
			WorldObjectDumpFillCommon.SetOwnerIfEditor(this, npc);
			stats.Spawned++;
		}

		GD.Print($"NpcsFill: considered={stats.RowsConsidered}, spawned={stats.Spawned}, parseErrors={stats.ParseErrors}");
	}

	private static void ApplyNpcTypeFixups(ObjectType objectType, ref string modelName, ref string iconName, ref NpcType npcType)
	{
		var modelNameTrimmed = modelName.Trim();
		modelName = modelNameTrimmed.StartsWith("npc", StringComparison.OrdinalIgnoreCase) ? modelNameTrimmed : string.Empty;

		var iconNameTrimmed = iconName.Trim();
		iconName = iconNameTrimmed.StartsWith("npc_", StringComparison.OrdinalIgnoreCase) ? iconNameTrimmed : string.Empty;

		if (objectType == ObjectType.NpcBanker)
		{
			iconName = "npc_banker";
		}

		switch (objectType)
		{
			case ObjectType.NpcQuestTitle:
				modelName = Random.Shared.Next(3) switch
				{
					0 => "npc06",
					1 => "npc07",
					_ => "npc08"
				};
				break;
			case ObjectType.NpcBanker:
				modelName = "npc29d";
				break;
			case ObjectType.NpcQuestKarma:
				modelName = "npc58";
				break;
			case ObjectType.NpcQuestDegree:
				modelName = "npc59";
				break;
		}

		npcType = objectType switch
		{
			ObjectType.NpcBanker => NpcType.Banker,
			ObjectType.NpcTournament => NpcType.Tournament,
			ObjectType.NpcGuilder => NpcType.Guilder,
			ObjectType.NpcQuestDegree => NpcType.QuestDegree,
			ObjectType.NpcQuestTitle => NpcType.QuestTitle,
			ObjectType.NpcQuestKarma => NpcType.QuestKarma,
			_ => npcType
		};
	}

	private static bool TryParseHexInt32(string s, out int value)
	{
		s = (s ?? string.Empty).Trim();
		if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			s = s[2..];
		}

		return int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
	}

	private static string ResolveDisplayName(string[] rnmsLines, int nameId)
	{
		var key = (4000 + nameId).ToString(CultureInfo.InvariantCulture);
		foreach (var line in rnmsLines)
		{
			var trimmed = line.TrimStart();
			if (trimmed.Length == 0 || trimmed.StartsWith("//", StringComparison.Ordinal))
			{
				continue;
			}

			var firstSpace = trimmed.IndexOf(' ');
			string prefix;
			string rest;
			if (firstSpace < 0)
			{
				prefix = trimmed;
				rest = string.Empty;
			}
			else
			{
				prefix = trimmed[..firstSpace];
				rest = trimmed[(firstSpace + 1)..].TrimEnd();
			}

			if (!prefix.Equals(key, StringComparison.Ordinal))
			{
				continue;
			}

			return string.IsNullOrEmpty(rest) ? nameId.ToString(CultureInfo.InvariantCulture) : rest;
		}

		return nameId.ToString(CultureInfo.InvariantCulture);
	}

	private static string SanitizeNodeName(string displayName)
	{
		var s = displayName.Replace(' ', '_');
		var sb = new StringBuilder(s.Length);
		foreach (var c in s)
		{
			if (char.IsLetterOrDigit(c) || c is '_' or '-' or '.')
			{
				sb.Append(c);
			}
			else
			{
				sb.Append('_');
			}
		}

		var result = sb.ToString();
		return string.IsNullOrEmpty(result) ? "NPC" : result;
	}

	private static string MakeUniqueNodeName(string baseName, HashSet<string> usedNames)
	{
		if (usedNames.Add(baseName))
		{
			return baseName;
		}

		for (var i = 2; ; i++)
		{
			var candidate = $"{baseName}_{i}";
			if (usedNames.Add(candidate))
			{
				return candidate;
			}
		}
	}

	private static string StripLeadingNpcPrefixes(string s)
	{
		s = (s ?? string.Empty).TrimStart();
		while (s.Length >= 4 && s.StartsWith("NPC_", StringComparison.OrdinalIgnoreCase))
		{
			s = s[4..].TrimStart();
		}

		return s;
	}

	private static string CollapseDuplicateNpcPrefix(string s)
	{
		s ??= string.Empty;
		while (s.Length >= 8 && s.StartsWith("NPC_NPC_", StringComparison.OrdinalIgnoreCase))
		{
			s = "NPC_" + s[8..];
		}

		return s;
	}

	private struct Stats
	{
		public int RowsConsidered;
		public int Spawned;
		public int ParseErrors;
	}
}

