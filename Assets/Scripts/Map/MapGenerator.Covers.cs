using System.Collections.Generic;
using UnityEngine;

[System.Flags]
public enum CoverableZones
{
    None = 0,
    Site = 1 << 0,
    Neutral = 1 << 1,
    Spawn = 1 << 2,
    SiteAndNeutral = Site | Neutral,
    All = Site | Neutral | Spawn
}

public partial class MapGenerator
{
    // Расстановка укрытий по принципу "ось атаки".
    // Для каждой зоны выявляются входы (клетки границы со стороны Main/Link) и центр.
    // Между парами (вход → центр, вход → вход) рисуются "оси" — Bresenham-линии.
    // На каждой оси ставится 1-2 укрытия в стратегических позициях:
    //  - "pocket" в первой трети от входа (corner-pick для входящего/защитника),
    //  - центральное на середине оси (если ось длинная и есть лимит).
    // Каждое укрытие может сдвигаться на 1 клетку перпендикулярно оси, чтобы давать "сбоку от линии".
    // Это даёт естественные corner-pick у входов, центральные укрытия на пересечении осей и
    // пристенные — там, где ось проходит вдоль стены (например, между двумя входами на одной стороне).
    void PlaceCovers()
    {
        if (coverPrefab == null)
            return;

        bool[,] hasCover = new bool[width, height];

        if ((coverableZones & CoverableZones.Site) != 0)
            PlaceCoversInZoneType(BlockType.Site, hasCover);
        if ((coverableZones & CoverableZones.Neutral) != 0 && generateNeutralZone)
            PlaceCoversInZoneType(BlockType.Neutral, hasCover);
        if ((coverableZones & CoverableZones.Spawn) != 0)
            PlaceCoversInZoneType(BlockType.Spawn, hasCover);
    }

    void PlaceCoversInZoneType(BlockType zoneType, bool[,] hasCover)
    {
        List<ZoneRegion> regions = ExtractRegionsForType(zoneType);
        foreach (ZoneRegion region in regions)
            PlaceCoversInRegion(region, hasCover);
    }

    void PlaceCoversInRegion(ZoneRegion region, bool[,] hasCover)
    {
        if (region.Cells.Count < 4)
            return;

        HashSet<Vector2Int> regionSet = new(region.Cells.Count);
        foreach (Vector2Int cell in region.Cells)
            regionSet.Add(cell);

        // Собираем входы и центр.
        List<Vector2Int> entrances = CollectEntrancePoints(region, regionSet);
        Vector2Int center = ResolveZoneCenter(region, regionSet);

        // Формируем список осей.
        List<(Vector2Int from, Vector2Int to)> axes = new();
        foreach (Vector2Int entrance in entrances)
            axes.Add((entrance, center));

        if (useEntranceToEntranceAxes)
        {
            for (int i = 0; i < entrances.Count; i++)
            {
                for (int j = i + 1; j < entrances.Count; j++)
                    axes.Add((entrances[i], entrances[j]));
            }
        }

        // Перемешиваем оси, чтобы порядок выбора укрытий не был детерминирован A→B→C.
        ShuffleList(axes);

        int hardLimit = coverMaxPerZone > 0 ? coverMaxPerZone : int.MaxValue;
        int fillLimit = Mathf.FloorToInt(region.Cells.Count * Mathf.Clamp01(coverMaxFillRatio));
        int limit = Mathf.Min(hardLimit, fillLimit);
        if (limit <= 0)
            return;

        int placed = 0;
        List<Vector2Int> placedCovers = new();
        int spacing = Mathf.Max(1, coverMinSpacing);

        // Подберём множество "блокированных под укрытие" клеток вокруг входов — туда ставить нельзя.
        HashSet<Vector2Int> entranceForbidden = BuildEntranceForbiddenSet(entrances);

        foreach (var axis in axes)
        {
            if (placed >= limit)
                break;

            int placedOnAxis = TryPlaceCoversOnAxis(
                axis.from, axis.to, regionSet, hasCover, placedCovers, entranceForbidden, spacing, limit - placed);
            placed += placedOnAxis;
        }
    }

