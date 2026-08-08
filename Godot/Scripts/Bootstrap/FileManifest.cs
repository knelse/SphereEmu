using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SphServer.Godot.Scripts.Bootstrap;

public sealed class FileManifest
{
	[JsonPropertyName("sha")]
	public string Sha { get; set; } = "";

	[JsonPropertyName("tag")]
	public string Tag { get; set; } = "";

	[JsonPropertyName("algorithm")]
	public string Algorithm { get; set; } = "sha256";

	[JsonPropertyName("files")]
	public Dictionary<string, FileManifestEntry> Files { get; set; } =
		new(StringComparer.OrdinalIgnoreCase);

	public static FileManifest? TryParse(string json)
	{
		try
		{
			var manifest = JsonSerializer.Deserialize<FileManifest>(json);
			if (manifest?.Files is null || manifest.Files.Count == 0)
			{
				return null;
			}

			manifest.Files = new Dictionary<string, FileManifestEntry>(manifest.Files,
				StringComparer.OrdinalIgnoreCase);
			return manifest;
		}
		catch
		{
			return null;
		}
	}

	public static FileManifest? TryLoad(string path)
	{
		if (!File.Exists(path))
		{
			return null;
		}

		try
		{
			return TryParse(File.ReadAllText(path));
		}
		catch
		{
			return null;
		}
	}

	/// <summary>
	///     Returns relative paths that are missing or whose sha256 does not match.
	/// </summary>
	public List<string> FindMismatches(string installDir)
	{
		var bad = new List<string>();
		foreach (var (rel, entry) in Files)
		{
			if (string.IsNullOrWhiteSpace(rel) || entry is null || string.IsNullOrWhiteSpace(entry.Sha256))
			{
				continue;
			}

			var full = Path.Combine(installDir, rel.Replace('/', Path.DirectorySeparatorChar));
			if (!File.Exists(full))
			{
				bad.Add(rel);
				continue;
			}

			var actual = HashFileSha256(full);
			if (!string.Equals(actual, entry.Sha256, StringComparison.OrdinalIgnoreCase))
			{
				bad.Add(rel);
			}
		}

		return bad;
	}

	public static string HashFileSha256(string path)
	{
		using var stream = File.OpenRead(path);
		var hash = SHA256.HashData(stream);
		return Convert.ToHexString(hash).ToLowerInvariant();
	}
}

public sealed class FileManifestEntry
{
	[JsonPropertyName("sha256")]
	public string Sha256 { get; set; } = "";

	[JsonPropertyName("bytes")]
	public long Bytes { get; set; }
}
