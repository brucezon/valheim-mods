# Mirror the Gale profile's plugins into the dedicated server. Run after every Gale "Pull update".
#   powershell -ExecutionPolicy Bypass -File .\sync-server-from-gale.ps1            (profile LAN-1.0)
#   powershell -ExecutionPolicy Bypass -File .\sync-server-from-gale.ps1 -Profile X (another name)
# Server configs are NOT touched: the server's config is the authority (ServerSync pushes it to clients).
param([string]$Profile = 'LAN-1.0')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$server = Join-Path $root 'server\BepInEx\plugins'
$src = Join-Path $env:APPDATA "com.kesomannen.gale\valheim\profiles\$Profile\BepInEx\plugins"
if (-not (Test-Path $src)) { throw "Gale profile plugins not found: $src  (import the profile in Gale first, or pass -Profile)" }
if (-not (Test-Path $server)) { throw "server plugins folder not found: $server (run laptop-setup.ps1 first)" }

# client-only mods never go on the server; server-only toggles are never deleted by the mirror
$clientOnly = @('*Unshamed*', '*PlantEasily*')
$serverOnly = @('BruceNetworking.dll', 'Serverside_Simulations_TEMP.dll')

if (Get-Process valheim_server -ErrorAction SilentlyContinue) { throw 'valheim_server.exe is running - close it first (it locks the dlls)' }

Write-Host "== $src"
Write-Host "== -> $server"
$args = @($src, $server, '/MIR', '/NJH', '/NJS', '/NDL', '/NP') + ($clientOnly | ForEach-Object { '/XD'; $_ }) + ($serverOnly | ForEach-Object { '/XF'; $_ })
& robocopy @args | Where-Object { $_ -match '\S' } | ForEach-Object { "  $_" }
# robocopy exit codes < 8 are success
if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE" }

Write-Host "`n== server plugins now:"
Get-ChildItem $server -Recurse -Filter *.dll | ForEach-Object { "  " + $_.FullName.Replace($server, '') }
Write-Host "`nRestart the server (tools\start-lan-server.bat)."
