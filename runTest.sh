#!/bin/sh

repoRoot=`pwd`
user=`whoami`

if [ -z $1 ]
then
   $repoRoot/build/build.sh -build -skiptests
fi

pkill dotnet

# "$repoRoot/artifacts/.dotnet/2.1.401/dotnet" exec --depsfile "$repoRoot/artifacts/Debug/bin/Microsoft.Build.Engine.UnitTests/netcoreapp2.1/Microsoft.Build.Engine.UnitTests.deps.json" --runtimeconfig "$repoRoot/artifacts/Debug/bin/Microsoft.Build.Engine.UnitTests/netcoreapp2.1/Microsoft.Build.Engine.UnitTests.runtimeconfig.json" "/Users/$user/.nuget/packages/xunit.runner.console/2.3.1/tools/netcoreapp1.0/xunit.console.dll" "$repoRoot/artifacts/Debug/bin/Microsoft.Build.Engine.UnitTests/netcoreapp2.1/Microsoft.Build.Engine.UnitTests.dll" -noautoreporters -xml "$repoRoot/artifacts/Debug/log/TestResults/Microsoft.Build.Engine.UnitTests_netcoreapp2.1_x64.xml" -notrait category=nonosxtests -notrait category=netcore-osx-failing -notrait category=nonnetcoreapptests > "$repoRoot/artifacts/Debug/log/TestResults/Microsoft.Build.Engine.UnitTests_netcoreapp2.1_x64.log" 2>&1

rm -rf $repoRoot/artifacts/Debug/log

"$repoRoot/artifacts/.dotnet/2.1.401/dotnet" exec --depsfile "$repoRoot/artifacts/Debug/bin/Microsoft.Build.Engine.UnitTests/netcoreapp2.1/Microsoft.Build.Engine.UnitTests.deps.json" --runtimeconfig "$repoRoot/artifacts/Debug/bin/Microsoft.Build.Engine.UnitTests/netcoreapp2.1/Microsoft.Build.Engine.UnitTests.runtimeconfig.json" "/Users/$user/.nuget/packages/xunit.runner.console/2.3.1/tools/netcoreapp1.0/xunit.console.dll" "$repoRoot/artifacts/Debug/bin/Microsoft.Build.Engine.UnitTests/netcoreapp2.1/Microsoft.Build.Engine.UnitTests.dll" -noautoreporters -xml "$repoRoot/artifacts/Debug/log/TestResults/Microsoft.Build.Engine.UnitTests_netcoreapp2.1_x64.xml" -notrait category=nonosxtests -notrait category=netcore-osx-failing -notrait category=nonnetcoreapptests -parallel none -class Microsoft.Build.Graph.UnitTests.IsolateProjectsTests