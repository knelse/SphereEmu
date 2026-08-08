using System.IO;
using System.Net.Http;
using System.Text.Json;
using Godot;
using NetHttpClient = System.Net.Http.HttpClient;
using IoFileAccess = global::System.IO.FileAccess;

namespace SphServer.Godot.Scripts.Bootstrap;

/// <summary>
///     Exported builds: optional code/runtime self-update, ensure heavy packs are present, then MainServer.
///     Editor / full project tree: skip downloads and go straight to MainServer.
/// </summary>
public partial class AssetBootstrap : Control
{
	private const string MainServerScene = "res://Godot/Scenes/MainServer.tscn";
	private const string DefaultReleaseBaseUrl =
		"https://github.com/knelse/SphereEmu/releases/download/asset-bundles";
	private const string DefaultGithubRepo = "knelse/SphereEmu";
	private const string DefaultTipTag = "windows-debug-slim";
	private const string SlimZipName = "SphServer-windows-debug-slim.zip";

	private static readonly JsonDocumentOptions AppSettingsJsonOptions = new()
	{
		CommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true
	};

	private static readonly string[] HeavyPackIds = ["models", "terrain", "textures", "terrainbake"];

	private Label? statusLabel;
	private Label? detailLabel;
	private Label? installedLabel;
	private Label? latestLabel;
	private ProgressBar? progressBar;
	private Button? retryButton;
	private Button? updateButton;
	private Button? continueButton;
	private Button? changeVersionButton;
	private Button? useTipButton;
	private ItemList? versionList;
	private VBoxContainer? versionPickerPanel;

	private string? lastError;
	private BuildInfo? installedInfo;
	private BuildInfo? tipInfo;
	private UpdateChannelState channel = new();
	private GithubReleaseClient releases = new(DefaultGithubRepo, DefaultTipTag, SlimZipName);
	private IReadOnlyList<VersionListEntry> versionEntries = [];
	private bool busy;
	private bool awaitingUserChoice;

	public override void _Ready()
	{
		BuildUi();
		_ = RunBootstrapAsync();
	}

	private void BuildUi()
	{
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

		var margins = new MarginContainer();
		margins.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		margins.AddThemeConstantOverride("margin_left", 48);
		margins.AddThemeConstantOverride("margin_top", 40);
		margins.AddThemeConstantOverride("margin_right", 48);
		margins.AddThemeConstantOverride("margin_bottom", 40);
		AddChild(margins);

		var root = new VBoxContainer
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill
		};
		margins.AddChild(root);

		statusLabel = new Label
		{
			Text = "Starting…",
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			AutowrapMode = TextServer.AutowrapMode.WordSmart
		};
		statusLabel.AddThemeFontSizeOverride("font_size", 22);
		root.AddChild(statusLabel);

		installedLabel = new Label
		{
			Text = "",
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			AutowrapMode = TextServer.AutowrapMode.WordSmart
		};
		root.AddChild(installedLabel);

		latestLabel = new Label
		{
			Text = "",
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			AutowrapMode = TextServer.AutowrapMode.WordSmart
		};
		root.AddChild(latestLabel);

		detailLabel = new Label
		{
			Text = "",
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			AutowrapMode = TextServer.AutowrapMode.WordSmart
		};
		root.AddChild(detailLabel);

		progressBar = new ProgressBar
		{
			MinValue = 0,
			MaxValue = 1,
			Value = 0,
			ShowPercentage = true,
			CustomMinimumSize = new Vector2(0, 28),
			SizeFlagsHorizontal = SizeFlags.ExpandFill
		};
		root.AddChild(progressBar);

