@echo off
setlocal
pushd "%~dp0"

set "GAME_PORT=7777"
if not "%~1"=="" set "GAME_PORT=%~1"

echo [1/3] Restoring ActionGame packages...
dotnet restore ".\ActionGame.slnx" --configfile ".\NuGet.Config"
if not "%ERRORLEVEL%"=="0" (
    echo Restore failed. The game was not started.
    popd
    exit /b 1
)

echo [2/3] Building ActionGame...
dotnet build ".\ActionGame.slnx" --configuration Debug --no-restore
if not "%ERRORLEVEL%"=="0" (
    echo Build failed. The game was not started.
    popd
    exit /b 1
)

echo [3/3] Starting server and two clients on port %GAME_PORT%...
start "ActionGame Server" cmd /k .\src\ActionGame.Server\bin\Debug\net10.0\ActionGame.Server.exe %GAME_PORT%
ping 127.0.0.1 -n 3 >nul
start "ActionGame Client 1" .\src\ActionGame.Client\bin\Debug\net10.0\ActionGame.Client.exe 127.0.0.1 %GAME_PORT%
start "ActionGame Client 2" .\src\ActionGame.Client\bin\Debug\net10.0\ActionGame.Client.exe 127.0.0.1 %GAME_PORT%

popd
exit /b 0
