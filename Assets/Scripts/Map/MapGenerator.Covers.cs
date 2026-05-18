using System.Collections.Generic;
using UnityEngine;

[System.Flags]
public enum CoverableZones
{
    None    = 0,
    Site    = 1 << 0,
    Neutral = 1 << 1,
    Spawn   = 1 << 2,
    Room    = 1 << 3,
    Main    = 1 << 4,
    Link    = 1 << 5,

    SiteAndNeutral  = Site | Neutral,
    SiteNeutralRoom = Site | Neutral | Room,
    All             = Site | Neutral | Spawn | Room | Main | Link
}

public partial class MapGenerator
{
    // ─────────────────────────────────────────────────────────────────────────
    // Два независимых алгоритма:
    //  А) Зонные укрытия (Site/Neutral/Room/Spawn): вес = видимость из входов, итерация.
    //  Б) Дорожные укрытия (Main/Link): 0-1 cover на путь, строго у стены, в середине.
    //     Никогда не блокирует выходы на зоны — концы путей исключены из кандидатов.
    // ─────────────────────────────────────────────────────────────────────────

    void PlaceCovers()
    {
        if (coverPrefab == null)
            return;

        bool[,] hasCover = new bool[width, height];

        // А) Зонные укрытия — через coverableZones.
        TryPlaceCoversForZoneType(BlockType.Site,    wallOnlyMode: false, hasCover);
        TryPlaceCoversForZoneType(BlockType.Neutral, wallOnlyMode: false, hasCover);
        TryPlaceCoversForZoneType(BlockType.Spawn,   wallOnlyMode: false, hasCover);
        TryPlaceCoversForZoneType(BlockType.Room,    wallOnlyMode: false, hasCover);

        // Б) Дорожные укрытия — также управляются через coverableZones.
        if ((coverableZones & CoverableZones.Main) != 0)
            PlaceRoadCoversOnPaths(mainRoadPaths, BlockType.Main, hasCover);
        if ((coverableZones & CoverableZones.Link) != 0)
            PlaceRoadCoversOnPaths(linkPaths, BlockType.Link, hasCover);
    }

    // ───── Б) Дорожные укрытия: wall-adjacent + экспозиция ───────────────────
    //
    //  1. Собрать wall-adjacent клетки в диапазоне [roadCoverRange] вдоль пути.
    //  2. Отфильтровать по экспозиции: клетки ниже порога не нужны
    //     (после поворота — и так прикрыто, cover не даёт тактического смысла).
    //  3. Отсортировать по убыванию экспозиции — первыми ставить на самые открытые места.
    //  4. Поставить roadCoversPerPath.Random() cover-ов с соблюдением spacing.
    //
    //  «У стены» = хотя бы один из 4 соседей — Wall/Empty/за краем карты.
    //  На 1-клеточном коридоре ВСЕ клетки wall-adjacent (стены с обеих сторон) → работает.
    //  На 2-клеточном — только крайние клетки → тоже корректно.
    void PlaceRoadCoversOnPaths(List<List<Vector2Int>> paths, BlockType roadType, bool[,] hasCover)
    {
        if (paths == null) return;

        int spacing = Mathf.Max(1, coverMinSpacing);
        int minExp  = Mathf.Max(1, roadCoverMinExposure);
        List<Vector2Int> placed = new(); // глобальный: spacing между путями

        foreach (List<Vector2Int> path in paths)
        {
            if (path == null || path.Count < 6) continue;
            if (Random.value > Mathf.Clamp01(roadCoverChance)) continue;

            int idxStart = Mathf.CeilToInt (path.Count * roadCoverRange.min);
            int idxEnd   = Mathf.FloorToInt(path.Count * roadCoverRange.max);
            idxStart = Mathf.Clamp(idxStart, 1, path.Count - 2);
            idxEnd   = Mathf.Clamp(idxEnd,   idxStart, path.Count - 2);

            // Кандидаты: клетки ВСЁ ЕЩЁ нужного типа дороги + wall-adjacent + достаточная экспозиция.
            // Ключевой фикс: PlaceRooms() мог перекрасить часть клеток пути в Room/Wall —
            // такие клетки из рассмотрения исключаем.
            List<(Vector2Int cell, int exposure)> candidates = new();
            for (int i = idxStart; i <= idxEnd; i++)
            {
                Vector2Int cell = path[i];
                if (!IsInsideMap(cell.x, cell.y)) continue;
                if (hasCover[cell.x, cell.y]) continue;

                // Клетка должна оставаться нужным типом дороги — не перекрашенной в Room/Wall.
                if (cellTypes[cell.x, cell.y] != roadType) continue;

                if (!IsRoadWallAdjacent(cell)) continue;

                int exp = ComputeRoadExposure(cell);
                if (exp >= minExp)
                    candidates.Add((cell, exp));
            }

            if (candidates.Count == 0) continue;

            // Сортируем: наиболее открытые — первыми.
            candidates.Sort((a, b) => b.exposure.CompareTo(a.exposure));

            int count  = roadCoversPerPath.Random();
            int placed_ = 0;
            foreach (var (chosen, _) in candidates)
            {
                if (placed_ >= count) break;
                if (HasCoverWithinSpacing(chosen, placed, spacing)) continue;
                PlaceCoverAt(chosen);
                hasCover[chosen.x, chosen.y] = true;
                placed.Add(chosen);
                placed_++;
            }
        }
    }

