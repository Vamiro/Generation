using System.Collections.Generic;
using UnityEngine;

public partial class MapGenerator
{
    // ─────────────────────────────────────────────────────────────────────────
    // Tactical slots: после PlaceCovers строим Hold/Peek-позиции у каждого
    // cover-блока. Бот идёт не в случайную точку зоны, а в осмысленную
    // тактическую точку, где у него есть бок-укрытие и facingDir в сторону угрозы.
    //
    // Правила (документация в Architecture.md, §5.2):
    //  1) Кандидат-клетка = клетка с типом одной из зональных Floor-категорий,
    //     соседствующая с хотя бы одним cover-блоком (coverOccupancy[nx,nz] == true).
    //  2) Сама клетка не должна быть cover.
    //  3) Тип слота определяется типом клетки:
    //        Site/Neutral/Room → HoldDefender
    //        Main/Link         → PeekAttacker
    //  4) Поза = центр клетки + случайный джиттер в пределах slotJitter*blockSize
    //     (чтобы боты не вставали ровно в узлы сетки).
    //  5) facingDir = усреднённое направление "от cover-а" + случайный yaw-сдвиг
    //     в пределах facingYawJitter градусов (чтобы взгляд не снэпался на 8 направлений).
    //  6) coverScore = число сторон 4-окрестности, закрытых cover/wall.
    //
    // Случайность детерминирована: Random.InitState(currentGenerationSeed) сделан
    // в MapGenerator.GenerateMap() ДО вызова BuildTacticalSlots.
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Tactical slots (Hold/Peek позиции у укрытий)")]
    [Range(0f, 0.45f)]
    [Tooltip("Случайный сдвиг позиции слота от центра клетки (в долях blockSize). 0 = боты стоят строго в узлах сетки. ~0.2 = живая, не-решёточная расстановка.")]
    [SerializeField] private float slotJitter = 0.2f;

    [Range(0f, 25f)]
    [Tooltip("Случайный сдвиг facingDir (градусы, симметричный). 0 = взгляд строго по оси cover-блока. ~10° = живой взгляд.")]
    [SerializeField] private float facingYawJitter = 8f;

