using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

public partial class MapGenerator
{
    void PlaceSpawnZones()
    {
        int spawnSizeAttacker = Mathf.Clamp(Random.Range(spawnZoneSizeMin, spawnZoneSizeMax + 1), 1, Mathf.Min(width, height));
        int spawnSizeDefender = Mathf.Clamp(Random.Range(spawnZoneSizeMin, spawnZoneSizeMax + 1), 1, Mathf.Min(width, height));

        int attackerHalf = spawnSizeAttacker / 2;
        int defenderHalf = spawnSizeDefender / 2;

        int attackerX = ResolveSpawnCenterX(spawnSizeAttacker);
        int defenderX = ResolveSpawnCenterX(spawnSizeDefender);

        int attackerZ = innerPadding + attackerHalf;
        int defenderZ = height - 1 - innerPadding - (spawnSizeDefender - defenderHalf - 1);

        attackerSpawn = new Vector2Int(attackerX, attackerZ);
        defenderSpawn = new Vector2Int(defenderX, defenderZ);

        MarkRectAround(attackerSpawn, attackerHalf, spawnSizeAttacker - attackerHalf, BlockType.Spawn);
        MarkRectAround(defenderSpawn, defenderHalf, spawnSizeDefender - defenderHalf, BlockType.Spawn);
    }

    int ResolveSpawnCenterX(int spawnSize)
    {
        int half = spawnSize / 2;
        int xMin = innerPadding + half;
        int xMax = width - 1 - innerPadding - (spawnSize - half - 1);
        if (xMax < xMin) xMax = xMin;

        int center = (width - 1) / 2;
        int jitter = Mathf.Max(0, spawnHorizontalOffset);
        int targetMin = Mathf.Max(xMin, center - jitter);
        int targetMax = Mathf.Min(xMax, center + jitter);
        if (targetMax < targetMin)
        {
            targetMin = xMin;
            targetMax = xMax;
        }

        return Random.Range(targetMin, targetMax + 1);
    }

    void PlaceSiteZones()
    {
        siteASize = Mathf.Clamp(Random.Range(siteZoneSizeMin, siteZoneSizeMax + 1), 1, Mathf.Min(width, height));
        siteBSize = Mathf.Clamp(Random.Range(siteZoneSizeMin, siteZoneSizeMax + 1), 1, Mathf.Min(width, height));

        int siteLineZ = ResolveSiteLineZ();

        siteA = ResolveSiteOrigin(leftHalf: true, siteASize, siteLineZ);
        siteB = ResolveSiteOrigin(leftHalf: false, siteBSize, siteLineZ);

        ClearZone(siteA.x, siteA.y, siteASize, siteASize, BlockType.Site);
        ClearZone(siteB.x, siteB.y, siteBSize, siteBSize, BlockType.Site);
    }

    int ResolveSiteLineZ()
    {
        float midZ = (attackerSpawn.y + defenderSpawn.y) * 0.5f;
        float bias = Mathf.Clamp01(siteLineBiasToDefender);
        float biasedZ = Mathf.Lerp(midZ, defenderSpawn.y, bias);
        return Mathf.RoundToInt(biasedZ);
    }

    Vector2Int ResolveSiteOrigin(bool leftHalf, int siteSize, int lineZ)
    {
        int globalXMin = innerPadding;
        int globalXMax = width - innerPadding - siteSize;
        int mapCenter = (width - 1) / 2;
        int minDistanceFromCenter = Mathf.Max(1, siteMinCenterDistanceMultiplier) * siteSize;
        int drift = Mathf.Max(0, siteCenterDriftMax);

        int xMin;
        int xMax;
        if (leftHalf)
        {
            int allowedMaxByDistance = mapCenter - minDistanceFromCenter - siteSize;
            xMin = globalXMin;
            xMax = Mathf.Min(globalXMin + drift, allowedMaxByDistance);
            if (xMax < xMin)
                xMax = Mathf.Min(globalXMax, Mathf.Max(globalXMin, allowedMaxByDistance));
        }
        else
        {
            int allowedMinByDistance = mapCenter + minDistanceFromCenter;
            xMin = Mathf.Max(globalXMax - drift, allowedMinByDistance);
            xMax = globalXMax;
            if (xMax < xMin)
                xMin = Mathf.Max(globalXMin, Mathf.Min(globalXMax, allowedMinByDistance));
        }

        if (xMax < xMin)
        {
            xMin = leftHalf ? globalXMin : Mathf.Max(globalXMin, globalXMax - drift);
            xMax = leftHalf ? Mathf.Min(globalXMax, globalXMin + drift) : globalXMax;
        }

        if (xMax < xMin) xMax = xMin;

        int originX = Mathf.Clamp(Random.Range(xMin, xMax + 1), globalXMin, globalXMax);

        int verticalJitter = Mathf.Max(0, siteVerticalJitter);
        int originZTarget = lineZ - siteSize / 2 + Random.Range(-verticalJitter, verticalJitter + 1);
        int globalZMin = innerPadding;
        int globalZMax = height - innerPadding - siteSize;
        int originZ = Mathf.Clamp(originZTarget, globalZMin, globalZMax);

        return new Vector2Int(originX, originZ);
    }

