using System.Collections.Generic;
using UnityEngine;

public enum EntranceFillMode
{
    Wall,
    Cover
}

public partial class MapGenerator
{
    // Стороны прямоугольной зоны для группировки граничных клеток.
    private enum ZoneSide
    {
        Top,    // z = Max.y, наружу +Z
        Bottom, // z = Min.y, наружу -Z
        Left,   // x = Min.x, наружу -X
        Right   // x = Max.x, наружу +X
    }

    // Кандидат на вход: внутренняя клетка зоны и соответствующая внешняя клетка-проход.
    private struct EntranceCandidate
    {
        public Vector2Int Inside;   // клетка внутри зоны (граничная)
        public Vector2Int Outside;  // соседняя клетка снаружи зоны (дорога)
        public ZoneSide Side;
        public BlockType OutsideType; // Main или Link — нужно для разделения сегментов по типу дороги
        // Координата вдоль стороны (x для Top/Bottom, z для Left/Right).
        public int AlongAxis;
    }

    // Окружает Spawn, Site, Neutral и Room стенами с ограниченными входами.
    // Pocket (открытые галереи) НЕ обносятся стенами — они открыты со стороны дороги.
    void ShapeZoneEnclosures()
    {
        if (!enableZoneEnclosures)
            return;

        ShapeZoneTypeEnclosures(BlockType.Spawn);
        ShapeZoneTypeEnclosures(BlockType.Site);
        if (generateNeutralZone)
            ShapeZoneTypeEnclosures(BlockType.Neutral);
        if (enableRooms)
            ShapeZoneTypeEnclosures(BlockType.Room); // только закрытые Room, не Pocket
    }

    void ShapeZoneTypeEnclosures(BlockType zoneType)
    {
        List<ZoneRegion> regions = ExtractRegionsForType(zoneType);
        foreach (ZoneRegion region in regions)
        {
            EncloseRegion(region);

            // Pre-site Room: дополнительно ставим стену между комнатой и сайтом,
            // оставляя ровно 1 клетку входа — как Hookah → B Site в Valorant.
            if (zoneType == BlockType.Room)
                SealRoomSiteBoundary(region);
        }
    }

    void EncloseRegion(ZoneRegion region)
    {
        // Собираем все дорожные кандидаты во входы по сторонам зоны. Empty-соседи позже
        // автоматически превратятся в стены при BuildOuterWalls — здесь их трогать не нужно.
        Dictionary<ZoneSide, List<EntranceCandidate>> roadCandidatesBySide = new()
        {
            { ZoneSide.Top, new List<EntranceCandidate>() },
            { ZoneSide.Bottom, new List<EntranceCandidate>() },
            { ZoneSide.Left, new List<EntranceCandidate>() },
            { ZoneSide.Right, new List<EntranceCandidate>() }
        };

        HashSet<Vector2Int> regionSet = new(region.Cells.Count);
        foreach (Vector2Int cell in region.Cells)
            regionSet.Add(cell);

        foreach (Vector2Int cell in region.Cells)
        {
            TryClassifyNeighbor(cell, new Vector2Int(0, 1), ZoneSide.Top, regionSet, roadCandidatesBySide);
            TryClassifyNeighbor(cell, new Vector2Int(0, -1), ZoneSide.Bottom, regionSet, roadCandidatesBySide);
            TryClassifyNeighbor(cell, new Vector2Int(-1, 0), ZoneSide.Left, regionSet, roadCandidatesBySide);
            TryClassifyNeighbor(cell, new Vector2Int(1, 0), ZoneSide.Right, regionSet, roadCandidatesBySide);
        }

        // Дорожные границы — выбираем входы и закрываем излишки стеной/укрытием.
        foreach (KeyValuePair<ZoneSide, List<EntranceCandidate>> entry in roadCandidatesBySide)
            ResolveEntrancesOnSide(entry.Value);
    }

    void TryClassifyNeighbor(
        Vector2Int insideCell,
        Vector2Int direction,
        ZoneSide side,
        HashSet<Vector2Int> regionSet,
        Dictionary<ZoneSide, List<EntranceCandidate>> roadCandidatesBySide)
    {
        Vector2Int outside = insideCell + direction;
        if (!IsInsideMap(outside.x, outside.y))
            return;

        // Если сосед — клетка той же зоны (изогнутая зона) — это не граница, пропускаем.
        if (regionSet.Contains(outside))
            return;

        BlockType outsideType = cellTypes[outside.x, outside.y];
        if (outsideType != BlockType.Main && outsideType != BlockType.Link)
            return;

        int along = (side == ZoneSide.Top || side == ZoneSide.Bottom) ? outside.x : outside.y;
        roadCandidatesBySide[side].Add(new EntranceCandidate
        {
            Inside = insideCell,
            Outside = outside,
            Side = side,
            OutsideType = outsideType,
            AlongAxis = along
        });
    }