    // Экспозиция = максимальная дальность прямого обзора по 4 осям.
    // ВАЖНО: луч идёт только по клеткам ТОГО ЖЕ типа дороги (Main по Main, Link по Link).
    // Это означает:
    //   - длинный прямой коридор → высокая экспозиция;
    //   - клетка на повороте → луч быстро упирается → низкая;
    //   - клетка у Room/Site → луч останавливается на границе → Room не «добавляет» экспозицию.
    int ComputeRoadExposure(Vector2Int cell)
    {
        BlockType myType = cellTypes[cell.x, cell.y];
        int[] dx = { 1, -1, 0, 0 };
        int[] dz = { 0, 0, 1, -1 };
        int maxSight = 0;

        for (int dir = 0; dir < 4; dir++)
        {
            int sight = 0;
            int nx = cell.x + dx[dir];
            int nz = cell.y + dz[dir];

            while (IsInsideMap(nx, nz))
            {
                if (cellTypes[nx, nz] != myType) break; // другой тип — стоп
                sight++;
                nx += dx[dir];
                nz += dz[dir];
            }

            maxSight = Mathf.Max(maxSight, sight);
        }

        return maxSight;
    }

    // Клетка коридора у стены — имеет хотя бы одного соседа Wall/Empty/вне карты.
    // Дополнительно: если сосед — Room, отклоняем: Room уже создаёт тактическое разнообразие
    // на этом участке дороги, дублировать cover рядом не нужно.
    bool IsRoadWallAdjacent(Vector2Int cell)
    {
        int[] dx = { 1, -1, 0, 0 };
        int[] dz = { 0, 0, 1, -1 };
        bool hasOuterWall = false;

        for (int i = 0; i < 4; i++)
        {
            int nx = cell.x + dx[i];
            int nz = cell.y + dz[i];

            if (!IsInsideMap(nx, nz))
            {
                hasOuterWall = true;
                continue;
            }

            BlockType nt = cellTypes[nx, nz];

            if (nt == BlockType.Room || nt == BlockType.Pocket) return false;

            if (nt == BlockType.Empty)
            {
                hasOuterWall = true;
            }
            else if (nt == BlockType.Wall)
            {
                // Wall — хорошо, но только если это внешняя стена, а не обводка Room.
                if (!IsRoomEnclosureWall(nx, nz))
                    hasOuterWall = true;
            }
        }

        return hasOuterWall;
    }

