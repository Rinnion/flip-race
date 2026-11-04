@echo off
echo Track Workshop 0.2 - http://127.0.0.1:4186/
echo Keep this window open. Press Ctrl+C to stop.
if exist "%~dp0TrackWorkshop.dll" (
    dotnet "%~dp0TrackWorkshop.dll"
) else (
    dotnet run --project "%~dp0TrackWorkshop.csproj" --no-launch-profile
)
if errorlevel 1 pause
