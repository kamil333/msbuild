using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Shared;
using Microsoft.Build.Utilities.FileSystem;
using Xunit;

namespace Microsoft.Build.UnitTests
{
    public class FileStorePerfTest
    {
        [Fact]
        public void PerfTest()
        {
            Environment.SetEnvironmentVariable("MSBuildSDKsPath", @"C:\Program Files\dotnet\sdk\2.1.4\Sdks");
            Environment.SetEnvironmentVariable("MSBuildExtensionsPath", @"E:\projects\msbuild\bin\Bootstrap\MSBuild");

            Process.GetCurrentProcess().ProcessorAffinity = new IntPtr(2);
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
            Thread.CurrentThread.Priority = ThreadPriority.Highest;

            Thread.Sleep(TimeSpan.FromSeconds(2));

            var projectDirs = new string[]
            {
                @"E:\delete\mvc",
                @"e:\delete\npm",
                @"E:\delete\OrchardCore"
            };

            //Debugger.Launch();

            foreach (var projectDir in projectDirs)
            {
                MeasureProjects(projectDir);
            }
        }

        private void MeasureProjects(string projectDir)
        {
            var csprojes = Directory.EnumerateFiles(projectDir, "*.csproj", SearchOption.AllDirectories)
                .Select(d => Tuple.Create(d, FileStore.FromRoot(Path.GetDirectoryName(d).EnsureTrailingSlash())))
                .ToList();

            Console.WriteLine($"{projectDir} ({csprojes.Count})");

            //warmup
            MeasureTime(() => EvaluateProjects(csprojes, false));

            var noCachingTime = MeasureTime(() => EvaluateProjects(csprojes, false));
            Console.WriteLine($"No Caching: {noCachingTime.TotalMilliseconds}ms");

            //warmup
            MeasureTime(() => EvaluateProjects(csprojes, true));

            var caching = MeasureTime(() => EvaluateProjects(csprojes, true));
            Console.WriteLine($"Caching: {caching.TotalMilliseconds}ms ({1 - caching.TotalMilliseconds / noCachingTime.TotalMilliseconds:##.##% improvement})");

            Console.WriteLine();
        }

        private TimeSpan MeasureTime(Action action)
        {
            var watch = Stopwatch.StartNew();
            action();
            watch.Stop();

            return watch.Elapsed;
        }

        public List<Project> EvaluateProjects(List<Tuple<string, IFileStore>> csprojes, bool useFileStore)
        {
            using (var collection = new ProjectCollection())
            {
                var evaluatedProjects = csprojes.Select(
                    f => Project.FromFile(
                        f.Item1,
                        new ProjectConstructionInfo
                        {
                            ProjectCollection = collection,
                            FileStore = useFileStore ? f.Item2 : null
                        }))
                    .ToList();
                return evaluatedProjects;
            }
        }

        private class FileStore : IFileStore
        {
            private static readonly char OtherSlash = Path.DirectorySeparatorChar == '/'
                ? '\\'
                : '/';

            private static readonly string DotDot = $"{Path.DirectorySeparatorChar}..";
            private static readonly string Dot = $"{Path.DirectorySeparatorChar}.{Path.DirectorySeparatorChar}";

            private static readonly IReadOnlyCollection<IFileNode> EmptyReadonOnlyCollection =
                new ReadOnlyCollection<IFileNode>(new List<IFileNode>());

            private readonly Dictionary<string, IFileNode> _nodeLookup;

            private readonly FileNode _rootNode;

            public FileStore(FileNode rootNode, Dictionary<string, IFileNode> nodeLookup)
            {
                _rootNode = rootNode;
                _nodeLookup = nodeLookup;
            }

            public static string Normalize(string path)
            {
                return FileMatcher.Normalize(path);
            }

            public NodeSearchResult TryGetNode(string path, out IFileNode fileNode)
            {
                path = FileMatcher.Normalize(path);

                AssertPathFormat(path);

                if (!path.StartsWith(_rootNode.FullPath))
                {
                    fileNode = null;
                    return NodeSearchResult.Unknown;
                }

                if (_nodeLookup.TryGetValue(path, out var foundNode))
                {
                    fileNode = foundNode;
                    return NodeSearchResult.Exists;
                }

                //Trace.Assert(!File.Exists(path) && !Directory.Exists(path));

                fileNode = null;
                return NodeSearchResult.DoesNotExist;
            }

            private static void AssertPathFormat(string path)
            {
                Trace.Assert(Path.IsPathRooted(path));
                Trace.Assert(path.IndexOf(OtherSlash) == -1);
                Trace.Assert(!path.Contains(Dot));
                Trace.Assert(!path.Contains(DotDot));
            }

            public static IFileStore FromRoot(string rootPath)
            {
                rootPath = FileMatcher.Normalize(rootPath);
                AssertPathFormat(rootPath);

                var nodeLookupDict = new Dictionary<string, IFileNode>();
                var rootNode = Wrap(rootPath, nodeLookupDict);

                Trace.Assert(rootNode.FullPath == rootNode.RootPath);

                return new FileStore(rootNode, nodeLookupDict);

                FileNode Wrap(string path, Dictionary<string, IFileNode> nodeLookup)
                {
                    FileNode node = null;

                    path = FileMatcher.Normalize(path);

                    if (Directory.Exists(path))
                    {
                        var children = Directory.EnumerateFileSystemEntries(path)
                            .Select(c => Wrap(c, nodeLookup))
                            .ToArray();

                        node = new FileNode
                        {
                            Children = children,
                            FullPath = path,
                            FileName = Path.GetFileName(path),
                            IsDirectory = true,
                            RootPath = rootPath
                        };

                        foreach (var child in children)
                        {
                            child.Parent = node;
                        }
                    }
                    else if (File.Exists(path))
                    {
                        node = new FileNode
                        {
                            Children = EmptyReadonOnlyCollection,
                            FullPath = path,
                            FileName = Path.GetFileName(path),
                            IsDirectory = false,
                            RootPath = rootPath
                        };
                    }

                    if (node == null)
                    {
                        throw new InvalidOperationException();
                    }

                    nodeLookup[path] = node;
                    return node;
                }
            }
        }

        private class FileNode : IFileNode
        {
            public bool IsDirectory { get; set; }
            public string FileName { get; set; }
            public string FullPath { get; set; }
            public string RootPath { get; set; }
            public IFileNode Parent { get; set; }
            public IReadOnlyCollection<IFileNode> Children { get; set; }
        }
    }
}
