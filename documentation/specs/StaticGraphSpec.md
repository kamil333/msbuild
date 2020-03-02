- [Problem](#problem)
- [Current assumptions](#current-assumptions)
- [Current graph specification elements](#current-graph-specification-elements)
- [What's nuget pack doing](#whats-nuget-pack-doing)
- [Potentially unrelated: Handling MSBuild task parameters](#potentially-unrelated-handling-msbuild-task-parameters)
  - [MSBuild task parameters that influence static graph construction](#msbuild-task-parameters-that-influence-static-graph-construction)
  - [MSBuild task parameters that do not (seem to) influence static graph](#msbuild-task-parameters-that-do-not-seem-to-influence-static-graph)
- [Solutions](#solutions)
  - [Design conundrums](#design-conundrums)
  - [1. New `CustomProjectReference` and enhanced `ProjectReferenceTargets`](#1-new-customprojectreference-and-enhanced-projectreferencetargets)
    - [XML specs](#xml-specs)
    - [Observations](#observations)
  - [Other paradigms](#other-paradigms)
    - [MSBuild tasks 1-1 mapping to specs](#msbuild-tasks-1-1-mapping-to-specs)
    - [Static analysis](#static-analysis)
    - [Dynamic analysis](#dynamic-analysis)

# Problem
Static graph specification cannot express calling patterns from Nuget pack and the ASPNet repo.

# Current assumptions
So far static graph didn't care about the MSBuild task too much. We assumed that:
1. A single reference carrying item would be enough (`ProjectReference`). Basically it assumes all possible MSBuild task calls will be covered by `ProjectReference`.
2. All global properties that a project sets on its references can be encoded in metadata on `ProjectReference`.
3. Entry targets do not influence graph construction.
4. `ProjectReferenceTargets` apply uniformly across all edges (except crosstargeting).
5. A project does not reference its entire transitive closure of dependencies, or filtered closure.
6. All MSBuild task parameters can be ignored.
7. Self calls (project msbuilds into itself with potential GP alterations) are exempt from project isolation. However, the crosstargeting pattern is given special, C# hardcoded attention to.

Most of these assumptions are broken by Nuget pack and the ASPNet repo.

# Current graph specification elements
1. `ProjectReference` item defines edges and nodes (path + global properties)
   - Global property interpretation done in C# to guess at what `AssignProjectConfiguration` task is doing
2. `ProjectReferenceTargets` to infer what targets to call on each node
3. `IsGraphBuild` global property added to all nodes so that users can react (e.g. turn off transitive references)
4. `GraphIsolationExemptReference` item that can be defined by projects to exclude references (by path) from /isolate constraints.
5. Crosstargeting inner and outer builds are defined via the properties `InnerBuildProperty` and `InnerBuildPropertyValues`. C# implementation then adds extra graph massaging for crosstargeting
   -  crosstargeting complications
      -  outer->inner edges differ if the outer build is a graph root or not (C# hardcoded). Reason: mimic runtime graph.
      -  `ProjectReference` applies to inner builds, not outer builds (C# hardcoded). Reason: mimic runtime graph.
      -  `ProjectReferenceTargets` targets for outer builds (e.g. `<ProjectReferenceTargets Include='Build' Targets='GetTargetFrameworks' IsCrossTargetingBuild='true'`) apply to both outer builds and inner builds. Reason: hack for supporting crosstargeting agnostic nodes which are both outer and inner at the same time.

More details: https://github.com/microsoft/msbuild/blob/master/documentation/specs/static-graph.md

# What's nuget pack doing

- (not supported) Add edge from outerbuilds (which got discovered by ProjectReference and not nuget stuff) to transitive closure (of outer builds); +GP: BPR=false; Targets=_GetProjectVersion
- (supported) Add edge from outerbuilds (which got discovered by ProjectReference and not nuget stuff) to innerbuilds, no GP alterations; Targets=[…]
- (not supported) Add edge from outerbuilds (which got discovered by ProjectReference and not nuget stuff) to innerbuilds; +GP:BPR=false; Targets=[…]. Cut recursive graph construction after the first level of references to the modified inner builds.
  - The problem is NOT with the modified innerbuilds which are exempt from /isolate constraints, but with their references (that inherit BPR=false), which are not exempt.

# Potentially unrelated: Handling [MSBuild task](https://docs.microsoft.com/en-us/visualstudio/msbuild/msbuild-task?view=vs-2019) parameters
We currently don't care about the other MSBuild task parameters. For the ones that can influence the graph, it would be nice to either support, or detect and error.
The list below splits MSBuild task parameters in parameters that influence and parameters that don't influence static graph generation.

## MSBuild task parameters that influence static graph construction
1. **Properties & RemoveProperties**
   - Static graph assumption that fails: ProjectReference item defines all the global properties, not the MSBuild task.
2. **SkipNonexistentTargets**
3. **Targets**
   - Already specified via the target protocol
4. **Projects**
   - Initial assumption that fails: ProjectReference contains a union of all projects that all MSBuild tasks will call into
5. **TargetOutputs** -> not sure if it can break static graph or not, needs investigation. Probably the results get cached and serialized
   - Potential solution if it does not just work
     - Option in target protocol to request target outputs
     - Static graph tells BuildXL to request this for each project
     - New MSBuild cmdline so BuildXL can request the target outputs per project invocation
   - Gotchas
     - TargetOutputs seems to be used a lot in common.targets.
6. **RunEachTargetSeparately** -> static graph should error/warn
   - Incompatible with static graph, where a project is visited only once.
7. **TargetAndPropertyListSeparators** -> static graph should error/warn
   - Ideally, we'd have to put this in the target protocol, so graph constructions knows how to split targets and properties. But I'd rather just not support it and fail.
## MSBuild task parameters that do not (seem to) influence static graph
1. RebaseOutputs: path combines the calling project's path over the returned items
2. StopOnFirstFailure
3. UnloadProjectsOnCompletion: never read in code, seems deprecated
4. ToolsVersion: based on our story that tv should be deprecated, we'll just ignore this. Otherwise, would have to update the target protocol and make BuildXL roundtrip it as a cmd line argument
5. BuildInParallel: if turned off, defeats the purpose, so ignore. Alternatively, error / warn if it's off.
6. UseResultsCache: never read in code, deprecated
7. SkipNonexistentProjects: right now, graph construction fails on declared but missing references. For now I'd like to keep it that way, so ignore the flag
Snippets

# Solutions

## Design conundrums

- Should we have separate gestures for specifying references and specifying target flow? Or a single gesture that represents an MSBuild call (and thus maybe have as many msbuild call specs as `<MSBuild>` task invocations there are). The former is inspired by Quickbuild's graph specification approach, where each node is built with the `Build` target. It might be too restrictive for the rest of the world. The later makes it conceptually easier to understand (1-1 mapping between `<MSBuild>` calls and graph specs), makes it easier to mechanically generate the specs from a runtime trace, but it might make it more verbose.
- Now that `ProjectReference` is not longer sufficient, how to specify metadata mutations for references (overwrite / add / remove)? Should we keep `ProjectReference` semantics (Properties, AdditionalProperties, Set*, GlobalPropertiesToRemove, RemoveProperties), or invent new names?
- As we change these gestures, should we version them and keep support for older stuff? The benefit is the capability of having new msbuild bits with old non updated msbuildian.
- Should we keep crosstargeting as a special case, hardcoded in C#? Instead of fully specifying it in msbuildian?

## 1. New `CustomProjectReference` and enhanced `ProjectReferenceTargets`

Most intuitive/simple change I came up with. This solution augments the existing gestures. It assumes ProjectReference as an implicit node provider.

### XML specs
```xml
<CustomProjectReference Include="1.csproj;@(ProjectReference)" ProjectReferenceKind="NugetPack" ReferenceTransitiveClosure="true" WithClosurePropertyValues="IsCrosstargetingBuild=true" AddGlobalProperties="Foo=Bar" RemoveGlobalProperties= "Zar" ReplaceGlobalProperties="Foo=bar"/>

<ProjectReferenceTargets Include="Build" Targets="GetCurrentProjectStaticWebAssets" 
ProjectReferenceKind="NugetPack;NugetRestore"
SkipIfAbsent="true"/>
```

`ProjectReferenceKind` gives an arbitrary name to the source of the edge/node. This is used to filter target propagation: `ProjectReferenceTargets` items with `ProjectReferenceKind` only flow on edges created by that `ProjectReferenceKind`. If absent, targets flow on all edges.

Nuget pack simulation:
```xml
<!--  Add edge from outerbuilds (which got discovered by ProjectReference and not nuget stuff) to transitive closure (of outer builds); +GP: BPR=false;Targets=_GetProjectVersion -->
<CustomProjectReference
    Condition="$(IsCrossTargetingBuild) == true and $(BuildProjectReferences) != false"
    Include="@(ProjectReference)"
    ReferenceTransitiveClosure="true"
    WithClosurePropertyValues="IsCrossTargetingBuild=true"
    ReplaceGlobalProperties="BuildProjectReference=False"
    ProjectReferenceKind="NugetPack_outerBuildClosure" />

<!-- This one's for when Pack is not called but the autopack property is set -->
<ProjectReferenceTargets Condition="($(GeneratePackageOnBuild) == true) and ($(IsCrossTargetingBuild) == true)"
Include="Build" Targets="_GetProjectVersion" 
ProjectReferenceKind="NugetPack_outerBuildClosure"
SkipIfAbsent="true"/>

<!-- This one's for when Pack is called without the autopack property being set -->
<ProjectReferenceTargets Condition="$(IsCrossTargetingBuild) == true"
Include="Pack" Targets="_GetProjectVersion" 
ProjectReferenceKind="NugetPack_outerBuildClosure"
SkipIfAbsent="true"/>
```
```xml
<!-- Add edge from outerbuild to innerbuilds; Targets=[…] -->

<!-- This one's for when Pack is not called but the autopack property is set -->
<ProjectReferenceTargets Condition="($(GeneratePackageOnBuild) == true) and ($(IsCrossTargetingBuild) == true)"
Include="Build" Targets="..."/>

<!-- This one's for when Pack is called without the autopack property being set -->
<ProjectReferenceTargets Condition="$(IsCrossTargetingBuild) == true"
Include="Pack" Targets="..."/>

```
```xml
<!-- Add edge from outerbuilds (which got discovered by ProjectReference and not nuget stuff) to innerbuilds; +GP:BPR=false; Targets=[…] (added nodes will call GetTargetFrameworks on reference outerbuilds and GetTargetPath on reference innerbuilds) -->
<CustomProjectReference
    Condition="$(IsCrossTargetingBuild) == true and $(BuildProjectReferences) != false"
    Include="@(MSBuildThisFileFullPath)"
    ReplaceGlobalProperties="BuildProjectReference=False"
    ProjectReferenceKind="NugetPack_innerBuildsWithBuildProjectReferences" />

<!-- This one's for when Pack is not called but the autopack property is set -->
<ProjectReferenceTargets Condition="($(GeneratePackageOnBuild) == true) and ($(IsCrossTargetingBuild) == true)"
Include="Build" Targets="..."
ProjectReferenceKind="NugetPack_innerBuildsWithBuildProjectReferences" />

<!-- This one's for when Pack is called without the autopack property being set -->
<ProjectReferenceTargets Condition="$(IsCrossTargetingBuild) == true"
Include="Pack" Targets="..." 
ProjectReferenceKind="NugetPack_innerBuildsWithBuildProjectReferences" />
```
### Observations
 `CustomProjectReference` needs to also care whether the reference is crosstargeting or not. If it's a crosstargeting node, then the usual crosstargeting graph massage needs to happen.

Extra feature: code construction API can also receive the entry targets and prune the unneeded nodes. For example, no use adding nodes for nuget if `/t:pack` isn't used.

## Other paradigms

### MSBuild tasks 1-1 mapping to specs

Rather than separate graph shape (via `ProjectReference` and `CustomProjectReference`) and target flow (via `ProjectReferenceTargets`), have a single gesture that represents one MSBuild task call.

```xml
<MSBuildTaskDeclaration
    Include="@(ProjectReference)"
    WhereProject="outerbuild"
    Target="_GetProjectReferenceTargetFrameworkProperties"
    RemoveGlobalProperties="..."
    OnEntryTargets="Build"
/>
```

### Static analysis

Symbolic execution over msbuild to infer msbuild task calls

### Dynamic analysis

Bring back Static MSBuild from MSR: find the project graph by running a fast, reduced build which avoids "expensive" tasks (e.g. csc, RAR).