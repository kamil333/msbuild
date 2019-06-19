// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Build.BackEnd;
using Microsoft.Build.Collections;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;
using Microsoft.Build.Execution;
using Microsoft.Build.Shared;

namespace Microsoft.Build.Experimental.Graph
{
    internal class GraphBuilder
    {
        /// <summary>
        /// The thread calling BuildGraph() will act as an implicit worker
        /// </summary>
        private const int ImplicitWorkerCount = 1;

        public IReadOnlyCollection<ProjectGraphNode> ProjectNodes { get; private set; }

        public IReadOnlyCollection<ProjectGraphNode> RootNodes { get; private set; }

        public IReadOnlyCollection<ProjectGraphNode> EntryPointNodes { get; private set; }

        public GraphEdges Edges { get; private set; }

        private readonly List<ConfigurationMetadata> _entryPointConfigurationMetadata;

        private readonly ParallelWorkSet<ConfigurationMetadata, ParsedProject> _graphWorkSet;

        private readonly ProjectCollection _projectCollection;

        private readonly ProjectInterpretation _projectInterpretation;

        private readonly ProjectGraph.ProjectInstanceFactoryFunc _projectInstanceFactory;

        public GraphBuilder(
            IEnumerable<ProjectGraphEntryPoint> entryPoints,
            ProjectCollection projectCollection,
            ProjectGraph.ProjectInstanceFactoryFunc projectInstanceFactory,
            ProjectInterpretation projectInterpretation,
            int degreeOfParallelism,
            CancellationToken cancellationToken)
        {
            _entryPointConfigurationMetadata = AddGraphBuildPropertyToEntryPoints(ExpandSolutionIfNecessary(entryPoints.ToImmutableArray()));
            
            IEqualityComparer<ConfigurationMetadata> configComparer = EqualityComparer<ConfigurationMetadata>.Default;

            _graphWorkSet = new ParallelWorkSet<ConfigurationMetadata, ParsedProject>(
                degreeOfParallelism - ImplicitWorkerCount,
                configComparer,
                cancellationToken);

            _projectCollection = projectCollection;
            _projectInstanceFactory = projectInstanceFactory;
            _projectInterpretation = projectInterpretation;
        }

        public void BuildGraph()
        {
            if (_graphWorkSet.IsCompleted)
            {
                return;
            }
            var allParsedProjects = FindGraphNodes();
            
            Edges = new GraphEdges();
            AddParsedEdges(allParsedProjects, Edges);
            _projectInterpretation.PostProcess(allParsedProjects, this);

            EntryPointNodes = _entryPointConfigurationMetadata.Select(e => allParsedProjects[e].GraphNode).ToList();

            DetectCycles(EntryPointNodes, _projectInterpretation, allParsedProjects);

            RootNodes = GetGraphRoots(EntryPointNodes);
            ProjectNodes = allParsedProjects.Values.Select(p => p.GraphNode).ToList();
        }

        private static IReadOnlyCollection<ProjectGraphNode> GetGraphRoots(IReadOnlyCollection<ProjectGraphNode> entryPointNodes)
        {
            var graphRoots = new List<ProjectGraphNode>(entryPointNodes.Count);

            foreach (var entryPointNode in entryPointNodes)
            {
                if (entryPointNode.ReferencingProjects.Count == 0)
                {
                    graphRoots.Add(entryPointNode);
                }
            }

            graphRoots.TrimExcess();

            return graphRoots;
        }

        private void AddParsedEdges(Dictionary<ConfigurationMetadata, ParsedProject> allParsedProjects, GraphEdges edges)
        {
            foreach (var parsedProject in allParsedProjects)
            {
                foreach (var referenceInfo in parsedProject.Value.ReferenceInfos)
                {
                    ErrorUtilities.VerifyThrow(
                        allParsedProjects.ContainsKey(referenceInfo.ReferenceConfiguration),
                        "all references should have been parsed");

                    parsedProject.Value.GraphNode.AddProjectReference(
                        allParsedProjects[referenceInfo.ReferenceConfiguration].GraphNode,
                        referenceInfo.ProjectReferenceItem,
                        edges);
                }
            }
        }

