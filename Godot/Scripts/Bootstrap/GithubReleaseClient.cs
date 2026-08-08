using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using NetHttpClient = System.Net.Http.HttpClient;

namespace SphServer.Godot.Scripts.Bootstrap;

public sealed class GithubReleaseClient
{
	private readonly string _apiBase;
	private readonly string _downloadBase;
	private readonly string _tipTag;
	private readonly string _zipName;

	public GithubReleaseClient(string ownerRepo, string tipTag, string zipName = "SphServer-windows-debug-slim.zip")
	{
		_apiBase = $"https://api.github.com/repos/{ownerRepo}";
		_downloadBase = $"https://github.com/{ownerRepo}/releases/download";
		_tipTag = tipTag;
		_zipName = zipName;
	}

	public string TipTag => _tipTag;

	public string TipBuildInfoUrl() => $"{_downloadBase}/{_tipTag}/build-info.json";

	public string TipZipUrl() => $"{_downloadBase}/{_tipTag}/{_zipName}";

	public string TagBuildInfoUrl(string tag) => $"{_downloadBase}/{tag}/build-info.json";

	public string TagZipUrl(string tag) => $"{_downloadBase}/{tag}/{_zipName}";

	public async Task<BuildInfo?> FetchBuildInfoAsync(string url, CancellationToken ct = default)
	{
		using var client = CreateHttpClient();
		using var response = await client.GetAsync(url, ct);
		if (!response.IsSuccessStatusCode)
		{
			return null;
		}

		var json = await response.Content.ReadAsStringAsync(ct);
		return BuildInfo.TryParse(json);
	}

	public async Task<IReadOnlyList<VersionListEntry>> ListMasterPrereleasesAsync(int keep = 20,
		CancellationToken ct = default)
	{
		using var client = CreateHttpClient();
		using var response = await client.GetAsync($"{_apiBase}/releases?per_page=100", ct);
		response.EnsureSuccessStatusCode();
		await using var stream = await response.Content.ReadAsStreamAsync(ct);
		using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

		var list = new List<VersionListEntry>();
		foreach (var el in doc.RootElement.EnumerateArray())
		{
			var prerelease = el.TryGetProperty("prerelease", out var pre) && pre.GetBoolean();
			var tag = el.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() : null;
			if (!prerelease || string.IsNullOrWhiteSpace(tag) || !tag.StartsWith("master-", StringComparison.Ordinal))
			{
				continue;
			}

			var title = el.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
			var published = el.TryGetProperty("published_at", out var pubEl) ? pubEl.GetString() : null;
			if (string.IsNullOrWhiteSpace(published) && el.TryGetProperty("created_at", out var createdEl))
			{
				published = createdEl.GetString();
			}

			list.Add(new VersionListEntry(tag!, title ?? tag!, published ?? ""));
		}

		return list
			.OrderByDescending(v => DateTimeOffset.TryParse(v.PublishedAt, out var dto) ? dto : DateTimeOffset.MinValue)
			.Take(keep)
			.ToList();
	}

	public static NetHttpClient CreateHttpClient()
	{
		var client = new NetHttpClient
		{
			Timeout = TimeSpan.FromHours(6)
		};
		client.DefaultRequestHeaders.UserAgent.ParseAdd("SphereEmu-AutoUpdater");
		client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
		return client;
	}
}

public readonly record struct VersionListEntry(string Tag, string Title, string PublishedAt)
{
	public string DisplayLine()
	{
		var when = PublishedAt;
		if (DateTimeOffset.TryParse(PublishedAt, out var dto))
		{
			when = dto.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
		}

		var shortTag = Tag.StartsWith("master-", StringComparison.Ordinal) && Tag.Length > 7 + 12
			? Tag[..(7 + 12)]
			: Tag;
		return string.IsNullOrWhiteSpace(when)
			? $"{Title} [{shortTag}]"
			: $"{Title} · {when} [{shortTag}]";
	}
}
