using System.Collections.Generic;
using UnityEngine;

public partial class MapGenerator
{
    void PlaceRooms()
    {
        if (!enableRooms)
            return;

        int offset = Mathf.Max(1, roomOffsetFromRoad);

        if (mainRoadPaths != null)
        {
            int sizeMin    = Mathf.Max(2, roomSize.min);
            int sizeMax    = Mathf.Max(sizeMin, roomSize.max);
            int preSizeMin = Mathf.Max(2, preSiteRoomSize.min);
            int preSizeMax = Mathf.Max(preSizeMin, preSiteRoomSize.max);

            foreach (List<Vector2Int> path in mainRoadPaths)
            {
                if (path == null || path.Count < 8) continue;

                // Тип А: Pocket-галерея — только на прямых участках (высокая экспозиция).
                if (roomsPerMainRoad > 0)
                    TryPlaceRoomsInRange(path, roomsPerMainRoad, 0.25f, 0.65f, sizeMin, sizeMax, offset, BlockType.Pocket);

                if (enablePreSiteRooms)
                    TryPlacePreSiteRoom(path, preSizeMin, preSizeMax);
            }
        }

        if (enableLinkRooms && linkPaths != null)
        {
            int cubMin = Mathf.Max(1, linkRoomSize.min);
            int cubMax = Mathf.Max(cubMin, linkRoomSize.max);

            foreach (List<Vector2Int> path in linkPaths)
            {
                if (path == null || path.Count < 6) continue;
                TryPlaceRoomsInRange(path, 1, 0.40f, 0.60f, cubMin, cubMax, offset, BlockType.Pocket);
            }
        }
    }

    // ───── Generic gallery/room placement ────────────────────────────────────
    void TryPlaceRoomsInRange(List<Vector2Int> path, int count, float rangeStart, float rangeEnd,
        int sizeMin, int sizeMax, int offset, BlockType zoneType)
    {
        int idxStart = Mathf.CeilToInt (path.Count * rangeStart);
        int idxEnd   = Mathf.FloorToInt(path.Count * rangeEnd);
        if (idxEnd <= idxStart) return;

        List<int> placedAt  = new();
        int maxAttempts     = count * 6;

        // Для Pocket: сначала ищем самые прямые (высокоэкспозиционные) позиции.
        List<int> candidateIndices = BuildCandidateIndices(path, idxStart, idxEnd, zoneType);

        for (int attempt = 0; attempt < maxAttempts && placedAt.Count < count; attempt++)
        {
            int idx = candidateIndices.Count > 0
                ? candidateIndices[Random.Range(0, candidateIndices.Count)]
                : Random.Range(idxStart, idxEnd + 1);

            if (IsTooCloseToPlaced(idx, placedAt, sizeMax + 2)) continue;

            int roomWidth = Random.Range(sizeMin, sizeMax + 1);
            int roomDepth = Random.Range(sizeMin, sizeMax + 1);

            if (TryPlaceGallery(path, idx, roomWidth, roomDepth, offset, zoneType))
                placedAt.Add(idx);
        }
    }

    // Для Pocket: отбираем индексы с достаточной экспозицией (прямые участки).
    // Для других типов: все индексы в диапазоне.
    List<int> BuildCandidateIndices(List<Vector2Int> path, int idxStart, int idxEnd, BlockType zoneType)
    {
        List<int> result = new();
        for (int i = idxStart; i <= idxEnd; i++)
        {
            if (zoneType == BlockType.Pocket)
            {
                int exp = ComputePathExposure(path, i);
                if (exp >= 3) result.Add(i); // только прямые участки ≥ 3 ячеек
            }
            else
            {
                result.Add(i);
            }
        }
        return result;
    }

    // Экспозиция вдоль самого пути: сколько соседних клеток пути видно в обоих направлениях.
    // Большое значение = длинный прямой участок = хорошее место для Pocket.
    int ComputePathExposure(List<Vector2Int> path, int idx)
    {
        int forward  = 0;
        int backward = 0;
        for (int i = idx + 1; i < path.Count; i++)
        {
            if ((path[i] - path[i - 1]).sqrMagnitude <= 2) forward++;
            else break;
        }
        for (int i = idx - 1; i >= 0; i--)
        {
            if ((path[i] - path[i + 1]).sqrMagnitude <= 2) backward++;
            else break;
        }
        return forward + backward;
    }

