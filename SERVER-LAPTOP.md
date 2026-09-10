# Dedicated server on the laptop — setup + launch (do this once, before leaving)

The server does NOT use Gale. Gale manages game clients; the dedicated server has its own BepInEx
install inside `server\`. Everything below is copy, click, run.

## 1. Get the server onto the laptop
**Download route (kit zip, 4 MB):** unzip `valheim-lan-server-kit.zip` anywhere (e.g. `C:\valheim-lan\`),
then in PowerShell inside that folder:
```
powershell -ExecutionPolicy Bypass -File .\laptop-setup.ps1
```
It downloads SteamCMD, installs the Valheim Dedicated Server (~2 GB, anonymous, no Steam login), and
lays our BepInEx + plugins + configs over it. Re-runnable.

**Copy route (no internet):** copy `valheim-modding\server\` and `valheim-modding\tools\` from the
desktop, side by side. The scripts find `server\` relative to `tools\`, so keep them siblings.

Either way: no Gale on the server, it has its own BepInEx inside `server\`.

## 2. Set world name + password
Edit `tools\start-lan-server.bat`: `WORLD_NAME=` and `PASSWORD=` (password ≥ 5 chars, must not be
part of the world name). Saves go to `server-saves\` next to `server\` (`-savedir`), so backing up
the world = copying that one folder.

## 3. Tailscale on the laptop
1. Install https://tailscale.com/download, sign in with the same account as the desktop
   (`bruceironsthefirst@`).
2. https://login.tailscale.com/admin/machines → the laptop's row → ⋯ → **Share…** → **Copy invite
   link** → pin it in Discord.
3. Note the laptop's `100.x.x.x` address (tray icon, or `tailscale ip -4`) → pin
   `100.x.x.x:2456` + password for remote players.

## 4. Firewall (Administrator, once)
Right-click PowerShell → Run as administrator:
```
powershell -ExecutionPolicy Bypass -File <somewhere>\valheim-modding\tools\firewall-allow-server.ps1
```
It prints two rules, both `Domain, Private, Public`. Without this, joins over Tailscale (Private
profile) and often over the GL.iNet LAN drop with no error.

## 5. Launch
Double-click `tools\start-lan-server.bat`. First start generates the world (~1 min). Leave the
window open; closing it stops the server (it saves on exit).

## 6. Verify (30 seconds)
```
powershell -ExecutionPolicy Bypass -File <somewhere>\valheim-modding\tools\verify-server.ps1
```
Expect: the plugin list matches the expected set, "Opened Steam server" in the log, the two firewall
rules, and the Tailscale IP. Then join from the desktop: LAN → `<laptop GL.iNet IP>:2456`.

## Toggles (server-only, restart after)
- `tools\sss-on.bat` / `sss-off.bat` — Serverside Simulations (server owns all zones). Emergency lag lever.
- **BruceNetworking** (our server-only networking mod: ZDO send priority, LAN-first zone ownership,
  multi-peer send loop, RPC area-of-interest, wear-tick throttle; each feature has its own toggle in
  `bruceirons.BruceNetworking.cfg`) is delivered through the Gale profile → `sync-server-from-gale.ps1`.
  It coexists with BetterNetworking. Its old name was LanServerOptimizations; never load both.

## World rules (launch line)
`-preset Hard -modifier DeathPenalty Default -setkey "movestaminarate 85" -setkey "skillreductionrate 60" -setkey "playerevents"`
= Combat Hard, Death Penalty Default scaled to 60% skill loss, cheaper sprint/jump, player-based raids. Details in
`docs\WORLD-KNOBS.md`. Re-applied every start; editing the .bat changes the existing world.

## Keeping server mods in sync with the clients (Gale on the laptop)
Install Gale on the laptop too and import the profile code (`LAN-1.0`), exactly like a friend would. Gale can't run the server, but its profile folder becomes the source of
truth. After every **Pull update** in Gale, with the server closed:
```
powershell -ExecutionPolicy Bypass -File <kit>\tools\sync-server-from-gale.ps1
```
It mirrors the profile's `BepInEx\plugins` into the server (client-only mods Unshamed and PlantEasily
excluded, the SSS/LSO toggle dlls preserved) and lists the result. Expected server set: AutoStore,
CraftyBoxes, BruceQoL, BetterNetworking, Endurance, Jotunn, PlantEverything_TEMP. Then start the server. Server
configs are never overwritten: the server's config is the authority and ServerSync pushes it to clients.

Before the first sync, the kit's overlay already put the launch set in place, so the script is only
needed when the profile changes.

**Mac player?** After the same Pull update, also rebuild their kit and resend it:
```
powershell -ExecutionPolicy Bypass -File <kit>\tools\build-mac-kit.ps1
```
Writes `dist\valheim-mac-mods.zip` next to the kit from the `LAN-1.0` profile on this machine
(BepInEx macOS launcher + all client mods + configs, BruceNetworking excluded). They run
`install-mac.command` again; it overwrites in place. Installer and README-MAC.md sources are in
`tools\mac-kit\`.
