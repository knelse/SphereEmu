# Export indoor base-tile shells only (no walkable) as translucent-blue GLBs.
# Same cluster membership as export_all_indoor_clusters.ps1.
param(
    [string]$ManifestPath = "D:/1/indoor-clusters-manifest.json",
    [string]$OutRoot = "",
    [int]$MaxClusters = 0,
    [switch]$SkipManifestRebuild
)

$ErrorActionPreference = "Stop"
$ToolsDir = $PSScriptRoot
. (Join-Path $ToolsDir "GodotPath.ps1")
$godot = Resolve-GodotExecutable
$RepoRoot = Split-Path $ToolsDir -Parent

if (-not $SkipManifestRebuild) {
    Write-Host "Building cluster manifest..."
    python (Join-Path $ToolsDir "build_indoor_cluster_manifest.py")
    if ($LASTEXITCODE -ne 0) { throw "manifest build failed" }
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $OutRoot) {
    $stamp = Get-Date -Format "yyyy-M-d_HH-mm-ss"
    $OutRoot = "D:/1/${stamp}_indoor-tiles"
}
New-Item -ItemType Directory -Force -Path $OutRoot | Out-Null

$clusters = @($manifest.clusters)
if ($MaxClusters -gt 0 -and $MaxClusters -lt $clusters.Count) {
    $clusters = $clusters[0..($MaxClusters - 1)]
}
$total = $clusters.Count
$ok = 0
$fail = 0
$logPath = Join-Path $OutRoot "_export_log.txt"
"godot=$godot clusters=$total max=$MaxClusters mesh_grace=$($manifest.mesh_grace_m) mode=tiles_no_walkable" |
    Set-Content -LiteralPath $logPath -Encoding UTF8

Write-Host "Exporting $total indoor tile clusters (translucent blue, no walkable) -> $OutRoot"
$sw = [System.Diagnostics.Stopwatch]::StartNew()

for ($i = 0; $i -lt $total; $i++) {
    $c = $clusters[$i]
    $id = [int]$c.id
    $label = [string]$c.label
    $cx = [double]$c.center[0]
    $cy = [double]$c.center[1]
    $cz = [double]$c.center[2]
    $radius = [double]$c.radius
    $outGlb = Join-Path $OutRoot ("cluster_{0}.glb" -f $id)
    $tempManifest = Join-Path $OutRoot ("cluster_{0}_manifest.txt" -f $id)
    $tempWalkable = Join-Path $OutRoot ("cluster_{0}_walkable.glb" -f $id)

    $members = [string]$c.members
    if (-not $members) {
        $members = "D:/1/indoor-cluster-members/cluster_{0:D3}.json" -f $id
    }
    Write-Host ("[{0}/{1}] id={2} n={3} r={4} {5}" -f `
        ($i + 1), $total, $id, $c.count, $radius, $label)

    $godotArgs = @(
        "--path", $RepoRoot,
        "--headless",
        "-s", "Tools/export_nearby_objects_glb.gd",
        "--",
        "--center", "$cx", "$cy", "$cz",
        "--radius", "$radius",
        "--members", $members,
        "--no-walkable",
        "--indoor-base-only",
        "--out", $outGlb
    )
    $proc = Start-Process -FilePath $godot -ArgumentList $godotArgs -Wait -PassThru -NoNewWindow
    $code = $proc.ExitCode
    if (Test-Path -LiteralPath $tempManifest) {
        Remove-Item -LiteralPath $tempManifest -Force
    }
    if (Test-Path -LiteralPath $tempWalkable) {
        Remove-Item -LiteralPath $tempWalkable -Force
    }
    $line = "id=$id exit=$code out=$outGlb exists=$(Test-Path -LiteralPath $outGlb)"
    Add-Content -LiteralPath $logPath -Value $line -Encoding UTF8
    if ($code -eq 0 -and (Test-Path -LiteralPath $outGlb)) {
        $ok++
        Write-Host ("  ok {0}" -f (Split-Path $outGlb -Leaf))
    } else {
        $fail++
        Write-Warning "FAILED id=$id exit=$code"
    }
}

$sw.Stop()
$summary = "done ok=$ok fail=$fail elapsed_s=$([int]$sw.Elapsed.TotalSeconds) out=$OutRoot"
Add-Content -LiteralPath $logPath -Value $summary -Encoding UTF8
Write-Host $summary
if ($fail -gt 0) { exit 1 }
exit 0
