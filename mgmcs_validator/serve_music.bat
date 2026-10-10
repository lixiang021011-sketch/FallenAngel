@echo off
rem Serve the music folder on a second port, read-only and only on this machine,
rem so the validator can load a song by URL instead of asking you to find the same
rem file in a dialog every time:
rem
rem   http://localhost:8124/index.html?v=8&doc=kiechau&audio=<url of the song>
rem
rem Nothing is uploaded anywhere: this is an HTTP server bound to 127.0.0.1, the
rem same way run.bat serves the validator itself. Close this window to stop it.
cd /d "%~dp0"

set "MUSIC_DIR=C:\Users\LorXer\Music"

where python >nul 2>nul
if errorlevel 1 (
  echo Python not found. Either install it or pass the song in by dragging it.
  pause
  goto :eof
)
if not exist "%MUSIC_DIR%" (
  echo The folder below does not exist - edit MUSIC_DIR in this file:
  echo   %MUSIC_DIR%
  pause
  goto :eof
)

echo Serving %MUSIC_DIR% on http://127.0.0.1:8125/  -  close this window to stop.
rem tools/serve_range.py, not `python -m http.server`: that one ignores Range
rem requests, and a browser that cannot range cannot seek - so clicking a bar
rem would jump nowhere. See the header of that file.
python "%~dp0tools\serve_range.py" --directory "%MUSIC_DIR%" --port 8125