		var actions = new HBoxContainer
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill
		};
		root.AddChild(actions);

		updateButton = new Button { Text = "Update", Visible = false };
		updateButton.Pressed += OnUpdatePressed;
		actions.AddChild(updateButton);

		continueButton = new Button { Text = "Continue", Visible = false };
		continueButton.Pressed += OnContinuePressed;
		actions.AddChild(continueButton);

		changeVersionButton = new Button { Text = "Change version…", Visible = false };
		changeVersionButton.Pressed += OnChangeVersionPressed;
		actions.AddChild(changeVersionButton);

		useTipButton = new Button { Text = "Follow latest (tip)", Visible = false };
		useTipButton.Pressed += OnUseTipPressed;
		actions.AddChild(useTipButton);

		retryButton = new Button { Text = "Retry", Visible = false };
		retryButton.Pressed += () =>
		{
			retryButton.Visible = false;
			lastError = null;
			_ = RunBootstrapAsync();
		};
		actions.AddChild(retryButton);

		versionPickerPanel = new VBoxContainer
		{
			Visible = false,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill
		};
		root.AddChild(versionPickerPanel);
		versionPickerPanel.AddChild(new Label
		{
			Text = "Select a build (last 20 master commits):",
			SizeFlagsHorizontal = SizeFlags.ExpandFill
		});
		versionList = new ItemList
		{
			CustomMinimumSize = new Vector2(0, 220),
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill
		};
		versionList.ItemActivated += OnVersionActivated;
		versionPickerPanel.AddChild(versionList);
		var applyPin = new Button { Text = "Install selected" };
		applyPin.Pressed += OnInstallSelectedPressed;
		versionPickerPanel.AddChild(applyPin);
	}

	private async Task RunBootstrapAsync()
	{
		if (busy)
		{
			return;
		}

		busy = true;
		SetActionButtonsVisible(false);
		if (versionPickerPanel is not null)
		{
			versionPickerPanel.Visible = false;
		}

		try
		{
			if (OS.HasFeature("editor") || HasProjectModelsOnRes())
			{
				SetStatus("Dev project detected — loading MainServer…");
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
				GoToMainServer();
				busy = false;
				return;
			}

			var installDir = GetInstallDirectory();
			var settings = LoadUpdaterSettings(installDir);
			releases = new GithubReleaseClient(settings.GithubRepo, settings.TipTag, SlimZipName);
			channel = UpdateChannelState.Load(
				Path.Combine(installDir, "updates", "channel.json"),
				settings.UpdateChannelMode,
				settings.UpdateChannelTag);

			installedInfo = BuildInfo.TryLoad(Path.Combine(installDir, "build-info.json"));
			SetInstalledLabel(installedInfo);

			SetStatus("Checking for updates…");
			tipInfo = await releases.FetchBuildInfoAsync(releases.TipBuildInfoUrl());
			SetLatestLabel(tipInfo);

			var targetTag = channel.IsPin ? channel.Tag! : releases.TipTag;
			var targetInfo = channel.IsPin
				? await releases.FetchBuildInfoAsync(releases.TagBuildInfoUrl(targetTag))
				: tipInfo;

			if (channel.IsPin && targetInfo is not null)
			{
				SetLatestLabel(targetInfo, pinned: true);
			}

			var behind = IsBehind(installedInfo, channel.IsPin ? targetInfo : tipInfo);
			awaitingUserChoice = true;
			SetStatus(behind
				? (channel.IsPin ? "Pinned build differs from installed." : "A newer build is available.")
				: "Build is up to date.");
			SetDetail(behind
				? "Update to apply code/config/packet definitions, or Continue with the installed build. Packs always refresh next."
				: "Continue to refresh asset packs and start the server.");

			if (updateButton is not null)
			{
				updateButton.Visible = behind;
				updateButton.Text = channel.IsPin ? "Install pinned build" : "Update to latest";
			}

			if (continueButton is not null)
			{
				continueButton.Visible = true;
			}

			if (changeVersionButton is not null)
			{
				changeVersionButton.Visible = true;
			}

			if (useTipButton is not null)
			{
				useTipButton.Visible = channel.IsPin;
			}

			// Allow Update / Change version while we wait (do not hold busy).
			busy = false;

			// Wait until user picks Update / Continue / Change version (Continue resumes packs).
			while (awaitingUserChoice)
			{
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			}

			return;
		}
		catch (Exception ex)
		{
			lastError = ex.ToString();
			GD.PrintErr(lastError);
			SetStatus("Update check failed.");
			SetDetail(ex.Message);
			if (progressBar is not null)
			{
				progressBar.Value = 0;
			}

			if (retryButton is not null)
			{
				retryButton.Visible = true;
			}

			if (continueButton is not null)
			{
				continueButton.Visible = true;
				continueButton.Text = "Continue offline";
			}

			busy = false;
			awaitingUserChoice = true;
			while (awaitingUserChoice)
			{
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			}
		}
	}

	private async Task ContinueWithPacksAsync()
	{
		if (busy)
		{
			return;
		}

		busy = true;
		SetActionButtonsVisible(false);
		if (versionPickerPanel is not null)
		{
			versionPickerPanel.Visible = false;
		}

		try
		{
			await EnsurePacksAndStartAsync();
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
		finally
		{
			busy = false;
		}
	}

	private void OnContinuePressed()
	{
		if (!awaitingUserChoice)
		{
			return;
		}

		awaitingUserChoice = false;
		_ = ContinueWithPacksAsync();
	}

	private void OnUpdatePressed()
	{
		if (!awaitingUserChoice || busy)
		{
			return;
		}

		awaitingUserChoice = false;
		_ = ApplyUpdateAsync(channel.IsPin ? channel.Tag! : releases.TipTag, pin: channel.IsPin);
	}

	private void OnUseTipPressed()
	{
		var installDir = GetInstallDirectory();
		channel.Mode = UpdateChannelState.ModeTip;
		channel.Tag = null;
		channel.Save(Path.Combine(installDir, "updates", "channel.json"));
		awaitingUserChoice = false;
		_ = RunBootstrapAsync();
	}

	private async void OnChangeVersionPressed()
	{
		if (busy)
		{
			return;
		}

		try
		{
			SetStatus("Loading version list…");
			versionEntries = await releases.ListMasterPrereleasesAsync(20);
			if (versionList is null || versionPickerPanel is null)
			{
				return;
			}

			versionList.Clear();
			foreach (var entry in versionEntries)
			{
				versionList.AddItem(entry.DisplayLine());
			}

			versionPickerPanel.Visible = true;
			if (useTipButton is not null)
			{
				useTipButton.Visible = true;
			}

			SetDetail(versionEntries.Count == 0
				? "No master-* prereleases found yet."
				: "Pick a build, then Install selected — or Follow latest (tip).");
		}
		catch (Exception ex)
		{
			SetStatus("Could not list versions.");
			SetDetail(ex.Message);
		}
	}

	private void OnInstallSelectedPressed()
	{
		if (versionList is null || versionList.GetSelectedItems().Length == 0)
		{
			SetDetail("Select a version in the list first.");
			return;
		}

		var index = versionList.GetSelectedItems()[0];
		if (index < 0 || index >= versionEntries.Count)
		{
			return;
		}

		var tag = versionEntries[index].Tag;
		awaitingUserChoice = false;
		_ = ApplyUpdateAsync(tag, pin: true);
	}

	private void OnVersionActivated(long index)
	{
		if (index < 0 || index >= versionEntries.Count)
		{
			return;
		}

		awaitingUserChoice = false;
		_ = ApplyUpdateAsync(versionEntries[(int)index].Tag, pin: true);
	}

	private async Task ApplyUpdateAsync(string tag, bool pin)
	{
		if (busy)
		{
			return;
		}

		busy = true;
		SetActionButtonsVisible(false);
		try
		{
			var installDir = GetInstallDirectory();
			channel.Mode = pin ? UpdateChannelState.ModePin : UpdateChannelState.ModeTip;
			channel.Tag = pin ? tag : null;
			channel.Save(Path.Combine(installDir, "updates", "channel.json"));

			var zipUrl = string.Equals(tag, releases.TipTag, StringComparison.Ordinal)
				? releases.TipZipUrl()
				: releases.TagZipUrl(tag);

			var progress = new Progress<(string status, double? fraction)>(p =>
			{
				SetStatus(p.status);
				if (p.fraction is { } f && progressBar is not null)
				{
					progressBar.Value = Math.Clamp(f, 0, 1);
				}
			});

			await WindowsUpdateApplier.ApplyAndRestartAsync(zipUrl, installDir, progress);
			SetStatus("Update staged — exiting to apply…");
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			GetTree().Quit();
		}
		catch (Exception ex)
		{
			lastError = ex.ToString();
			GD.PrintErr(lastError);
			SetStatus("Update failed.");
			SetDetail(ex.Message);
			if (retryButton is not null)
			{
				retryButton.Visible = true;
			}

			if (continueButton is not null)
			{
				continueButton.Visible = true;
			}

			awaitingUserChoice = true;
		}
		finally
		{
			busy = false;
		}
	}

	private async Task EnsurePacksAndStartAsync()
	{
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
				foreach (var old in Directory.GetFiles(packsDir, $"{id}-*"))
				{
					try
					{
						File.Delete(old);
					}
					catch
					{
						/* ignore */
					}
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

	private static bool IsBehind(BuildInfo? installed, BuildInfo? target)
	{
		if (target is null || string.IsNullOrWhiteSpace(target.Sha))
		{
			return false;
		}

		if (installed is null || string.IsNullOrWhiteSpace(installed.Sha))
		{
			return true;
		}

		return !string.Equals(installed.Sha, target.Sha, StringComparison.OrdinalIgnoreCase);
	}

	private void SetInstalledLabel(BuildInfo? info)
	{
		if (installedLabel is null)
		{
			return;
		}

		installedLabel.Text = info is null
			? "Installed: (no build-info.json — update recommended)"
			: $"Installed: {info.DisplayLine()}";
	}

	private void SetLatestLabel(BuildInfo? info, bool pinned = false)
	{
		if (latestLabel is null)
		{
			return;
		}

		if (info is null)
		{
			latestLabel.Text = pinned ? "Pinned: (unavailable)" : "Latest: (unavailable)";
			return;
		}

		latestLabel.Text = pinned
			? $"Pinned: {info.DisplayLine()}"
			: $"Latest: {info.DisplayLine()}";
	}

	private void SetActionButtonsVisible(bool visible)
	{
		if (updateButton is not null)
		{
			updateButton.Visible = visible;
		}

		if (continueButton is not null)
		{
			continueButton.Visible = visible;
			continueButton.Text = "Continue";
		}

		if (changeVersionButton is not null)
		{
			changeVersionButton.Visible = visible;
		}

		if (useTipButton is not null)
		{
			useTipButton.Visible = visible && channel.IsPin;
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

	private static string GetInstallDirectory()
	{
		return Path.GetDirectoryName(OS.GetExecutablePath()) ?? ".";
	}

	private static string GetPacksDirectory()
	{
		return Path.Combine(GetInstallDirectory(), "packs");
	}

	private sealed class UpdaterSettings
	{
		public string GithubRepo { get; init; } = DefaultGithubRepo;
		public string TipTag { get; init; } = DefaultTipTag;
		public string? UpdateChannelMode { get; init; }
		public string? UpdateChannelTag { get; init; }
	}

	private static UpdaterSettings LoadUpdaterSettings(string installDir)
	{
		var result = new UpdaterSettings();
		try
		{
			var settingsPath = Path.Combine(installDir, "appsettings.json");
			if (!File.Exists(settingsPath))
			{
				return result;
			}

			using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath), AppSettingsJsonOptions);
			var root = doc.RootElement;
			var repo = root.TryGetProperty("GithubRepo", out var repoEl) ? repoEl.GetString() : null;
			var tip = root.TryGetProperty("UpdateTipReleaseTag", out var tipEl) ? tipEl.GetString() : null;
			var mode = root.TryGetProperty("UpdateChannel", out var modeEl) ? modeEl.GetString() : null;
			var tag = root.TryGetProperty("UpdateChannelTag", out var tagEl) ? tagEl.GetString() : null;

			return new UpdaterSettings
			{
				GithubRepo = string.IsNullOrWhiteSpace(repo) ? DefaultGithubRepo : repo!,
				TipTag = string.IsNullOrWhiteSpace(tip) ? DefaultTipTag : tip!,
				UpdateChannelMode = mode,
				UpdateChannelTag = tag
			};
		}
		catch (Exception ex)
		{
			GD.PushWarning($"AssetBootstrap: could not read updater settings ({ex.Message})");
			return result;
		}
	}

	private static string ResolveReleaseBaseUrl()
	{
		try
		{
			var settingsPath = Path.Combine(GetInstallDirectory(), "appsettings.json");
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
