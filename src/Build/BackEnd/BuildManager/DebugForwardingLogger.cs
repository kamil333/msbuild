// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//-----------------------------------------------------------------------
// </copyright>
// <summary>Implementation of the Build Manager.</summary>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Build.Framework;
using Microsoft.Build.Shared;

namespace Microsoft.Build.Execution
{
    public class DebugForwardingLogger : IForwardingLogger
    {
        public LoggerVerbosity Verbosity { get; set; } = LoggerVerbosity.Normal;

        public string Parameters { get; set; }

        public void Initialize(IEventSource eventSource)
        {
            eventSource.AnyEventRaised += ProcessEvent;
        }

        public void Initialize(IEventSource eventSource, int nodeCount)
        {
            Initialize(eventSource);
        }

        private static readonly ISet<Type> _allowableTypes = new HashSet<Type>
        {
            typeof(ProjectStartedEventArgs),
            typeof(ProjectFinishedEventArgs),
            typeof(TargetStartedEventArgs),
            typeof(TargetFinishedEventArgs),
            typeof(BuildErrorEventArgs),
        };

        private void ProcessEvent(object sender, BuildEventArgs eventArgs)
        {
            string nodeId;

            switch (DebugUtils.GetNodeMode())
            {
                case DebugUtils.NodeMode.Central:
                    nodeId = "1";
                    break;
                case DebugUtils.NodeMode.OutOfProcNode:
                case DebugUtils.NodeMode.OutOfProcTaskHostNode:
                    nodeId = eventArgs.BuildEventContext?.NodeId.ToString() ?? "NA";
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            var baseString = $"SubmissionId={eventArgs.BuildEventContext?.SubmissionId}_NodeId={nodeId}_{DebugUtils.ExecutionId}_DebugForwardingLogger_Hash={GetHashCode()}";

            if (_allowableTypes.Contains(eventArgs.GetType()))
            {
                if (eventArgs is BuildErrorEventArgs error)
                {
                    var csvPrinter = DebugUtils.CsvPrinter.WithFileName($"{baseString}_BuildErrors");
                    csvPrinter.WriteCsvLine(
                        error.File,
                        $"{error.ColumnNumber}:{error.LineNumber}",
                        $"targetID: {error.BuildEventContext.TargetId}",
                        error.Message
                        );
                }
                else
                {
                    var csvPrinter = DebugUtils.CsvPrinter.WithFileName(baseString);
                    csvPrinter.WriteBuildEvent(eventArgs);
                }

                BuildEventRedirector?.ForwardEvent(eventArgs);
            }
        }

        public void Shutdown()
        {
        }

        public IEventRedirector BuildEventRedirector { get; set; }
        public int NodeId { get; set; }
    }
}
