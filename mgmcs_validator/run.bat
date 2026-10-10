@echo off
rem Open the validator. Nothing to install: if Python is here it serves the
rem folder (so the page has a real origin for cache), otherwise the browser
rem opens the file directly, which this tool also supports.
cd /d "%~dp0"
where python >nul 2>nul
if errorlevel 1 goto noPython
echo Serving http://localhost:8123/  -  close this window to stop.
start "" http://localhost:8123/
rem tools/serve_range.py, not `python -m http.server`: that one ignores Range
rem requests, and a browser that cannot range cannot seek - so clicking a bar in
rem the timeline would jump nowhere. The songs in static/audio/ need it too.
python "%~dp0tools\serve_range.py" --directory "%~dp0static" --port 8123
goto :eof

:noPython
echo Python not found. Opening the page as a local file instead.
start "" "%~dp0static\index.html"