        private IReadOnlyCollection<ProjectGraphEntryPoint> ExpandSolutionIfNecessary(IReadOnlyCollection<ProjectGraphEntryPoint> entryPoints)
        {
            if (entryPoints.Count == 0 || !entryPoints.Any(e => FileUtilities.IsSolutionFilename(e.ProjectFile)))
            {
                return entryPoints;
            }

            if (entryPoints.Count != 1)
            {
                throw new ArgumentException(
                    ResourceUtilities.FormatResourceStringIgnoreCodeAndKeyword(
                        "StaticGraphAcceptsSingleSolutionEntryPoint",
                        string.Join(";", entryPoints.Select(e => e.ProjectFile))));
            }

            ErrorUtilities.VerifyThrowArgument(entryPoints.Count == 1, "StaticGraphAcceptsSingleSolutionEntryPoint");

            var solutionEntryPoint = entryPoints.Single();
            var solutionGlobalProperties = ImmutableDictionary.CreateRange(
                keyComparer: StringComparer.OrdinalIgnoreCase,
                valueComparer: StringComparer.OrdinalIgnoreCase,
                items: solutionEntryPoint.GlobalProperties ?? ImmutableDictionary<string, string>.Empty);

            var solution = SolutionFile.Parse(FileUtilities.NormalizePath(solutionEntryPoint.ProjectFile));

            if (solution.SolutionParserWarnings.Count != 0 || solution.SolutionParserErrorCodes.Count != 0)
            {
                throw new InvalidProjectFileException(
                    ResourceUtilities.FormatResourceStringIgnoreCodeAndKeyword(
                        "StaticGraphSolutionLoaderEncounteredSolutionWarningsAndErrors",
                        solutionEntryPoint.ProjectFile,
                        string.Join(";", solution.SolutionParserWarnings),
                        string.Join(";", solution.SolutionParserErrorCodes)));
            }

            var projectsInSolution = GetBuildableProjects(solution);

            var currentSolutionConfiguration = SelectSolutionConfiguration(solution, solutionGlobalProperties);

            var newEntryPoints = new List<ProjectGraphEntryPoint>(projectsInSolution.Count);

            foreach (var project in projectsInSolution)
            {
                if (project.ProjectConfigurations.Count == 0)
                {
                    continue;
                }

                var projectConfiguration = SelectProjectConfiguration(currentSolutionConfiguration, project.ProjectConfigurations);

                if (projectConfiguration.IncludeInBuild)
                {
                    newEntryPoints.Add(
                        new ProjectGraphEntryPoint(
                            project.AbsolutePath,
                            solutionGlobalProperties
                                .SetItem("Configuration", projectConfiguration.ConfigurationName)
                                .SetItem("Platform", projectConfiguration.PlatformName)
                            ));
                }
            }

            newEntryPoints.TrimExcess();

            return newEntryPoints;

            IReadOnlyCollection<ProjectInSolution> GetBuildableProjects(SolutionFile solutionFile)
            {
                var buildableProjects = solutionFile.ProjectsInOrder.Where(p => p.ProjectType == SolutionProjectType.KnownToBeMSBuildFormat).ToImmutableArray();

                var projectsWithSolutionDependencies = buildableProjects.Where(p => p.Dependencies.Count != 0);

                if (projectsWithSolutionDependencies.Any())
                {
                    throw new ArgumentException(
                        ResourceUtilities.FormatResourceStringIgnoreCodeAndKeyword(
                            "StaticGraphDoesNotHandleSolutionOnlyDependencies",
                            solutionFile.FullPath,
                            string.Join(";", projectsWithSolutionDependencies))
                        );
                }

                return buildableProjects;
            }

            SolutionConfigurationInSolution SelectSolutionConfiguration(SolutionFile solutionFile, ImmutableDictionary<string, string> globalProperties)
            {
                var solutionConfiguration = globalProperties.ContainsKey("Configuration")
                    ? globalProperties["Configuration"]
                    : solutionFile.GetDefaultConfigurationName();

                var solutionPlatform = globalProperties.ContainsKey("Platform")
                    ? globalProperties["Platform"]
                    : solutionFile.GetDefaultPlatformName();

                return new SolutionConfigurationInSolution(solutionConfiguration, solutionPlatform);
            }

            ProjectConfigurationInSolution SelectProjectConfiguration(
                SolutionConfigurationInSolution solutionConfig,
                IReadOnlyDictionary<string, ProjectConfigurationInSolution> projectConfigs)
            {
                // implements the matching described in https://docs.microsoft.com/en-us/visualstudio/ide/understanding-build-configurations?view=vs-2019#how-visual-studio-assigns-project-configuration

                var solutionConfigFullName = solutionConfig.FullName;

                if (projectConfigs.ContainsKey(solutionConfigFullName))
                {
                    return projectConfigs[solutionConfigFullName];
                }

                var partiallyMarchedConfig = projectConfigs.FirstOrDefault(pc => pc.Value.ConfigurationName.Equals(solutionConfig.ConfigurationName, StringComparison.OrdinalIgnoreCase)).Value;
                return partiallyMarchedConfig ?? projectConfigs.First().Value;
            }
        }

