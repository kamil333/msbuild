// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Xunit;
using Microsoft.Build.Shared;

namespace Microsoft.Build.UnitTests
{
    public class FileMatcherPerformanceTests
    {
        [Fact]
        public void FileMatcherPerf()
        {
            var directories = new Dictionary<string, string[]>
            {
                {
                    "SSD",
                    new[]
                    {
                        @"E:\delete\consoleNet46",
                        @"E:\projects\msbuild\src\Shared",
                        @"E:\projects\msbuild\src\Build",
                        @"E:\delete\nhibernate-core",
                        @"E:\projects"
                    }
                },
                {
                    "HDD",
                    new[]
                    {
                        @"D:\delete\TestProject",
                        @"D:\projects\msbuild\src\Shared",
                        @"D:\projects\msbuild\src\Build",
                        @"D:\delete\nhibernate-core",
                        @"D:\projects"
                    }
                }
            };

            Test(directories, "FileMatcher", s => FileMatcher.GetFiles(s, "**/*.cs"));
            Test(directories, nameof(EnumerateFilesAPI), EnumerateFilesAPI);
        }

        private void Test(Dictionary<string, string[]> directories, string enumerationDescription, Func<string, string[]> fileEnumerator)
        {
            //warmup
            fileEnumerator(Directory.GetCurrentDirectory());

            foreach (var directoryGroup in directories)
            {
                Console.WriteLine($"\n{directoryGroup.Key}");

                foreach (var testDir in directoryGroup.Value)
                {
                    Console.WriteLine($"{enumerationDescription}: {testDir}");

                    var firstRunTime = double.MinValue;

                    for (int i = 1; i <= 3; i++)
                    {
                        var result = MeasureEnumeration(testDir, fileEnumerator);

                        var iterationTime = result.Item2.TotalMilliseconds;

                        if (i == 1)
                        {
                            firstRunTime = iterationTime;
                        }

                        Console.WriteLine($"{i}: {iterationTime}ms; {result.Item1.Length} entries; ({1 - (iterationTime / firstRunTime):.##%})");
                    }

                    Console.WriteLine();
                }

            }
        }

        private Tuple<string[], TimeSpan> MeasureEnumeration(string directory, Func<string, string[]> fileEnumerator)
        {
            var timer = Stopwatch.StartNew();
            var files = fileEnumerator(directory);
            timer.Stop();

            return Tuple.Create(files, timer.Elapsed);
        }

        private static string[] EnumerateFilesRecursive(string directory)
        {
            void EnumerateFilesRecursiveImpl(string currentDirectory, List<string> result)
            {
                result.AddRange(Directory.GetFiles(currentDirectory));

                foreach (var subdirectory in Directory.GetDirectories(currentDirectory))
                {
                    EnumerateFilesRecursiveImpl(subdirectory, result);
                }
            }

            Trace.Assert(Directory.Exists(directory));

            var files = new List<string>();
            EnumerateFilesRecursiveImpl(directory, files);

            return files.ToArray();
        }

        private static string[] EnumerateFilesAPI(string directory)
        {
            Trace.Assert(Directory.Exists(directory));

            return Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories);
        }
    }
}





