using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

public enum ZoneShapeMode
{
    Square,
    Brush
}

public partial class MapGenerator
{
    void PlaceSpawnZones()
    {
        Vector2Int spawnSizeAttacker = RandomZoneSize(spawnZoneSize);
        Vector2Int spawnSizeDefender = RandomZoneSize(spawnZoneSize);

        int attackerHalfX = spawnSizeAttacker.x / 2;
        int attackerHalfZ = spawnSizeAttacker.y / 2;
        int defenderHalfX = spawnSizeDefender.x / 2;
        int defenderHalfZ = spawnSizeDefender.y / 2;

        int attackerX = ResolveSpawnCenterX(spawnSizeAttacker.x);
        int defenderX = ResolveSpawnCenterX(spawnSizeDefender.x);

        int attackerZ = innerPadding + attackerHalfZ;
        int defenderZ = height - 1 - innerPadding - (spawnSizeDefender.y - defenderHalfZ - 1);

        attackerSpawn = new Vector2Int(attackerX, attackerZ);
        defenderSpawn = new Vector2Int(defenderX, defenderZ);

        PaintZoneAroundCenter(attackerSpawn, attackerHalfX, spawnSizeAttacker.x - attackerHalfX, attackerHalfZ, spawnSizeAttacker.y - attackerHalfZ, BlockType.Spawn);
        PaintZoneAroundCenter(defenderSpawn, defenderHalfX, spawnSizeDefender.x - defenderHalfX, defenderHalfZ, spawnSizeDefender.y - defenderHalfZ, BlockType.Spawn);
    }

