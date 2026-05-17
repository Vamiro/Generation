using System.Collections.Generic;
using UnityEngine;

public partial class MapGenerator
{
    // Размещает комнаты двух типов:
    //  - Gallery: посередине main-дороги, ломает long sight-line.
    //  - Pre-site: у входа в сайт (аналог Hookah/Showers в Valorant),
    //    создаёт staging area для атакующих и off-site hold для защитников.
    void PlaceRooms()
    {
        if (!enableRooms || mainRoadPaths == null || mainRoadPaths.Count == 0)
            return;

        int sizeMin = Mathf.Max(2, roomSizeMin);
        int sizeMax = Mathf.Max(sizeMin, roomSizeMax);
        int offset = Mathf.Max(1, roomOffsetFromRoad);
        int galleriesPerRoad = Mathf.Max(0, roomsPerMainRoad);

        int preSizeMin = Mathf.Max(2, preSiteRoomSizeMin);
        int preSizeMax = Mathf.Max(preSizeMin, preSiteRoomSizeMax);

        foreach (List<Vector2Int> path in mainRoadPaths)
        {
            if (path == null || path.Count < 8)
                continue;

            // Тип А: галерея в средней части пути (25%–65%).
            if (galleriesPerRoad > 0)
                TryPlaceRoomsInRange(path, galleriesPerRoad, 0.25f, 0.65f, sizeMin, sizeMax, offset);

            // Тип Б: pre-site комната у конца пути (70%–88%) — staging area перед сайтом.
            if (enablePreSiteRooms)
                TryPlaceRoomsInRange(path, 1, 0.70f, 0.88f, preSizeMin, preSizeMax, offset);
        }
    }

    void TryPlaceRoomsInRange(
        List<Vector2Int> path,
        int count,
        float rangeStart,
        float rangeEnd,
        int sizeMin,
        int sizeMax,
        int offset)
    {
        int idxStart = Mathf.CeilToInt(path.Count * rangeStart);
        int idxEnd = Mathf.FloorToInt(path.Count * rangeEnd);

        if (idxEnd <= idxStart)
            return;

        List<int> placedAt = new();
        int maxAttempts = count * 6;

        for (int attempt = 0; attempt < maxAttempts && placedAt.Count < count; attempt++)
        {
            int idx = Random.Range(idxStart, idxEnd + 1);

            if (IsTooCloseToPlaced(idx, placedAt, sizeMax + 2))
                continue;

            int roomWidth = Random.Range(sizeMin, sizeMax + 1);
            int roomDepth = Random.Range(sizeMin, sizeMax + 1);

            if (TryPlaceGallery(path, idx, roomWidth, roomDepth, offset))
                placedAt.Add(idx);
        }
    }

    // Пробует поставить галерею в точке path[idx].
    // Определяет направление дороги, пробует обе перпендикулярные стороны.
    bool TryPlaceGallery(List<Vector2Int> path, int idx, int roomWidth, int roomDepth, int offset)
    {
        Vector2Int dir = GetPathDirection(path, idx);
        bool isHorizontal = Mathf.Abs(dir.x) >= Mathf.Abs(dir.y);

        Vector2Int perpA = isHorizontal ? new Vector2Int(0, 1) : new Vector2Int(1, 0);
        Vector2Int perpB = -perpA;
        Vector2Int[] perps = Random.value < 0.5f ? new[] { perpA, perpB } : new[] { perpB, perpA };

        Vector2Int roadCell = path[idx];

        foreach (Vector2Int perp in perps)
        {
            int rx, rz, rw, rh;
            if (isHorizontal)
            {
                rw = roomWidth;
                rh = roomDepth;
                rx = roadCell.x - roomWidth / 2;
                rz = perp.y > 0
                    ? roadCell.y + offset
                    : roadCell.y - offset - roomDepth + 1;
            }
            else
            {
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

    // Проверяет, что прямоугольник:
    // - целиком внутри карты
    // - с буфером outerWallThickness от краёв карты (чтобы всегда было место для стены)
    // - не перекрывает Spawn/Site/Neutral/Wall
    bool CanPlaceRoom(int startX, int startZ, int sizeX, int sizeZ)
    {
        if (sizeX <= 0 || sizeZ <= 0)
            return false;

        int wallBuffer = Mathf.Max(1, outerWallThickness);

        for (int x = startX; x < startX + sizeX; x++)
        {
            for (int z = startZ; z < startZ + sizeZ; z++)
            {
                if (!IsInsideMap(x, z))
                    return false;

                // Буфер от края карты — иначе внешняя стена не сможет образоваться.
                if (x < wallBuffer || x >= width - wallBuffer ||
                    z < wallBuffer || z >= height - wallBuffer)
                    return false;

                BlockType t = cellTypes[x, z];
                if (t == BlockType.Spawn || t == BlockType.Site ||
                    t == BlockType.Neutral || t == BlockType.Wall)
                    return false;
            }
        }

        return true;
    }

    // Направление пути в точке idx — усредненное по ±2 клетки для сглаживания.
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
            if (Mathf.Abs(idx - p) < minGap)
                return true;
        return false;
    }
}
