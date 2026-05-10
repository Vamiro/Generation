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

        Vector2Int[] spawns = { attackerSpawn, defenderSpawn };
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

        if (generateNeutralZone && IsWalkableCell(neutralCenter.x, neutralCenter.y))
        {
            foreach (Vector2Int spawn in spawns)
            {
                if (!IsReachable(spawn, neutralCenter))
                    Debug.LogWarning($"MapGenerator: нет пути от спавна {spawn} к нейтральной зоне {neutralCenter}.");
            }

            Vector2Int[] siteCenters = { GetSiteCenter(siteA, siteASize), GetSiteCenter(siteB, siteBSize) };
            foreach (Vector2Int siteCenter in siteCenters)
            {
                if (!IsReachable(neutralCenter, siteCenter))
                    Debug.LogWarning($"MapGenerator: нет пути от нейтральной зоны {neutralCenter} к сайту {siteCenter}.");
            }
        }
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
