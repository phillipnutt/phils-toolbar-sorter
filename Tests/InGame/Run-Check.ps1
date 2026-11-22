# Runs the Toolbar Sorter in-game check (TBSCheck.cs) on a KSP install: installs the mod, runs KSP twice (phase 1,
# then phase 2 after a relaunch), archives everything in out\<time>, then removes what the test added (its plugin, its
# career save "zz_TBSCheck") and puts back what it changed: the player's own setup file, exactly as it was (the test
# starts from it), and the previous KSP.log. The mod stays installed.
# Build first: MSBuild on ..\..\PhilsToolbarSorter.csproj and on TBSCheck.csproj.
# Refuses to start if KSP is running. Closes only a KSP it started, and only after a timeout.
param([string]$KspRoot = 'C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program')
$ErrorActionPreference = 'Stop'
$K     = $KspRoot
$here  = $PSScriptRoot
$repo  = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$arch  = Join-Path $here ('out\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$pack  = "$K\GameData\zzz_DefinitiveEdition\Settings\StockToolbar.cfg"    # Phil's modpack, if present: must stay as it was
$mod   = "$K\GameData\PhilsToolbarSorter"
$setup = "$mod\PluginData\Toolbar.cfg"
$extra = @("$setup.before-per-scene", "$setup.tmp")                        # files the mod may make next to the setup

if (Get-Process KSP_x64 -ErrorAction SilentlyContinue) { 'ABORT: KSP is running'; exit 2 }
if (Test-Path "$K\saves\zz_TBSCheck") { 'ABORT: saves\zz_TBSCheck already exists'; exit 3 }
New-Item -ItemType Directory -Force $arch, "$K\GameData\zz_TBSCheck\Plugins", "$K\TBSCheck" | Out-Null

# Keep the last KSP.log and the player's setup (both put back afterwards).
Copy-Item "$K\KSP.log" "$arch\KSP.previous.log" -Force
$packHash  = if (Test-Path $pack) { (Get-FileHash $pack -Algorithm MD5).Hash } else { $null }
$setupHash = $null
if (Test-Path $setup) { Copy-Item $setup "$arch\Toolbar.player.cfg" -Force; $setupHash = (Get-FileHash $setup -Algorithm MD5).Hash; "Player setup kept: md5 $setupHash" }
$extraBefore = $extra | Where-Object { Test-Path $_ }
# Files KSP and ModuleManager rewrite on a launch: kept, put back md5-identical afterwards.
New-Item -ItemType Directory -Force "$arch\kept" | Out-Null
$kept = @("$K\settings.cfg", "$K\PartDatabase.cfg") + (Get-ChildItem "$K\GameData" -File -Filter 'ModuleManager.*' | Where-Object { $_.Extension -ne '.dll' } | ForEach-Object FullName)
$keptHash = @{}
foreach ($f in $kept) { if (Test-Path $f) { Copy-Item $f "$arch\kept\" -Force; $keptHash[$f] = (Get-FileHash $f -Algorithm MD5).Hash } }
"Kept $($keptHash.Count) files KSP rewrites"

try {
  # The mod (stays installed) and the check plugin (removed afterwards).
  New-Item -ItemType Directory -Force "$mod\Plugins", "$mod\Textures" | Out-Null
  Copy-Item "$repo\bin\Release\PhilsToolbarSorter.dll" "$mod\Plugins\" -Force
  Copy-Item "$repo\GameData\PhilsToolbarSorter\Textures\*" "$mod\Textures\" -Force
  Copy-Item "$here\bin\TBSCheck.dll" "$K\GameData\zz_TBSCheck\Plugins\" -Force

  foreach ($phase in 1, 2) {
    Set-Content "$K\TBSCheck\run.flag" "$phase" -Encoding ascii
    $p = Start-Process -FilePath "$K\KSP_x64.exe" -WorkingDirectory $K -PassThru
    $t0 = Get-Date; "Phase $phase : started KSP pid $($p.Id) at $($t0.ToString('HH:mm:ss'))"
    if (-not $p.WaitForExit(14 * 60 * 1000)) {
      'TIMEOUT after 14 min; closing the KSP this script started'
      [void]$p.CloseMainWindow(); if (-not $p.WaitForExit(60000)) { Stop-Process -Id $p.Id -Force }
    }
    "Phase $phase : KSP exited after $([int]((Get-Date) - $t0).TotalSeconds)s"
    Copy-Item "$K\KSP.log" "$arch\KSP-phase$phase.log" -Force
    if (Test-Path $setup) { Copy-Item $setup "$arch\Toolbar-after-phase$phase.cfg" -Force }
  }
  Get-ChildItem "$K\TBSCheck" -File | Where-Object { $_.Name -ne 'run.flag' } | Copy-Item -Destination $arch -Force
}
finally {
  # Remove what the test added; put back what it changed. The mod itself stays.
  foreach ($x in "$K\GameData\zz_TBSCheck", "$K\TBSCheck", "$K\saves\zz_TBSCheck") { if (Test-Path $x) { Remove-Item $x -Recurse -Force } }
  Get-ChildItem "$K\thumbs" -Filter 'zz_TBSCheck*' -ErrorAction SilentlyContinue | Remove-Item -Force
  foreach ($x in $extra) { if ((Test-Path $x) -and ($x -notin $extraBefore)) { Remove-Item $x -Force } }
  if ($setupHash) {
    Copy-Item "$arch\Toolbar.player.cfg" $setup -Force
    "Player setup restored: " + ((Get-FileHash $setup -Algorithm MD5).Hash -eq $setupHash)
  } elseif (Test-Path $setup) { Remove-Item $setup -Force }   # there was none: start fresh
  Copy-Item "$arch\KSP.previous.log" "$K\KSP.log" -Force
  foreach ($f in $keptHash.Keys) { Copy-Item (Join-Path "$arch\kept" (Split-Path $f -Leaf)) $f -Force }
  $bad = $keptHash.Keys | Where-Object { (Get-FileHash $_ -Algorithm MD5).Hash -ne $keptHash[$_] }
  "Rewritten files restored ($($keptHash.Count)); md5 mismatches: " + $(if ($bad) { $bad -join ', ' } else { 'none' })
  if ($packHash) { "Pack file unchanged: " + ((Get-FileHash $pack -Algorithm MD5).Hash -eq $packHash) }
  "Left behind: " + ((@("$K\GameData\zz_TBSCheck", "$K\TBSCheck", "$K\saves\zz_TBSCheck") | Where-Object { Test-Path $_ }) -join ', ')
  "Archive: $arch"
}
Get-Content "$arch\results-phase1.log", "$arch\results-phase2.log" -ErrorAction SilentlyContinue | Select-String 'FAIL|DONE|FATAL'
