using System.Text.Json;
using System.Text.Json.Serialization;

namespace SphServer.Godot.Scripts.Bootstrap;

public sealed class BuildInfo
{
	[JsonPropertyName("sha")]
	public string Sha { get; set; } = "";

	[JsonPropertyName("shortSha")]
	public string ShortSha { get; set; } = "";

	[JsonPropertyName("tag")]
	public string Tag { get; set; } = "";

	[JsonPropertyName("message")]
	public string Message { get; set; } = "";

	[JsonPropertyName("committedAt")]
	public string CommittedAt { get; set; } = "";

	[JsonPropertyName("builtAt")]
	public string BuiltAt { get; set; } = "";

	[JsonPropertyName("channelTipTag")]
	public string ChannelTipTag { get; set; } = "windows-debug-slim";

	public string DisplayLine()
	{
		var msg = string.IsNullOrWhiteSpace(Message) ? "(unknown)" : Message.Trim();
		var when = FormatWhen(CommittedAt);
		if (string.IsNullOrWhiteSpace(when))
		{
			when = FormatWhen(BuiltAt);
		}

		var sha = string.IsNullOrWhiteSpace(ShortSha)
			? (Sha.Length >= 12 ? Sha[..12] : Sha)
			: ShortSha;
		if (string.IsNullOrWhiteSpace(sha))
		{
			sha = "unknown";
		}

		return string.IsNullOrWhiteSpace(when)
			? $"{msg} ({sha})"
			: $"{msg} · {when} ({sha})";
	}

	public static BuildInfo? TryLoad(string path)
	{
		if (!File.Exists(path))
		{
			return null;
		}

		try
		{
			return JsonSerializer.Deserialize<BuildInfo>(File.ReadAllText(path));
		}
		catch
		{
			return null;
		}
	}

	public static BuildInfo? TryParse(string json)
	{
		try
		{
			return JsonSerializer.Deserialize<BuildInfo>(json);
		}
		catch
		{
			return null;
		}
	}

	private static string FormatWhen(string iso)
	{
		if (string.IsNullOrWhiteSpace(iso))
		{
			return "";
		}

		if (DateTimeOffset.TryParse(iso, out var dto))
		{
			return dto.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
		}

		return iso;
	}
}