    // Размещает комнату перпендикулярно пути в point path[idx].
    // preferSiteDirection = true: сначала пробует сторону, ближайшую к сайту.
    bool TryPlaceGallery(List<Vector2Int> path, int idx, int roomWidth, int roomDepth, int offset,
        BlockType zoneType, bool preferSiteDirection = false)
    {
        Vector2Int dir = GetPathDirection(path, idx);
        bool isHorizontal = Mathf.Abs(dir.x) >= Mathf.Abs(dir.y);

        Vector2Int perpA = isHorizontal ? new Vector2Int(0, 1) : new Vector2Int(1, 0);
        Vector2Int perpB = -perpA;

        Vector2Int[] perps;
        if (preferSiteDirection)
        {
            Vector2Int roadCell = path[idx];
            Vector2Int siteDir  = GetDirectionTowardNearestSite(roadCell);
            float dotA = perpA.x * siteDir.x + perpA.y * siteDir.y;
            perps = dotA >= 0 ? new[] { perpA, perpB } : new[] { perpB, perpA };
        }
        else
        {
            perps = Random.value < 0.5f ? new[] { perpA, perpB } : new[] { perpB, perpA };
        }

        Vector2Int roadCell2 = path[idx];
        foreach (Vector2Int perp in perps)
        {
            int rx, rz, rw, rh;
            if (isHorizontal)
            {
                rw = roomWidth; rh = roomDepth;
                rx = roadCell2.x - roomWidth / 2;
                rz = perp.y > 0
                    ? roadCell2.y + offset
                    : roadCell2.y - offset - roomDepth + 1;
            }
            else
            {
                rw = roomDepth; rh = roomWidth;
                rx = perp.x > 0
                    ? roadCell2.x + offset
                    : roadCell2.x - offset - roomDepth + 1;
                rz = roadCell2.y - roomWidth / 2;
            }

            if (CanPlaceRoom(rx, rz, rw, rh))
            {
                PaintZoneByRect(rx, rz, rw, rh, zoneType);
                return true;
            }
        }
        return false;
    }

    // Ближайший из двух сайтов по Manhattan-расстоянию.
    Vector2Int GetDirectionTowardNearestSite(Vector2Int cell)
    {
        Vector2Int centerA = new Vector2Int(siteA.x + siteASize.x / 2, siteA.y + siteASize.y / 2);
        Vector2Int centerB = new Vector2Int(siteB.x + siteBSize.x / 2, siteB.y + siteBSize.y / 2);

        Vector2Int nearest = (Mathf.Abs(centerA.x - cell.x) + Mathf.Abs(centerA.y - cell.y)) <
                             (Mathf.Abs(centerB.x - cell.x) + Mathf.Abs(centerB.y - cell.y))
            ? centerA : centerB;

        Vector2Int delta = nearest - cell;
        return new Vector2Int(Mathf.Clamp(delta.x, -1, 1), Mathf.Clamp(delta.y, -1, 1));
    }

    bool CanPlaceRoom(int startX, int startZ, int sizeX, int sizeZ)
    {
        if (sizeX <= 0 || sizeZ <= 0) return false;

        int wallBuffer = Mathf.Max(1, outerWallThickness);
        for (int x = startX; x < startX + sizeX; x++)
        {
            for (int z = startZ; z < startZ + sizeZ; z++)
            {
                if (!IsInsideMap(x, z)) return false;
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

    Vector2Int GetPathDirection(List<Vector2Int> path, int idx)
    {
        int ahead  = Mathf.Min(idx + 2, path.Count - 1);
        int behind = Mathf.Max(idx - 2, 0);
        Vector2Int delta = path[ahead] - path[behind];
        return new Vector2Int(Mathf.Clamp(delta.x, -1, 1), Mathf.Clamp(delta.y, -1, 1));
    }

    bool IsTooCloseToPlaced(int idx, List<int> placed, int minGap)
    {
        foreach (int p in placed)
            if (Mathf.Abs(idx - p) < minGap) return true;
        return false;
    }
}
