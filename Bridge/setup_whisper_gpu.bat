@echo off
rem Sets up speech to text on this PC's NVIDIA GPU for the bridge (once per PC, and again after a driver
rem update): downloads whisper.cpp's CUDA build and the small.en model into Bridge\whisper\, then warms
rem Whisper up so its GPU code is compiled before a participant's first sentence. See setup_whisper_gpu.ps1.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup_whisper_gpu.ps1"
pause