        private static List<ConfigurationMetadata> AddGraphBuildPropertyToEntryPoints(IEnumerable<ProjectGraphEntryPoint> entryPoints)
        {
            {
                var entryPointConfigurationMetadata = new List<ConfigurationMetadata>();

                foreach (var entryPoint in entryPoints)
                {
                    var globalPropertyDictionary = CreatePropertyDictionary(entryPoint.GlobalProperties);

                    AddGraphBuildGlobalVariable(globalPropertyDictionary);

                    var configurationMetadata = new ConfigurationMetadata(FileUtilities.NormalizePath(entryPoint.ProjectFile), globalPropertyDictionary);
                    entryPointConfigurationMetadata.Add(configurationMetadata);
                }

                return entryPointConfigurationMetadata;
            }

            void AddGraphBuildGlobalVariable(PropertyDictionary<ProjectPropertyInstance> globalPropertyDictionary)
            {
                if (globalPropertyDictionary.GetProperty(PropertyNames.IsGraphBuild) == null)
                {
                    globalPropertyDictionary[PropertyNames.IsGraphBuild] = ProjectPropertyInstance.Create(PropertyNames.IsGraphBuild, "true");
                }
            }
        }

        /// <remarks>
        ///     Traverse the found nodes and add edges.
        ///     Maintain the state of each node (InProcess and Processed) to detect cycles.
        ///     Returns false if cycles were detected.
        /// </remarks>
        private void DetectCycles(
            IReadOnlyCollection<ProjectGraphNode> entryPointNodes,
            ProjectInterpretation projectInterpretation,
            Dictionary<ConfigurationMetadata, ParsedProject> allParsedProjects)
        {
            var nodeStates = new Dictionary<ProjectGraphNode, NodeVisitationState>();

            foreach (var entryPointNode in entryPointNodes)
            {
                if (!nodeStates.ContainsKey(entryPointNode))
                {
                    VisitNode(entryPointNode, nodeStates);
                }
                else
                {
                    ErrorUtilities.VerifyThrow(
                        nodeStates[entryPointNode] == NodeVisitationState.Processed,
                        "entrypoints should get processed after a call to detect cycles");
                }
            }

            return;

            (bool success, List<string> projectsInCycle) VisitNode(
                ProjectGraphNode node,
                IDictionary<ProjectGraphNode, NodeVisitationState> nodeState)
            {
                nodeState[node] = NodeVisitationState.InProcess;

                foreach (var referenceNode in node.ProjectReferences)
                {
                    if (nodeState.TryGetValue(referenceNode, out var projectReferenceNodeState))
                    {
                        // Because this is a depth-first search, we should only encounter new nodes or nodes whose subgraph has been completely processed.
                        // If we encounter a node that is currently being processed(InProcess state), it must be one of the ancestors in a circular dependency.
                        if (projectReferenceNodeState == NodeVisitationState.InProcess)
                        {
                            if (node.Equals(referenceNode))
                            {
                                // the project being evaluated has a reference to itself
                                var selfReferencingProjectString =
                                    FormatCircularDependencyError(new List<string> {node.ProjectInstance.FullPath, node.ProjectInstance.FullPath});
                                throw new CircularDependencyException(
                                    string.Format(
                                        ResourceUtilities.GetResourceString("CircularDependencyInProjectGraph"),
                                        selfReferencingProjectString));
                            }

                            // the project being evaluated has a circular dependency involving multiple projects
                            // add this project to the list of projects involved in cycle 
                            var projectsInCycle = new List<string> {referenceNode.ProjectInstance.FullPath};
                            return (false, projectsInCycle);
                        }
                    }
                    else
                    {
                        // recursively process newly discovered references
                        var loadReference = VisitNode(referenceNode, nodeState);
                        if (!loadReference.success)
                        {
                            if (loadReference.projectsInCycle[0].Equals(node.ProjectInstance.FullPath))
                            {
                                // we have reached the nth project in the cycle, form error message and throw
                                loadReference.projectsInCycle.Add(referenceNode.ProjectInstance.FullPath);
                                loadReference.projectsInCycle.Add(node.ProjectInstance.FullPath);

                                var errorMessage = FormatCircularDependencyError(loadReference.projectsInCycle);
                                throw new CircularDependencyException(
                                    string.Format(
                                        ResourceUtilities.GetResourceString("CircularDependencyInProjectGraph"),
                                        errorMessage));
                            }

                            // this is one of the projects in the circular dependency
                            // update the list of projects in cycle and return the list to the caller
                            loadReference.projectsInCycle.Add(referenceNode.ProjectInstance.FullPath);
                            return (false, loadReference.projectsInCycle);
                        }
                    }
                }

                nodeState[node] = NodeVisitationState.Processed;
                return (true, null);
            }
        }

