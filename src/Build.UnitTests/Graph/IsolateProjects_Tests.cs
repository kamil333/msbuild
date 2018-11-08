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
    public class IsolateProjectsTests
    {
        private readonly string _project = @"
                <Project DefaultTargets='BuildSelf'>

                    <ItemGroup>
                        <ProjectReference Include='{0}'/>
                    </ItemGroup>

                    <Target Name='BuildDeclaredReference'>
                        <MSBuild
                            Projects='{1}'
                            Targets='DeclaredReferenceTarget'
                            {3}
                        />
                    </Target>

                    <Target Name='BuildUndeclaredReference'>
                        <MSBuild
                            Projects='{2}'
                            Targets='UndeclaredReferenceTarget'
                            {3}
                        />
                    </Target>

                    <Target Name='BuildSelf'>
                        <MSBuild
                            Projects='$(MSBuildThisFile)'
                            Targets='SelfTarget'
                            {3}
                        />
                    </Target>

                    <Target Name='CallTarget'>
                        <CallTarget Targets='SelfTarget'/>  
                    </Target>

                    <Target Name='SelfTarget'>
                    </Target>
                </Project>";

        private readonly string _declaredReference = @"
                <Project>
                    <Target Name='DeclaredReferenceTarget'>
                    </Target>
                </Project>";

        private readonly string _undeclaredReference = @"
                <Project>
                    <Target Name='UndeclaredReferenceTarget'>
                    </Target>
                </Project>";

        private readonly ITestOutputHelper _testOutput;

        public IsolateProjectsTests(ITestOutputHelper testOutput)
        {
            _testOutput = testOutput;
        }

        // [Theory]
        // //[InlineData(BuildResultCode.Success, new string[] { })]
        // [InlineData(BuildResultCode.Success, new[] {"BuildSelf"})]
        // public void CacheAndTaskEnforcementShouldAcceptSelfReferences(BuildResultCode expectedBuildResult, string[] targets)
        // {
        
        //         AssertBuild(
        //             targets,
        //             (result, logger) =>
        //             {
        //                 result.OverallResult.ShouldBe(BuildResultCode.Success);

        //                 logger.Errors.ShouldBeEmpty();
        //             });

        //         PrintLineDebugger.DefaultWithProcessInfo.Value.Log("Test_end");
            
        // }

       [Fact]
        public void M1()
        {
            using (var env = TestEnvironment.Create())
            {
                env.SetEnvironmentVariable("MSBUILDDEBUGCOMM", "1");

                env.SetEnvironmentVariable("MSBUILDDEBUGPATH", Path.Combine(PrintLineDebuggerWriters.ArtifactsLogDirectory, "TestResults"));

                var composite = new PrintLineDebuggerWriters.CompositeWriter(new []
                {
                    // PrintLineDebuggerWriters.StdOutWriter,
                    //PrintLineDebuggerWriters.IdBasedFilesWriter.FromArtifactLogDirectory("TestResults").Writer
                    (Action<string, string, IEnumerable<string>>) ((id, callsite, args) => File.Exists(PrintLineDebuggerWriters.SimpleFormat(id, callsite, args)))
                });

                env.CreatePrintLineDebugger(composite.Writer);

                PrintLineDebugger.DefaultWithProcessInfo.Value.Log("M1_Start");

                AssertBuild(
                    new[] {"SelfTarget"},
                    (result, logger) =>
                    {
                        result.OverallResult.ShouldBe(BuildResultCode.Success);

                        logger.Errors.ShouldBeEmpty();
                    },
                    false,
                    false,
                    false,
                    null,
                    null,
                    disableInprocNode: true);

                PrintLineDebugger.DefaultWithProcessInfo.Value.Log("M1_end");
            }
        }

        [Fact]
        public void M2()
        {
            using (var env = TestEnvironment.Create())
            {
                env.SetEnvironmentVariable("MSBUILDDEBUGCOMM", "1");

                env.SetEnvironmentVariable("MSBUILDDEBUGPATH", Path.Combine(PrintLineDebuggerWriters.ArtifactsLogDirectory, "TestResults"));

                var composite = new PrintLineDebuggerWriters.CompositeWriter(new []
                {
                    // PrintLineDebuggerWriters.StdOutWriter,
                    //PrintLineDebuggerWriters.IdBasedFilesWriter.FromArtifactLogDirectory("TestResults").Writer
                    (Action<string, string, IEnumerable<string>>) ((id, callsite, args) => File.Exists(PrintLineDebuggerWriters.SimpleFormat(id, callsite, args)))
                });

                env.CreatePrintLineDebugger(composite.Writer);

                PrintLineDebugger.DefaultWithProcessInfo.Value.Log("M2_start");

                AssertBuild(
                    new[] {"SelfTarget"},
                    (result, logger) =>
                    {
                        result.OverallResult.ShouldBe(BuildResultCode.Success);

                        logger.Errors.ShouldBeEmpty();
                    },
                    false,
                    false,
                    false,
                    null,
                    null,
                    disableInprocNode: true);

                PrintLineDebugger.DefaultWithProcessInfo.Value.Log("M2_end");
            }
        }

        private void AssertBuild(
            string[] targets,
            Action<BuildResult, MockLogger> assert,
            bool buildDeclaredReference = false,
            bool buildUndeclaredReference = false,
            bool addContinueOnError = false,
            Func<string, string> projectReferenceModifier = null,
            Func<string, string> msbuildOnDeclaredReferenceModifier = null,
            bool disableInprocNode =true)
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
                var declaredReferenceFile = env.CreateFile().Path;
                var undeclaredReferenceFile = env.CreateFile().Path;

                File.WriteAllText(
                    projectFile,
                    string.Format(
                        _project,
                        projectReferenceModifier?.Invoke(declaredReferenceFile) ?? declaredReferenceFile,
                        msbuildOnDeclaredReferenceModifier?.Invoke(declaredReferenceFile) ?? declaredReferenceFile,
                        undeclaredReferenceFile,
                        addContinueOnError ? "ContinueOnError='WarnAndContinue'" : string.Empty));

                File.WriteAllText(declaredReferenceFile, _declaredReference);
                File.WriteAllText(undeclaredReferenceFile, _undeclaredReference);

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

                    if (buildDeclaredReference)
                    {
                        buildManager.BuildRequest(
                            new BuildRequestData(
                                declaredReferenceFile,
                                new Dictionary<string, string>(),
                                MSBuildConstants.CurrentToolsVersion,
                                new[] {"DeclaredReferenceTarget"},
                                null))
                            .OverallResult.ShouldBe(BuildResultCode.Success);
                    }

                    if (buildUndeclaredReference)
                    {
                        buildManager.BuildRequest(
                            new BuildRequestData(
                                undeclaredReferenceFile,
                                new Dictionary<string, string>(),
                                MSBuildConstants.CurrentToolsVersion,
                                new[] {"UndeclaredReferenceTarget"},
                                null))
                            .OverallResult.ShouldBe(BuildResultCode.Success);
                    }

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
    }
}
