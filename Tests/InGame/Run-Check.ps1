# Runs the Toolbar Sorter in-game check (TBSCheck.cs) on a KSP install: installs the mod, runs KSP twice (phase 1,
# then phase 2 after a relaunch), archives everything in out\<time>, then removes what the test added (its plugin, its
# career save "zz_TBSCheck", the setup file it made) and puts the previous KSP.log back. The mod stays installed.
# Build first: MSBuild on ..\..\PhilsToolbarSorter.csproj and on TBSCheck.csproj.
# Refuses to start if KSP is running, or if the install already has a Toolbar Sorter setup (it would be replaced).
# Closes only a KSP it started, and only after a timeout.
param([string]$KspRoot = 'C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program')
$ErrorActionPreference = 'Stop'
$K     = $KspRoot
$here  = $PSScriptRoot
$repo  = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$arch  = Join-Path $here ('out\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$pack  = "$K\GameData\zzz_DefinitiveEdition\Settings\StockToolbar.cfg"    # Phil's modpack, if present: must stay as it was
$setup = "$K\GameData\PhilsToolbarSorter\PluginData\Toolbar.cfg"

if (Get-Process KSP_x64 -ErrorAction SilentlyContinue) { 'ABORT: KSP is running'; exit 2 }
if (Test-Path "$K\saves\zz_TBSCheck") { 'ABORT: saves\zz_TBSCheck already exists'; exit 3 }
if (Test-Path $setup) { 'ABORT: a Toolbar Sorter setup already exists on the main install'; exit 4 }
New-Item -ItemType Directory -Force $arch, "$K\GameData\zz_TBSCheck\Plugins", "$K\TBSCheck" | Out-Null

# Keep Phil's last KSP.log (put back afterwards).
Copy-Item "$K\KSP.log" "$arch\KSP.previous.log" -Force
$packHash = if (Test-Path $pack) { (Get-FileHash $pack -Algorithm MD5).Hash } else { $null }

try {
  # The mod (stays installed) and the check plugin (removed afterwards).
  New-Item -ItemType Directory -Force "$K\GameData\PhilsToolbarSorter\Plugins" | Out-Null
  Copy-Item "$repo\bin\Release\PhilsToolbarSorter.dll" "$K\GameData\PhilsToolbarSorter\Plugins\" -Force
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
  if (Test-Path $setup) { Remove-Item $setup -Force }          # Phil starts from a fresh setup
  Copy-Item "$arch\KSP.previous.log" "$K\KSP.log" -Force
  if ($packHash) { "Pack file unchanged: " + ((Get-FileHash $pack -Algorithm MD5).Hash -eq $packHash) }
  "Left behind: " + ((@("$K\GameData\zz_TBSCheck", "$K\TBSCheck", "$K\saves\zz_TBSCheck", $setup) | Where-Object { Test-Path $_ }) -join ', ')
  "Archive: $arch"
}
Get-Content "$arch\results-phase1.log", "$arch\results-phase2.log" -ErrorAction SilentlyContinue | Select-String 'FAIL|DONE|FATAL'
