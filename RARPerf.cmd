@echo off
setlocal enabledelayedexpansion

set project=%1

call cibuild.cmd --target All --scope Compile --bootstrap-only --config Release

robocopy bin\Bootstrap\MSBuild\15.0 bin\Bootstrap-NetCore\15.0 /MIR

set MSBuildSDKsPath=C:\Program Files\dotnet\sdk\2.1.1-preview-007118\Sdks

set RARLogs=%~dp0\RARLogs

rd /s /q RARLogs

echo ====== Do not skip nuget references =====
echo .

set SKIPNUGETREFERENCES=

set RARLOG=%RARLogs%\NoSkipFF.csv
call :FFBuild %project%
set RARLOG=%RARLogs%\NoSkipCore.csv
call :CoreBuild %project%

echo ====== Skip nuget refernces =====
echo .

set SKIPNUGETREFERENCES=1

set RARLOG=%RARLogs%\SkipNugetsFF.csv
call :FFBuild %project%
set RARLOG=%RARLogs%\SkipNugetsCore.csv
call :CoreBuild %project%

echo ====== DoNotBreakEarly =====
echo .

set DoNotBreakEarly=1

set RARLOG=%RARLogs%\BreakEarlyFF.csv
call :FFBuild %project%
set RARLOG=%RARLogs%\BreakEarlyCore.csv
call :CoreBuild %project%

echo ====== SkipSecondClosureComputation =====
echo .

set SkipSecondClosureComputation=1

set RARLOG=%RARLogs%\SkipClosureFF.csv
call :FFBuild %project%
set RARLOG=%RARLogs%\SkipClosureCore.csv
call :CoreBuild %project%


goto :eof

:FFBuild
echo ====== Full ======
bin\Bootstrap\MSBuild\15.0\Bin\MSBuild.exe /v:quiet /m:1 %~1

goto :eof

:CoreBuild
echo ====== Core ======
Tools\dotnetcli\dotnet.exe bin\Bootstrap-NetCore\MSBuild.dll /v:quiet /m:1 %~1
goto :eof