    void MarkNeutralZone()
    {
        if (!generateNeutralZone)
            return;

        int sizeX = Mathf.Max(1, neutralZoneSize.x);
        int sizeZ = Mathf.Max(1, neutralZoneSize.y);
        int halfX = sizeX / 2;
        int halfZ = sizeZ / 2;

        int xClampMin = innerPadding + halfX;
        int xClampMax = width - 1 - innerPadding - (sizeX - halfX - 1);
        int zClampMin = innerPadding + halfZ;
        int zClampMax = height - 1 - innerPadding - (sizeZ - halfZ - 1);

        int mapCenterX = (width - 1) / 2;
        int mapCenterZ = (height - 1) / 2;

        int sitesLineZ = (siteA.y + siteASize / 2 + siteB.y + siteBSize / 2) / 2;
        float bias = Mathf.Clamp01(neutralZoneBiasToSites);
        int targetZ = Mathf.RoundToInt(Mathf.Lerp(mapCenterZ, sitesLineZ, bias));

        int xJitter = Mathf.Max(0, neutralZoneHorizontalJitter);
        int zJitter = Mathf.Max(0, neutralZoneVerticalJitter);

        int centerX = mapCenterX + Random.Range(-xJitter, xJitter + 1);
        int centerZ = targetZ + Random.Range(-zJitter, zJitter + 1);

        if (xClampMax < xClampMin) xClampMax = xClampMin;
        if (zClampMax < zClampMin) zClampMax = zClampMin;

        centerX = Mathf.Clamp(centerX, xClampMin, xClampMax);
        centerZ = Mathf.Clamp(centerZ, zClampMin, zClampMax);

        neutralCenter = new Vector2Int(centerX, centerZ);
        ClearZone(centerX - halfX, centerZ - halfZ, sizeX, sizeZ, BlockType.Neutral);
    }

    void BuildStructuredRoutes()
    {
        BuildMainRoutes();
        BuildAttackerLinks();
        BuildDefenderLink();
        BuildNeutralToSiteLinks();
    }

    void BuildMainRoutes()
    {
        Vector2Int[] spawns = { attackerSpawn, defenderSpawn };
        Vector2Int[] sites = { siteA, siteB };
        int[] siteSizes = { siteASize, siteBSize };
        for (int i = 0; i < spawns.Length; i++)
        {
            for (int j = 0; j < sites.Length; j++)
            {
                Vector2Int endpoint = GetClosestEdgePoint(sites[j], siteSizes[j], siteSizes[j], spawns[i]);
                CreateConfiguredPath(spawns[i], endpoint, BlockType.Main);
            }
        }
    }

    void BuildAttackerLinks()
    {
        int mapCenterX = (width - 1) / 2;
        int baseOffset = Mathf.Max(1, attackerLinkOffsetFromCenter);
        int jitter = Mathf.Max(0, attackerLinkOffsetJitter);

        int leftOffset = baseOffset + Random.Range(-jitter, jitter + 1);
        int rightOffset = baseOffset + Random.Range(-jitter, jitter + 1);

        int leftX = ClampGridX(mapCenterX - leftOffset);
        int rightX = ClampGridX(mapCenterX + rightOffset);

        Vector2Int leftStart = new Vector2Int(leftX, ClampGridZ(attackerSpawn.y));
        Vector2Int rightStart = new Vector2Int(rightX, ClampGridZ(attackerSpawn.y));

        CreateLinkConnectionFromSpawnLine(leftStart, neutralCenter);
        CreateLinkConnectionFromSpawnLine(rightStart, neutralCenter);
    }

