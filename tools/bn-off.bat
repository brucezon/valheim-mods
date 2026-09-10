@echo off
REM Disable BruceNetworking (server-only). Restart the server afterwards.
del /Q "%~dp0..\server\BepInEx\plugins\BruceNetworking.dll" 2>nul
echo BruceNetworking DISABLED. Restart the server (close its window, run start-lan-server.bat).
