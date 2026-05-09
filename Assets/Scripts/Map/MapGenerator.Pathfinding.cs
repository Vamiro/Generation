using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

public partial class MapGenerator
{
    void CreatePath(
        Vector2Int startPoint,
        Vector2Int endPoint,
        BlockType blockType,
        int pathWidth,
        float horizontalMoveChance,
        bool writeWeight)
    {
        Vector2Int start = new Vector2Int(ClampGridX(startPoint.x), ClampGridZ(startPoint.y));
        Vector2Int end = new Vector2Int(ClampGridX(endPoint.x), ClampGridZ(endPoint.y));
        List<Vector2Int> path = FindPathAStar(start, end, blockType, horizontalMoveChance);
        if ((path == null || path.Count == 0) && useFallbackPathWhenAstarFails)
            path = BuildFallbackPath(start, end, horizontalMoveChance);

        if (path == null || path.Count == 0)
        {
            Debug.LogWarning($"MapGenerator: не удалось построить путь {blockType} от {start} до {end}.");
            return;
        }

        int randomizedPathWidth = ResolvePathWidth(pathWidth, start, end, blockType);
        int weight = 1;
        for (int i = 0; i < path.Count; i++)
        {
            Vector2Int cell = path[i];
            int localWidth = randomizedPathWidth;
            int? paintedWeight = writeWeight ? (int?)weight : null;
            PaintPathBrush(cell.x, cell.y, localWidth, blockType, paintedWeight);

            if (writeWeight)
                weight++;
        }
    }

    int ResolvePathWidth(int baseWidth, Vector2Int start, Vector2Int end, BlockType blockType)
    {
        if (roadWidthRandomDelta <= 0)
            return Mathf.Max(0, baseWidth);

        int typeSeed = blockType == BlockType.Main ? 137 : 263;
        float widthNoise = GetDeterministicJitter(
            start.x + end.x * 17 + typeSeed,
            start.y + end.y * 31 + typeSeed * 3);
        int offset = Mathf.RoundToInt((widthNoise * 2f - 1f) * roadWidthRandomDelta);
        return Mathf.Max(0, baseWidth + offset);
    }

    List<Vector2Int> FindPathAStar(Vector2Int start, Vector2Int end, BlockType blockType, float horizontalPreference)
    {
        if (!CanTraverseForPath(start.x, start.y) || !CanTraverseForPath(end.x, end.y))
            return null;

        float[,] gScore = new float[width, height];
        float[,] fScore = new float[width, height];
        bool[,] closed = new bool[width, height];
        bool[,] inOpen = new bool[width, height];
        bool[,] hasParent = new bool[width, height];
        Vector2Int[,] parent = new Vector2Int[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                gScore[x, z] = float.PositiveInfinity;
                fScore[x, z] = float.PositiveInfinity;
            }
        }

        List<Vector2Int> open = new() { start };
        inOpen[start.x, start.y] = true;
        gScore[start.x, start.y] = 0f;
        fScore[start.x, start.y] = Heuristic(start, end);

        while (open.Count > 0)
        {
            int bestIndex = FindBestOpenIndex(open, fScore, end, horizontalPreference);
            Vector2Int current = open[bestIndex];
            open.RemoveAt(bestIndex);
            inOpen[current.x, current.y] = false;

            if (current == end)
                return ReconstructPath(parent, hasParent, start, end);

            closed[current.x, current.y] = true;
            int[] dx = astarAllowDiagonalMoves
                ? new[] { 0, 1, 0, -1, 1, 1, -1, -1 }
                : new[] { 0, 1, 0, -1 };
            int[] dz = astarAllowDiagonalMoves
                ? new[] { 1, 0, -1, 0, 1, -1, 1, -1 }
                : new[] { 1, 0, -1, 0 };

            for (int i = 0; i < dx.Length; i++)
            {
                int nx = current.x + dx[i];
                int nz = current.y + dz[i];
                if (!CanTraverseForPath(nx, nz) || closed[nx, nz])
                    continue;

                if (dx[i] != 0 && dz[i] != 0 && !CanTraverseDiagonal(current.x, current.y, nx, nz))
                    continue;

                Vector2Int neighbor = new Vector2Int(nx, nz);
                float tentativeG = gScore[current.x, current.y] +
                    GetMoveCost(current, neighbor, blockType, parent, hasParent);
                if (tentativeG >= gScore[nx, nz])
                    continue;

                parent[nx, nz] = current;
                hasParent[nx, nz] = true;
                gScore[nx, nz] = tentativeG;
                fScore[nx, nz] = tentativeG + Heuristic(neighbor, end);
                if (!inOpen[nx, nz])
                {
                    open.Add(neighbor);
                    inOpen[nx, nz] = true;
                }
            }
        }

