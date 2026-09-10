# Rebuilds the macOS client kit (dist\valheim-mac-mods.zip) from a Gale profile.
# Run after every profile change that friends need (new mod, updated mod, config default change):
#   powershell -ExecutionPolicy Bypass -File tools\build-mac-kit.ps1              (profile LAN-1.0)
#   powershell -ExecutionPolicy Bypass -File tools\build-mac-kit.ps1 -Profile X
# Works on any machine that has Gale with the profile imported (desktop or laptop).
param(
  [string]$Profile = 'LAN-1.0',
  [string[]]$ExcludeMods = @('bruceirons-team-BruceNetworking'),   # server-only mods never go to clients
  [string]$Out = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$src = Join-Path $env:APPDATA "com.kesomannen.gale\valheim\profiles\$Profile"
$tpl = Join-Path $PSScriptRoot 'mac-kit'                       # install-mac.command + README-MAC.md live here
$stage = Join-Path $root 'dist\mac-kit'
$mods = Join-Path $stage 'mods'
if (-not $Out) { $Out = Join-Path $root 'dist\valheim-mac-mods.zip' }

if (-not (Test-Path "$src\BepInEx\plugins")) { throw "Gale profile not found: $src (import it in Gale first, or pass -Profile)" }
foreach ($need in 'start_game_bepinex.sh', 'doorstop_config.ini', '.doorstop_version', 'doorstop_libs\libdoorstop_x64.dylib') {
  if (-not (Test-Path (Join-Path $src $need))) { throw "profile is missing $need - is BepInExPack installed in it?" }
}

Write-Host "== profile: $src"
if (Test-Path $mods) { Remove-Item $mods -Recurse -Force }
foreach ($d in 'BepInEx\core', 'BepInEx\patchers', 'BepInEx\plugins', 'BepInEx\config', 'doorstop_libs') { New-Item -ItemType Directory -Force (Join-Path $mods $d) | Out-Null }

Copy-Item "$src\BepInEx\core\*" "$mods\BepInEx\core\" -Recurse -Force
if (Get-ChildItem "$src\BepInEx\patchers" -ErrorAction SilentlyContinue) { Copy-Item "$src\BepInEx\patchers\*" "$mods\BepInEx\patchers\" -Recurse -Force }
$included = @()
foreach ($p in Get-ChildItem "$src\BepInEx\plugins" -Directory) {
  if ($ExcludeMods -contains $p.Name) { Write-Host "   skip (server-only): $($p.Name)"; continue }
  Copy-Item $p.FullName (Join-Path "$mods\BepInEx\plugins" $p.Name) -Recurse -Force
  $included += $p.Name
}
# configs: everything except per-player data files and server-only mod configs
Get-ChildItem "$src\BepInEx\config" -File | Where-Object { $_.Extension -ne '.dat' -and $_.Name -notmatch 'BruceNetworking' } | ForEach-Object { Copy-Item $_.FullName "$mods\BepInEx\config\" -Force }
Copy-Item "$src\doorstop_libs\*" "$mods\doorstop_libs\" -Force
Copy-Item "$src\doorstop_config.ini", "$src\start_game_bepinex.sh" $mods -Force
Copy-Item "$src\.doorstop_version" $mods -Force
Copy-Item "$tpl\install-mac.command", "$tpl\README-MAC.md" $stage -Force

# shell files must have LF line endings or macOS refuses them
foreach ($f in "$mods\start_game_bepinex.sh", "$stage\install-mac.command") {
  $t = [IO.File]::ReadAllText($f) -replace "`r`n", "`n"
  [IO.File]::WriteAllText($f, $t, (New-Object System.Text.UTF8Encoding($false)))
}

# zip with forward-slash entry names (Compress-Archive writes backslashes, which macOS unpacks as literal file names)
if (Test-Path $Out) { Remove-Item $Out -Force }
Push-Location $stage
& tar.exe -a -cf $Out install-mac.command README-MAC.md mods
Pop-Location

Write-Host "== mods in kit:"; $included | ForEach-Object { "   $_" }
Write-Host ("== wrote {0} ({1:N1} MB)" -f $Out, ((Get-Item $Out).Length / 1MB))
Write-Host "   Send the zip to the Mac player; they run install-mac.command again (overwrites in place)."
