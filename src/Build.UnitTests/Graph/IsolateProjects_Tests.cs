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

        [Theory]
        //[InlineData(BuildResultCode.Success, new string[] { })]
        [InlineData(BuildResultCode.Success, new[] {"BuildSelf"})]
        public void CacheAndTaskEnforcementShouldAcceptSelfReferences(BuildResultCode expectedBuildResult, string[] targets)
        {
            using (var env = TestEnvironment.Create())
            {
                env.SetEnvironmentVariable("MSBUILDDEBUGCOMM", "1");

                env.SetEnvironmentVariable("MSBUILDDEBUGPATH", Path.Combine(PrintLineDebuggerWriters.ArtifactsLogDirectory, "TestResults"));

                var composite = new PrintLineDebuggerWriters.CompositeWriter(new []
                {
                    PrintLineDebuggerWriters.StdOutWriter,
                    PrintLineDebuggerWriters.IdBasedFilesWriter.FromArtifactLogDirectory("TestResults").Writer
                });

                env.CreatePrintLineDebugger(composite.Writer);

                PrintLineDebugger.DefaultWithProcessInfo.Value.Log("Test_start");

                AssertBuild(
                    targets,
                    (result, logger) =>
                    {
                        result.OverallResult.ShouldBe(BuildResultCode.Success);

                        logger.Errors.ShouldBeEmpty();
                    });

                PrintLineDebugger.DefaultWithProcessInfo.Value.Log("Test_end");
            }
        }

        [Fact(Skip = "")]
        public void Minimal()
        {
            using (var env = TestEnvironment.Create())
            {
                env.SetEnvironmentVariable("MSBUILDDEBUGCOMM", "1");

                env.SetEnvironmentVariable("MSBUILDDEBUGPATH", Path.Combine(PrintLineDebuggerWriters.ArtifactsLogDirectory, "TestResults"));

                var composite = new PrintLineDebuggerWriters.CompositeWriter(new []
                {
                    PrintLineDebuggerWriters.StdOutWriter,
                    PrintLineDebuggerWriters.IdBasedFilesWriter.FromArtifactLogDirectory("TestResults").Writer
                });

                env.CreatePrintLineDebugger(composite.Writer);

                PrintLineDebugger.DefaultWithProcessInfo.Value.Log("Test_start");

                AssertBuild(
                    new[] {"BuildSelf"},
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
                    isolate: false);

                PrintLineDebugger.DefaultWithProcessInfo.Value.Log("Test_end");
            }
        }

        [Fact]
        public void CacheAndTaskEnforcementShouldAcceptCallTarget()
        {
            AssertBuild(new []{"CallTarget"},
                (result, logger) =>
                {
                    result.OverallResult.ShouldBe(BuildResultCode.Success);

                    logger.Errors.ShouldBeEmpty();
                });
        }

        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/3876")]
        public void CacheEnforcementShouldFailWhenReferenceWasNotPreviouslyBuiltAndOnContinueOnError()
        {
            CacheEnforcementShouldFailWhenReferenceWasNotPreviouslyBuilt2(true);
        }

        [Fact]
        public void CacheEnforcementShouldFailWhenReferenceWasNotPreviouslyBuiltWithoutContinueOnError()
        {
            CacheEnforcementShouldFailWhenReferenceWasNotPreviouslyBuilt2(false);
        }

        private void CacheEnforcementShouldFailWhenReferenceWasNotPreviouslyBuilt2(bool addContinueOnError)
        {
            AssertBuild(
                new[] {"BuildDeclaredReference"},
                (result, logger) =>
                {
                    result.OverallResult.ShouldBe(BuildResultCode.Failure);

                    logger.ErrorCount.ShouldBe(1);

                    logger.Errors.First()
                        .Message.ShouldStartWith("MSB4252:");
                },
                addContinueOnError: addContinueOnError);
        }

        [Fact]
        public void CacheEnforcementShouldAcceptPreviouslyBuiltReferences()
        {
            AssertBuild(new []{"BuildDeclaredReference"},
                (result, logger) =>
                {
                    result.OverallResult.ShouldBe(BuildResultCode.Success);

                    logger.Errors.ShouldBeEmpty();
                },
                buildDeclaredReference: true);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TaskEnforcementShouldFailOnUndeclaredReference(bool addContinueOnError)
        {
            AssertBuild(new[] { "BuildUndeclaredReference" },
                (result, logger) =>
                {
                    result.OverallResult.ShouldBe(BuildResultCode.Failure);

                    logger.ErrorCount.ShouldBe(1);

                    logger.Errors.First().Message.ShouldStartWith("MSB4254:");
                },
                addContinueOnError: addContinueOnError);
        }

        [Fact]
        public void TaskEnforcementShouldNotAcceptPreviouslyBuiltReferences()
        {
            AssertBuild(new[] { "BuildUndeclaredReference" },
                (result, logger) =>
                {
                    result.OverallResult.ShouldBe(BuildResultCode.Failure);

                    logger.ErrorCount.ShouldBe(1);

                    logger.Errors.First().Message.ShouldStartWith("MSB4254:");
                },
                buildUndeclaredReference: true);
        }

        public static IEnumerable<object[]> TaskEnforcementShouldNormalizeFilePathsTestData
        {
            get
            {
                Func<string, string> preserve = path => path;

                Func<string, string> fullToRelative = path =>
                {
                    var directory = Path.GetDirectoryName(path);
                    var file = Path.GetFileName(path);

                    return Path.Combine("..", directory, file);
                };

                Func<string, string> ToForwardSlash = path => path.ToSlash();

                Func<string, string> ToBackwardSlash = path => path.ToBackwardSlash();

                Func<string, string> ToDuplicateSlashes = path => path.Replace("/", "//").Replace(@"\", @"\\");

                yield return new object[]
                {
                    preserve,
                    fullToRelative
                };

                yield return new object[]
                {
                    fullToRelative,
                    preserve
                };

                yield return new object[]
                {
                    preserve,
                    ToForwardSlash
                };

                yield return new object[]
                {
                    ToForwardSlash,
                    preserve
                };

                yield return new object[]
                {
                    preserve,
                    ToBackwardSlash
                };

                yield return new object[]
                {
                    ToBackwardSlash,
                    preserve
                };

                yield return new object[]
                {
                    preserve,
                    ToDuplicateSlashes
                };

                yield return new object[]
                {
                    ToDuplicateSlashes,
                    preserve
                };

                yield return new object[]
                {
                    ToBackwardSlash,
                    ToDuplicateSlashes
                };

                yield return new object[]
                {
                    ToDuplicateSlashes,
                    ToForwardSlash
                };

                yield return new object[]
                {
                    ToDuplicateSlashes,
                    fullToRelative
                };

                yield return new object[]
                {
                    fullToRelative,
                    ToBackwardSlash
                };
            }
        }

        [Theory]
        [MemberData(nameof(TaskEnforcementShouldNormalizeFilePathsTestData))]
        public void TaskEnforcementShouldNormalizeFilePaths(Func<string, string> projectReferenceModifier, Func<string, string> msbuildProjectModifier)
        {
            AssertBuild(new []{"BuildDeclaredReference"},
                (result, logger) =>
                {
                    result.OverallResult.ShouldBe(BuildResultCode.Success);

                    logger.Errors.ShouldBeEmpty();
                },
                buildDeclaredReference: true,
                buildUndeclaredReference: false,
                addContinueOnError: false,
                projectReferenceModifier,
                msbuildProjectModifier);
        }

        private void AssertBuild(
            string[] targets,
            Action<BuildResult, MockLogger> assert,
            bool buildDeclaredReference = false,
            bool buildUndeclaredReference = false,
            bool addContinueOnError = false,
            Func<string, string> projectReferenceModifier = null,
            Func<string, string> msbuildOnDeclaredReferenceModifier = null,
            bool isolate = true)
        {
            using (var env = TestEnvironment.Create())
            using (var buildManager = new BuildManager())
            {
                if (NativeMethodsShared.IsOSX)
                {
                    // OSX links /var into /private, which makes Path.GetTempPath() to return "/var..." but Directory.GetCurrentDirectory to return "/private/var..."
                    // this discrepancy fails the msbuild task enforcements due to failed path equality checks
                    // The path cannot be too long otherwise it breaks the max 108 character pipe path length on Unix
                    env.SetTempPath(Path.Combine("/tmp", Guid.NewGuid().ToString("N")), deleteTempDirectory:true);
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
                    IsolateProjects = isolate,
                    Loggers = new ILogger[] {logger},
                    EnableNodeReuse = false,
                    DisableInProcNode = true,
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