    void ResolveEntrancesOnSide(List<EntranceCandidate> candidates)
    {
        if (candidates.Count == 0)
            return;

        // Удалим дубликаты по Outside (одна и та же дорожная клетка может граничить сразу с двумя соседними внутренними клетками,
        // но логически это один "проход").
        Dictionary<Vector2Int, EntranceCandidate> uniqueByOutside = new();
        foreach (EntranceCandidate candidate in candidates)
        {
            if (!uniqueByOutside.ContainsKey(candidate.Outside))
                uniqueByOutside[candidate.Outside] = candidate;
        }

        List<EntranceCandidate> sorted = new(uniqueByOutside.Values);
        sorted.Sort((a, b) => a.AlongAxis.CompareTo(b.AlongAxis));

        // Группируем подряд идущие клетки в сегменты. Разделяем по типу дороги:
        // Main и Link — это разные тактические "входы" (например main с фланга и link от нейтрала),
        // и каждый должен иметь собственный гарантированный проход.
        List<List<EntranceCandidate>> segments = new();
        List<EntranceCandidate> current = new() { sorted[0] };
        for (int i = 1; i < sorted.Count; i++)
        {
            bool isAdjacent = sorted[i].AlongAxis == sorted[i - 1].AlongAxis + 1;
            bool sameType = sorted[i].OutsideType == sorted[i - 1].OutsideType;
            if (isAdjacent && sameType)
            {
                current.Add(sorted[i]);
            }
            else
            {
                segments.Add(current);
                current = new List<EntranceCandidate> { sorted[i] };
            }
        }
        segments.Add(current);

        foreach (List<EntranceCandidate> segment in segments)
            ApplyEntrancePolicyToSegment(segment);
    }

    void ApplyEntrancePolicyToSegment(List<EntranceCandidate> segment)
    {
        int maxWidth = Mathf.Max(1, maxEntranceWidth);

        // Короткий проход — оставляем как есть (естественный вход).
        if (segment.Count <= maxWidth)
            return;

        // Длинный проход — оставляем maxWidth клеток по центру, остальное закрываем.
        int keep = maxWidth;
        int totalToBlock = segment.Count - keep;
        int blockOnLeft = totalToBlock / 2;
        int keepEnd = blockOnLeft + keep; // [blockOnLeft, keepEnd)

        for (int i = 0; i < segment.Count; i++)
        {
            if (i >= blockOnLeft && i < keepEnd)
                continue;

            EntranceCandidate candidate = segment[i];
            BlockEntranceCell(candidate);
        }
    }

    void BlockEntranceCell(EntranceCandidate candidate)
    {
        // Выбираем способ блокировки: глухая стена или укрытие.
        // Стена: гарантированный choke point. Укрытие: тактическое прикрытие, но проходимо обходом.
        if (entranceFillMode == EntranceFillMode.Wall || coverPrefab == null)
        {
            TryMarkBlock(candidate.Outside.x, candidate.Outside.y, BlockType.Wall, trackZone: false);
            return;
        }

        // Превращаем клетку в стену (чтобы перекрыть линию visibility) только если её ещё не отметили.
        // Для режима Cover оставляем тип дороги, но вешаем сверху префаб укрытия.
        Vector3 worldPos = new Vector3(candidate.Outside.x * blockSize, blockSize, candidate.Outside.y * blockSize);
        Instantiate(coverPrefab, worldPos, Quaternion.identity, transform);
    }

    // ───── Стена между Room и Site ────────────────────────────────────────────
    // Для pre-site Room: находит все Room-клетки, смежные с Site, и ставит Wall
    // на каждую из них КРОМЕ одной центральной — это вход из Room в Site (1 клетка).
    // Нельзя стенить со стороны Site (Site защищён), поэтому стены ставим со стороны Room.
    void SealRoomSiteBoundary(ZoneRegion region)
    {
        HashSet<Vector2Int> regionSet = new(region.Cells.Count);
        foreach (Vector2Int cell in region.Cells) regionSet.Add(cell);

        int[] dx = { 0,  0, -1, 1 };
        int[] dz = { 1, -1,  0, 0 };
        bool[] horizontal = { true, true, false, false }; // Top/Bottom → сортируем по X; Left/Right → по Z

        // Группируем Room-клетки у Site-границы по стороне.
        Dictionary<int, List<Vector2Int>> sideGroups = new()
        {
            { 0, new() }, // Top
            { 1, new() }, // Bottom
            { 2, new() }, // Left
            { 3, new() }  // Right
        };

        foreach (Vector2Int cell in region.Cells)
        {
            for (int i = 0; i < 4; i++)
            {
                Vector2Int outside = new Vector2Int(cell.x + dx[i], cell.y + dz[i]);
                if (!IsInsideMap(outside.x, outside.y)) continue;
                if (regionSet.Contains(outside)) continue;
                if (cellTypes[outside.x, outside.y] == BlockType.Site)
                {
                    sideGroups[i].Add(cell);
                    break; // одна сторона на клетку — первое найденное направление к Site
                }
            }
        }

        foreach (var entry in sideGroups)
        {
            List<Vector2Int> cells = entry.Value;
            if (cells.Count == 0) continue;

            bool isHoriz = horizontal[entry.Key];
            cells.Sort((a, b) => isHoriz ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));

            // Разбиваем на связные сегменты подряд идущих клеток.
            List<List<Vector2Int>> segments = new();
            List<Vector2Int> current = new() { cells[0] };
            for (int i = 1; i < cells.Count; i++)
            {
                int prev = isHoriz ? cells[i - 1].x : cells[i - 1].y;
                int curr = isHoriz ? cells[i].x     : cells[i].y;
                if (curr == prev + 1) current.Add(cells[i]);
                else { segments.Add(current); current = new List<Vector2Int> { cells[i] }; }
            }
            segments.Add(current);

            foreach (List<Vector2Int> segment in segments)
            {
                if (segment.Count <= 1) continue; // уже 1 клетка = вход, ничего не делаем

                // Оставляем 1 клетку по центру как вход, остальные → Wall.
                int entranceIdx = segment.Count / 2;
                for (int i = 0; i < segment.Count; i++)
                {
                    if (i == entranceIdx) continue;
                    // Стеним Room-клетку (не Site-клетку — та защищена).
                    TryMarkBlock(segment[i].x, segment[i].y, BlockType.Wall, trackZone: false);
                }
            }
        }
    }
}
