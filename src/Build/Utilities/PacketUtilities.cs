// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using Microsoft.Build.BackEnd;
using Microsoft.Build.BackEnd.SdkResolution;
using Microsoft.Build.Execution;
using Microsoft.Build.Shared;
using Microsoft.Build.Shared.Debugging;

namespace Microsoft.Build.Internal
{
    internal static class PacketUtilities
    {
        /// <summary>
        /// Ensures that the packet type matches the expected type
        /// </summary>
        /// <typeparam name="I">The instance-type of packet being expected</typeparam>
        public static I ExpectPacketType<I>(INodePacket packet, NodePacketType expectedType) where I : class, INodePacket
        {
            I castPacket = packet as I;

            // PERF: Not using VerifyThrow here to avoid boxing of expectedType.
            if (castPacket == null)
            {
                ErrorUtilities.ThrowInternalError("Incorrect packet type: {0} should have been {1}", packet.Type, expectedType);
            }

            return castPacket;
        }

        public static void LogPacket(int node, INodePacket packet, string packetDirection)
        {
            try
            {
                var args = new List<string>();

                args.Add(packetDirection);
                args.Add(packet.Type.ToString());
                args.Add($"Node={node}");

                int requestId = Int32.MinValue;
                string extraInfo = null;

                switch (packet.Type)
                {
                    case NodePacketType.BuildRequestBlocker:
                        BuildRequestBlocker blocker = PacketUtilities.ExpectPacketType<BuildRequestBlocker>(packet, NodePacketType.BuildRequestBlocker);

                        requestId = blocker.BlockingRequestId;
                        extraInfo = $"Blocked:{blocker.BlockedRequestId}";

                        break;

                    case NodePacketType.BuildRequestConfiguration:
                        BuildRequestConfiguration requestConfiguration = PacketUtilities.ExpectPacketType<BuildRequestConfiguration>(packet, NodePacketType.BuildRequestConfiguration);

                        extraInfo = $"ConfigId={requestConfiguration.ConfigurationId};Path={requestConfiguration.ProjectFullPath ?? "NA"}";

                        break;

                    case NodePacketType.BuildResult:
                        BuildResult result = PacketUtilities.ExpectPacketType<BuildResult>(packet, NodePacketType.BuildResult);

                        requestId = result.GlobalRequestId;
                        extraInfo = $"Result={result.OverallResult}; Exception={result.Exception?.Message}";

                        break;

                    case NodePacketType.NodeShutdown:
                        // Remove the node from the list of active nodes.  When they are all done, we have shut down fully
                        NodeShutdown shutdownPacket = PacketUtilities.ExpectPacketType<NodeShutdown>(packet, NodePacketType.NodeShutdown);

                        extraInfo = $"Reason={shutdownPacket.Reason};Exception={shutdownPacket.Exception?.Message}";

                        break;
                    case NodePacketType.NodeConfiguration:
                        var nodeConfiguration = ExpectPacketType<NodeConfiguration>(packet, NodePacketType.NodeConfiguration);
                        break;
                    case NodePacketType.BuildRequestConfigurationResponse:
                        var requestConfigurationResponse = ExpectPacketType<BuildRequestConfigurationResponse>(packet, NodePacketType.BuildRequestConfigurationResponse);
                        break;
                    case NodePacketType.BuildRequestUnblocker:
                        var unblocker = ExpectPacketType<BuildRequestUnblocker>(packet, NodePacketType.BuildRequestUnblocker);

                        extraInfo = $"UnblockedRequest={unblocker.BlockedRequestId};Result={unblocker.Result.OverallResult}";

                        break;
                    case NodePacketType.BuildRequest:
                        var request = ExpectPacketType<BuildRequest>(packet, NodePacketType.BuildRequest);

                        requestId = request.GlobalRequestId;

                        extraInfo = $"ConfigId={request.ConfigurationId};Targets={string.Join(";", request.Targets)}";

                        break;
                    case NodePacketType.LogMessage:
                        var logMessage = ExpectPacketType<LogMessagePacket>(packet, NodePacketType.LogMessage);
                        break;
                    case NodePacketType.NodeBuildComplete:
                        var nodeBuildComplete = ExpectPacketType<NodeBuildComplete>(packet, NodePacketType.NodeBuildComplete);
                        break;
                    case NodePacketType.TaskHostConfiguration:
                        var taskHostConfiguration = ExpectPacketType<TaskHostConfiguration>(packet, NodePacketType.TaskHostConfiguration);
                        break;
                    case NodePacketType.TaskHostTaskComplete:
                        var taskHostTaskComplete = ExpectPacketType<TaskHostTaskComplete>(packet, NodePacketType.TaskHostTaskComplete);
                        break;
                    case NodePacketType.TaskHostTaskCancelled:
                        var taskHostTaskCancelled = ExpectPacketType<TaskHostTaskCancelled>(packet, NodePacketType.TaskHostTaskCancelled);
                        break;
                    case NodePacketType.ResolveSdkRequest:
                        var resolveSdkRequest = ExpectPacketType<SdkResolverRequest>(packet, NodePacketType.ResolveSdkRequest);
                        break;
                    case NodePacketType.ResolveSdkResponse:
                        var resolveSdkResponse = ExpectPacketType<SdkResult>(packet, NodePacketType.ResolveSdkResponse);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                args.Add($"RequestId={requestId}");
                args.Add(extraInfo);

                PrintLineDebugger.DefaultWithProcessInfo.Value.Log(args);
            }

            catch (Exception e)
            {
                PrintLineDebugger.DefaultWithProcessInfo.Value.Log(new []{$"Exception while logging packet: {e.StackTrace}"});
            }
        }
    }
}
