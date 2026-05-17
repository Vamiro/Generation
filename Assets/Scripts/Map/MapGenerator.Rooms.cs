using System.Collections.Generic;
using UnityEngine;

public partial class MapGenerator
{
    // Размещает комнаты-галереи вдоль main-дорог.
    // Галерея — прямоугольная выпуклость, расположенная перпендикулярно дороге
    // в её средней части. Ломает длинные sight-line и создаёт карманы для пика/держания.
    void PlaceRooms()
    {
        if (!enableRooms || mainRoadPaths == null || mainRoadPaths.Count == 0)
            return;

        int sizeMin = Mathf.Max(2, roomSizeMin);
        int sizeMax = Mathf.Max(sizeMin, roomSizeMax);
        int offset = Mathf.Max(1, roomOffsetFromRoad);
        int perRoad = Mathf.Max(0, roomsPerMainRoad);

        foreach (List<Vector2Int> path in mainRoadPaths)
        {
            if (path == null || path.Count < 8)
                continue;

            TryPlaceRoomsOnPath(path, perRoad, sizeMin, sizeMax, offset);
        }
    }

    void TryPlaceRoomsOnPath(List<Vector2Int> path, int count, int sizeMin, int sizeMax, int offset)
    {
        // Пропускаем первые и последние 25% пути (слишком близко к спавну/сайту).
        int skipCells = Mathf.CeilToInt(path.Count * 0.25f);
        int rangeStart = skipCells;
        int rangeEnd = path.Count - 1 - skipCells;

        if (rangeEnd <= rangeStart)
            return;

        // Набор уже занятых зон пути — чтобы комнаты не слипались.
        List<int> placedAt = new();
        int maxAttempts = count * 6;

        for (int attempt = 0; attempt < maxAttempts && placedAt.Count < count; attempt++)
        {
            int idx = Random.Range(rangeStart, rangeEnd + 1);

            // Комнаты не должны стоять слишком близко друг к другу.
            if (IsTooCloseToPlaced(idx, placedAt, sizeMax + 2))
                continue;

            // Ширина (вдоль дороги) и глубина (перпендикулярно).
            int roomWidth = Random.Range(sizeMin, sizeMax + 1);
            int roomDepth = Random.Range(sizeMin, sizeMax + 1);

            if (TryPlaceGallery(path, idx, roomWidth, roomDepth, offset))
                placedAt.Add(idx);
        }
    }

    // Пробует поставить галерею в точке path[idx].
    // Пробует обе перпендикулярные стороны; возвращает true если удалось.
    bool TryPlaceGallery(List<Vector2Int> path, int idx, int roomWidth, int roomDepth, int offset)
    {
        Vector2Int dir = GetPathDirection(path, idx);
        bool isHorizontal = Mathf.Abs(dir.x) >= Mathf.Abs(dir.y);

        // Два перпендикулярных направления от дороги.
        // Для горизонтальной дороги (вдоль X) — перпендикуляр по Z.
        // Для вертикальной дороги (вдоль Z) — перпендикуляр по X.
        Vector2Int perpA = isHorizontal ? new Vector2Int(0, 1) : new Vector2Int(1, 0);
        Vector2Int perpB = -perpA;

        // Случайный порядок: пробуем обе стороны.
        Vector2Int[] perps = Random.value < 0.5f ? new[] { perpA, perpB } : new[] { perpB, perpA };
        Vector2Int roadCell = path[idx];

        foreach (Vector2Int perp in perps)
        {
            int rx, rz, rw, rh;
            if (isHorizontal)
            {
                // Дорога горизонтальная → комната тянется по Z.
                rw = roomWidth;
                rh = roomDepth;
                rx = roadCell.x - roomWidth / 2;
                rz = perp.y > 0
                    ? roadCell.y + offset
                    : roadCell.y - offset - roomDepth + 1;
            }
            else
            {
                // Дорога вертикальная → комната тянется по X.
                rw = roomDepth;
                rh = roomWidth;
                rx = perp.x > 0
                    ? roadCell.x + offset
                    : roadCell.x - offset - roomDepth + 1;
                rz = roadCell.y - roomWidth / 2;
            }

            if (CanPlaceRoom(rx, rz, rw, rh))
            {
                PaintZoneByRect(rx, rz, rw, rh, BlockType.Room);
                return true;
            }
        }

        return false;
    }

    // Проверяет, что прямоугольник целиком внутри карты и не перекрывает защищённые зоны.
    bool CanPlaceRoom(int startX, int startZ, int sizeX, int sizeZ)
    {
        if (sizeX <= 0 || sizeZ <= 0)
            return false;

        for (int x = startX; x < startX + sizeX; x++)
        {
            for (int z = startZ; z < startZ + sizeZ; z++)
            {
                if (!IsInsideMap(x, z))
                    return false;

                BlockType t = cellTypes[x, z];
                if (t == BlockType.Spawn || t == BlockType.Site ||
                    t == BlockType.Neutral || t == BlockType.Wall)
                    return false;
            }
        }

        return true;
    }

    // Направление пути в точке idx — смотрим на несколько клеток вперёд/назад для сглаживания.
    Vector2Int GetPathDirection(List<Vector2Int> path, int idx)
    {
        int ahead = Mathf.Min(idx + 2, path.Count - 1);
        int behind = Mathf.Max(idx - 2, 0);
        Vector2Int delta = path[ahead] - path[behind];
        return new Vector2Int(Mathf.Clamp(delta.x, -1, 1), Mathf.Clamp(delta.y, -1, 1));
    }

    bool IsTooCloseToPlaced(int idx, List<int> placed, int minGap)
    {
        foreach (int p in placed)
        {
            if (Mathf.Abs(idx - p) < minGap)
                return true;
        }
        return false;
    }
}
