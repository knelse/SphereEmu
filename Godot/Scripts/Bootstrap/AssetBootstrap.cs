using System.IO;
using System.Net.Http;
using System.Text.Json;
using Godot;
using NetHttpClient = System.Net.Http.HttpClient;
using IoFileAccess = global::System.IO.FileAccess;

namespace SphServer.Godot.Scripts.Bootstrap;

/// <summary>
///     Exported builds: ensure heavy packs (models/terrain/textures) are present, load them, then MainServer.
///     Editor / full project tree: skip downloads and go straight to MainServer.
/// </summary>
public partial class AssetBootstrap : Control
{
	private const string MainServerScene = "res://Godot/Scenes/MainServer.tscn";
	private const string DefaultReleaseBaseUrl =
		"https://github.com/knelse/SphereEmu/releases/download/asset-bundles";

	private static readonly JsonDocumentOptions AppSettingsJsonOptions = new()
	{
		CommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true
	};

	private static readonly string[] HeavyPackIds = ["models", "terrain", "textures", "terrainbake"];

	private Label? statusLabel;
	private Label? detailLabel;
	private ProgressBar? progressBar;
	private Button? retryButton;
	private string? lastError;

	public override void _Ready()
	{
		BuildUi();
		_ = RunBootstrapAsync();
	}

	private void BuildUi()
	{
		var root = new VBoxContainer
		{
			AnchorsPreset = (int)LayoutPreset.FullRect,
			OffsetLeft = 40,
			OffsetTop = 40,
			OffsetRight = -40,
			OffsetBottom = -40
		};
		AddChild(root);

		statusLabel = new Label { Text = "Starting…" };
		statusLabel.AddThemeFontSizeOverride("font_size", 22);
		root.AddChild(statusLabel);

		detailLabel = new Label { Text = "", AutowrapMode = TextServer.AutowrapMode.WordSmart };
		root.AddChild(detailLabel);

		progressBar = new ProgressBar
		{
			MinValue = 0,
			MaxValue = 1,
			Value = 0,
			ShowPercentage = true,
			CustomMinimumSize = new Vector2(0, 24)
		};
		root.AddChild(progressBar);

		retryButton = new Button { Text = "Retry", Visible = false };
		retryButton.Pressed += () =>
		{
			retryButton.Visible = false;
			lastError = null;
			_ = RunBootstrapAsync();
		};
		root.AddChild(retryButton);
	}

	private async Task RunBootstrapAsync()
	{
		try
		{
			if (OS.HasFeature("editor") || HasProjectModelsOnRes())
			{
				SetStatus("Dev project detected — loading MainServer…");
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
				GoToMainServer();
				return;
			}

			SetStatus("Checking asset packs…");
			var baseUrl = ResolveReleaseBaseUrl().TrimEnd('/');
			var packsDir = GetPacksDirectory();
			Directory.CreateDirectory(packsDir);

			var manifestUrl = $"{baseUrl}/manifest.json";
			SetDetail($"Manifest: {manifestUrl}");
			var manifestJson = await DownloadStringAsync(manifestUrl);
			using var manifestDoc = JsonDocument.Parse(manifestJson);
			var packs = manifestDoc.RootElement.GetProperty("packs");

			var statePath = Path.Combine(packsDir, "state.json");
			var state = LoadState(statePath);

			foreach (var id in HeavyPackIds)
			{
				if (!packs.TryGetProperty(id, out var packInfo))
				{
					throw new InvalidOperationException($"Manifest is missing pack '{id}'.");
				}

				var crc = packInfo.GetProperty("crc32").GetString()
						  ?? throw new InvalidOperationException($"Pack '{id}' has no crc32.");
				var file = packInfo.GetProperty("file").GetString()
						   ?? throw new InvalidOperationException($"Pack '{id}' has no file.");
				var dest = Path.Combine(packsDir, file);
				var bytes = packInfo.TryGetProperty("bytes", out var bytesEl) ? bytesEl.GetInt64() : -1;

				var haveMatching = state.TryGetValue(id, out var localCrc)
								   && string.Equals(localCrc, crc, StringComparison.OrdinalIgnoreCase)
								   && File.Exists(dest);

				if (!haveMatching)
				{
					// Remove older files for this pack id (.pck or .zip).
					foreach (var old in Directory.GetFiles(packsDir, $"{id}-*"))
					{
						try { File.Delete(old); } catch { /* ignore */ }
					}

					var url = $"{baseUrl}/{file}";
					SetStatus($"Downloading {id}…");
					SetDetail(file);
					await DownloadFileAsync(url, dest, bytes);
					state[id] = crc;
					SaveState(statePath, state);
				}
				else
				{
					SetStatus($"Pack {id} up to date.");
					SetDetail(file);
				}
			}

			SetStatus("Loading resource packs…");
			foreach (var id in HeavyPackIds)
			{
				var file = packs.GetProperty(id).GetProperty("file").GetString()!;
				var path = Path.Combine(packsDir, file);
				SetDetail(path);
				if (!ProjectSettings.LoadResourcePack(path))
				{
					throw new InvalidOperationException($"LoadResourcePack failed for {path}");
				}
			}

			SetStatus("Starting server…");
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			GoToMainServer();
		}
		catch (Exception ex)
		{
			lastError = ex.ToString();
			GD.PrintErr(lastError);
			SetStatus("Asset bootstrap failed.");
			SetDetail(ex.Message);
			if (progressBar is not null)
			{
				progressBar.Value = 0;
			}

			if (retryButton is not null)
			{
				retryButton.Visible = true;
			}
		}
	}

