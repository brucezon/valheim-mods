# Valheim LAN server kit (laptop)

1. Steam → Library → filter **Tools** → **Valheim Dedicated Server** → Install (2 GB, same account).
   (If you skip this, the setup script downloads it with SteamCMD instead.)
2. Unzip this kit anywhere (e.g. `C:\valheim-lan\`). In PowerShell inside that folder:
   ```
   powershell -ExecutionPolicy Bypass -File .\laptop-setup.ps1
   ```
   It finds the Steam server install, links `server\` to it, and lays our BepInEx + plugins +
   configs over it.
3. Follow `SERVER-LAPTOP.md` from step 2: world name + password, Tailscale, firewall, launch, verify.

Gale is not involved: it manages the game client, not `valheim_server.exe`.

Layout after setup:
```
laptop-setup.ps1
SERVER-LAPTOP.md
server\            <- link to the Steam "Valheim dedicated server" folder (or server-steamcmd\)
server-overlay\    <- what the script copies into the server (BepInEx, doorstop, winhttp.dll)
server-saves\      <- world saves (created on first launch)
tools\             <- start-lan-server.bat, toggles, firewall + verify scripts, benched dlls
```