    // Wall-клетка является стеной обводки Room (от ShapeZoneEnclosures) если
    // хотя бы один её 4-сосед — Room. Такие стены не должны привлекать road cover.
    bool IsRoomEnclosureWall(int wx, int wz)
    {
        int[] dx = { 1, -1, 0, 0 };
        int[] dz = { 0, 0, 1, -1 };
        for (int i = 0; i < 4; i++)
        {
            int nx = wx + dx[i];
            int nz = wz + dz[i];
            if (IsInsideMap(nx, nz) && cellTypes[nx, nz] == BlockType.Room) return true;
        }
        return false;
    }

    void TryPlaceCoversForZoneType(BlockType zoneType, bool wallOnlyMode, bool[,] hasCover)
    {
        if (!IsZoneCoverable(zoneType)) return;

        List<ZoneRegion> regions = ExtractRegionsForType(zoneType);
        foreach (ZoneRegion region in regions)
            PlaceCoversInRegion(region, wallOnlyMode, hasCover);
    }

    bool IsZoneCoverable(BlockType zoneType)
    {
        return zoneType switch
        {
            BlockType.Site    => (coverableZones & CoverableZones.Site)    != 0,
            BlockType.Neutral => (coverableZones & CoverableZones.Neutral) != 0 && generateNeutralZone,
            BlockType.Spawn   => (coverableZones & CoverableZones.Spawn)   != 0,
            BlockType.Room    => (coverableZones & CoverableZones.Room)    != 0,
            _                 => false
        };
    }

    // ───── Основной метод для одной зоны ─────────────────────────────────────

    void PlaceCoversInRegion(ZoneRegion region, bool wallOnlyMode, bool[,] hasCover)
    {
        if (region.Cells.Count < 4) return;

        HashSet<Vector2Int> regionSet = new(region.Cells.Count);
        foreach (Vector2Int cell in region.Cells) regionSet.Add(cell);

        // Входы зоны.
        List<Vector2Int> entrances = CollectEntrancePoints(region, regionSet);
        if (entrances.Count == 0) return;

        HashSet<Vector2Int> forbidden = BuildEntranceForbidden(entrances, regionSet);

        // Весовая карта. В wall-only режиме кандидатами могут быть только wall-adjacent клетки.
        HashSet<Vector2Int> candidates = wallOnlyMode
            ? BuildWallAdjacentCells(region, regionSet)
            : regionSet;
        if (candidates.Count == 0) return;

        int[,] weight = BuildEntranceVisibilityWeights(regionSet, candidates, entrances, hasCover);

        int hardLimit  = coverMaxPerZone > 0 ? coverMaxPerZone : int.MaxValue;
        int fillLimit  = Mathf.FloorToInt(region.Cells.Count * Mathf.Clamp01(coverMaxFillRatio));
        int limit      = Mathf.Min(hardLimit, fillLimit);
        if (limit <= 0) return;

        // Начальный максимум — порог остановки вычисляется от него.
        int initialMaxWeight = FindMaxWeight(candidates, weight, hasCover);
        if (initialMaxWeight <= 0) return;
        int stopThreshold = Mathf.Max(1, Mathf.CeilToInt(initialMaxWeight * Mathf.Clamp01(coverStopFraction)));

        int spacing = Mathf.Max(1, coverMinSpacing);
        List<Vector2Int> placed = new();
        int placedCount = 0;

        while (placedCount < limit)
        {
            if (!TryFindBestCoverCell(candidates, weight, hasCover, forbidden, placed, spacing,
                    out Vector2Int chosen, out int chosenWeight))
                break;

            // Два условия остановки:
            //  1. Абсолютный порог (coverMinEntranceVisibility) — минимальная видимость.
            //  2. Относительный порог (coverStopFraction) — когда основные "горячие точки" уже прикрыты.
            if (chosenWeight < coverMinEntranceVisibility) break;
            if (chosenWeight < stopThreshold) break;

            // Поставить основной блок.
            PlaceCoverAt(chosen);
            hasCover[chosen.x, chosen.y] = true;
            placed.Add(chosen);
            weight[chosen.x, chosen.y] = 0;
            placedCount++;

            // Пересчитать веса после основного блока.
            UpdateWeightsAfterCover(chosen, entrances, regionSet, hasCover, weight);

            // Попробовать поставить второй блок (double cover).
            if (placedCount < limit && TryPickDoubleCoverNeighbor(chosen, candidates, weight,
                    hasCover, forbidden, placed, spacing, out Vector2Int neighbor))
            {
                PlaceCoverAt(neighbor);
                hasCover[neighbor.x, neighbor.y] = true;
                placed.Add(neighbor);
                weight[neighbor.x, neighbor.y] = 0;
                placedCount++;
                UpdateWeightsAfterCover(neighbor, entrances, regionSet, hasCover, weight);
            }
        }
    }

