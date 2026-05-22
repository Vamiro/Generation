using System.Collections.Generic;
using UnityEngine;

[System.Flags]
public enum CoverPlacementMode
{
    None = 0,
    Prefabs = 1 << 0,
    WeightLabels = 1 << 1,
}

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
    private const int EntrancePassageWindowRadius = 1; // 3×3 вокруг клетки входа
    private const int WeightRayDirectionCount = 4;

    private static readonly int[] WeightRayDx = { 1, -1, 0, 0 };
    private static readonly int[] WeightRayDz = { 0, 0, 1, -1 };
    private static readonly Color WeightLabelColorLow = new(0.15f, 1f, 0.2f, 1f);
    private static readonly Color WeightLabelColorHigh = Color.red;

    bool UsesCoverPrefabs =>
        enableCovers && (coverPlacementMode & CoverPlacementMode.Prefabs) != 0 && coverPrefab != null;

    bool UsesWeightLabels =>
        enableCovers && (coverPlacementMode & CoverPlacementMode.WeightLabels) != 0;

    void PlaceCovers()
    {
        if (!enableCovers || coverPlacementMode == CoverPlacementMode.None)
            return;

        RecomputeCellWeightsFromRays();

        bool placePrefabs = UsesCoverPrefabs;
        if (placePrefabs)
        {
            bool[,] hasCover = coverOccupancy;
            TryPlaceCoversForZoneType(BlockType.Site, hasCover, cellWeights);
            TryPlaceCoversForZoneType(BlockType.Neutral, hasCover, cellWeights);
            PlaceAllRoadCovers(hasCover, cellWeights);
        }

        if (UsesWeightLabels)
        {
            RecomputeCellWeightsFromRays();
            VisualizeFloorWeightLabels();
        }
    }

    readonly struct RoadWallRunCandidate
    {
        public readonly Vector2Int Cell;
        public readonly int RunLength;
        public readonly int Weight;

        public RoadWallRunCandidate(Vector2Int cell, int runLength, int weight)
        {
            Cell = cell;
            RunLength = runLength;
            Weight = weight;
        }
    }

    void PlaceAllRoadCovers(bool[,] hasCover, int[,] cellWeightGrid)
    {
        if (maxCoversOnRoads <= 0) return;

        bool includeMain = (coverableZones & CoverableZones.Main) != 0;
        bool includeLink = (coverableZones & CoverableZones.Link) != 0;
        if (!includeMain && !includeLink) return;

        int spacing = Mathf.Max(1, roadCoverMinSpacing);
        var candidates = new List<RoadWallRunCandidate>();
        if (includeMain)
            CollectRoadWallRunCoverCandidates(mainRoadPaths, BlockType.Main, hasCover, cellWeightGrid, candidates);
        if (includeLink)
            CollectRoadWallRunCoverCandidates(linkPaths, BlockType.Link, hasCover, cellWeightGrid, candidates);

        if (candidates.Count == 0)
            return;

        SortRoadWallRunCandidates(candidates, cellWeightGrid);

        var placed = new List<Vector2Int>();
        int placedCount = 0;
        foreach (RoadWallRunCandidate candidate in candidates)
        {
            if (placedCount >= maxCoversOnRoads)
                break;

            Vector2Int cell = candidate.Cell;
            if (hasCover[cell.x, cell.y])
                continue;
            if (HasCoverWithinSpacing(cell, placed, spacing))
                continue;

            PlaceCoverAt(cell);
            hasCover[cell.x, cell.y] = true;
            RecomputeCellWeightsFromRays();
            placed.Add(cell);
            placedCount++;
        }
    }

    int GetPathBrushRadius(BlockType roadType) =>
        Mathf.Max(0, roadType == BlockType.Main ? mainWidth : linkWidth);

    void CollectRoadWallRunCoverCandidates(
        List<List<Vector2Int>> paths,
        BlockType roadType,
        bool[,] hasCover,
        int[,] cellWeightGrid,
        List<RoadWallRunCandidate> candidates)
    {
        HashSet<Vector2Int> rangeCells = BuildRoadCoverRangeCells(paths, roadType);
        if (rangeCells.Count == 0)
            return;

        int minRun = Mathf.Max(2, roadWallRunMinLength);
        var bestByCell = new Dictionary<Vector2Int, RoadWallRunCandidate>();

        CollectGridRoadWallRuns(roadType, hasCover, rangeCells, minRun, cellWeightGrid, bestByCell);
        CollectHighWeightRoadWallFallback(roadType, hasCover, rangeCells, cellWeightGrid, bestByCell);

        foreach (RoadWallRunCandidate candidate in bestByCell.Values)
            candidates.Add(candidate);
    }

    HashSet<Vector2Int> BuildRoadCoverRangeCells(List<List<Vector2Int>> paths, BlockType roadType)
    {
        var rangeCells = new HashSet<Vector2Int>();
        if (paths == null)
            return rangeCells;

        int brushRadius = GetPathBrushRadius(roadType);

        foreach (List<Vector2Int> path in paths)
        {
            if (path == null || path.Count < 2)
                continue;

            int idxStart = Mathf.CeilToInt(path.Count * roadCoverRange.min);
            int idxEnd = Mathf.FloorToInt(path.Count * roadCoverRange.max);
            idxStart = Mathf.Clamp(idxStart, 0, path.Count - 1);
            idxEnd = Mathf.Clamp(idxEnd, idxStart, path.Count - 1);

            for (int i = idxStart; i <= idxEnd; i++)
            {
                foreach (Vector2Int cell in EnumeratePathBrushCells(path[i], brushRadius))
                {
                    if (!IsInsideMap(cell.x, cell.y))
                        continue;
                    if (cellTypes[cell.x, cell.y] == roadType)
                        rangeCells.Add(cell);
                }
            }
        }

        return rangeCells;
    }

    IEnumerable<Vector2Int> EnumeratePathBrushCells(Vector2Int center, int brushRadius)
    {
        if (brushRadius <= 0)
        {
            yield return center;
            yield break;
        }

        for (int dx = -brushRadius; dx <= brushRadius; dx++)
        {
            for (int dz = -brushRadius; dz <= brushRadius; dz++)
            {
                if (useCircularPathBrush && dx * dx + dz * dz > brushRadius * brushRadius)
                    continue;

                yield return new Vector2Int(center.x + dx, center.y + dz);
            }
        }
    }

    void CollectGridRoadWallRuns(
        BlockType roadType,
        bool[,] hasCover,
        HashSet<Vector2Int> rangeCells,
        int minRunLength,
        int[,] cellWeightGrid,
        Dictionary<Vector2Int, RoadWallRunCandidate> bestByCell)
    {
        int[] wallDx = { 0, 1, 0, -1 };
        int[] wallDz = { 1, 0, -1, 0 };

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                Vector2Int cell = new(x, z);
                if (!rangeCells.Contains(cell))
                    continue;

                for (int wallDir = 0; wallDir < 4; wallDir++)
                {
                    int wdx = wallDx[wallDir];
                    int wdz = wallDz[wallDir];
                    if (!IsRoadWallHugCell(x, z, wdx, wdz, roadType, hasCover))
                        continue;

                    int runDx = wallDx[(wallDir + 1) % 4];
                    int runDz = wallDz[(wallDir + 1) % 4];
                    int prevX = x - runDx;
                    int prevZ = z - runDz;
                    if (IsInsideMap(prevX, prevZ)
                        && IsRoadWallHugCell(prevX, prevZ, wdx, wdz, roadType, hasCover))
                        continue;

                    List<Vector2Int> run = BuildRoadWallRun(
                        cell, runDx, runDz, wdx, wdz, roadType, hasCover, rangeCells);
                    if (run.Count < minRunLength)
                        continue;

                    Vector2Int mid = run[run.Count / 2];
                    RegisterRoadWallRunCandidate(bestByCell, mid, run.Count, cellWeightGrid);
                }
            }
        }
    }

    List<Vector2Int> BuildRoadWallRun(
        Vector2Int start,
        int runDx,
        int runDz,
        int wdx,
        int wdz,
        BlockType roadType,
        bool[,] hasCover,
        HashSet<Vector2Int> rangeCells)
    {
        var run = new List<Vector2Int> { start };
        Vector2Int cur = start;

        while (true)
        {
            Vector2Int next = new(cur.x + runDx, cur.y + runDz);
            if (!rangeCells.Contains(next))
                break;
            if (!IsRoadWallHugCell(next.x, next.y, wdx, wdz, roadType, hasCover))
                break;

            run.Add(next);
            cur = next;
        }

        return run;
    }

    void CollectHighWeightRoadWallFallback(
        BlockType roadType,
        bool[,] hasCover,
        HashSet<Vector2Int> rangeCells,
        int[,] cellWeightGrid,
        Dictionary<Vector2Int, RoadWallRunCandidate> bestByCell)
    {
        int[] wallDx = { 0, 1, 0, -1 };
        int[] wallDz = { 1, 0, -1, 0 };

        foreach (Vector2Int cell in rangeCells)
        {
            if (hasCover[cell.x, cell.y])
                continue;
            if (cellTypes[cell.x, cell.y] != roadType)
                continue;

            int weight = cellWeightGrid[cell.x, cell.y];
            if (weight < coverMinOpenness)
                continue;

            bool hugsWall = false;
            for (int wallDir = 0; wallDir < 4; wallDir++)
            {
                if (!IsRoadWallHugCell(cell.x, cell.y, wallDx[wallDir], wallDz[wallDir], roadType, hasCover))
                    continue;

                hugsWall = true;
                break;
            }

            if (!hugsWall)
                continue;

            RegisterRoadWallRunCandidate(bestByCell, cell, 0, cellWeightGrid);
        }
    }

    void RegisterRoadWallRunCandidate(
        Dictionary<Vector2Int, RoadWallRunCandidate> bestByCell,
        Vector2Int cell,
        int runLength,
        int[,] cellWeightGrid)
    {
        int weight = cellWeightGrid[cell.x, cell.y];
        var candidate = new RoadWallRunCandidate(cell, runLength, weight);

        if (!bestByCell.TryGetValue(cell, out RoadWallRunCandidate existing))
        {
            bestByCell[cell] = candidate;
            return;
        }

        if (candidate.RunLength > existing.RunLength
            || (candidate.RunLength == existing.RunLength && candidate.Weight > existing.Weight))
            bestByCell[cell] = candidate;
    }

    bool IsRoadWallHugCell(int x, int z, int wdx, int wdz, BlockType roadType, bool[,] hasCover)
    {
        if (!IsInsideMap(x, z))
            return false;
        if (cellTypes[x, z] != roadType)
            return false;
        if (hasCover[x, z])
            return false;
        if (!IsOutwardBlockingCell(x + wdx, z + wdz))
            return false;

        int runDx = wdz;
        int runDz = -wdx;
        return IsRoadCorridorPassable(x + runDx, z + runDz, hasCover)
            || IsRoadCorridorPassable(x - runDx, z - runDz, hasCover);
    }

    bool IsRoadCorridorPassable(int x, int z, bool[,] hasCover)
    {
        if (!IsInsideMap(x, z))
            return false;
        if (hasCover[x, z])
            return false;

        return IsOutsideWalkableType(cellTypes[x, z]);
    }

    bool IsOutwardBlockingCell(int x, int z)
    {
        if (!IsInsideMap(x, z))
            return true;

        BlockType type = cellTypes[x, z];
        return type is BlockType.Wall or BlockType.Empty;
    }

    void SortRoadWallRunCandidates(List<RoadWallRunCandidate> candidates, int[,] cellWeightGrid)
    {
        candidates.Sort((a, b) =>
        {
            if (a.RunLength != b.RunLength)
                return b.RunLength.CompareTo(a.RunLength);

            if (a.Weight != b.Weight)
                return b.Weight.CompareTo(a.Weight);

            if (a.Cell.x != b.Cell.x)
                return a.Cell.x.CompareTo(b.Cell.x);

            return a.Cell.y.CompareTo(b.Cell.y);
        });

        ApplyRunLengthTieShuffle(candidates);
    }

    void ApplyRunLengthTieShuffle(List<RoadWallRunCandidate> candidates)
    {
        float bias = Mathf.Clamp01(coverRandomBias);
        if (bias <= 0f || candidates.Count < 2)
            return;

        int topRun = candidates[0].RunLength;
        int tieCount = 1;
        while (tieCount < candidates.Count && candidates[tieCount].RunLength == topRun)
            tieCount++;

        if (tieCount < 2)
            return;

        int shuffleCount = Mathf.Max(2, Mathf.RoundToInt(tieCount * bias));
        shuffleCount = Mathf.Min(shuffleCount, tieCount);
        ShuffleListRange(candidates, 0, shuffleCount);
    }

    void RecomputeCellWeightsFromRays()
    {
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                int weight = ComputeCellWeightFromRays(x, z);
                cellWeights[x, z] = weight;

                BlockComponent floor = floorInstances[x, z];
                if (floor != null)
                    floor.weight = weight;
            }
        }
    }

    HashSet<Vector2Int> CollectAllEntranceBorderCells()
    {
        HashSet<Vector2Int> allEntrances = new();
        CollectZoneEntranceCells(allEntrances, null);
        return allEntrances;
    }

    // Клетки без меток веса: внутри зоны у выхода + Main/Link на входе в Site/Neutral/Spawn.
    HashSet<Vector2Int> CollectWeightLabelHiddenCells()
    {
        HashSet<Vector2Int> hidden = new();
        CollectZoneEntranceCells(hidden, hidden);
        return hidden;
    }

    void CollectZoneEntranceCells(HashSet<Vector2Int> insideExits, HashSet<Vector2Int> roadEntrances)
    {
        BlockType[] zoneTypes =
        {
            BlockType.Site,
            BlockType.Neutral,
            BlockType.Spawn
        };

        int[] dx = { 1, -1, 0, 0 };
        int[] dz = { 0, 0, 1, -1 };

        foreach (BlockType zoneType in zoneTypes)
        {
            if (zoneType == BlockType.Neutral && !generateNeutralZone)
                continue;

            List<ZoneRegion> regions = ExtractRegionsForType(zoneType);
            foreach (ZoneRegion region in regions)
            {
                HashSet<Vector2Int> regionSet = new(region.Cells.Count);
                foreach (Vector2Int cell in region.Cells)
                    regionSet.Add(cell);

                if (insideExits != null)
                {
                    foreach (Vector2Int inside in CollectEntranceBorderCells(region, regionSet))
                        insideExits.Add(inside);
                }

                if (roadEntrances == null)
                    continue;

                foreach (Vector2Int cell in region.Cells)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        int nx = cell.x + dx[i];
                        int nz = cell.y + dz[i];
                        if (!IsInsideMap(nx, nz))
                            continue;

                        Vector2Int outside = new(nx, nz);
                        if (regionSet.Contains(outside))
                            continue;

                        BlockType outsideType = cellTypes[nx, nz];
                        if (outsideType is BlockType.Main or BlockType.Link)
                            roadEntrances.Add(outside);
                    }
                }
            }
        }
    }

    // Проходима для луча: пол/зона; стоп на Wall, Empty, край карты и уже поставленном укрытии.
    bool IsWeightRayPassable(int x, int z)
    {
        if (!IsInsideMap(x, z))
            return false;

        if (coverOccupancy != null && coverOccupancy[x, z])
            return false;

        BlockType type = cellTypes[x, z];
        return type is not (BlockType.Wall or BlockType.Empty or BlockType.None);
    }

    // Число клеток, через которые прошёл луч в одну сторону, до первой стены.
    int CastWeightRay(int x, int z, int dx, int dz)
    {
        int passed = 0;
        int nx = x + dx;
        int nz = z + dz;

        while (IsWeightRayPassable(nx, nz))
        {
            passed++;
            nx += dx;
            nz += dz;
        }

        return passed;
    }

    int ComputeCellWeightFromRays(int x, int z)
    {
        if (!IsWeightRayPassable(x, z))
            return 0;

        int sum = 0;
        for (int dir = 0; dir < WeightRayDirectionCount; dir++)
            sum += CastWeightRay(x, z, WeightRayDx[dir], WeightRayDz[dir]);

        return sum / WeightRayDirectionCount;
    }

    void TryPlaceCoversForZoneType(BlockType zoneType, bool[,] hasCover, int[,] cellWeightGrid)
    {
        if (!IsZoneCoverable(zoneType))
            return;

        int limit = zoneType == BlockType.Site ? maxCoversPerSite : maxCoversPerOtherZone;
        if (limit <= 0)
            return;

        List<ZoneRegion> regions = ExtractRegionsForType(zoneType);
        foreach (ZoneRegion region in regions)
            PlaceCoversInRegion(region, hasCover, cellWeightGrid, limit);
    }

    bool IsZoneCoverable(BlockType zoneType)
    {
        return zoneType switch
        {
            BlockType.Site    => (coverableZones & CoverableZones.Site) != 0,
            BlockType.Neutral => (coverableZones & CoverableZones.Neutral) != 0 && generateNeutralZone,
            BlockType.Spawn   => (coverableZones & CoverableZones.Spawn) != 0,
            _                 => false
        };
    }

    void PlaceCoversInRegion(ZoneRegion region, bool[,] hasCover, int[,] cellWeightGrid, int limit)
    {
        if (region.Cells.Count < 2 || limit <= 0)
            return;

        HashSet<Vector2Int> regionSet = new(region.Cells.Count);
        foreach (Vector2Int cell in region.Cells)
            regionSet.Add(cell);

        List<Vector2Int> entranceCells = CollectEntranceBorderCells(region, regionSet);
        Vector2 regionCentroid = ComputeRegionCentroid(region);
        int placedCount = 0;

        while (placedCount < limit)
        {
            int maxWeightInRegion = 0;
            var candidates = new List<Vector2Int>();
            foreach (Vector2Int cell in region.Cells)
            {
                if (hasCover[cell.x, cell.y]) continue;
                if (entranceCells.Contains(cell)) continue;

                int w = cellWeights[cell.x, cell.y];
                if (w <= 0) continue;

                maxWeightInRegion = Mathf.Max(maxWeightInRegion, w);
                candidates.Add(cell);
            }

            if (candidates.Count == 0)
                break;

            int effectiveMinOpen = Mathf.Max(1, Mathf.Min(coverMinOpenness, maxWeightInRegion));
            candidates.RemoveAll(c => cellWeights[c.x, c.y] < effectiveMinOpen);
            if (candidates.Count == 0)
                break;

            SortZoneCoverCandidates(candidates, cellWeights, regionCentroid);

            bool placedThisRound = false;
            foreach (Vector2Int cell in candidates)
            {
                if (cellWeights[cell.x, cell.y] < effectiveMinOpen)
                    break;
                if (!CanPlaceWithoutBlockingEntrances(cell, hasCover, regionSet, entranceCells))
                    continue;

                PlaceCoverAt(cell);
                hasCover[cell.x, cell.y] = true;
                RecomputeCellWeightsFromRays();
                placedCount++;
                placedThisRound = true;
                break;
            }

            if (!placedThisRound)
                break;
        }
    }

    // Не ломаем проход: в каждом 3×3 вокруг клетки входа остаётся связность «снаружи ↔ внутри зоны».
    bool CanPlaceWithoutBlockingEntrances(
        Vector2Int candidate,
        bool[,] hasCover,
        HashSet<Vector2Int> regionSet,
        List<Vector2Int> entranceCells)
    {
        if (entranceCells.Count == 0)
            return true;

        foreach (Vector2Int entrance in entranceCells)
        {
            if (ChebyshevDistance(candidate, entrance) > EntrancePassageWindowRadius)
                continue;

            if (!EntranceWindowHasPassage(entrance, regionSet, hasCover, candidate))
                return false;
        }

        return true;
    }

    bool EntranceWindowHasPassage(
        Vector2Int entranceCell,
        HashSet<Vector2Int> regionSet,
        bool[,] hasCover,
        Vector2Int additionalCover)
    {
        HashSet<Vector2Int> window = CollectPassageWindow(entranceCell, regionSet, hasCover, additionalCover);
        if (window.Count == 0)
            return true;

        List<Vector2Int> outsideSeeds = new();
        List<Vector2Int> insideSeeds = new();

        foreach (Vector2Int cell in window)
        {
            if (regionSet.Contains(cell))
                insideSeeds.Add(cell);
            else
                outsideSeeds.Add(cell);
        }

        if (outsideSeeds.Count == 0 || insideSeeds.Count == 0)
            return true;

        HashSet<Vector2Int> visited = new();
        Queue<Vector2Int> queue = new();

        foreach (Vector2Int seed in outsideSeeds)
        {
            queue.Enqueue(seed);
            visited.Add(seed);
        }

        while (queue.Count > 0)
        {
            Vector2Int cur = queue.Dequeue();
            if (regionSet.Contains(cur))
                return true;

            int[] dx = { 1, -1, 0, 0 };
            int[] dz = { 0, 0, 1, -1 };
            for (int i = 0; i < 4; i++)
            {
                Vector2Int nb = new Vector2Int(cur.x + dx[i], cur.y + dz[i]);
                if (!window.Contains(nb) || visited.Contains(nb)) continue;
                visited.Add(nb);
                queue.Enqueue(nb);
            }
        }

        return false;
    }

    HashSet<Vector2Int> CollectPassageWindow(
        Vector2Int center,
        HashSet<Vector2Int> regionSet,
        bool[,] hasCover,
        Vector2Int additionalCover)
    {
        HashSet<Vector2Int> window = new();
        for (int ddx = -EntrancePassageWindowRadius; ddx <= EntrancePassageWindowRadius; ddx++)
        {
            for (int ddz = -EntrancePassageWindowRadius; ddz <= EntrancePassageWindowRadius; ddz++)
            {
                Vector2Int c = new Vector2Int(center.x + ddx, center.y + ddz);
                if (!IsPassageWalkable(c, regionSet, hasCover, additionalCover)) continue;
                window.Add(c);
            }
        }
        return window;
    }

    bool IsPassageWalkable(
        Vector2Int cell,
        HashSet<Vector2Int> regionSet,
        bool[,] hasCover,
        Vector2Int additionalCover)
    {
        if (!IsInsideMap(cell.x, cell.y)) return false;
        if (cell == additionalCover || hasCover[cell.x, cell.y]) return false;
        if (cellTypes[cell.x, cell.y] == BlockType.Wall) return false;

        if (regionSet.Contains(cell))
            return true;

        return IsOutsideWalkableType(cellTypes[cell.x, cell.y]);
    }

    static bool IsOutsideWalkableType(BlockType type) =>
        type is BlockType.Main or BlockType.Link or BlockType.Road
            or BlockType.Neutral or BlockType.Spawn or BlockType.Site
            or BlockType.Room or BlockType.Pocket or BlockType.Floor;

    static int ChebyshevDistance(Vector2Int a, Vector2Int b) =>
        Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));

    static Vector2 ComputeRegionCentroid(ZoneRegion region)
    {
        long sumX = 0;
        long sumY = 0;
        foreach (Vector2Int cell in region.Cells)
        {
            sumX += cell.x;
            sumY += cell.y;
        }

        int n = region.Cells.Count;
        return new Vector2(sumX / (float)n, sumY / (float)n);
    }

    void SortZoneCoverCandidates(List<Vector2Int> cells, int[,] cellWeightGrid, Vector2 centroid)
    {
        cells.Sort((a, b) =>
        {
            int oa = cellWeightGrid[a.x, a.y];
            int ob = cellWeightGrid[b.x, b.y];
            if (oa != ob) return ob.CompareTo(oa);

            float da = (a.x - centroid.x) * (a.x - centroid.x) + (a.y - centroid.y) * (a.y - centroid.y);
            float db = (b.x - centroid.x) * (b.x - centroid.x) + (b.y - centroid.y) * (b.y - centroid.y);
            if (!Mathf.Approximately(da, db)) return da.CompareTo(db);

            if (a.x != b.x) return a.x.CompareTo(b.x);
            return a.y.CompareTo(b.y);
        });

        ApplyWeightTieShuffle(cells, cellWeightGrid);
    }

    void SortCellsByWeight(List<Vector2Int> cells, int[,] cellWeightGrid)
    {
        cells.Sort((a, b) =>
        {
            int wa = cellWeightGrid[a.x, a.y];
            int wb = cellWeightGrid[b.x, b.y];
            if (wa != wb) return wb.CompareTo(wa);
            if (a.x != b.x) return a.x.CompareTo(b.x);
            return a.y.CompareTo(b.y);
        });

        ApplyWeightTieShuffle(cells, cellWeightGrid);
    }

    void ApplyWeightTieShuffle(List<Vector2Int> cells, int[,] cellWeightGrid)
    {
        float bias = Mathf.Clamp01(coverRandomBias);
        if (bias <= 0f || cells.Count < 2) return;

        int topOpen = cellWeightGrid[cells[0].x, cells[0].y];
        int tieCount = 1;
        while (tieCount < cells.Count && cellWeightGrid[cells[tieCount].x, cells[tieCount].y] == topOpen)
            tieCount++;

        if (tieCount < 2) return;

        int shuffleCount = Mathf.Max(2, Mathf.RoundToInt(tieCount * bias));
        shuffleCount = Mathf.Min(shuffleCount, tieCount);
        ShuffleListRange(cells, 0, shuffleCount);
    }

    static void ShuffleListRange<T>(List<T> list, int start, int count)
    {
        int end = start + count - 1;
        for (int i = end; i > start; i--)
        {
            int j = Random.Range(start, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    bool IsSingleSidedRoadWallCover(int x, int z) =>
        CountRoadCoverWallSides(x, z) == 1;

    int CountRoadCoverWallSides(int x, int z)
    {
        int[] dx = { 1, -1, 0, 0 };
        int[] dz = { 0, 0, 1, -1 };
        int wallSides = 0;

        for (int i = 0; i < 4; i++)
        {
            int nx = x + dx[i];
            int nz = z + dz[i];

            if (!IsInsideMap(nx, nz))
            {
                wallSides++;
                continue;
            }

            BlockType nt = cellTypes[nx, nz];
            if (nt is BlockType.Wall or BlockType.Empty)
                wallSides++;
        }

        return wallSides;
    }

    List<Vector2Int> CollectEntranceBorderCells(ZoneRegion region, HashSet<Vector2Int> regionSet)
    {
        List<Vector2Int> entrances = new();
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
                if (outside is BlockType.Main or BlockType.Link or BlockType.Room
                    or BlockType.Site or BlockType.Neutral or BlockType.Spawn)
                {
                    if (!regionSet.Contains(new Vector2Int(nx, nz)))
                    {
                        entrances.Add(cell);
                        break;
                    }
                }
            }
        }

        return entrances;
    }

    bool HasCoverWithinSpacing(Vector2Int cell, List<Vector2Int> placed, int spacing)
    {
        foreach (Vector2Int p in placed)
        {
            if (ChebyshevDistance(cell, p) < spacing)
                return true;
        }
        return false;
    }

    static void ShuffleList<T>(List<T> list)
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
            Instantiate(coverPrefab, pos, Quaternion.identity, geometryRoot);
        }
    }

    void VisualizeFloorWeightLabels()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        HashSet<Vector2Int> hiddenCells = CollectWeightLabelHiddenCells();

        int minDisplay = int.MaxValue;
        int maxDisplay = int.MinValue;
        CollectFloorLabelDisplayRange(hiddenCells, ref minDisplay, ref maxDisplay);
        if (minDisplay == int.MaxValue)
            return;

        GameObject labelsRoot = new GameObject("WeightLabels");
        labelsRoot.transform.SetParent(geometryRoot, false);

        float y = blockSize * weightLabelHeight;
        float charSize = weightLabelCharacterSize * blockSize;

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (!TryGetFloorLabelDisplay(x, z, hiddenCells, out int display))
                    continue;

                float t = DisplayToNormalized(display, minDisplay, maxDisplay);
                Color color = Color.Lerp(WeightLabelColorLow, WeightLabelColorHigh, t);

                GameObject labelGo = new GameObject($"W_{x}_{z}_{display}");
                labelGo.transform.SetParent(labelsRoot.transform, false);
                labelGo.transform.position = new Vector3(x * blockSize, y, z * blockSize);
                labelGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

                labelGo.AddComponent<TextMesh>();
                FloorWeightLabel label = labelGo.AddComponent<FloorWeightLabel>();
                label.Configure(display.ToString(), font, charSize, color, weightLabelOutlineWidth);
            }
        }
    }

    void CollectFloorLabelDisplayRange(HashSet<Vector2Int> hiddenCells, ref int minDisplay, ref int maxDisplay)
    {
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (!TryGetFloorLabelDisplay(x, z, hiddenCells, out int display))
                    continue;

                minDisplay = Mathf.Min(minDisplay, display);
                maxDisplay = Mathf.Max(maxDisplay, display);
            }
        }
    }

    bool TryGetFloorLabelDisplay(int x, int z, HashSet<Vector2Int> hiddenCells, out int display)
    {
        display = 0;
        if (floorInstances[x, z] == null)
            return false;

        Vector2Int cell = new(x, z);
        if (hiddenCells.Contains(cell))
            return false;

        BlockType type = cellTypes[x, z];
        if (type is BlockType.Empty or BlockType.Wall or BlockType.None)
            return false;

        display = cellWeights[x, z];
        return true;
    }

    static float DisplayToNormalized(int display, int minDisplay, int maxDisplay)
    {
        if (maxDisplay <= minDisplay)
            return 1f;
        return Mathf.Clamp01((display - minDisplay) / (float)(maxDisplay - minDisplay));
    }
}
