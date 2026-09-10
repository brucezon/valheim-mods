# Valheim LAN mods — macOS (no Gale needed)

Gale, the mod manager everyone else uses, has no Mac version. This kit installs the same mods by hand.
Five minutes.

## 1. Install Valheim from Steam and run it once
Let it reach the main menu, then quit. This creates the folders the installer looks for.

## 2. Unzip this kit
Double-click `valheim-mac-mods.zip`. You get a folder with `install-mac.command`, `mods`, and this file.

## 3. Run the installer
Open **Terminal** (Spotlight → type Terminal). Type `bash ` (with a space), then drag
`install-mac.command` from Finder into the Terminal window, and press Enter.

It copies the mods into the game folder and prints one line for the next step. It also puts that
line on your clipboard.

"Permission denied"? That happens when you double-click the file or run it without `bash` in front:
a zip made on Windows can't mark it as runnable. Use the `bash ` + drag method above, or run
`chmod +x install-mac.command` once and then double-click it.

## 4. Tell Steam to start the game with mods
Steam → Library → right-click **Valheim** → **Properties** → **General** → **Launch Options** → paste
the line the installer printed. On an Apple Silicon Mac (M1/M2/M3/M4) it looks like:
```
/usr/bin/arch -x86_64 /bin/sh "/Users/you/Library/Application Support/Steam/steamapps/common/Valheim/start_game_bepinex.sh" %command%
```
On an Intel Mac the same line without the `/usr/bin/arch -x86_64 /bin/sh` part. Close the window. That's it.

The `arch -x86_64` part matters: without it Steam reports "Failed to start process: OS error 260",
because the game and the mod loader are Intel builds and Steam's own shell is not.

## 5. Launch and join
Play from Steam as usual. First start is slower. In game: Join Game → Join IP → address + password
from the Discord pin. Crossplay must stay **off**.

If macOS says something "cannot be opened because the developer cannot be verified", open
**System Settings → Privacy & Security**, scroll down, click **Allow Anyway**, and launch again.

## Updating
When the host announces a mod update, download the new kit and run step 3 again. It overwrites in place.

## Removing
Clear the Launch Options box in Steam. The game starts vanilla again; the files can stay.

## Launching without Steam (fallback)
If Steam refuses the launch line, this starts the modded game directly from Terminal:
```
cd ~/Library/Application\ Support/Steam/steamapps/common/Valheim && /usr/bin/arch -x86_64 /bin/sh start_game_bepinex.sh
```
(drop `/usr/bin/arch -x86_64 /bin/sh` on an Intel Mac). It must be run from inside the game folder.

## If it doesn't work
Send the host the file `BepInEx/LogOutput.log` from the Valheim folder
(`~/Library/Application Support/Steam/steamapps/common/Valheim/`). If that file doesn't exist, the
launcher never ran: re-check the Launch Options line.
