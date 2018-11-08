// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;
using Microsoft.Build.Shared;
using Microsoft.Build.Shared.Debugging;
using Microsoft.Build.UnitTests;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Build.Graph.UnitTests
{
    public class PipeConnectionHangsOnMacOs_Tests : IDisposable
    {
        private readonly string _project = @"
                <Project>
                    <Target Name='SelfTarget'>
                    </Target>
                </Project>";

        private readonly ITestOutputHelper _testOutput;
        private TestEnvironment _env;

        public PipeConnectionHangsOnMacOs_Tests(ITestOutputHelper testOutput)
        {
            _testOutput = testOutput;
            _env = TestEnvironment.Create(_testOutput);

            _env.SetEnvironmentVariable("MSBUILDDEBUGCOMM", "1");

            _env.SetEnvironmentVariable("MSBUILDDEBUGPATH", Path.Combine(PrintLineDebuggerWriters.ArtifactsLogDirectory, "TestResults"));

            var composite = new PrintLineDebuggerWriters.CompositeWriter(new []
            {
                //PrintLineDebuggerWriters.StdOutWriter,
                PrintLineDebuggerWriters.IdBasedFilesWriter.FromArtifactLogDirectory("TestResults").Writer,

                // this one causes the logs to be merged with the log of IO tracking tools (e.g. fs_usage)
                //(Action<string, string, IEnumerable<string>>) ((id, callsite, args) => File.Exists(PrintLineDebuggerWriters.SimpleFormat(id, callsite, args)))
            });

            _env.CreatePrintLineDebugger(composite.Writer);
        }

       [Fact]
        public void M1()
        {

                PrintLineDebugger.DefaultWithProcessInfo.Value.Log("M1_Start");

                AssertBuild(
                    new[] {"SelfTarget"},
                    (result, logger) =>
                    {
                        result.OverallResult.ShouldBe(BuildResultCode.Success);

                        logger.Errors.ShouldBeEmpty();
                    },
                    disableInprocNode: true);

                PrintLineDebugger.DefaultWithProcessInfo.Value.Log("M1_end");

        }

        [Fact]
        public void M2()
        {
            

                PrintLineDebugger.DefaultWithProcessInfo.Value.Log("M2_start");

                AssertBuild(
                    new[] {"SelfTarget"},
                    (result, logger) =>
                    {
                        result.OverallResult.ShouldBe(BuildResultCode.Success);

                        logger.Errors.ShouldBeEmpty();
                    },
                    disableInprocNode: true);

                PrintLineDebugger.DefaultWithProcessInfo.Value.Log("M2_end");
        }

        private void AssertBuild(
            string[] targets,
            Action<BuildResult, MockLogger> assert,
            bool disableInprocNode = true)
        {
            using (var env = TestEnvironment.Create())
            using (var buildManager = new BuildManager())
            {
                if (NativeMethodsShared.IsOSX)
                {
                    // OSX links /var into /private, which makes Path.GetTempPath() to return "/var..." but Directory.GetCurrentDirectory to return "/private/var..."
                    // this discrepancy fails the msbuild task enforcements due to failed path equality checks
                    // The path cannot be too long otherwise it breaks the max 108 character pipe path length on Unix

                    var newTemp = Path.Combine("/tmp", Guid.NewGuid().ToString("N"));

                    Directory.CreateDirectory(newTemp);

                    env.SetTempPath(newTemp, deleteTempDirectory:true);
                    env.SetCurrentDirectory(newTemp);
                }

                var projectFile = env.CreateFile().Path;

                File.WriteAllText(projectFile, _project);

                var logger = new MockLogger(_testOutput);

                var buildParameters = new BuildParameters
                {
                    IsolateProjects = false,
                    Loggers = new ILogger[] {logger},
                    EnableNodeReuse = false,
                    DisableInProcNode = disableInprocNode,
                    MaxNodeCount = 1
                };

                var rootRequest = new BuildRequestData(
                    projectFile,
                    new Dictionary<string, string>(),
                    MSBuildConstants.CurrentToolsVersion,
                    targets,
                    null);

                try
                {
                    buildManager.BeginBuild(buildParameters);

                    PrintLineDebugger.DefaultWithProcessInfo.Value.Log("before build");

                    var result = buildManager.BuildRequest(rootRequest);

                    _testOutput.WriteLine($"{result.Exception?.Message}\n{result.Exception?.StackTrace}");

                    PrintLineDebugger.DefaultWithProcessInfo.Value.Log($"{result.Exception?.Message}\n{result.Exception?.StackTrace}");

                    PrintLineDebugger.DefaultWithProcessInfo.Value.Log("after build");

                    assert(result, logger);
                }
                finally
                {
                    buildManager.EndBuild();
                }
            }
        }

        public void Dispose()
        {
            _env.Dispose();
        }
    }
}