        private ParsedProject ParseProject(ConfigurationMetadata configurationMetadata)
        {
            // TODO: ProjectInstance just converts the dictionary back to a PropertyDictionary, so find a way to directly provide it.
            var globalProperties = configurationMetadata.GlobalProperties.ToDictionary();

            var projectInstance = _projectInstanceFactory(
                configurationMetadata.ProjectFullPath,
                globalProperties,
                _projectCollection);

            if (projectInstance == null)
            {
                throw new InvalidOperationException(ResourceUtilities.GetResourceString("NullReferenceFromProjectInstanceFactory"));
            }

            var graphNode = new ProjectGraphNode(projectInstance);

            var referenceInfos = ParseReferences(graphNode);

            return new ParsedProject(configurationMetadata, graphNode, referenceInfos);
        }

        /// <summary>
        ///     Load a graph with root node at entryProjectFile
        ///     Maintain a queue of projects to be processed and evaluate projects in parallel
        ///     Returns false if loading the graph is not successful
        /// </summary>
        private Dictionary<ConfigurationMetadata, ParsedProject> FindGraphNodes()
        {
            foreach (ConfigurationMetadata projectToEvaluate in _entryPointConfigurationMetadata)
            {
                SubmitProjectForParsing(projectToEvaluate);
                                /*todo: fix the following double check-then-act concurrency bug: one thread can pass the two checks, loose context,
                             meanwhile another thread passes the same checks with the same data and inserts its reference. The initial thread regains context
                             and duplicates the information, leading to wasted work
                             */
            }

            _graphWorkSet.WaitForAllWork();
            _graphWorkSet.Complete();
            _graphWorkSet.WaitForCompletion();

            return _graphWorkSet.CompletedWork;
        }

        private void SubmitProjectForParsing(ConfigurationMetadata projectToEvaluate)
        {
            _graphWorkSet.AddWork(projectToEvaluate, () => ParseProject(projectToEvaluate));
        }

