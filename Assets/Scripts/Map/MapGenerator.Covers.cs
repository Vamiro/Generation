using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

public partial class MapGenerator
{
    bool IsEdgeBlock(int gridX, int gridZ, BlockType zoneType)
    {
        int[] dx = { 0, 1, 0, -1 };
        int[] dz = { 1, 0, -1, 0 };
        for (int i = 0; i < 4; i++)
        {
            int nx = gridX + dx[i];
            int nz = gridZ + dz[i];
            if (IsInsideMap(nx, nz) && cellTypes[nx, nz] != zoneType)
                return true;
        }

        return false;
    }

    bool IsJunctionBlock(int gridX, int gridZ, BlockType zoneType)
    {
        int diffCount = 0;
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                if (dx == 0 && dz == 0)
                    continue;

                int nx = gridX + dx;
                int nz = gridZ + dz;
                if (IsInsideMap(nx, nz) && cellTypes[nx, nz] != zoneType)
                    diffCount++;
            }
        }

        return diffCount >= 2;
    }

    void PlaceCovers()
    {
        if (coverPrefab == null)
            return;

        foreach (BlockType zone in CoverZones)
        {
            if (!zoneBlocks.TryGetValue(zone, out HashSet<BlockComponent> blocks))
                continue;

            foreach (BlockComponent block in blocks)
            {
                int gridX = Mathf.RoundToInt(block.transform.position.x / blockSize);
                int gridZ = Mathf.RoundToInt(block.transform.position.z / blockSize);
                float density = CalculateLocalDensity(gridX, gridZ, zone);

                if (!TryGetCoverProbability(zone, gridX, gridZ, density, out float baseProbability))
                    continue;

                float probability = Mathf.Clamp01(baseProbability * coverSpawnMultiplier);
                if (Random.value < probability)
                {
                    Vector3 coverPos = block.transform.position + new Vector3(0f, blockSize, 0f);
                    Instantiate(coverPrefab, coverPos, Quaternion.identity, transform);
                }
            }
        }
    }

    bool TryGetCoverProbability(BlockType zone, int gridX, int gridZ, float density, out float probability)
    {
        probability = 0f;
        if (zone == BlockType.Main || zone == BlockType.Link)
        {
            if (!IsEdgeBlock(gridX, gridZ, zone) && !IsJunctionBlock(gridX, gridZ, zone))
                return false;

            float baseRoadProbability = zone == BlockType.Main
                ? Mathf.Lerp(coverMaxProbabilityMain, coverMinProbabilityMain, density)
                : Mathf.Lerp(coverMaxProbabilityLink, coverMinProbabilityLink, density);
            int localWidth = EstimateCorridorWidth(gridX, gridZ, zone);
            float widthMultiplier = localWidth <= narrowCorridorWidthThreshold
                ? narrowCorridorCoverMultiplier
                : 1f;
            probability = Mathf.Clamp01(baseRoadProbability * widthMultiplier);
            return true;
        }

        switch (zone)
        {
            case BlockType.Spawn:
                probability = Mathf.Lerp(coverMaxProbabilitySpawn, coverMinProbabilitySpawn, density) *
                              GetOpenAreaModifier(density);
                return true;
            case BlockType.Site:
                probability = Mathf.Lerp(coverMaxProbabilitySite, coverMinProbabilitySite, density) *
                              GetOpenAreaModifier(density);
                return true;
            case BlockType.Neutral:
                probability = Mathf.Lerp(coverMaxProbabilityNeutral, coverMinProbabilityNeutral, density) *
                              GetOpenAreaModifier(density);
                return true;
            default:
                return false;
        }
    }

    float GetOpenAreaModifier(float density)
    {
        float openness = 1f - Mathf.Clamp01(density);
        return Mathf.Lerp(1f, openAreaCoverMultiplier, openness);
    }

    int EstimateCorridorWidth(int gridX, int gridZ, BlockType zoneType)
    {
        int horizontalSpan = 1;
        for (int offset = 1; offset <= 3; offset++)
        {
            if (IsInsideMap(gridX + offset, gridZ) && cellTypes[gridX + offset, gridZ] == zoneType)
                horizontalSpan++;
            else
                break;
        }

        for (int offset = 1; offset <= 3; offset++)
        {
            if (IsInsideMap(gridX - offset, gridZ) && cellTypes[gridX - offset, gridZ] == zoneType)
                horizontalSpan++;
            else
                break;
        }

        int verticalSpan = 1;
        for (int offset = 1; offset <= 3; offset++)
        {
            if (IsInsideMap(gridX, gridZ + offset) && cellTypes[gridX, gridZ + offset] == zoneType)
                verticalSpan++;
            else
                break;
        }

        for (int offset = 1; offset <= 3; offset++)
        {
            if (IsInsideMap(gridX, gridZ - offset) && cellTypes[gridX, gridZ - offset] == zoneType)
                verticalSpan++;
            else
                break;
        }

        return Mathf.Min(horizontalSpan, verticalSpan);
    }

    float CalculateLocalDensity(int gridX, int gridZ, BlockType zoneType)
    {
        int count = 0;
        int total = 0;
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                int nx = gridX + dx;
                int nz = gridZ + dz;
                if (!IsInsideMap(nx, nz))
                    continue;

                total++;
                if (cellTypes[nx, nz] == zoneType)
                    count++;
            }
        }

        return total == 0 ? 0f : (float)count / total;
    }
}
