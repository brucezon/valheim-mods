# Valheim LAN server - laptop setup. Run from the folder you unzipped the kit into:
#   powershell -ExecutionPolicy Bypass -File .\laptop-setup.ps1
#
# Finds the Valheim Dedicated Server (Steam -> Library -> Tools -> "Valheim Dedicated Server"), or
# downloads it with SteamCMD if it is not installed, then lays BepInEx + our plugins/configs over it.
# Re-runnable. Pass -ServerDir "<path>" to point at a server folder somewhere else.
param([string]$ServerDir = '')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$link = Join-Path $root 'server'
$overlay = Join-Path $root 'server-overlay'
Write-Host "== kit root: $root"

# 1) locate an existing Steam install of the dedicated server
if (-not $ServerDir) {
  $candidates = @()
  foreach ($base in @('C:\Program Files (x86)\Steam', 'C:\Program Files\Steam', "$env:ProgramFiles\Steam")) {
    $candidates += Join-Path $base 'steamapps\common\Valheim dedicated server'
    $vdf = Join-Path $base 'steamapps\libraryfolders.vdf'
    if (Test-Path $vdf) {
      foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) {
        $candidates += Join-Path ($m.Groups[1].Value -replace '\\\\', '\') 'steamapps\common\Valheim dedicated server'
      }
    }
  }
  $ServerDir = $candidates | Where-Object { Test-Path (Join-Path $_ 'valheim_server.exe') } | Select-Object -First 1
}

# 2) or download it with SteamCMD
if (-not $ServerDir) {
  Write-Host '== no Steam "Valheim Dedicated Server" install found - downloading with SteamCMD (~2 GB)'
  $steam = Join-Path $root 'steamcmd'; $ServerDir = Join-Path $root 'server-steamcmd'
  if (-not (Test-Path (Join-Path $steam 'steamcmd.exe'))) {
    New-Item -ItemType Directory -Force $steam | Out-Null
    Invoke-WebRequest 'https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip' -OutFile (Join-Path $steam 'steamcmd.zip') -UseBasicParsing
    Expand-Archive (Join-Path $steam 'steamcmd.zip') $steam -Force
  }
  for ($i = 1; $i -le 2; $i++) {
    & (Join-Path $steam 'steamcmd.exe') +force_install_dir $ServerDir +login anonymous +app_update 896660 validate +quit
    if (Test-Path (Join-Path $ServerDir 'valheim_server.exe')) { break }
    Write-Host "  steamcmd exit $LASTEXITCODE, retrying..."
  }
  if (-not (Test-Path (Join-Path $ServerDir 'valheim_server.exe'))) { throw 'valheim_server.exe missing after SteamCMD - check the output above' }
}
Write-Host "== server: $ServerDir"

# 3) make <kit>\server point at it, so tools\*.bat and *.ps1 find it
if (Test-Path $link) {
  $item = Get-Item $link -Force
  if ($item.LinkType -eq 'Junction' -or $item.LinkType -eq 'SymbolicLink') { $item.Delete() } elseif ((Resolve-Path $link).Path -ne (Resolve-Path $ServerDir).Path) { throw "$link exists and is a real folder - remove or rename it" }
}
if (-not (Test-Path $link)) { New-Item -ItemType Junction -Path $link -Target $ServerDir | Out-Null; Write-Host "== linked $link -> $ServerDir" }

# 4) overlay BepInEx + mods + configs
Write-Host '== applying BepInEx + mods overlay'
Copy-Item (Join-Path $overlay '*') $ServerDir -Recurse -Force
Get-ChildItem $overlay -Force -Filter '.doorstop_version' | ForEach-Object { Copy-Item $_.FullName $ServerDir -Force }

Write-Host '== done. Next:'
Write-Host '   1. edit tools\start-lan-server.bat  (WORLD_NAME, PASSWORD)'
Write-Host '   2. run tools\firewall-allow-server.ps1 as Administrator'
Write-Host '   3. install Tailscale, sign in, share this machine (invite link)'
Write-Host '   4. double-click tools\start-lan-server.bat, then run tools\verify-server.ps1'
