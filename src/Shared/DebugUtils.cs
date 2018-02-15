using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using Microsoft.Build.BackEnd;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;

namespace Microsoft.Build.Shared
{
    internal static class DebugUtils
    {
        public static void FileBasedVSBreakpoint(string file)
        {
            if (!Debugger.IsAttached && File.Exists(file))
            {
                File.AppendAllText(
                    file,
                    $"{Process.GetCurrentProcess().Id}\n");
                while (!Debugger.IsAttached)
                {
                }

                Debugger.Break();
            }
        }

        public static bool HasDTB(BuildRequestData request)
        {
            return HasPropertiesTrue(request, "SkipCompilerExecution", "ProvideCommandLineArgs");
        }

        public enum NodeMode
        {
            Central,
            OutOfProcNode,
            OutOfProcTaskHostNode
        }

        public static NodeMode GetNodeMode()
        {
            return ScanNodeMode(Environment.CommandLine);

            NodeMode ScanNodeMode(string input)
            {
                var match = Regex.Match(input, @"/nodemode:(?<nodemode>[12\s])(\s|$)", RegexOptions.IgnoreCase);

                if (!match.Success)
                {
                    return NodeMode.Central;
                }

                var nodeMode = match.Groups["nodemode"].Value;

                Trace.Assert(!string.IsNullOrEmpty(nodeMode));

                switch (nodeMode)
                {
                    case "1":
                        return NodeMode.OutOfProcNode;
                    case "2":
                        return NodeMode.OutOfProcTaskHostNode;
                    default:
                        throw new NotImplementedException();
                }
            }
        }

        public static bool HasPropertiesTrue(BuildRequestData request, params string[] properties)
        {
            var propertySet = properties.ToImmutableHashSet();
            return HasPropertiesTrue(request.GlobalProperties, propertySet) ||
                   HasPropertiesTrue(request?.ProjectInstance.Properties, propertySet);
        }

        private static bool HasPropertiesTrue(
            IEnumerable<ProjectPropertyInstance> properties,
            ISet<string> propertyNames)
        {
            return properties != null &&
                   properties.Any(
                       p => propertyNames.Contains(p.Name, StringComparer.OrdinalIgnoreCase) &&
                            p.EvaluatedValue.Equals("true", StringComparison.OrdinalIgnoreCase));
        }

        public static string ExecutionId => $"NodeMode={GetNodeMode()}_PID={Process.GetCurrentProcess().Id}_{Process.GetCurrentProcess().ProcessName}";

        //public static string ExecutionId => $"{Process.GetCurrentProcess().Id}_{Process.GetCurrentProcess().ProcessName}_{Thread.CurrentThread.Name}-{Thread.CurrentThread.ManagedThreadId}";

        public class CsvPrinter
        {
            private readonly string _directory;
            private readonly string _fileName;
            private const string DefaultPath = @"e:\delete\events";

            public static CsvPrinter Default = new CsvPrinter(DefaultPath, @"events.csv");
            public static CsvPrinter Error = new CsvPrinter(DefaultPath, "Error.csv");

            private readonly string _path;

            public CsvPrinter(string fullPath) : this(Path.GetDirectoryName(fullPath), Path.GetFileName(fullPath))
            {
            }

            public CsvPrinter(string directory, string fileName)
            {
                _directory = directory;
                _fileName = fileName;

                Directory.CreateDirectory(_directory);
                _path = Path.Combine(_directory, _fileName);
            }

            public static CsvPrinter WithFullPath(string path)
            {
                return new CsvPrinter(path);
            }

            public static CsvPrinter WithFileName(string fileName)
            {
                return new CsvPrinter(DefaultPath, fileName);
            }

            public void WriteCsvLine(params object[] elements)
            {
                var line = string.Join(",", elements);

                var counter = 0;
                var errorDisplayed = false;

                while (true)
                    try
                    {
                        File.AppendAllText(_path, line + $",writeCounter={counter}\n");
                        break;
                    }
                    catch(Exception e)
                    {
                        counter++;
                        Thread.Sleep(TimeSpan.FromMilliseconds(50));

                        PrintError(e, $"file write exception to {_path}");

                        if (!errorDisplayed && counter > 50)
                        {
                            errorDisplayed = true;

                            PrintError(e, $"can't take file lock on {_path}");

                            break;
                        }
                    }
            }

            public void PrintError(Exception exception, string message)
            {
                var file = Path.Combine(_path + "_DebugUtilsError_" + Guid.NewGuid());

                message = $"{message}\ne.message: {exception.Message}\n{EscapeForCsv(exception.StackTrace)}";

                File.WriteAllText(file, message);
            }

            public void WriteBuildEvent(BuildEventArgs eventArgs, int eventArgsHashCount = 0, int packetHashCount = 0, INodePacket packet = null)
            {
                try
                {
                    var message = new List<string>();

                    bool write = false;

                    BuildEventContext eventContext = eventArgs.BuildEventContext;

                    message.Add(eventContext != null ? eventContext.NodeId.ToString() : "NA");

                    message.Add(eventContext != null ? $"ProjectContextId={eventContext.ProjectContextId}" : "NA");

                    switch (eventArgs)
                    {
                        case TargetStartedEventArgs targetStarted:
                            write = true;
                            message.AddRange(
                                new List<string>
                                {
                                    "Target Started",
                                    targetStarted.TargetName,
                                    $"TargetId={eventContext.TargetId}"
                                });
                            break;

                        case TargetFinishedEventArgs targetEnded:
                            write = true;
                            message.AddRange(
                                new List<string>
                                {
                                    "Target Finished",
                                    targetEnded.TargetName,
                                    $"TargetId={eventContext.TargetId}"
                                });
                            break;

                        case ProjectStartedEventArgs projectStarted:
                            write = true;
                            message.AddRange(
                                new List<string>
                                {
                                    "Project Started",
                                    projectStarted.ProjectFile,
                                    $"Targets=[{projectStarted.TargetNames}]"
                                });
                            break;

                        case ProjectFinishedEventArgs projectFinished:
                            write = true;
                            message.AddRange(
                                new List<string>
                                {
                                    "Project Finished",
                                    projectFinished.ProjectFile,
                                });
                            break;

                        //case BuildStartedEventArgs buildStarted:
                        //    write = true;
                        //    message.AddRange(
                        //        new List<string>
                        //        {
                        //            "Build Started",
                        //            "Build",
                        //        });
                        //    break;

                        //case BuildFinishedEventArgs buildFinished:
                        //    write = true;
                        //    message.AddRange(
                        //        new List<string>
                        //        {
                        //            "Build Finished",
                        //            "Build",
                        //            $"Succeeded: {buildFinished.Succeeded}",
                        //            $"Message: {buildFinished.Message}",

                        //        });
                        //    break;
                    }

                    message.Add($"eventArgsHash={eventArgs.GetHashCode()}");
                    message.Add($"eventArgsHashCount={eventArgsHashCount}");

                    message.Add($"packetHash={packet?.GetHashCode()}");
                    message.Add($"packetHashCount={packetHashCount}");

                    if (write)
                    {
                        WriteCsvLine(message.ToArray());
                    }
                }
                catch (Exception e)
                {
                    PrintError(e, $"{nameof(WriteBuildEvent)} failed");
                }
            }

            private static string EscapeForCsv(string aString) => aString.Replace(',', ';');
        }
    }
}