    // ───── Двойной блок (double cover) ───────────────────────────────────────

    // Проверяет, нужно ли ставить второй блок рядом с primary.
    // Правило Valorant: в открытых местах — double/stack; у стен — обычно single.
    bool TryPickDoubleCoverNeighbor(
        Vector2Int primary,
        HashSet<Vector2Int> candidates,
        int[,] weight,
        bool[,] hasCover,
        HashSet<Vector2Int> forbidden,
        List<Vector2Int> placed,
        int spacing,
        out Vector2Int neighbor)
    {
        neighbor = default;
        float chance = coverMultiCellChance.Random();
        if (Random.value >= chance) return false;

        int[] dx = { 1, -1, 0, 0 };
        int[] dz = { 0, 0, 1, -1 };

        int bestWeight = 0; // минимальный вес соседа — хватит любого ненулевого
        bool found = false;

        for (int i = 0; i < 4; i++)
        {
            Vector2Int nb = new Vector2Int(primary.x + dx[i], primary.y + dz[i]);
            if (!candidates.Contains(nb)) continue;
            if (hasCover[nb.x, nb.y]) continue;
            if (forbidden.Contains(nb)) continue;

            // Для соседа проверяем spacing относительно ВСЕХ поставленных кроме primary.
            bool tooClose = false;
            for (int p = 0; p < placed.Count - 1; p++) // -1 потому что primary уже в placed
            {
                if (Mathf.Max(Mathf.Abs(nb.x - placed[p].x), Mathf.Abs(nb.y - placed[p].y)) < spacing - 1)
                {
                    tooClose = true;
                    break;
                }
            }
            if (tooClose) continue;

            int w = weight[nb.x, nb.y];
            if (w > bestWeight)
            {
                bestWeight = w;
                neighbor = nb;
                found = true;
            }
        }

        return found;
    }

    // ───── Шаг 2: Начальная весовая карта ─────────────────────────────────────

    int[,] BuildEntranceVisibilityWeights(
        HashSet<Vector2Int> regionSet,
        HashSet<Vector2Int> candidates,
        List<Vector2Int> entrances,
        bool[,] hasCover)
    {
        int[,] w = new int[width, height];

        foreach (Vector2Int entrance in entrances)
        {
            foreach (Vector2Int cell in candidates)
            {
                if (hasCover[cell.x, cell.y]) continue;
                if (IsVisibleInRegion(entrance, cell, regionSet, hasCover))
                    w[cell.x, cell.y]++;
            }
        }

        return w;
    }

    // ───── Wall-adjacent клетки (для дорог) ──────────────────────────────────

