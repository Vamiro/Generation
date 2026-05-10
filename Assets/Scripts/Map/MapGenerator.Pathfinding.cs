using System;
using System.Collections.Generic;
using UnityEngine;

public partial class MapGenerator
{
    readonly struct PathProfile
    {
        public PathProfile(int width, float horizontalBias, bool writeWeight)
        {
            Width = width;
            HorizontalBias = horizontalBias;
            WriteWeight = writeWeight;
        }

        public int Width { get; }
        public float HorizontalBias { get; }
        public bool WriteWeight { get; }
    }

    List<Vector2Int> CreateConfiguredPath(
        Vector2Int startPoint,
        Vector2Int endPoint,
        BlockType blockType,
        Func<Vector2Int, bool> paintSkipPredicate = null)
    {
        PathProfile profile = GetPathProfile(blockType);
        return CreatePath(
            startPoint,
            endPoint,
            blockType,
            profile.Width,
            profile.HorizontalBias,
            profile.WriteWeight,
            paintSkipPredicate);
    }

    PathProfile GetPathProfile(BlockType blockType)
    {
        return blockType switch
        {
            BlockType.Main => new PathProfile(mainWidth, astarMainHorizontalBias, true),
            BlockType.Link => new PathProfile(linkWidth, astarLinkHorizontalBias, false),
            _ => new PathProfile(1, 0.5f, false)
        };
    }

    List<Vector2Int> CreatePath(
        Vector2Int startPoint,
        Vector2Int endPoint,
        BlockType blockType,
        int pathWidth,
        float horizontalMoveChance,
        bool writeWeight,
        Func<Vector2Int, bool> paintSkipPredicate)
    {
        Vector2Int start = new Vector2Int(ClampGridX(startPoint.x), ClampGridZ(startPoint.y));
        Vector2Int end = new Vector2Int(ClampGridX(endPoint.x), ClampGridZ(endPoint.y));
        List<Vector2Int> path = FindPathAStar(start, end, blockType, horizontalMoveChance);

        if (path == null || path.Count == 0)
        {
            Debug.LogWarning($"MapGenerator: не удалось построить путь {blockType} от {start} до {end}.");
            return null;
        }

        int weight = 1;
        bool exitedSkipZone = paintSkipPredicate == null;
        for (int i = 0; i < path.Count; i++)
        {
            Vector2Int cell = path[i];
            if (!exitedSkipZone)
            {
                if (paintSkipPredicate(cell))
                    continue;

                exitedSkipZone = true;
            }

            int? paintedWeight = writeWeight ? (int?)weight : null;
            PaintPathBrush(cell.x, cell.y, pathWidth, blockType, paintedWeight);

            if (writeWeight)
                weight++;
        }

        return path;
    }

    List<Vector2Int> FindPathAStar(
        Vector2Int start,
        Vector2Int end,
        BlockType blockType,
        float horizontalPreference)
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

        int[] dx = { 0, 1, 0, -1 };
        int[] dz = { 1, 0, -1, 0 };

        while (open.Count > 0)
        {
            int bestIndex = FindBestOpenIndex(open, fScore, end, horizontalPreference);
            Vector2Int current = open[bestIndex];
            open.RemoveAt(bestIndex);
            inOpen[current.x, current.y] = false;

            if (current == end)
                return ReconstructPath(parent, hasParent, start, end);

            closed[current.x, current.y] = true;

            for (int i = 0; i < dx.Length; i++)
            {
                int nx = current.x + dx[i];
                int nz = current.y + dz[i];
                if (!CanTraverseForPath(nx, nz) || closed[nx, nz])
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

    float GetMoveCost(
        Vector2Int current,
        Vector2Int next,
        BlockType blockType,
        Vector2Int[,] parent,
        bool[,] hasParent)
    {
        float cost = 1f;

        if (IsBorder(next.x, next.y))
            cost += astarBorderPenalty;

        BlockType cellType = mapGrid[next.x, next.y].blockType.Current;
        if (cellType == BlockType.Main || cellType == BlockType.Link)
            cost += astarRoadReusePenalty;

        bool isCrossTypeRoadStep =
            (blockType == BlockType.Link && cellType == BlockType.Main) ||
            (blockType == BlockType.Main && cellType == BlockType.Link);
        if (isCrossTypeRoadStep)
            cost += astarCrossTypePenalty;

        if (blockType == BlockType.Link)
        {
            float halfWidth = Mathf.Max(1f, (width - 1) * 0.5f);
            float centerInfluence = 1f - Mathf.Abs(next.x - halfWidth) / halfWidth;
            cost += centerInfluence * astarLinkCenterPenalty;
        }
        else if (blockType == BlockType.Main)
        {
            float interiorPenalty = 1f - GetBorderCloseness(next.x, next.y);
            cost += interiorPenalty * astarMainOuterBias;
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

    float GetBorderCloseness(int x, int z)
    {
        int nearestBorderDistance = Mathf.Min(
            Mathf.Min(x, width - 1 - x),
            Mathf.Min(z, height - 1 - z));
        float maxDistance = Mathf.Max(1f, Mathf.Min(width, height) * 0.5f);
        return 1f - Mathf.Clamp01(nearestBorderDistance / maxDistance);
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

    bool CanTraverseForPath(int x, int z)
    {
        return IsInsideMap(x, z) && mapGrid[x, z].blockType.Current != BlockType.Wall;
    }

    float Heuristic(Vector2Int from, Vector2Int to)
    {
        int dx = Mathf.Abs(from.x - to.x);
        int dz = Mathf.Abs(from.y - to.y);
        return dx + dz;
    }

    void PaintPathBrush(int centerX, int centerZ, int pathWidth, BlockType blockType, int? weight)
    {
        for (int dx = -pathWidth; dx <= pathWidth; dx++)
        {
            for (int dz = -pathWidth; dz <= pathWidth; dz++)
            {
                if (useCircularPathBrush && dx * dx + dz * dz > pathWidth * pathWidth)
                    continue;

                int targetX = centerX + dx;
                int targetZ = centerZ + dz;
                if (!IsInsideMap(targetX, targetZ))
                    continue;

                if (blockType == BlockType.Link && mapGrid[targetX, targetZ].blockType.Current == BlockType.Main)
                    continue;

                TryMarkBlock(targetX, targetZ, blockType, weight);
            }
        }
    }
}
