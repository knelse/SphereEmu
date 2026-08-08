using System.Text.Json;
using System.Text.Json.Serialization;

namespace SphServer.Godot.Scripts.Bootstrap;

public sealed class UpdateChannelState
{
	public const string ModeTip = "tip";
	public const string ModePin = "pin";

	[JsonPropertyName("mode")]
	public string Mode { get; set; } = ModeTip;

	[JsonPropertyName("tag")]
	public string? Tag { get; set; }

	public bool IsPin =>
		string.Equals(Mode, ModePin, StringComparison.OrdinalIgnoreCase)
		&& !string.IsNullOrWhiteSpace(Tag);

	public static UpdateChannelState Load(string path, string? appsettingsMode, string? appsettingsTag)
	{
		UpdateChannelState state;
		if (File.Exists(path))
		{
			try
			{
				state = JsonSerializer.Deserialize<UpdateChannelState>(File.ReadAllText(path))
						?? new UpdateChannelState();
			}
			catch
			{
				state = new UpdateChannelState();
			}
		}
		else
		{
			state = new UpdateChannelState();
		}

		if (!string.IsNullOrWhiteSpace(appsettingsMode))
		{
			state.Mode = appsettingsMode.Trim();
		}

		if (!string.IsNullOrWhiteSpace(appsettingsTag))
		{
			state.Tag = appsettingsTag.Trim();
			if (string.Equals(state.Mode, ModePin, StringComparison.OrdinalIgnoreCase))
			{
				// keep pin
			}
			else if (!string.IsNullOrWhiteSpace(state.Tag))
			{
				state.Mode = ModePin;
			}
		}

		return state;
	}

	public void Save(string path)
	{
		var dir = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(dir))
		{
			Directory.CreateDirectory(dir);
		}

		var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
		File.WriteAllText(path, json);
	}
}