    // Группируем подряд идущие клетки-входы (по бордеру зоны) в один "вход" и берём его центр.
    List<Vector2Int> CollectEntrancePoints(ZoneRegion region, HashSet<Vector2Int> regionSet)
    {
        // Сначала найдём все клетки региона, прилегающие к Main/Link.
        List<Vector2Int> rawEntrances = new();
        int[] dx = { 1, -1, 0, 0 };
        int[] dz = { 0, 0, 1, -1 };
        foreach (Vector2Int cell in region.Cells)
        {
            for (int i = 0; i < 4; i++)
            {
                int nx = cell.x + dx[i];
                int nz = cell.y + dz[i];
                if (!IsInsideMap(nx, nz))
                    continue;

                BlockType outside = cellTypes[nx, nz];
                if (outside == BlockType.Main || outside == BlockType.Link)
                {
                    rawEntrances.Add(cell);
                    break;
                }
            }
        }

        if (rawEntrances.Count == 0)
            return rawEntrances;

        // Группируем смежные (Чебышёв ≤ 1) клетки в кластеры — это один логический "вход".
        List<List<Vector2Int>> clusters = ClusterAdjacentCells(rawEntrances);

        List<Vector2Int> entrancePoints = new();
        foreach (List<Vector2Int> cluster in clusters)
            entrancePoints.Add(ClusterCenter(cluster));

        return entrancePoints;
    }

    List<List<Vector2Int>> ClusterAdjacentCells(List<Vector2Int> cells)
    {
        List<List<Vector2Int>> clusters = new();
        HashSet<Vector2Int> remaining = new(cells);

        while (remaining.Count > 0)
        {
            Vector2Int seed = default;
            foreach (Vector2Int c in remaining) { seed = c; break; }

            List<Vector2Int> cluster = new();
            Queue<Vector2Int> queue = new();
            queue.Enqueue(seed);
            remaining.Remove(seed);

            while (queue.Count > 0)
            {
                Vector2Int current = queue.Dequeue();
                cluster.Add(current);

                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (dx == 0 && dz == 0)
                            continue;
                        Vector2Int neighbor = new Vector2Int(current.x + dx, current.y + dz);
                        if (remaining.Remove(neighbor))
                            queue.Enqueue(neighbor);
                    }
                }
            }

