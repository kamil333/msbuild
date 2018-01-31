@echo off
powershell -NoLogo -NoProfile -ExecutionPolicy ByPass "%~dp0build\build.ps1" -log -restore -build -test %*
exit /b %ErrorLevel%
