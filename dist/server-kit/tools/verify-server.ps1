# One-shot health check for the dedicated server machine. Safe to run any time (read-only).
$root = Split-Path $PSScriptRoot -Parent
$S = Join-Path $root 'server'
$expected = @('AzuAutoStore.dll','AzuCraftyBoxes.dll','BruceQoL.dll','CW_Jesse.BetterNetworking.dll','Endurance.dll','Jotunn.dll','Advize_PlantEverything_TEMP.dll')
$optional = @('BruceNetworking.dll','Serverside_Simulations_TEMP.dll')

Write-Host "== server folder: $S"
if (-not (Test-Path "$S\valheim_server.exe")) { Write-Host 'MISSING valheim_server.exe' -ForegroundColor Red }
if (-not (Test-Path "$S\winhttp.dll")) { Write-Host 'MISSING winhttp.dll (BepInEx doorstop) - mods will not load' -ForegroundColor Red }

Write-Host "`n== plugins"
$have = Get-ChildItem "$S\BepInEx\plugins" -Filter *.dll -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name
foreach ($e in $expected) { if ($have -contains $e) { Write-Host "  ok       $e" } else { Write-Host "  MISSING  $e" -ForegroundColor Red } }
foreach ($o in $optional) { if ($have -contains $o) { Write-Host "  TOGGLE ON  $o (server-only; bn-off.bat / sss-off.bat to disable)" -ForegroundColor Yellow } }
foreach ($h in $have) { if (($expected + $optional) -notcontains $h) { Write-Host "  UNEXPECTED $h" -ForegroundColor Yellow } }

Write-Host "`n== launch script"
$bat = Join-Path $PSScriptRoot 'start-lan-server.bat'
$w = (Select-String -Path $bat -Pattern '^set WORLD_NAME=(.*)$').Matches[0].Groups[1].Value
$p = (Select-String -Path $bat -Pattern '^set PASSWORD=(.*)$').Matches[0].Groups[1].Value
if ($w -eq 'CHANGEME' -or $p -eq 'CHANGEME') { Write-Host "  WORLD_NAME/PASSWORD still CHANGEME in start-lan-server.bat" -ForegroundColor Red } else { Write-Host "  world '$w', password set ($($p.Length) chars)" }

Write-Host "`n== last server log"
$log = "$S\BepInEx\LogOutput.log"
if (Test-Path $log) {
  Select-String -Path $log -Pattern 'plugins to load|Opened Steam server|HarmonyException|MissingMethod|Could not load' | ForEach-Object { "  " + $_.Line.Substring(0, [Math]::Min(110, $_.Line.Length)) } | Select-Object -First 8
} else { Write-Host '  (no log yet - server never started here)' }

Write-Host "`n== firewall"
$rules = Get-NetFirewallRule -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -match 'Valheim dedicated' }
if ($rules) { $rules | ForEach-Object { "  {0,-45} {1}" -f $_.DisplayName, $_.Profile } } else { Write-Host '  no all-profile rules - run firewall-allow-server.ps1 as admin' -ForegroundColor Red }

Write-Host "`n== tailscale"
$ts = 'C:\Program Files\Tailscale\tailscale.exe'
if (Test-Path $ts) { "  ip: " + (& $ts ip -4 2>&1 | Select-Object -First 1); & $ts status 2>&1 | Select-Object -First 6 | ForEach-Object { "  $_" } } else { Write-Host '  Tailscale not installed' -ForegroundColor Yellow }

Write-Host "`n== LAN addresses (give the GL.iNet one to LAN players)"
Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue | Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } | ForEach-Object { "  {0,-16} {1}" -f $_.IPAddress, $_.InterfaceAlias }
