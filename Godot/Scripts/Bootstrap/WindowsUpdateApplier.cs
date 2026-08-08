using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Godot;

namespace SphServer.Godot.Scripts.Bootstrap;

/// <summary>
///     Downloads a slim zip, extracts to staging, and spawns a PowerShell helper that replaces files after exit.
/// </summary>
public static class WindowsUpdateApplier
{
	private static readonly string[] PreserveNames =
	[
		"packs",
		"logs",
		"updates",
		"appsettings.json",
		"sph.db",
		"sph.db-lock",
		"sph-log.db",
		"sph-log.db-lock"
	];

	public static async Task ApplyAndRestartAsync(
		string zipUrl,
		string installDir,
		IProgress<(string status, double? fraction)>? progress = null,
		CancellationToken ct = default)
	{
		var updatesDir = Path.Combine(installDir, "updates");
		var stagingDir = Path.Combine(updatesDir, "staging");
		var zipPath = Path.Combine(updatesDir, "pending.zip");
		var applyScript = Path.Combine(updatesDir, "apply-update.ps1");
		var logPath = Path.Combine(updatesDir, "apply-update.log");

		Directory.CreateDirectory(updatesDir);
		if (Directory.Exists(stagingDir))
		{
			Directory.Delete(stagingDir, true);
		}

		Directory.CreateDirectory(stagingDir);

		progress?.Report(("Downloading build…", 0));
		await DownloadFileAsync(zipUrl, zipPath, progress, ct);

		progress?.Report(("Extracting…", null));
		ZipFile.ExtractToDirectory(zipPath, stagingDir, overwriteFiles: true);

		var exeName = Path.GetFileName(OS.GetExecutablePath());
		if (string.IsNullOrWhiteSpace(exeName))
		{
			exeName = "SphServer.exe";
		}

		var pid = global::System.Environment.ProcessId;
		var script = BuildApplyScript(installDir, stagingDir, exeName, pid, logPath, PreserveNames);
		await File.WriteAllTextAsync(applyScript, script, Encoding.UTF8, ct);

		progress?.Report(("Restarting to apply update…", 1));

		var psi = new ProcessStartInfo
		{
			FileName = "powershell.exe",
			Arguments =
				$"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{applyScript}\"",
			UseShellExecute = false,
			CreateNoWindow = true,
			WorkingDirectory = updatesDir
		};
		Process.Start(psi);
	}

	private static string BuildApplyScript(
		string installDir,
		string stagingDir,
		string exeName,
		int waitPid,
		string logPath,
		string[] preserveNames)
	{
		var preserveList = string.Join(", ", preserveNames.Select(n => $"'{n.Replace("'", "''")}'"));
		var installEsc = installDir.Replace("'", "''");
		var stagingEsc = stagingDir.Replace("'", "''");
		var exeEsc = exeName.Replace("'", "''");
		var logEsc = logPath.Replace("'", "''");

		return $$"""
		         $ErrorActionPreference = 'Stop'
		         $log = '{{logEsc}}'
		         function Write-Log([string]$msg) {
		           $line = ("{0:o} {1}" -f [DateTimeOffset]::UtcNow, $msg)
		           Add-Content -LiteralPath $log -Value $line -Encoding utf8
		         }
		         Write-Log 'apply-update started'
		         $pidToWait = {{waitPid}}
		         try {
		           $p = Get-Process -Id $pidToWait -ErrorAction SilentlyContinue
		           if ($p) {
		             Write-Log "waiting for pid $pidToWait"
		             Wait-Process -Id $pidToWait -Timeout 120 -ErrorAction SilentlyContinue
		           }
		         } catch {
		           Write-Log "wait error: $_"
		         }
		         Start-Sleep -Seconds 1

		         $install = '{{installEsc}}'
		         $staging = '{{stagingEsc}}'
		         $preserve = @({{preserveList}})
		         $exeName = '{{exeEsc}}'

		         Get-ChildItem -LiteralPath $staging -Force | ForEach-Object {
		           $name = $_.Name
		           if ($preserve -contains $name) {
		             Write-Log "skip preserve name from staging: $name"
		             return
		           }
		           $dest = Join-Path $install $name
		           if ($_.PSIsContainer) {
		             if (Test-Path -LiteralPath $dest) {
		               Remove-Item -LiteralPath $dest -Recurse -Force
		             }
		             Copy-Item -LiteralPath $_.FullName -Destination $dest -Recurse -Force
		           } else {
		             Copy-Item -LiteralPath $_.FullName -Destination $dest -Force
		           }
		           Write-Log "copied $name"
		         }

		         $exe = Join-Path $install $exeName
		         Write-Log "starting $exe"
		         Start-Process -FilePath $exe -WorkingDirectory $install
		         Write-Log 'apply-update done'
		         """;
	}

	private static async Task DownloadFileAsync(
		string url,
		string destPath,
		IProgress<(string status, double? fraction)>? progress,
		CancellationToken ct)
	{
		var tempPath = destPath + ".partial";
		if (File.Exists(tempPath))
		{
			File.Delete(tempPath);
		}

		using var client = GithubReleaseClient.CreateHttpClient();
		using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
		response.EnsureSuccessStatusCode();
		var total = response.Content.Headers.ContentLength ?? -1;
		await using (var input = await response.Content.ReadAsStreamAsync(ct))
		await using (var output = new FileStream(tempPath, FileMode.Create, global::System.IO.FileAccess.Write,
					   FileShare.None, 1024 * 128, true))
		{
			var buffer = new byte[1024 * 128];
			long readTotal = 0;
			int read;
			while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
			{
				await output.WriteAsync(buffer.AsMemory(0, read), ct);
				readTotal += read;
				if (total > 0)
				{
					progress?.Report(($"Downloading build… {readTotal / (1024 * 1024)} / {total / (1024 * 1024)} MB",
						(double)readTotal / total));
				}
				else
				{
					progress?.Report(($"Downloading build… {readTotal / (1024 * 1024)} MB", null));
				}
			}
		}

		if (File.Exists(destPath))
		{
			File.Delete(destPath);
		}

		File.Move(tempPath, destPath);
	}
}