    void BuildDefenderLink()
    {
        int mapCenterX = (width - 1) / 2;
        int jitter = Mathf.Max(0, defenderLinkOffsetFromCenter);
        int x = ClampGridX(mapCenterX + Random.Range(-jitter, jitter + 1));

        Vector2Int start = new Vector2Int(x, ClampGridZ(defenderSpawn.y));
        CreateLinkConnectionFromSpawnLine(start, neutralCenter);
    }

    void BuildNeutralToSiteLinks()
    {
        Vector2Int[] sites = { siteA, siteB };
        int[] siteSizes = { siteASize, siteBSize };
        for (int i = 0; i < sites.Length; i++)
        {
            Vector2Int siteEntry = GetClosestEdgePoint(sites[i], siteSizes[i], siteSizes[i], neutralCenter);
            CreateLinkConnection(neutralCenter, siteEntry);
        }
    }

    List<Vector2Int> CreateLinkConnection(Vector2Int start, Vector2Int end)
    {
        return CreateConfiguredPath(start, end, BlockType.Link);
    }

    List<Vector2Int> CreateLinkConnectionFromSpawnLine(Vector2Int start, Vector2Int end)
    {
        return CreateConfiguredPath(start, end, BlockType.Link, paintSkipPredicate: IsSpawnOrMainCell);
    }

    bool IsSpawnOrMainCell(Vector2Int cell)
    {
        if (!IsInsideMap(cell.x, cell.y))
            return false;

        BlockType type = mapGrid[cell.x, cell.y].blockType.Current;
        return type == BlockType.Spawn || type == BlockType.Main;
    }

    void ClearZone(int startX, int startZ, int sizeX, int sizeZ, BlockType type)
    {
        for (int x = startX; x < startX + sizeX; x++)
        {
            for (int z = startZ; z < startZ + sizeZ; z++)
                TryMarkBlock(x, z, type);
        }
    }

    void MarkRectAround(Vector2Int center, int halfBefore, int halfAfter, BlockType type)
    {
        int sizeX = Mathf.Max(1, halfBefore + halfAfter);
        int sizeZ = sizeX;
        int startX = center.x - halfBefore;
        int startZ = center.y - halfBefore;
        ClearZone(startX, startZ, sizeX, sizeZ, type);
    }

    Vector2Int GetClosestEdgePoint(Vector2Int zoneOrigin, int zoneWidth, int zoneHeight, Vector2Int referencePoint)
    {
        Vector2Int topCenter = new Vector2Int(zoneOrigin.x + zoneWidth / 2, zoneOrigin.y);
        Vector2Int bottomCenter = new Vector2Int(zoneOrigin.x + zoneWidth / 2, zoneOrigin.y + zoneHeight - 1);
        Vector2Int leftCenter = new Vector2Int(zoneOrigin.x, zoneOrigin.y + zoneHeight / 2);
        Vector2Int rightCenter = new Vector2Int(zoneOrigin.x + zoneWidth - 1, zoneOrigin.y + zoneHeight / 2);

        float dTop = Vector2Int.Distance(referencePoint, topCenter);
        float dBottom = Vector2Int.Distance(referencePoint, bottomCenter);
        float dLeft = Vector2Int.Distance(referencePoint, leftCenter);
        float dRight = Vector2Int.Distance(referencePoint, rightCenter);

        Vector2Int closest = topCenter;
        float best = dTop;
        if (dBottom < best)
        {
            best = dBottom;
            closest = bottomCenter;
        }

        if (dLeft < best)
        {
            best = dLeft;
            closest = leftCenter;
        }

        if (dRight < best)
            closest = rightCenter;

        return new Vector2Int(ClampGridX(closest.x), ClampGridZ(closest.y));
    }

    Vector2Int GetSiteCenter(Vector2Int origin, int siteSize)
    {
        return new Vector2Int(origin.x + siteSize / 2, origin.y + siteSize / 2);
    }

    int ManhattanDistance(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }
}
