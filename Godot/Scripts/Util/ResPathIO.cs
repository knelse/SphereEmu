using System.Text;
using Godot;
using GodotFileAccess = global::Godot.FileAccess;
using IoDirectory = global::System.IO.Directory;
using IoFile = global::System.IO.File;
using IoPath = global::System.IO.Path;

namespace SphServer.Godot.Scripts.Util;

/// <summary>
///     Read helpers that work for both packed <c>res://</c> / <c>user://</c> paths and real filesystem
///     paths. <c>System.IO.File</c> + <see cref="ProjectSettings.GlobalizePath"/> fails for
///     resources that only exist inside a PCK/zip resource pack (slim exports).
/// </summary>
public static class ResPathIO
{
	public static bool IsVirtualPath(string path) =>
		path.Contains("://", StringComparison.Ordinal);

	public static bool FileExists(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return false;
		}

		if (IsVirtualPath(path))
		{
			return GodotFileAccess.FileExists(path);
		}

		var absolute = path.Replace('/', IoPath.DirectorySeparatorChar);
		return IoFile.Exists(absolute);
	}

	public static bool TryReadAllBytes(string path, out byte[] bytes)
	{
		bytes = [];
		if (string.IsNullOrWhiteSpace(path))
		{
			return false;
		}

		if (IsVirtualPath(path))
		{
			if (!GodotFileAccess.FileExists(path))
			{
				return false;
			}

			using var file = GodotFileAccess.Open(path, GodotFileAccess.ModeFlags.Read);
			if (file is null)
			{
				return false;
			}

			bytes = file.GetBuffer((long)file.GetLength());
			return true;
		}

		var absolute = path.Replace('/', IoPath.DirectorySeparatorChar);
		if (!IoFile.Exists(absolute))
		{
			return false;
		}

		bytes = IoFile.ReadAllBytes(absolute);
		return true;
	}

	public static bool TryReadAllText(string path, out string text, Encoding? encoding = null)
	{
		text = "";
		if (!TryReadAllBytes(path, out var bytes))
		{
			return false;
		}

		encoding ??= Encoding.UTF8;
		text = encoding.GetString(bytes);
		if (text.Length > 0 && text[0] == '\uFEFF')
		{
			text = text[1..];
		}

		return true;
	}

	/// <summary>True when <paramref name="directory"/> contains at least one non-dir entry ending with <paramref name="suffix"/>.</summary>
	public static bool DirectoryHasFileSuffix(string directory, string suffix)
	{
		if (string.IsNullOrWhiteSpace(directory))
		{
			return false;
		}

		var dir = directory.TrimEnd('/', '\\') + (IsVirtualPath(directory) ? "/" : IoPath.DirectorySeparatorChar.ToString());

		if (IsVirtualPath(directory))
		{
			using var da = DirAccess.Open(dir);
			if (da is null)
			{
				return false;
			}

			da.ListDirBegin();
			while (true)
			{
				var name = da.GetNext();
				if (string.IsNullOrEmpty(name))
				{
					break;
				}

				if (name is "." or ".." || da.CurrentIsDir())
				{
					continue;
				}

				if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}

			return false;
		}

		var absolute = dir.Replace('/', IoPath.DirectorySeparatorChar);
		return IoDirectory.Exists(absolute)
			   && IoDirectory.GetFiles(absolute, "*" + suffix).Length > 0;
	}

	/// <summary>Enumerate non-directory file names (not full paths) in a virtual or filesystem directory.</summary>
	public static IEnumerable<string> EnumerateFileNames(string directory, string? suffix = null)
	{
		if (string.IsNullOrWhiteSpace(directory))
		{
			yield break;
		}

		if (IsVirtualPath(directory))
		{
			var dir = directory.TrimEnd('/') + "/";
			using var da = DirAccess.Open(dir);
			if (da is null)
			{
				yield break;
			}

			da.ListDirBegin();
			while (true)
			{
				var name = da.GetNext();
				if (string.IsNullOrEmpty(name))
				{
					break;
				}

				if (name is "." or ".." || da.CurrentIsDir())
				{
					continue;
				}

				if (suffix is null || name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
				{
					yield return name;
				}
			}

			yield break;
		}

		var absolute = directory.Replace('/', IoPath.DirectorySeparatorChar);
		if (!IoDirectory.Exists(absolute))
		{
			yield break;
		}

		var pattern = suffix is null ? "*" : "*" + suffix;
		foreach (var file in IoDirectory.GetFiles(absolute, pattern))
		{
			yield return IoPath.GetFileName(file);
		}
	}

	public static string JoinVirtual(string root, params string[] parts)
	{
		var sb = new StringBuilder(root.TrimEnd('/', '\\'));
		foreach (var part in parts)
		{
			if (string.IsNullOrEmpty(part))
			{
				continue;
			}

			sb.Append('/');
			sb.Append(part.Replace('\\', '/').Trim('/'));
		}

		return sb.ToString();
	}
}
