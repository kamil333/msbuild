@echo off
powershell -NoLogo -NoProfile -ExecutionPolicy ByPass "& '%~dp0build.ps1'" -log -restore -build -test -pack -sign -ci -prepareMachine %*
exit /b %ErrorLevel%