# Run as Administrator on the machine that hosts the dedicated server.
# Allows valheim_server.exe inbound on UDP/TCP 2456-2458 for ALL network profiles.
# Windows auto-creates rules for the Public profile only; the Tailscale adapter is Private and a
# LAN behind a travel router often is too, so without this remote/LAN joins drop silently.
$exe = Join-Path (Split-Path $PSScriptRoot -Parent) 'server\valheim_server.exe'
if (-not (Test-Path $exe)) { throw "server exe not found at $exe (keep tools\ and server\ side by side)" }
New-NetFirewallRule -DisplayName 'Valheim dedicated server (UDP, all profiles)' -Direction Inbound -Action Allow -Protocol UDP -LocalPort 2456-2458 -Program $exe -Profile Domain,Private,Public | Out-Null
New-NetFirewallRule -DisplayName 'Valheim dedicated server (TCP, all profiles)' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 2456-2458 -Program $exe -Profile Domain,Private,Public | Out-Null
Get-NetFirewallRule | Where-Object { $_.DisplayName -match 'Valheim dedicated' } | ForEach-Object { "{0,-45} {1}" -f $_.DisplayName, $_.Profile }
Write-Host "Done for $exe. Both rules should list Domain, Private, Public."