        return null;
    }

    int FindBestOpenIndex(List<Vector2Int> open, float[,] fScore, Vector2Int end, float horizontalPreference)
    {
        int bestIndex = 0;
        float bestValue = float.PositiveInfinity;
        for (int i = 0; i < open.Count; i++)
        {
            Vector2Int node = open[i];
            float tieBreaker = GetHorizontalTieBreaker(node, end, horizontalPreference);
            float score = fScore[node.x, node.y] + tieBreaker;
            if (score >= bestValue)
                continue;

            bestValue = score;
            bestIndex = i;
        }

        return bestIndex;
    }

    float GetHorizontalTieBreaker(Vector2Int node, Vector2Int end, float horizontalPreference)
    {
        float normalizedPref = Mathf.Clamp01(horizontalPreference);
        int xDelta = Mathf.Abs(node.x - end.x);
        int zDelta = Mathf.Abs(node.y - end.y);
        if (xDelta == zDelta)
            return 0f;

        bool shouldBiasHorizontal = xDelta > zDelta;
        return shouldBiasHorizontal
            ? (1f - normalizedPref) * 0.05f
            : normalizedPref * 0.05f;
    }

    bool CanTraverseDiagonal(int fromX, int fromZ, int toX, int toZ)
    {
        int stepX = toX > fromX ? 1 : -1;
        int stepZ = toZ > fromZ ? 1 : -1;
        return CanTraverseForPath(fromX + stepX, fromZ) &&
               CanTraverseForPath(fromX, fromZ + stepZ);
    }

    float GetMoveCost(
        Vector2Int current,
        Vector2Int next,
        BlockType blockType,
        Vector2Int[,] parent,
        bool[,] hasParent)
    {
        bool isDiagonal = current.x != next.x && current.y != next.y;
        float cost = isDiagonal ? 1.4142135f : 1f;
        if (isDiagonal)
            cost += astarDiagonalPenalty;

        if (IsBorder(next.x, next.y))
            cost += astarBorderPenalty;

        BlockType cellType = mapGrid[next.x, next.y].blockType.Current;
        if (cellType == BlockType.Main || cellType == BlockType.Link)
        {
            float reusePenalty = astarRoadReusePenalty;
            if (blockType == BlockType.Link && cellType == BlockType.Main && linkCanMergeIntoMain)
                reusePenalty *= 0.2f;
            cost += reusePenalty;
        }

        if (blockType == BlockType.Link && cellType == BlockType.Main)
            cost += linkCanMergeIntoMain ? astarLinkMergeMainPenalty : astarLinkAvoidMainPenalty;
        else if (blockType == BlockType.Main && cellType == BlockType.Link)
            cost += astarMainAvoidLinkPenalty;

        if (blockType == BlockType.Link)
        {
            float halfWidth = Mathf.Max(1f, (width - 1) * 0.5f);
            float centerInfluence = 1f - Mathf.Abs(next.x - halfWidth) / halfWidth;
            cost += centerInfluence * astarLinkCenterPenalty;
        }

        if (hasParent[current.x, current.y])
        {
            Vector2Int prev = parent[current.x, current.y];
            Vector2Int dirA = current - prev;
            Vector2Int dirB = next - current;
            if (dirA != dirB)
                cost += astarTurnPenalty;
        }

        cost += astarRandomJitter * GetDeterministicJitter(next.x, next.y);
        return Mathf.Max(0.01f, cost);
    }

    float GetDeterministicJitter(int x, int z)
    {
        int seedComponent = useFixedSeed ? generationSeed : 7919;
        int hash = x * 73856093 ^ z * 19349663 ^ seedComponent * 83492791;
        uint value = (uint)hash;
        return (value & 1023) / 1023f;
    }

    List<Vector2Int> ReconstructPath(
        Vector2Int[,] parent,
        bool[,] hasParent,
        Vector2Int start,
        Vector2Int end)
    {
        List<Vector2Int> path = new() { end };
        Vector2Int current = end;
        while (current != start)
        {
            if (!hasParent[current.x, current.y])
                return null;

            current = parent[current.x, current.y];
            path.Add(current);
        }

        path.Reverse();
        return path;
    }

    List<Vector2Int> BuildFallbackPath(Vector2Int start, Vector2Int end, float horizontalMoveChance)
    {
        int x = start.x;
        int z = start.y;
        int endX = end.x;
        int endZ = end.y;
        int maxIterations = width * height * 4;
        int iteration = 0;
        List<Vector2Int> path = new() { new Vector2Int(x, z) };

        while ((x != endX || z != endZ) && iteration < maxIterations)
        {
            StepTowardsTarget(ref x, ref z, endX, endZ, horizontalMoveChance);
            path.Add(new Vector2Int(x, z));
            iteration++;
        }

        return path;
    }

    bool CanTraverseForPath(int x, int z)
    {
        return IsInsideMap(x, z) && mapGrid[x, z].blockType.Current != BlockType.Wall;
    }

    float Heuristic(Vector2Int from, Vector2Int to)
    {
        int dx = Mathf.Abs(from.x - to.x);
        int dz = Mathf.Abs(from.y - to.y);
        if (!astarAllowDiagonalMoves)
            return dx + dz;

        int diagonal = Mathf.Min(dx, dz);
        int straight = Mathf.Abs(dx - dz);
        return diagonal * 1.4142135f + straight;
    }

    void PaintPathBrush(int centerX, int centerZ, int pathWidth, BlockType blockType, int? weight)
    {
        for (int dx = -pathWidth; dx <= pathWidth; dx++)
        {
            for (int dz = -pathWidth; dz <= pathWidth; dz++)
            {
                if (useCircularPathBrush && dx * dx + dz * dz > pathWidth * pathWidth)
                    continue;

                TryMarkBlock(centerX + dx, centerZ + dz, blockType, weight);
            }
        }
    }

    void StepTowardsTarget(ref int x, ref int z, int targetX, int targetZ, float horizontalChance)
    {
        bool moveXFirst = Random.value < horizontalChance;
        if (moveXFirst)
        {
            if (MoveAxis(ref x, targetX))
                return;
            MoveAxis(ref z, targetZ);
            return;
        }

        if (MoveAxis(ref z, targetZ))
            return;
        MoveAxis(ref x, targetX);
    }

    bool MoveAxis(ref int current, int target)
    {
        if (current == target)
            return false;

        current += current < target ? 1 : -1;
        return true;
    }
}
