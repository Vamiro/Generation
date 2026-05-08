using System.Collections.Generic;
using UnityEngine;

public partial class MapGenerator
{
    void ValidateGeneratedLayout()
    {
        if (!zoneBlocks.TryGetValue(BlockType.Main, out HashSet<BlockComponent> mainBlocks) || mainBlocks.Count == 0)
            Debug.LogWarning("MapGenerator: после генерации отсутствуют Main пути.");

        if (!zoneBlocks.TryGetValue(BlockType.Link, out HashSet<BlockComponent> linkBlocks) || linkBlocks.Count == 0)
            Debug.LogWarning("MapGenerator: после генерации отсутствуют Link пути.");

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (mapGrid[x, z].blockType.Current == BlockType.Site && IsBorder(x, z))
                {
                    Debug.LogWarning($"MapGenerator: сайт попал на границу карты ({x}, {z}).");
                    break;
                }
            }
        }

        Vector2Int[] spawns = { spawnA, spawnB };
        Vector2Int[] sites = { siteA, siteB };
        foreach (Vector2Int spawn in spawns)
        {
            foreach (Vector2Int site in sites)
            {
                if (IsReachable(spawn, site))
                    continue;

                Debug.LogWarning($"MapGenerator: нет пути от спавна {spawn} к сайту {site}.");
            }
        }

        ValidateSiteAccessAndFairness(siteA, "A");
        ValidateSiteAccessAndFairness(siteB, "B");
    }

    void ValidateSiteAccessAndFairness(Vector2Int siteOrigin, string siteLabel)
    {
        Vector2Int siteCenter = GetSiteCenter(siteOrigin);
        int entryCount = CountSiteEntries(siteOrigin);
        if (entryCount < siteEntryMin)
            Debug.LogWarning($"MapGenerator: у сайта {siteLabel} недостаточно входов ({entryCount}/{siteEntryMin}).");

        int distanceA = GetShortestPathDistance(spawnA, siteCenter);
        int distanceB = GetShortestPathDistance(spawnB, siteCenter);
        if (distanceA < 0 || distanceB < 0)
            return;

        if (Mathf.Abs(distanceA - distanceB) > siteFairnessTolerance)
        {
            Debug.LogWarning(
                $"MapGenerator: fairness по сайту {siteLabel} нарушен (A={distanceA}, B={distanceB}, tol={siteFairnessTolerance}).");
        }
    }

    int CountSiteEntries(Vector2Int siteOrigin)
    {
        int entries = 0;
        for (int x = siteOrigin.x; x < siteOrigin.x + siteZoneWidth; x++)
        {
            for (int z = siteOrigin.y; z < siteOrigin.y + siteZoneHeight; z++)
            {
                if (!IsInsideMap(x, z))
                    continue;

                entries += CountExternalWalkableNeighbors(x, z, BlockType.Site);
            }
        }

        return entries;
    }

    int CountExternalWalkableNeighbors(int x, int z, BlockType ownType)
    {
        int[] dx = { 0, 1, 0, -1 };
        int[] dz = { 1, 0, -1, 0 };
        int count = 0;
        for (int i = 0; i < 4; i++)
        {
            int nx = x + dx[i];
            int nz = z + dz[i];
            if (!IsInsideMap(nx, nz) || !IsWalkableCell(nx, nz))
                continue;

            if (mapGrid[nx, nz].blockType.Current != ownType)
                count++;
        }

        return count;
    }

    bool IsReachable(Vector2Int start, Vector2Int end)
    {
        int startX = ClampGridX(start.x);
        int startZ = ClampGridZ(start.y);
        int endX = ClampGridX(end.x);
        int endZ = ClampGridZ(end.y);

        if (!IsWalkableCell(startX, startZ) || !IsWalkableCell(endX, endZ))
            return false;

        bool[,] visited = new bool[width, height];
        Queue<Vector2Int> queue = new();
        queue.Enqueue(new Vector2Int(startX, startZ));
        visited[startX, startZ] = true;

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            if (current.x == endX && current.y == endZ)
                return true;

            TryEnqueueReachable(current.x + 1, current.y, visited, queue);
            TryEnqueueReachable(current.x - 1, current.y, visited, queue);
            TryEnqueueReachable(current.x, current.y + 1, visited, queue);
            TryEnqueueReachable(current.x, current.y - 1, visited, queue);
        }

        return false;
    }

    int GetShortestPathDistance(Vector2Int start, Vector2Int end)
    {
        int startX = ClampGridX(start.x);
        int startZ = ClampGridZ(start.y);
        int endX = ClampGridX(end.x);
        int endZ = ClampGridZ(end.y);

        if (!IsWalkableCell(startX, startZ) || !IsWalkableCell(endX, endZ))
            return -1;

        bool[,] visited = new bool[width, height];
        Queue<Vector2Int> queue = new();
        Queue<int> distanceQueue = new();
        queue.Enqueue(new Vector2Int(startX, startZ));
        distanceQueue.Enqueue(0);
        visited[startX, startZ] = true;

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            int distance = distanceQueue.Dequeue();
            if (current.x == endX && current.y == endZ)
                return distance;

            EnqueueDistanceNeighbor(current.x + 1, current.y, distance + 1, visited, queue, distanceQueue);
            EnqueueDistanceNeighbor(current.x - 1, current.y, distance + 1, visited, queue, distanceQueue);
            EnqueueDistanceNeighbor(current.x, current.y + 1, distance + 1, visited, queue, distanceQueue);
            EnqueueDistanceNeighbor(current.x, current.y - 1, distance + 1, visited, queue, distanceQueue);
        }

        return -1;
    }

    void EnqueueDistanceNeighbor(
        int x,
        int z,
        int distance,
        bool[,] visited,
        Queue<Vector2Int> queue,
        Queue<int> distanceQueue)
    {
        if (!IsInsideMap(x, z) || visited[x, z] || !IsWalkableCell(x, z))
            return;

        visited[x, z] = true;
        queue.Enqueue(new Vector2Int(x, z));
        distanceQueue.Enqueue(distance);
    }

    void TryEnqueueReachable(int x, int z, bool[,] visited, Queue<Vector2Int> queue)
    {
        if (!IsInsideMap(x, z) || visited[x, z] || !IsWalkableCell(x, z))
            return;

        visited[x, z] = true;
        queue.Enqueue(new Vector2Int(x, z));
    }

    bool IsWalkableCell(int x, int z)
    {
        return IsInsideMap(x, z) && mapGrid[x, z].blockType.Current != BlockType.Wall;
    }
}
