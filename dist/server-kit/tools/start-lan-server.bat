@echo off
REM LAN server launch line. Edit WORLD_NAME and PASSWORD before first use.
REM World keys are re-applied on EVERY start, so editing them here and restarting changes an existing world.
REM Remove each -setkey as the corresponding mod comes back (see WORLD-KNOBS.md).
set WORLD_NAME=CHANGEME
set PASSWORD=CHANGEME
set SteamAppId=892970
cd /d "%~dp0..\server"
REM Carry weight (BruceQoL Hauling skill) and stamina regen (Endurance) come from our mods, so no
REM stand-in keys. Death penalty: preset Hard, DeathPenalty slider back to Default, then
REM skillreductionrate 60 = 60% of the default skill loss on death.
REM Keys go AFTER -preset (a preset resets the list).
REM Saves live next to the server folder (server-saves\) so backing up = copying one folder.
valheim_server.exe -nographics -batchmode -name "Vikings LAN" -port 2456 -world "%WORLD_NAME%" -password "%PASSWORD%" -public 0 -savedir "%~dp0..\server-saves" -preset Hard -modifier DeathPenalty Default -setkey "movestaminarate 85" -setkey "skillreductionrate 60" -setkey "playerevents"
