# Joining the Valheim server (1.0) — 3 steps, 5 minutes

## 1. Install Gale (the mod manager — this is how mods stay in sync automatically)
https://github.com/Kesomannen/gale/releases/latest → download `Gale_x.x.x_x64_en-US.msi`, run it.
Open Gale, pick **Valheim** when it asks for the game.

## 2. Import the server profile
Gale → **Import** → **…profile from code** → paste the code pinned in Discord → OK.
Gale downloads everything the server uses, including BepInEx itself. You never pick mods yourself.
That's it for mods — there is nothing to download by hand.

## 3. Launch and join
Click **Launch** in Gale (NOT the Steam play button — Steam launches vanilla and you'll be refused).
In game: Start Game → your character → **Join Game** → **Join IP** → address + password from the Discord pin.
Crossplay must stay **OFF** (it is by default when modded).

## On a Mac? Skip steps 1–2
Gale has no Mac version. Ask the host for `valheim-mac-mods.zip` and follow the README-MAC.md inside it:
run one installer, paste one line into Steam's Launch Options. When mods update, you get a new zip
and run the installer again. Then continue from step 3.

## Remote players only (not at the cabin): Tailscale, once, 5 minutes
The cabin has no port forwarding, so the server is reached through Tailscale, a free private network.
1. Install Tailscale: https://tailscale.com/download → sign in with any account (Google, Microsoft,
   GitHub, Apple). Free.
2. Open the **Tailscale invite link** from the Discord pin → sign in → **Accept**. That gives your
   account access to the server machine only, nothing else.
3. Keep Tailscale running (tray icon, "Connected").
4. In game: Join IP → the `100.x.x.x` address from the Discord pin, port `2456`, same password.
If the join times out: check the tray icon says Connected, and that you accepted the share. Wired
beats wifi for everyone, and that goes double over Tailscale.

## Rules
- **Always launch through Gale.** Steam's own play button = vanilla = can't join.
- Don't add other mods — the profile is locked to the server's list and the server checks versions.
- When we add or update mods, Gale pulls the change automatically. If you're refused with an
  "incompatible version" message: close the game, open Gale, click **Pull update** on the profile, Launch.

## If something breaks
- "Failed to read BepInEx core directory. Is BepInEx installed?" → you skipped step 2.
  Import the profile code; if it still says that, Gale → **Browse** → search `BepInExPack Valheim`
  (author denikson) → Install → Launch.
- Anything else: send a screenshot of the error and the file `BepInEx\LogOutput.log` from the
  profile folder (Gale → the profile → **Open folder**).
