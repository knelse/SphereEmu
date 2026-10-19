using System.Globalization;
using System.Text;
using SphServer.Server.Config;
using SphServer.Shared.Db;

namespace SphServer.Server.Simulation;

public enum NameRejectReason
{
	None = 0,
	Empty,
	TooLong,
	BadCharacters,
	LeadingOrTrailingSpace,
	Duplicate,
	Blocked
}

public readonly record struct NameCheckResult (bool Ok, NameRejectReason Reason)
{
	public static NameCheckResult Accept () => new (true, NameRejectReason.None);

	public static NameCheckResult Reject (NameRejectReason reason) => new (false, reason);
}

/// <summary>
/// Skeleton fold, then empty, length, characters, spaces, duplicates, and the blocklist
/// </summary>
public static class NamePolicy
{
	public const int MaxLength = 19;

	private static readonly string[] Languages = ["en", "ru", "pt", "es", "it", "fr", "de"];

	private static readonly object LoadLock = new ();

	private static HashSet<string>? terms;

	public static NameCheckResult Check (string? name)
	{
		if (string.IsNullOrEmpty (name))
		{
			return NameCheckResult.Reject (NameRejectReason.Empty);
		}

		if (char.IsWhiteSpace (name[0]) || char.IsWhiteSpace (name[^1]))
		{
			return NameCheckResult.Reject (NameRejectReason.LeadingOrTrailingSpace);
		}

		if (name.Length > MaxLength)
		{
			return NameCheckResult.Reject (NameRejectReason.TooLong);
		}

		foreach (var c in name)
		{
			if (c == ' ')
			{
				continue;
			}

			if (!char.IsLetter (c))
			{
				return NameCheckResult.Reject (NameRejectReason.BadCharacters);
			}
		}

		if (IsDuplicate (name))
		{
			return NameCheckResult.Reject (NameRejectReason.Duplicate);
		}

		var skeleton = Fold (name);
		foreach (var term in Terms ())
		{
			if (term.Length > 0 && skeleton.Contains (term, StringComparison.Ordinal))
			{
				return NameCheckResult.Reject (NameRejectReason.Blocked);
			}
		}

		return NameCheckResult.Accept ();
	}

	private static bool IsDuplicate (string name)
	{
		if (DbConnection.Db is null)
		{
			return false;
		}

		foreach (var character in DbConnection.Characters.FindAll ())
		{
			if (character.Name is not null
				&& string.Equals (character.Name, name, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}

	private static HashSet<string> Terms ()
	{
		if (terms is not null)
		{
			return terms;
		}

		lock (LoadLock)
		{
			if (terms is not null)
			{
				return terms;
			}

			var loaded = new HashSet<string> (StringComparer.Ordinal);
			foreach (var lang in Languages)
			{
				LoadLanguage (loaded, lang);
			}

			terms = loaded;
			return terms;
		}
	}

	private static void LoadLanguage (HashSet<string> loaded, string lang)
	{
		var text = ReadRes (lang) ?? ReadDisk (lang);
		if (text is null)
		{
			return;
		}

		foreach (var raw in text.Split ('\n'))
		{
			var line = raw.Trim ().TrimEnd ('\r');
			if (line.Length == 0 || line[0] == '#' || line.StartsWith ("//", StringComparison.Ordinal))
			{
				continue;
			}

			var folded = Fold (line);
			if (folded.Length > 0)
			{
				loaded.Add (folded);
			}
		}
	}

	private static string? ReadRes (string lang)
	{
		try
		{
			var path = $"res://Data/Names/{lang}.txt";
			if (!global::Godot.FileAccess.FileExists (path))
			{
				return null;
			}

			var file = global::Godot.FileAccess.Open (path, global::Godot.FileAccess.ModeFlags.Read);
			return file?.GetAsText ();
		}
		catch
		{
			return null;
		}
	}

	private static string? ReadDisk (string lang)
	{
		foreach (var dir in CandidateDirs ())
		{
			var path = Path.Combine (dir, lang + ".txt");
			if (!File.Exists (path))
			{
				continue;
			}

			return File.ReadAllText (path);
		}

		return null;
	}

	private static IEnumerable<string> CandidateDirs ()
	{
		yield return Path.Combine (Directory.GetCurrentDirectory (), "Data", "Names");
		yield return Path.Combine (AppContext.BaseDirectory, "Data", "Names");
		string repo;
		try
		{
			repo = ServerConfig.AppConfig.RepositoryPath;
		}
		catch
		{
			yield break;
		}

		if (!string.IsNullOrWhiteSpace (repo))
		{
			yield return Path.Combine (repo, "Data", "Names");
		}
	}

	private static string Fold (string name)
	{
		var lower = name.ToLowerInvariant ();
		var mapped = MapLetters (lower);
		var marks = StripMarks (mapped);
		var letters = KeepLetters (marks);
		var digraphs = ReplaceDigraphs (letters);
		return Collapse (digraphs);
	}

	private static string StripMarks (string value)
	{
		var decomposed = value.Normalize (NormalizationForm.FormKD);
		var builder = new StringBuilder (decomposed.Length);
		foreach (var c in decomposed)
		{
			if (CharUnicodeInfo.GetUnicodeCategory (c) == UnicodeCategory.NonSpacingMark)
			{
				continue;
			}

			builder.Append (c);
		}

		return builder.ToString ();
	}

	private static string MapLetters (string value)
	{
		var builder = new StringBuilder (value.Length);
		foreach (var c in value)
		{
			builder.Append (MapChar (c));
		}

		return builder.ToString ();
	}

	private static string MapChar (char c) => c switch
	{
		'ё' => "e",
		'ß' => "ss",
		'ö' or 'œ' => "oe",
		'ä' or 'æ' => "ae",
		'а' => "a",
		'е' => "e",
		'о' => "o",
		'р' => "p",
		'с' => "c",
		'у' => "y",
		'х' => "x",
		'і' or 'ї' => "i",
		'ј' => "j",
		'ѕ' => "s",
		'һ' => "h",
		'ԁ' => "d",
		'ԛ' => "q",
		'ԝ' => "w",
		'к' => "k",
		'м' => "m",
		'т' => "t",
		'в' => "b",
		'н' => "h",
		_ => c.ToString ()
	};

	private static string KeepLetters (string value)
	{
		var builder = new StringBuilder (value.Length);
		foreach (var c in value)
		{
			if (char.IsLetter (c))
			{
				builder.Append (c);
			}
		}

		return builder.ToString ();
	}

	private static string ReplaceDigraphs (string value)
	{
		var current = value;
		while (true)
		{
			var next = current.Replace ("yo", "e", StringComparison.Ordinal)
				.Replace ("sz", "ss", StringComparison.Ordinal);
			if (next == current)
			{
				return current;
			}

			current = next;
		}
	}

	private static string Collapse (string value)
	{
		if (value.Length == 0)
		{
			return value;
		}

		var builder = new StringBuilder (value.Length);
		var prev = '\0';
		foreach (var c in value)
		{
			if (c == prev)
			{
				continue;
			}

			builder.Append (c);
			prev = c;
		}

		return builder.ToString ();
	}
}

/// <summary>
/// Gender 0 or 1, hair color and face 0..3, head and hair style at least 0
/// </summary>
public static class LookPolicy
{
	public static bool Check (int gender, int hairColor, int face, int head, int hairStyle)
	{
		if (gender is not (0 or 1))
		{
			return false;
		}

		if (hairColor is < 0 or > 3 || face is < 0 or > 3)
		{
			return false;
		}

		if (head < 0 || hairStyle < 0)
		{
			return false;
		}

		return true;
	}
}
