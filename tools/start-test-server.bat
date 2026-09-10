@echo off
REM Local test server for mod validation. Not for production - -public 0 keeps it unlisted.
set SteamAppId=892970
cd /d "%~dp0..\server"
valheim_server.exe -nographics -batchmode -name "ModTest" -port 2456 -world "modtest" -password "testpass123" -public 0