    // Клетка коридора у стены = имеет соседа, который не является тем же типом зоны
    // (Wall, Empty, другая зона). Такие клетки — единственные кандидаты для road cover.
    HashSet<Vector2Int> BuildWallAdjacentCells(ZoneRegion region, HashSet<Vector2Int> regionSet)
    {
        HashSet<Vector2Int> wallAdjacent = new();
        int[] dx = { 1, -1, 0, 0 };
        int[] dz = { 0, 0, 1, -1 };

        foreach (Vector2Int cell in region.Cells)
        {
            for (int i = 0; i < 4; i++)
            {
                int nx = cell.x + dx[i];
                int nz = cell.y + dz[i];

                // Сосед вне карты или не принадлежит тому же региону = мы у стены.
                if (!IsInsideMap(nx, nz) || !regionSet.Contains(new Vector2Int(nx, nz)))
                {
                    wallAdjacent.Add(cell);
                    break;
                }
            }
        }

        return wallAdjacent;
    }

    // ───── Поиск лучшей клетки ────────────────────────────────────────────────

    int FindMaxWeight(HashSet<Vector2Int> candidates, int[,] weight, bool[,] hasCover)
    {
        int max = 0;
        foreach (Vector2Int cell in candidates)
        {
            if (!hasCover[cell.x, cell.y] && weight[cell.x, cell.y] > max)
                max = weight[cell.x, cell.y];
        }
        return max;
    }

    bool TryFindBestCoverCell(
        HashSet<Vector2Int> candidates,
        int[,] weight,
        bool[,] hasCover,
        HashSet<Vector2Int> forbidden,
        List<Vector2Int> placed,
        int spacing,
        out Vector2Int chosen,
        out int chosenWeight)
    {
        chosen = default;
        chosenWeight = 0;

        float bestRanked = -1f;
        bool found = false;

        foreach (Vector2Int cell in candidates)
        {
            if (hasCover[cell.x, cell.y]) continue;
            if (forbidden.Contains(cell)) continue;

            int w = weight[cell.x, cell.y];
            if (w <= 0) continue;

            if (HasCoverWithinSpacing(cell, placed, spacing)) continue;

            float ranked = w * (1f + Random.value * Mathf.Clamp01(coverRandomBias));
            if (ranked > bestRanked)
            {
                bestRanked = ranked;
                chosen = cell;
                chosenWeight = w;
                found = true;
            }
        }

        return found;
    }

    // ───── Пересчёт весов после постановки cover-а ────────────────────────────

    void UpdateWeightsAfterCover(
        Vector2Int cover,
        List<Vector2Int> entrances,
        HashSet<Vector2Int> regionSet,
        bool[,] hasCover,
        int[,] weight)
    {
        foreach (Vector2Int entrance in entrances)
            ReduceWeightAlongShadow(entrance, cover, regionSet, hasCover, weight);
    }

    void ReduceWeightAlongShadow(
        Vector2Int entrance,
        Vector2Int cover,
        HashSet<Vector2Int> regionSet,
        bool[,] hasCover,
        int[,] weight)
    {
        int dx = cover.x - entrance.x;
        int dz = cover.y - entrance.y;
        if (dx == 0 && dz == 0) return;

        // Луч продолжается ЗА cover-ом в том же направлении.
        Vector2Int farPoint = new Vector2Int(
            Mathf.Clamp(cover.x + dx, 0, width - 1),
            Mathf.Clamp(cover.y + dz, 0, height - 1));

        List<Vector2Int> shadowRay = BresenhamLine(cover, farPoint);

        for (int i = 1; i < shadowRay.Count; i++)
        {
            Vector2Int c = shadowRay[i];
            if (!regionSet.Contains(c)) break;
            if (hasCover[c.x, c.y]) break;

            if (weight[c.x, c.y] > 0)
                weight[c.x, c.y]--;
        }
    }

    // ───── Видимость ─────────────────────────────────────────────────────────

    bool IsVisibleInRegion(
        Vector2Int from,
        Vector2Int to,
        HashSet<Vector2Int> regionSet,
        bool[,] hasCover)
    {
        List<Vector2Int> line = BresenhamLine(from, to);
        for (int i = 1; i < line.Count - 1; i++)
        {
            Vector2Int c = line[i];
            if (!regionSet.Contains(c)) return false;
            if (hasCover[c.x, c.y]) return false;
        }
        return true;
    }

