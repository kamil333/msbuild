@echo off
setlocal enabledelayedexpansion

set project=%1

REM call cibuild.cmd --target All --scope Compile --bootstrap-only

robocopy bin\Bootstrap\MSBuild\15.0 bin\Bootstrap-NetCore\15.0 /MIR

set MSBuildSDKsPath=C:\Program Files\dotnet\sdk\2.1.1-preview-007118\Sdks

set SKIPNUGETREFERENCES=

echo ====== Do not skip nuget references =====

call :FFBuild %project%
call :CoreBuild %project%

set SKIPNUGETREFERENCES=1

echo ====== Skip nuget refernces =====
echo

call :FFBuild %project%
call :CoreBuild %project%

goto :eof

:FFBuild
echo ====== Full ======
bin\Bootstrap\MSBuild\15.0\Bin\MSBuild.exe /v:quiet  %~1

goto :eof

:CoreBuild
echo ====== Core ======
Tools\dotnetcli\dotnet.exe bin\Bootstrap-NetCore\MSBuild.dll /v:quiet  %~1
goto :eof

