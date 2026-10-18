@echo off
rem ROVR bridge for a headset on Wi-Fi: no cable needed once the app is on the headset.
rem The headset must be on the same network as this PC (your own Wi-Fi or a phone hotspot,
rem not a shared one: anyone on the network could reach the bridge). It finds this PC by itself.
call "%~dp0run_bridge.bat" --wifi
