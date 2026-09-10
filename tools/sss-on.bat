@echo off
REM Enable Serverside Simulations (server-only): the server simulates every zone instead of clients.
REM Use if remote players' zones are lagging the LAN party. Restart the server afterwards.
copy /Y "%~dp0..\mods\temp-builds\Serverside_Simulations_TEMP.dll" "%~dp0..\server\BepInEx\plugins\" >nul
echo Serverside Simulations ENABLED. Restart the server (close its window, run start-lan-server.bat).