            clusters.Add(cluster);
        }

        return clusters;
    }

    Vector2Int ClusterCenter(List<Vector2Int> cluster)
    {
        int sx = 0, sz = 0;
        foreach (Vector2Int c in cluster) { sx += c.x; sz += c.y; }
        return new Vector2Int(sx / cluster.Count, sz / cluster.Count);
    }

    Vector2Int ResolveZoneCenter(ZoneRegion region, HashSet<Vector2Int> regionSet)
    {
        Vector2Int rawCenter = new Vector2Int(
            (region.Min.x + region.Max.x) / 2,
            (region.Min.y + region.Max.y) / 2);
        if (regionSet.Contains(rawCenter))
            return rawCenter;

        // Центр bounding box не лежит в регионе (вырезанная форма) — берём ближайшую клетку региона.
        Vector2Int best = region.Cells[0];
        int bestDist = int.MaxValue;
        foreach (Vector2Int c in region.Cells)
        {
            int d = Mathf.Abs(c.x - rawCenter.x) + Mathf.Abs(c.y - rawCenter.y);
            if (d < bestDist) { bestDist = d; best = c; }
        }
        return best;
    }

    // Возвращает множество клеток, в которые нельзя ставить укрытия, потому что они слишком близко
    // к входу (заблокировали бы сам вход).
    HashSet<Vector2Int> BuildEntranceForbiddenSet(List<Vector2Int> entrances)
    {
        HashSet<Vector2Int> forbidden = new();
        foreach (Vector2Int entrance in entrances)
        {
            // Сам вход и его 8 соседей.
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                    forbidden.Add(new Vector2Int(entrance.x + dx, entrance.y + dz));
            }
        }
        return forbidden;
    }

    int TryPlaceCoversOnAxis(
        Vector2Int from,
        Vector2Int to,
        HashSet<Vector2Int> regionSet,
        bool[,] hasCover,
        List<Vector2Int> placedCovers,
        HashSet<Vector2Int> entranceForbidden,
        int spacing,
        int budget)
    {
        if (budget <= 0)
            return 0;

        List<Vector2Int> line = BresenhamLine(from, to);
        if (line.Count < 4)
            return 0; // ось слишком короткая — нечего на ней ставить осмысленно

        int max = Mathf.Min(coversPerAxis, budget);
        int placed = 0;

        // Точки-кандидаты по оси: pocket около "from" и (если возможно) середина.
        List<int> candidateIndices = new();
        int pocketIdx = Mathf.Clamp(axisPocketDistance, 1, line.Count - 2);
        candidateIndices.Add(pocketIdx);
        if (max >= 2)
        {
            int midIdx = line.Count / 2;
            // Только если midIdx достаточно далеко от pocketIdx — иначе это та же клетка.
            if (Mathf.Abs(midIdx - pocketIdx) >= 2)
                candidateIndices.Add(midIdx);
        }

        foreach (int idx in candidateIndices)
        {
            if (placed >= max)
                break;

            Vector2Int axisDir = line[Mathf.Min(idx + 1, line.Count - 1)] - line[Mathf.Max(idx - 1, 0)];
            Vector2Int candidate = line[idx];

            Vector2Int? chosen = TryPickCoverAroundCandidate(
                candidate, axisDir, regionSet, hasCover, placedCovers, entranceForbidden, spacing);
            if (!chosen.HasValue)
                continue;

            PlaceCoverAt(chosen.Value);
            hasCover[chosen.Value.x, chosen.Value.y] = true;
            placedCovers.Add(chosen.Value);
            placed++;
        }

        return placed;
    }

    // Возвращает клетку для укрытия рядом с candidate. Сначала пробует саму clientcandidate, затем
    // (с вероятностью lateralOffsetChance) — сдвиг на 1 клетку перпендикулярно оси, в обе стороны.
    Vector2Int? TryPickCoverAroundCandidate(
        Vector2Int candidate,
        Vector2Int axisDir,
        HashSet<Vector2Int> regionSet,
        bool[,] hasCover,
        List<Vector2Int> placedCovers,
        HashSet<Vector2Int> entranceForbidden,
        int spacing)
    {
        // Перпендикуляр: повёрнутый axisDir на 90 градусов (по модулю +/-).
        // axisDir может быть (1,0), (0,1), (1,1)... — берём перпендикуляр через Cross-like trick.
        Vector2Int perp = new Vector2Int(-Mathf.Clamp(axisDir.y, -1, 1), Mathf.Clamp(axisDir.x, -1, 1));
        if (perp == Vector2Int.zero)
            perp = new Vector2Int(0, 1);

        bool tryLateral = Random.value < Mathf.Clamp01(axisLateralOffsetChance);
        List<Vector2Int> trial = new();
        if (tryLateral)
        {
            // Сначала пробуем сдвинутые позиции (corner-pick), потом саму ось.
            trial.Add(candidate + perp);
            trial.Add(candidate - perp);
            trial.Add(candidate);
        }
        else
        {
            trial.Add(candidate);
            trial.Add(candidate + perp);
            trial.Add(candidate - perp);
        }

        foreach (Vector2Int cell in trial)
        {
            if (!regionSet.Contains(cell))
                continue;
            if (hasCover[cell.x, cell.y])
                continue;
            if (entranceForbidden.Contains(cell))
                continue;
            if (HasCoverWithinSpacing(cell, placedCovers, spacing))
                continue;

            return cell;
        }

        return null;
    }

    bool HasCoverWithinSpacing(Vector2Int cell, List<Vector2Int> placedCovers, int spacing)
    {
        foreach (Vector2Int placed in placedCovers)
        {
            int dx = Mathf.Abs(cell.x - placed.x);
            int dz = Mathf.Abs(cell.y - placed.y);
            if (Mathf.Max(dx, dz) < spacing)
                return true;
        }
        return false;
    }

    static List<Vector2Int> BresenhamLine(Vector2Int from, Vector2Int to)
    {
        List<Vector2Int> points = new();
        int x0 = from.x, y0 = from.y;
        int x1 = to.x, y1 = to.y;
        int dx = Mathf.Abs(x1 - x0);
        int dy = Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;
        while (true)
        {
            points.Add(new Vector2Int(x0, y0));
            if (x0 == x1 && y0 == y1)
                break;
            int e2 = err * 2;
            if (e2 > -dy) { err -= dy; x0 += sx; }
            if (e2 < dx) { err += dx; y0 += sy; }
        }
        return points;
    }

    void ShuffleList<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    void PlaceCoverAt(Vector2Int cell)
    {
        int levels = Mathf.Max(1, coverHeight);
        for (int level = 0; level < levels; level++)
        {
            Vector3 pos = new Vector3(cell.x * blockSize, blockSize + level * blockSize, cell.y * blockSize);
            Instantiate(coverPrefab, pos, Quaternion.identity, transform);
        }
    }
}
