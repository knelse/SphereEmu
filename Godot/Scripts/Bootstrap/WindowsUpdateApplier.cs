using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Godot;

namespace SphServer.Godot.Scripts.Bootstrap;

/// <summary>
///     Downloads a slim zip, extracts to staging, and spawns a detached helper that replaces files after exit.
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
			Directory.Delete(stagingDir, recursive: true);
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
		await File.WriteAllTextAsync(applyScript, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);

		progress?.Report(("Restarting to apply update…", 1));

		// Never shell-execute .cmd/.ps1 (often open in Notepad). Invoke cmd.exe and use `start`
		// so PowerShell is detached from Godot's process job and survives Quit.
		var startArgs =
			"/c start \"SphereEmuUpdate\" /min powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"" +
			applyScript + "\"";
		var psi = new ProcessStartInfo
		{
			FileName = "cmd.exe",
			Arguments = startArgs,
			UseShellExecute = false,
			CreateNoWindow = true,
			WorkingDirectory = updatesDir
		};
		if (Process.Start(psi) is null)
		{
			throw new InvalidOperationException($"Failed to start update helper via cmd: {applyScript}");
		}

		GD.Print($"AssetBootstrap: update helper spawned for {exeName} (pid wait {pid})");

		// Give the detached PowerShell a moment to open the log / attach to our PID before we quit.
		await Task.Delay(750, ct);
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
		         $ErrorActionPreference = 'Continue'
		         $log = '{{logEsc}}'
		         function Write-Log([string]$msg) {
		           try {
		             $line = ("{0:o} {1}" -f [DateTimeOffset]::UtcNow, $msg)
		             Add-Content -LiteralPath $log -Value $line -Encoding utf8 -ErrorAction SilentlyContinue
		           } catch {}
		         }
		         Write-Log 'apply-update started'
		         $pidToWait = {{waitPid}}
		         try {
		           $p = Get-Process -Id $pidToWait -ErrorAction SilentlyContinue
		           if ($p) {
		             Write-Log "waiting for pid $pidToWait"
		             Wait-Process -Id $pidToWait -ErrorAction SilentlyContinue
		           } else {
		             Write-Log "pid $pidToWait already gone"
		           }
		         } catch {
		           Write-Log "wait error: $($_.Exception.Message)"
		         }

		         # Extra settle time for file locks / AV.
		         Start-Sleep -Seconds 2

		         $install = '{{installEsc}}'
		         $staging = '{{stagingEsc}}'
		         $preserve = @({{preserveList}})
		         $exeName = '{{exeEsc}}'

		         function Copy-WithRetry([string]$src, [string]$dest, [bool]$isDir) {
		           $attempts = 0
		           while ($true) {
		             $attempts++
		             try {
		               if ($isDir) {
		                 if (Test-Path -LiteralPath $dest) {
		                   Remove-Item -LiteralPath $dest -Recurse -Force -ErrorAction Stop
		                 }
		                 Copy-Item -LiteralPath $src -Destination $dest -Recurse -Force -ErrorAction Stop
		               } else {
		                 $destDir = Split-Path -Parent $dest
		                 if ($destDir -and -not (Test-Path -LiteralPath $destDir)) {
		                   New-Item -ItemType Directory -Force -Path $destDir | Out-Null
		                 }
		                 if (Test-Path -LiteralPath $dest) {
		                   $bak = "$dest.pending-old"
		                   if (Test-Path -LiteralPath $bak) {
		                     Remove-Item -LiteralPath $bak -Force -ErrorAction SilentlyContinue
		                   }
		                   try {
		                     Move-Item -LiteralPath $dest -Destination $bak -Force -ErrorAction Stop
		                   } catch {
		                     # Fall through to overwrite attempt.
		                   }
		                 }
		                 Copy-Item -LiteralPath $src -Destination $dest -Force -ErrorAction Stop
		                 if (Test-Path -LiteralPath "$dest.pending-old") {
		                   Remove-Item -LiteralPath "$dest.pending-old" -Force -ErrorAction SilentlyContinue
		                 }
		               }
		               return
		             } catch {
		               Write-Log "copy attempt $attempts failed for $(Split-Path -Leaf $dest): $($_.Exception.Message)"
		               if ($attempts -ge 15) { throw }
		               Start-Sleep -Milliseconds 500
		             }
		           }
		         }

		         try {
		           Get-ChildItem -LiteralPath $staging -Force | ForEach-Object {
		             $name = $_.Name
		             if ($preserve -contains $name) {
		               Write-Log "skip preserve: $name"
		               return
		             }
		             $dest = Join-Path $install $name
		             Copy-WithRetry $_.FullName $dest $_.PSIsContainer
		             Write-Log "copied $name"
		           }

		           $exe = Join-Path $install $exeName
		           if (-not (Test-Path -LiteralPath $exe)) {
		             throw "Updated exe missing: $exe"
		           }
		           Write-Log "starting $exe"
		           Start-Process -FilePath $exe -WorkingDirectory $install
		           Write-Log 'apply-update done'
		         } catch {
		           Write-Log "FATAL: $($_.Exception.Message)"
		           exit 1
		         }
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
