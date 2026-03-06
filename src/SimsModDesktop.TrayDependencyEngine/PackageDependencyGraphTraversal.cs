namespace SimsModDesktop.TrayDependencyEngine;

internal sealed record PackageDependencyGraphTraversalResult(
    IReadOnlyList<string> FilePaths,
    int VisitedNodes,
    long VisitedEdges,
    bool HadSeedMatch);

internal static class PackageDependencyGraphTraversal
{
    public static PackageDependencyGraphTraversalResult Traverse(
        PackageDependencyGraph graph,
        IReadOnlyList<ResolvedResourceRef> directMatches,
        CancellationToken cancellationToken)
    {
        if (graph.PackageCount == 0 || graph.PackagePathsById.Length == 0)
        {
            return new PackageDependencyGraphTraversalResult(Array.Empty<string>(), 0, 0, false);
        }

        var packageIdByPath = new Dictionary<string, int>(graph.PackageCount, StringComparer.OrdinalIgnoreCase);
        for (var packageId = 0; packageId < graph.PackagePathsById.Length; packageId++)
        {
            packageIdByPath[graph.PackagePathsById[packageId]] = packageId;
        }

        var visited = new bool[graph.PackageCount];
        var queue = new Queue<int>(Math.Min(graph.PackageCount, 256));
        var hadSeedMatch = false;
        for (var index = 0; index < directMatches.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = directMatches[index].FilePath;
            if (string.IsNullOrWhiteSpace(path) || !packageIdByPath.TryGetValue(path, out var packageId))
            {
                continue;
            }

            hadSeedMatch = true;
            if (visited[packageId])
            {
                continue;
            }

            visited[packageId] = true;
            queue.Enqueue(packageId);
        }

        if (!hadSeedMatch)
        {
            return new PackageDependencyGraphTraversalResult(Array.Empty<string>(), 0, 0, false);
        }

        var visitedNodes = 0;
        long visitedEdges = 0;
        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceId = queue.Dequeue();
            visitedNodes++;

            var start = graph.EdgeOffsets[sourceId];
            var end = graph.EdgeOffsets[sourceId + 1];
            visitedEdges += Math.Max(0, end - start);
            for (var edgeIndex = start; edgeIndex < end; edgeIndex++)
            {
                var targetId = graph.EdgeTargets[(int)edgeIndex];
                if (targetId < 0 || targetId >= graph.PackageCount || visited[targetId])
                {
                    continue;
                }

                visited[targetId] = true;
                queue.Enqueue(targetId);
            }
        }

        var filePaths = new List<string>(visitedNodes);
        for (var packageId = 0; packageId < graph.PackageCount; packageId++)
        {
            if (!visited[packageId])
            {
                continue;
            }

            filePaths.Add(graph.PackagePathsById[packageId]);
        }

        return new PackageDependencyGraphTraversalResult(filePaths, visitedNodes, visitedEdges, true);
    }
}