        private List<ProjectInterpretation.ReferenceInfo> ParseReferences(ProjectGraphNode parsedProject)
        {
            var referenceInfos = new List<ProjectInterpretation.ReferenceInfo>();

            foreach (var referenceInfo in _projectInterpretation.GetReferences(parsedProject.ProjectInstance))
            {
                if (FileUtilities.IsSolutionFilename(referenceInfo.ReferenceConfiguration.ProjectFullPath))
                {
                    throw new InvalidOperationException(ResourceUtilities.FormatResourceStringIgnoreCodeAndKeyword(
                        "StaticGraphDoesNotSupportSlnReferences",
                        referenceInfo.ReferenceConfiguration.ProjectFullPath,
                        referenceInfo.ReferenceConfiguration.ProjectFullPath
                        ));
                }
                
                SubmitProjectForParsing(referenceInfo.ReferenceConfiguration);

                referenceInfos.Add(referenceInfo);
            }

            return referenceInfos;
        }

        internal static string FormatCircularDependencyError(List<string> projectsInCycle)
        {
            var errorMessage = new StringBuilder(projectsInCycle.Select(p => p.Length).Sum());

            errorMessage.AppendLine();
            for (var i = projectsInCycle.Count - 1; i >= 0; i--)
            {
                if (i != 0)
                {
                    errorMessage.Append(projectsInCycle[i])
                        .Append(" ->")
                        .AppendLine();
                }
                else
                {
                    errorMessage.Append(projectsInCycle[i]);
                }
            }

            return errorMessage.ToString();
        }

        private static PropertyDictionary<ProjectPropertyInstance> CreatePropertyDictionary(IDictionary<string, string> properties)
        {
            PropertyDictionary<ProjectPropertyInstance> propertyDictionary;
            if (properties == null)
            {
                propertyDictionary = new PropertyDictionary<ProjectPropertyInstance>(0);
            }
            else
            {
                propertyDictionary = new PropertyDictionary<ProjectPropertyInstance>(properties.Count);
                foreach (var entry in properties)
                {
                    propertyDictionary[entry.Key] = ProjectPropertyInstance.Create(entry.Key, entry.Value);
                }
            }

            return propertyDictionary;
        }

        internal class GraphEdges
        {
            private ConcurrentDictionary<(ProjectGraphNode, ProjectGraphNode), ProjectItemInstance> ReferenceItems =
                new ConcurrentDictionary<(ProjectGraphNode, ProjectGraphNode), ProjectItemInstance>();

            internal int Count => ReferenceItems.Count;

            public ProjectItemInstance this[(ProjectGraphNode node, ProjectGraphNode reference) key]
            {
                get
                {
                    ErrorUtilities.VerifyThrow(ReferenceItems.ContainsKey(key), "All requested keys should exist");
                    return ReferenceItems[key];
                }

                // First edge wins, in accordance with vanilla msbuild behaviour when multiple msbuild tasks call into the same logical project
                set => ReferenceItems.TryAdd(key, value);
            }

            public void RemoveEdge((ProjectGraphNode node, ProjectGraphNode reference) key)
            {
                ErrorUtilities.VerifyThrow(ReferenceItems.ContainsKey(key), "All requested keys should exist");

                ReferenceItems.TryRemove(key, out _);
            }

            internal bool TestOnly_HasEdge((ProjectGraphNode node, ProjectGraphNode reference) key) => ReferenceItems.ContainsKey(key);
        }

        private enum NodeVisitationState
        {
            // the project has been evaluated and its project references are being processed
            InProcess,
            // all project references of this project have been processed
            Processed
        }
    }

    internal readonly struct ParsedProject
    {
        public ConfigurationMetadata ConfigurationMetadata { get; }
        public ProjectGraphNode GraphNode { get; }
        public List<ProjectInterpretation.ReferenceInfo> ReferenceInfos { get; }

        public ParsedProject(ConfigurationMetadata configurationMetadata, ProjectGraphNode graphNode, List<ProjectInterpretation.ReferenceInfo> referenceInfos)
        {
            ConfigurationMetadata = configurationMetadata;
            GraphNode = graphNode;
            ReferenceInfos = referenceInfos;
        }
    }
}