    void BuildTacticalSlots()
    {
        if (coverOccupancy == null) return;
        MapManager mapManager = MapManager.Instance;
        if (mapManager == null) return;

        // На всякий случай чистим: при R-регенерации зоны пересоздаются с нуля,
        // но если когда-то понадобится повторный вызов на той же сцене — будет безопасно.
        foreach (MapZoneComponent zone in mapManager.Zones)
        {
            if (zone != null) zone.ClearTacticalSlots();
        }

        MapZoneComponent[,] cellToZone = BuildCellToZoneIndex(mapManager);

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (coverOccupancy[x, z]) continue;

                BlockType t = cellTypes[x, z];
                if (!IsSlotEligibleCellType(t)) continue;

                MapZoneComponent zone = cellToZone[x, z];
                if (zone == null) continue;

                // Считаем cover/wall в 4-окрестности и копим нормаль facingDir.
                int coverScore = 0;
                Vector2Int coverNormal = Vector2Int.zero;
                int coverNeighbours = 0;

                int[] dx = { 1, -1, 0, 0 };
                int[] dz = { 0, 0, 1, -1 };
                for (int i = 0; i < 4; i++)
                {
                    int nx = x + dx[i];
                    int nz = z + dz[i];

                    bool isWall = IsCellWall(nx, nz);
                    bool isCover = IsCellOccupiedByCover(nx, nz);

                    if (isWall || isCover) coverScore++;
                    if (isCover)
                    {
                        // facingDir = ОТ cover-а → бот стоит ЗА cover, смотрит наружу через него.
                        coverNormal += new Vector2Int(-dx[i], -dz[i]);
                        coverNeighbours++;
                    }
                }

                if (coverNeighbours == 0) continue; // только стен мало, нужен реальный cover-блок рядом

                // Базовая позиция = центр клетки. Добавляем независимый джиттер по X и Z
                // в пределах ±slotJitter*blockSize — этого достаточно, чтобы убрать
                // визуальный эффект "решётки" без сложной геометрии прижатия к cover-у.
                Vector3 cellWorld = new Vector3(x * blockSize, 0f, z * blockSize);
                float jx = (Random.value - 0.5f) * 2f * slotJitter * blockSize;
                float jz = (Random.value - 0.5f) * 2f * slotJitter * blockSize;
                Vector3 worldPos = cellWorld + new Vector3(jx, 0f, jz);

                Vector3 facing = ComputeFacingDir(coverNormal, x, z, zone);

                TacticalSlot slot = new TacticalSlot
                {
                    worldPos    = worldPos,
                    facingDir   = facing,
                    coverScore  = coverScore,
                    kind        = SlotKindForCellType(t),
                };
                zone.AddTacticalSlot(slot);
            }
        }
    }

    // Какие типы клеток могут стать слотом. Spawn исключаем (там тактика не нужна),
    // Pocket — это, по сути, ниша в стене, обычно слишком тесная для бота.
    static bool IsSlotEligibleCellType(BlockType t)
    {
        return t == BlockType.Site
            || t == BlockType.Neutral
            || t == BlockType.Room
            || t == BlockType.Main
            || t == BlockType.Link;
    }

    static TacticalSlotKind SlotKindForCellType(BlockType t)
    {
        return (t == BlockType.Main || t == BlockType.Link)
            ? TacticalSlotKind.PeekAttacker
            : TacticalSlotKind.HoldDefender;
    }

    // facingDir: усреднённое направление "от ковра". Если ковер только с одной стороны,
    // получается чистый нормал. Если с нескольких — компромисс (бот будет смотреть в
    // открытое пространство). На случай если вектор оказался нулевым (например, ковры
    // окружили со всех сторон, что для нас геометрически почти невозможно), фоллбэк —
    // в сторону центра зоны: лучше так, чем NaN-направление.
    // Дополнительно крутим на случайный yaw в пределах facingYawJitter — чтобы 100 слотов
    // не смотрели в одну и ту же сторону строго по оси.
    Vector3 ComputeFacingDir(Vector2Int coverNormalCells, int x, int z, MapZoneComponent zone)
    {
        Vector3 dir = new Vector3(coverNormalCells.x, 0f, coverNormalCells.y);
        if (dir.sqrMagnitude < 0.001f && zone != null)
        {
            Vector3 cellWorld = new Vector3(x * blockSize, 0f, z * blockSize);
            dir = zone.transform.position - cellWorld;
            dir.y = 0f;
        }
        if (dir.sqrMagnitude < 0.001f)
            dir = Vector3.forward;
        dir.Normalize();

        if (facingYawJitter > 0.01f)
        {
            float yaw = (Random.value - 0.5f) * 2f * facingYawJitter;
            dir = Quaternion.Euler(0f, yaw, 0f) * dir;
        }

        return dir;
    }

    // Индекс клетка→зона. Для каждой зарегистрированной MapZoneComponent проверяем
    // попадание мировой точки центра клетки в её BoxCollider-сегменты. Один проход.
    MapZoneComponent[,] BuildCellToZoneIndex(MapManager mapManager)
    {
        MapZoneComponent[,] index = new MapZoneComponent[width, height];

        foreach (MapZoneComponent zone in mapManager.Zones)
        {
            if (zone == null) continue;
            foreach (BoxCollider col in zone.BoxColliders)
            {
                if (col == null) continue;
                Bounds b = col.bounds;
                int xMin = Mathf.Max(0, Mathf.FloorToInt(b.min.x / blockSize));
                int xMax = Mathf.Min(width - 1, Mathf.CeilToInt(b.max.x / blockSize));
                int zMin = Mathf.Max(0, Mathf.FloorToInt(b.min.z / blockSize));
                int zMax = Mathf.Min(height - 1, Mathf.CeilToInt(b.max.z / blockSize));

                for (int x = xMin; x <= xMax; x++)
                {
                    for (int z = zMin; z <= zMax; z++)
                    {
                        if (index[x, z] != null) continue;
                        Vector3 worldCenter = new Vector3(x * blockSize, b.center.y, z * blockSize);
                        if (b.Contains(worldCenter))
                            index[x, z] = zone;
                    }
                }
            }
        }

        return index;
    }
}
