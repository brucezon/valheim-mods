@echo off
REM Disable Serverside Simulations (server-only). Restart the server afterwards.
del /Q "%~dp0..\server\BepInEx\plugins\Serverside_Simulations_TEMP.dll" 2>nul
echo Serverside Simulations DISABLED. Restart the server (close its window, run start-lan-server.bat).
