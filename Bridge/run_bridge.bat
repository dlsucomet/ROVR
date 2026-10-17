@echo off
rem ROVR bridge launcher: checks Ollama, connects the headset over the USB cable, starts the bridge.
rem Double-click it, or run it from any terminal. Leave the window open while testing.
setlocal
cd /d "%~dp0.."

set ADB=C:\Program Files\Unity\Hub\Editor\6000.6.2f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe
set PORT=8765

echo Checking Ollama...
curl -s -m 3 -o nul http://127.0.0.1:11434/api/tags
if errorlevel 1 (
    echo   Ollama is not running. Start the Ollama app, or run "ollama serve" in another terminal.
) else (
    echo   Ollama is running.
)

echo Connecting the headset over USB...
if not exist "%ADB%" (
    echo   adb not found at "%ADB%". Skipping the cable link.
    goto bridge
)
"%ADB%" reverse tcp:%PORT% tcp:%PORT% >nul 2>&1
if errorlevel 1 (
    echo   No headset linked. Plug in the Quest and accept "Allow USB debugging" in the headset,
    echo   then run this again. The bridge still works for the Unity Editor on this PC.
) else (
    echo   Headset linked: its port %PORT% now leads to this PC.
)

:bridge
echo.
python Bridge\rovr_bridge.py --port %PORT%
pause