	private void GoToMainServer()
	{
		var err = GetTree().ChangeSceneToFile(MainServerScene);
		if (err != Error.Ok)
		{
			throw new InvalidOperationException($"ChangeSceneToFile({MainServerScene}) failed: {err}");
		}
	}

	private static bool HasProjectModelsOnRes()
	{
		using var dir = DirAccess.Open("res://Godot/Models");
		if (dir is null)
		{
			return false;
		}

		dir.ListDirBegin();
		while (true)
		{
			var name = dir.GetNext();
			if (string.IsNullOrEmpty(name))
			{
				break;
			}

			if (name is "." or "..")
			{
				continue;
			}

			return true;
		}

		return false;
	}

	private static string GetPacksDirectory()
	{
		var exeDir = Path.GetDirectoryName(OS.GetExecutablePath()) ?? ".";
		return Path.Combine(exeDir, "packs");
	}

	private static string ResolveReleaseBaseUrl()
	{
		try
		{
			var exeDir = Path.GetDirectoryName(OS.GetExecutablePath()) ?? ".";
			var settingsPath = Path.Combine(exeDir, "appsettings.json");
			if (File.Exists(settingsPath))
			{
				using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath), AppSettingsJsonOptions);
				if (doc.RootElement.TryGetProperty("AssetPacksReleaseBaseUrl", out var urlEl))
				{
					var url = urlEl.GetString();
					if (!string.IsNullOrWhiteSpace(url))
					{
						return url!;
					}
				}
			}
		}
		catch (Exception ex)
		{
			GD.PushWarning($"AssetBootstrap: could not read AssetPacksReleaseBaseUrl ({ex.Message})");
		}

		return DefaultReleaseBaseUrl;
	}

	private static Dictionary<string, string> LoadState(string path)
	{
		var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (!File.Exists(path))
		{
			return result;
		}

		try
		{
			using var doc = JsonDocument.Parse(File.ReadAllText(path));
			if (!doc.RootElement.TryGetProperty("packs", out var packs))
			{
				return result;
			}

			foreach (var prop in packs.EnumerateObject())
			{
				if (prop.Value.TryGetProperty("crc32", out var crcEl))
				{
					var crc = crcEl.GetString();
					if (!string.IsNullOrEmpty(crc))
					{
						result[prop.Name] = crc!;
					}
				}
			}
		}
		catch
		{
			// Treat corrupt state as empty.
		}

		return result;
	}

	private static void SaveState(string path, Dictionary<string, string> state)
	{
		using var stream = new MemoryStream();
		using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
		{
			writer.WriteStartObject();
			writer.WritePropertyName("packs");
			writer.WriteStartObject();
			foreach (var (id, crc) in state)
			{
				writer.WritePropertyName(id);
				writer.WriteStartObject();
				writer.WriteString("crc32", crc);
				writer.WriteEndObject();
			}

			writer.WriteEndObject();
			writer.WriteEndObject();
		}

		File.WriteAllBytes(path, stream.ToArray());
	}

	private async Task<string> DownloadStringAsync(string url)
	{
		using var client = CreateHttpClient();
		using var response = await client.GetAsync(url);
		response.EnsureSuccessStatusCode();
		return await response.Content.ReadAsStringAsync();
	}

	private async Task DownloadFileAsync(string url, string destPath, long expectedBytes)
	{
		var tempPath = destPath + ".partial";
		if (File.Exists(tempPath))
		{
			File.Delete(tempPath);
		}

		using var client = CreateHttpClient();
		using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
		response.EnsureSuccessStatusCode();
		var total = response.Content.Headers.ContentLength ?? expectedBytes;
		await using (var input = await response.Content.ReadAsStreamAsync())
		await using (var output = new FileStream(tempPath, FileMode.Create, IoFileAccess.Write, FileShare.None,
					   1024 * 128, true))
		{
			var buffer = new byte[1024 * 128];
			long readTotal = 0;
			int read;
			while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
			{
				await output.WriteAsync(buffer.AsMemory(0, read));
				readTotal += read;
				if (total > 0 && progressBar is not null)
				{
					progressBar.Value = Math.Clamp((double)readTotal / total, 0, 1);
				}

				SetDetail($"{readTotal / (1024 * 1024)} / {(total > 0 ? total / (1024 * 1024) : '?')} MB");
			}
		}

		if (File.Exists(destPath))
		{
			File.Delete(destPath);
		}

		File.Move(tempPath, destPath);
		if (progressBar is not null)
		{
			progressBar.Value = 1;
		}
	}

	private static NetHttpClient CreateHttpClient()
	{
		return new NetHttpClient
		{
			Timeout = TimeSpan.FromHours(6)
		};
	}

	private void SetStatus(string text)
	{
		if (statusLabel is not null)
		{
			statusLabel.Text = text;
		}

		GD.Print($"AssetBootstrap: {text}");
	}

	private void SetDetail(string text)
	{
		if (detailLabel is not null)
		{
			detailLabel.Text = text;
		}
	}
}
