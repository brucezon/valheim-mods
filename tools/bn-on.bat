@echo off
REM Enable BruceNetworking (server-only). Restart the server afterwards.
copy /Y "%~dp0..\mods\brucenetworking-src\BruceNetworking\bin\Release\BruceNetworking.dll" "%~dp0..\server\BepInEx\plugins\BruceNetworking.dll" >nul
echo BruceNetworking ENABLED. Restart the server (close its window, run start-lan-server.bat).