    int ResolveSpawnCenterX(int spawnSizeX)
    {
        int half = spawnSizeX / 2;
        int xMin = innerPadding + half;
        int xMax = width - 1 - innerPadding - (spawnSizeX - half - 1);
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

    // Случайный размер зоны: x и z генерируются независимо в диапазоне [min, max].
    Vector2Int RandomZoneSize(IntRange range) => RandomZoneSize(range.min, range.max);

    Vector2Int RandomZoneSize(int min, int max)
    {
        int lo = Mathf.Max(1, Mathf.Min(min, max));
        int hi = Mathf.Max(lo, Mathf.Max(min, max));
        int maxSide = Mathf.Min(width, height);
        int sx = Mathf.Clamp(Random.Range(lo, hi + 1), 1, maxSide);
        int sz = Mathf.Clamp(Random.Range(lo, hi + 1), 1, maxSide);
        return new Vector2Int(sx, sz);
    }

    void PlaceSiteZones()
    {
        siteASize = RandomZoneSize(siteZoneSize);
        siteBSize = RandomZoneSize(siteZoneSize);

        int siteLineZ = ResolveSiteLineZ();

        siteA = ResolveSiteOrigin(leftHalf: true, siteASize, siteLineZ);
        siteB = ResolveSiteOrigin(leftHalf: false, siteBSize, siteLineZ);

        PaintZoneByRect(siteA.x, siteA.y, siteASize.x, siteASize.y, BlockType.Site);
        PaintZoneByRect(siteB.x, siteB.y, siteBSize.x, siteBSize.y, BlockType.Site);
    }

    int ResolveSiteLineZ()
    {
        const float maxBias = 0.5f;
        float midZ = (attackerSpawn.y + defenderSpawn.y) * 0.5f;
        float bias = Mathf.Clamp(siteLineBiasToDefender, -maxBias, maxBias);
        float t = Mathf.Abs(bias) / maxBias;
        float targetZ = bias >= 0f ? defenderSpawn.y : attackerSpawn.y;
        float biasedZ = Mathf.Lerp(midZ, targetZ, t);
        return Mathf.RoundToInt(biasedZ);
    }

    Vector2Int ResolveSiteOrigin(bool leftHalf, Vector2Int siteSize, int lineZ)
    {
        int globalXMin = innerPadding;
        int globalXMax = width - innerPadding - siteSize.x;
        int mapCenter = (width - 1) / 2;
        // Минимальная дистанция от центра карты для сайта пропорциональна его ширине.
        int minDistanceFromCenter = Mathf.Max(1, siteMinCenterDistanceMultiplier) * siteSize.x;
        int drift = Mathf.Max(0, siteCenterDriftMax);

        int xMin;
        int xMax;
        if (leftHalf)
        {
            int allowedMaxByDistance = mapCenter - minDistanceFromCenter - siteSize.x;
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
        int originZTarget = lineZ - siteSize.y / 2 + Random.Range(-verticalJitter, verticalJitter + 1);
        int globalZMin = innerPadding;
        int globalZMax = height - innerPadding - siteSize.y;
        int originZ = Mathf.Clamp(originZTarget, globalZMin, globalZMax);

        return new Vector2Int(originX, originZ);
    }

    void MarkNeutralZone()
    {
        if (!generateNeutralZone)
            return;

        neutralSize = RandomZoneSize(neutralZoneSize);
        int sizeX = neutralSize.x;
        int sizeZ = neutralSize.y;
        int halfX = sizeX / 2;
        int halfZ = sizeZ / 2;

        int xClampMin = innerPadding + halfX;
        int xClampMax = width - 1 - innerPadding - (sizeX - halfX - 1);
        int zClampMin = innerPadding + halfZ;
        int zClampMax = height - 1 - innerPadding - (sizeZ - halfZ - 1);

        int mapCenterX = (width - 1) / 2;
        int mapCenterZ = (height - 1) / 2;

        // Линия сайтов считается по центрам зон с учётом размеров по Z.
        int sitesLineZ = (siteA.y + siteASize.y / 2 + siteB.y + siteBSize.y / 2) / 2;
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
        PaintZoneByRect(centerX - halfX, centerZ - halfZ, sizeX, sizeZ, BlockType.Neutral);
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
        // Пути атакующего (spawn[0] → siteA, spawn[0] → siteB).
        BuildTeamMainPaths(attackerSpawn, new[] { siteA, siteB }, new[] { siteASize, siteBSize },
            attackerMainPaths);
        // Пути защитника (spawn[1] → siteA, spawn[1] → siteB).
        BuildTeamMainPaths(defenderSpawn, new[] { siteA, siteB }, new[] { siteASize, siteBSize },
            defenderMainPaths);

        // mainRoadPaths — объединение для использования в PlaceRooms.
        mainRoadPaths.AddRange(attackerMainPaths);
        mainRoadPaths.AddRange(defenderMainPaths);
    }

    void BuildTeamMainPaths(Vector2Int spawn, Vector2Int[] sites, Vector2Int[] siteSizes,
        List<List<Vector2Int>> outPaths)
    {
        for (int j = 0; j < sites.Length; j++)
        {
            Vector2Int endpoint = GetClosestEdgePoint(sites[j], siteSizes[j].x, siteSizes[j].y, spawn);
            List<Vector2Int> path = BuildPathWithWaypoints(
                spawn, endpoint, BlockType.Main,
                mainWaypointCount.Random(), mainWaypointOffset.Random());
            if (path != null && path.Count > 0)
                outPaths.Add(path);
        }
    }

    void BuildAttackerLinks()
    {
        // Каждый link получает независимое случайное значение branch-point.
        // Это гарантирует, что два link атакующего (к сайту A и к сайту B)
        // ответвляются в разных местах и не совпадают визуально.
        foreach (List<Vector2Int> mainPath in attackerMainPaths)
        {
            if (mainPath == null || mainPath.Count < 4)
                continue;

            float fraction = Mathf.Clamp(attackerLinkBranch.Random(), 0.1f, 0.9f);
            int branchIdx = Mathf.Clamp(Mathf.RoundToInt(mainPath.Count * fraction), 1, mainPath.Count - 1);
            Vector2Int branchPoint = mainPath[branchIdx];

            List<Vector2Int> link = BuildLinkWithWaypoints(branchPoint, neutralCenter);
            if (link != null && link.Count > 0)
                linkPaths.Add(link);
        }
    }

    void BuildDefenderLink()
    {
        if (defenderMainPaths == null || defenderMainPaths.Count == 0)
            return;

        List<Vector2Int> mainPath = defenderMainPaths[Random.Range(0, defenderMainPaths.Count)];
        if (mainPath == null || mainPath.Count < 4)
            return;

        float fraction = Mathf.Clamp(defenderLinkBranch.Random(), 0.1f, 0.95f);
        int branchIdx = Mathf.Clamp(Mathf.RoundToInt(mainPath.Count * fraction), 1, mainPath.Count - 1);
        Vector2Int branchPoint = mainPath[branchIdx];

        List<Vector2Int> link = BuildLinkWithWaypoints(branchPoint, neutralCenter);
        if (link != null && link.Count > 0)
            linkPaths.Add(link);
    }

    List<Vector2Int> BuildLinkWithWaypoints(Vector2Int from, Vector2Int to)
    {
        int wps = linkWaypointCount.Random();
        int offset = linkWaypointOffset.Random();
        return BuildPathWithWaypoints(from, to, BlockType.Link, wps, offset,
            paintSkipPredicate: IsSpawnOrMainCell);
    }

    // Строит путь с промежуточными waypoints для органичных изгибов.
    // Каждый waypoint смещён перпендикулярно прямой линии from→to на случайную величину.
    // Каждый сегмент решается отдельным вызовом A*, что даёт визуально естественные
    // изгибы типа Valorant без изменения cost-функций A*.
    List<Vector2Int> BuildPathWithWaypoints(
        Vector2Int from,
        Vector2Int to,
        BlockType type,
        int waypointCount,
        int maxPerpOffset,
        Func<Vector2Int, bool> paintSkipPredicate = null)
    {
        List<Vector2Int> checkpoints = new() { from };

        if (waypointCount > 0)
            checkpoints.AddRange(GenerateWaypoints(from, to, waypointCount, maxPerpOffset));

        checkpoints.Add(to);

        List<Vector2Int> fullPath = new();
        for (int i = 0; i < checkpoints.Count - 1; i++)
        {
            // Первый сегмент может иметь paintSkipPredicate (например, не красить спавн/main).
            // Последующие — обычный проход.
            Func<Vector2Int, bool> predicate = (i == 0) ? paintSkipPredicate : null;
            List<Vector2Int> segment = CreateConfiguredPath(
                checkpoints[i], checkpoints[i + 1], type, predicate);

            if (segment == null || segment.Count == 0)
                continue;

            // Пропускаем первую точку сегмента (кроме самого первого) чтобы не дублировать стыки.
            int startFrom = (fullPath.Count > 0 && segment.Count > 1) ? 1 : 0;
            for (int k = startFrom; k < segment.Count; k++)
                fullPath.Add(segment[k]);
        }

        return fullPath.Count > 0 ? fullPath : null;
    }

    // Генерирует промежуточные waypoints между from и to.
    // Точки равномерно распределены по длине пути и смещены перпендикулярно.
    List<Vector2Int> GenerateWaypoints(Vector2Int from, Vector2Int to, int count, int maxOffset)
    {
        List<Vector2Int> wps = new();
        Vector2 dir = ((Vector2)(to - from)).normalized;
        // Перпендикуляр: поворот на 90°.
        Vector2Int perp = new Vector2Int(Mathf.RoundToInt(-dir.y), Mathf.RoundToInt(dir.x));
        if (perp == Vector2Int.zero)
            perp = new Vector2Int(0, 1);

        for (int i = 1; i <= count; i++)
        {
            float t = (float)i / (count + 1);
            int baseX = Mathf.RoundToInt(Mathf.Lerp(from.x, to.x, t));
            int baseZ = Mathf.RoundToInt(Mathf.Lerp(from.y, to.y, t));

            int offset = maxOffset > 0 ? Random.Range(-maxOffset, maxOffset + 1) : 0;

            int wpX = Mathf.Clamp(baseX + perp.x * offset, innerPadding, width - 1 - innerPadding);
            int wpZ = Mathf.Clamp(baseZ + perp.y * offset, innerPadding, height - 1 - innerPadding);
            wps.Add(new Vector2Int(wpX, wpZ));
        }

        return wps;
    }

    void BuildNeutralToSiteLinks()
    {
        Vector2Int[] sites = { siteA, siteB };
        Vector2Int[] siteSizes = { siteASize, siteBSize };
        for (int i = 0; i < sites.Length; i++)
        {
            Vector2Int siteEntry = GetClosestEdgePoint(sites[i], siteSizes[i].x, siteSizes[i].y, neutralCenter);
            List<Vector2Int> link = BuildLinkWithWaypoints(neutralCenter, siteEntry);
            if (link != null && link.Count > 0)
                linkPaths.Add(link);
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

        BlockType type = cellTypes[cell.x, cell.y];
        return type == BlockType.Spawn || type == BlockType.Main;
    }

    // Заполняет зону по прямоугольнику (startX, startZ, sizeX, sizeZ) с учётом текущего zoneShapeMode.
    // Square — обычный прямоугольник. Brush — круглая/квадратная кисть из центра.
    void PaintZoneByRect(int startX, int startZ, int sizeX, int sizeZ, BlockType type)
    {
        if (sizeX <= 0 || sizeZ <= 0)
            return;

        float centerX = startX + (sizeX - 1) * 0.5f;
        float centerZ = startZ + (sizeZ - 1) * 0.5f;
        float radiusX = Mathf.Max(0.5f, sizeX * 0.5f);
        float radiusZ = Mathf.Max(0.5f, sizeZ * 0.5f);

        bool useCircular = zoneShapeMode != ZoneShapeMode.Square && useCircularZoneBrush;
        StampZoneShape(centerX, centerZ, radiusX, radiusZ, useCircular, type);
    }

    // Один "штамп" зоны: круглой (эллипс) или квадратной формы.
    void StampZoneShape(
        float centerX,
        float centerZ,
        float radiusX,
        float radiusZ,
        bool circular,
        BlockType type)
    {
        int xMin = Mathf.FloorToInt(centerX - radiusX);
        int xMax = Mathf.CeilToInt(centerX + radiusX);
        int zMin = Mathf.FloorToInt(centerZ - radiusZ);
        int zMax = Mathf.CeilToInt(centerZ + radiusZ);

        for (int x = xMin; x <= xMax; x++)
        {
            for (int z = zMin; z <= zMax; z++)
            {
                if (!IsInsideMap(x, z))
                    continue;

                if (circular)
                {
                    float nx = (x - centerX) / radiusX;
                    float nz = (z - centerZ) / radiusZ;
                    if (nx * nx + nz * nz > 1f)
                        continue;
                }
                else
                {
                    if (Mathf.Abs(x - centerX) > radiusX || Mathf.Abs(z - centerZ) > radiusZ)
                        continue;
                }

                TryMarkBlock(x, z, type);
            }
        }
    }

    // Заполняет зону вокруг центра, учитывая ассиметрию полу-размеров по X и Z раздельно.
    void PaintZoneAroundCenter(Vector2Int center, int halfXBefore, int halfXAfter, int halfZBefore, int halfZAfter, BlockType type)
    {
        int sizeX = Mathf.Max(1, halfXBefore + halfXAfter);
        int sizeZ = Mathf.Max(1, halfZBefore + halfZAfter);
        int startX = center.x - halfXBefore;
        int startZ = center.y - halfZBefore;
        PaintZoneByRect(startX, startZ, sizeX, sizeZ, type);
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

    Vector2Int GetSiteCenter(Vector2Int origin, Vector2Int siteSize)
    {
        return new Vector2Int(origin.x + siteSize.x / 2, origin.y + siteSize.y / 2);
    }

    int ManhattanDistance(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }
}