    // ───── Входы зоны ─────────────────────────────────────────────────────────

    List<Vector2Int> CollectEntrancePoints(ZoneRegion region, HashSet<Vector2Int> regionSet)
    {
        List<Vector2Int> rawEntrances = new();
        int[] dx = { 1, -1, 0, 0 };
        int[] dz = { 0, 0, 1, -1 };

        foreach (Vector2Int cell in region.Cells)
        {
            for (int i = 0; i < 4; i++)
            {
                int nx = cell.x + dx[i];
                int nz = cell.y + dz[i];
                if (!IsInsideMap(nx, nz)) continue;

                BlockType outside = cellTypes[nx, nz];
                if (outside == BlockType.Main || outside == BlockType.Link ||
                    outside == BlockType.Room  || outside == BlockType.Site ||
                    outside == BlockType.Neutral || outside == BlockType.Spawn)
                {
                    // Сосед другого типа, но ещё Floor-зона = это вход в данную зону.
                    if (!regionSet.Contains(new Vector2Int(nx, nz)))
                    {
                        rawEntrances.Add(cell);
                        break;
                    }
                }
            }
        }

        if (rawEntrances.Count == 0) return rawEntrances;

        List<List<Vector2Int>> clusters = ClusterAdjacentCells(rawEntrances);
        List<Vector2Int> points = new();
        foreach (List<Vector2Int> cluster in clusters)
            points.Add(ClusterCenter(cluster));
        return points;
    }

    HashSet<Vector2Int> BuildEntranceForbidden(
        List<Vector2Int> entrances,
        HashSet<Vector2Int> regionSet)
    {
        HashSet<Vector2Int> forbidden = new();
        int r = Mathf.Max(0, coverEntranceForbiddenRadius);
        if (r == 0) return forbidden;

        foreach (Vector2Int entrance in entrances)
        {
            for (int ddx = -r; ddx <= r; ddx++)
            {
                for (int ddz = -r; ddz <= r; ddz++)
                {
                    if (Mathf.Max(Mathf.Abs(ddx), Mathf.Abs(ddz)) > r) continue;
                    Vector2Int c = new Vector2Int(entrance.x + ddx, entrance.y + ddz);
                    if (regionSet.Contains(c)) forbidden.Add(c);
                }
            }
        }

        return forbidden;
    }

    // ───── Вспомогательные ────────────────────────────────────────────────────

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
                Vector2Int cur = queue.Dequeue();
                cluster.Add(cur);
                for (int ddx = -1; ddx <= 1; ddx++)
                for (int ddz = -1; ddz <= 1; ddz++)
                {
                    if (ddx == 0 && ddz == 0) continue;
                    Vector2Int nb = new Vector2Int(cur.x + ddx, cur.y + ddz);
                    if (remaining.Remove(nb)) queue.Enqueue(nb);
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

    bool HasCoverWithinSpacing(Vector2Int cell, List<Vector2Int> placed, int spacing)
    {
        foreach (Vector2Int p in placed)
        {
            if (Mathf.Max(Mathf.Abs(cell.x - p.x), Mathf.Abs(cell.y - p.y)) < spacing)
                return true;
        }
        return false;
    }

    static List<Vector2Int> BresenhamLine(Vector2Int from, Vector2Int to)
    {
        List<Vector2Int> points = new();
        int x0 = from.x, y0 = from.y, x1 = to.x, y1 = to.y;
        int dx = Mathf.Abs(x1 - x0), dy = Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;
        while (true)
        {
            points.Add(new Vector2Int(x0, y0));
            if (x0 == x1 && y0 == y1) break;
            int e2 = err * 2;
            if (e2 > -dy) { err -= dy; x0 += sx; }
            if (e2 < dx)  { err += dx; y0 += sy; }
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
