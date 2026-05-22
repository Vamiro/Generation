using System.Collections.Generic;
using UnityEngine;

public partial class MapGenerator
{
    private const int PreSiteGapCells = 1;

    // Pre-site Room: 1 клетка зазора до Main и Site, кольцо-стена с одним проходом на дорогу и на сайт.
    void TryPlacePreSiteRoom(List<Vector2Int> path, int sizeMin, int sizeMax)
    {
        int lastMainIdx = -1;
        for (int i = path.Count - 1; i >= 0; i--)
        {
            Vector2Int c = path[i];
            if (IsInsideMap(c.x, c.y) && cellTypes[c.x, c.y] == BlockType.Main)
            {
                lastMainIdx = i;
                break;
            }
        }

        if (lastMainIdx < 3)
            return;

        int roomWidth = Random.Range(sizeMin, sizeMax + 1);
        int roomDepth = Random.Range(sizeMin, sizeMax + 1);
        int targetIdx = Mathf.Clamp(lastMainIdx - 1, 1, path.Count - 2);
        Vector2Int roadCell = path[targetIdx];
        Vector2Int siteDir = GetDirectionTowardNearestSite(roadCell);
        Vector2Int siteCenter = GetNearestSiteCenter(roadCell);

        Vector2Int pathDir = GetPathDirection(path, targetIdx);
        bool isHorizontal = Mathf.Abs(pathDir.x) >= Mathf.Abs(pathDir.y);
        Vector2Int perpA = isHorizontal ? new Vector2Int(0, 1) : new Vector2Int(1, 0);
        Vector2Int perpB = -perpA;
        float dotA = perpA.x * siteDir.x + perpA.y * siteDir.y;
        Vector2Int[] perps = dotA >= 0 ? new[] { perpA, perpB } : new[] { perpB, perpA };

        int clearance = PreSiteGapCells + 1;

        foreach (Vector2Int perp in perps)
        {
            int rx, rz, rw, rh;
            if (isHorizontal)
            {
                rw = roomWidth;
                rh = roomDepth;
                rx = roadCell.x - roomWidth / 2;
                if (perp.y > 0)
                    rz = roadCell.y + clearance;
                else
                    rz = roadCell.y - clearance - roomDepth + 1;
            }
            else
            {
                rw = roomDepth;
                rh = roomWidth;
                if (perp.x > 0)
                    rx = roadCell.x + clearance;
                else
                    rx = roadCell.x - clearance - roomDepth + 1;
                rz = roadCell.y - roomWidth / 2;
            }

            if (!CanPlacePreSiteRoom(rx, rz, rw, rh, roadCell, siteCenter))
                continue;

            PaintZoneByRect(rx, rz, rw, rh, BlockType.Room);
            BuildPreSiteRoomWalls(rx, rz, rw, rh, roadCell, siteCenter);
            return;
        }
    }

    Vector2Int GetNearestSiteCenter(Vector2Int fromCell)
    {
        Vector2Int centerA = new(siteA.x + siteASize.x / 2, siteA.y + siteASize.y / 2);
        Vector2Int centerB = new(siteB.x + siteBSize.x / 2, siteB.y + siteBSize.y / 2);
        int distA = Mathf.Abs(centerA.x - fromCell.x) + Mathf.Abs(centerA.y - fromCell.y);
        int distB = Mathf.Abs(centerB.x - fromCell.x) + Mathf.Abs(centerB.y - fromCell.y);
        return distA <= distB ? centerA : centerB;
    }

    bool CanPlacePreSiteRoom(int startX, int startZ, int sizeX, int sizeZ, Vector2Int roadCell, Vector2Int siteCenter)
    {
        if (sizeX <= 0 || sizeZ <= 0)
            return false;

        HashSet<Vector2Int> roomCells = BuildRectCells(startX, startZ, sizeX, sizeZ);
        HashSet<Vector2Int> ringCells = CollectOneCellRing(roomCells);
        if (ringCells.Count == 0)
            return false;

        int wallBuffer = Mathf.Max(1, outerWallThickness);
        bool hasRoadSide = false;
        bool hasSiteSide = false;

        foreach (Vector2Int cell in roomCells)
        {
            if (!IsInsideMap(cell.x, cell.y))
                return false;
            if (cell.x < wallBuffer || cell.x >= width - wallBuffer ||
                cell.y < wallBuffer || cell.y >= height - wallBuffer)
                return false;

            BlockType t = cellTypes[cell.x, cell.y];
            if (t is BlockType.Spawn or BlockType.Site or BlockType.Neutral or BlockType.Wall or BlockType.Room)
                return false;

            if (TouchesBlockType(cell, BlockType.Main) || TouchesBlockType(cell, BlockType.Site))
                return false;
        }

        foreach (Vector2Int cell in ringCells)
        {
            if (!IsInsideMap(cell.x, cell.y))
                return false;
            if (cell.x < wallBuffer || cell.x >= width - wallBuffer ||
                cell.y < wallBuffer || cell.y >= height - wallBuffer)
                return false;

            BlockType t = cellTypes[cell.x, cell.y];
            if (t is BlockType.Spawn or BlockType.Neutral or BlockType.Wall or BlockType.Room)
                return false;

            if (TouchesBlockType(cell, BlockType.Main))
                hasRoadSide = true;
            if (TouchesBlockType(cell, BlockType.Site))
                hasSiteSide = true;
        }

        return hasRoadSide && hasSiteSide;
    }

    static HashSet<Vector2Int> BuildRectCells(int startX, int startZ, int sizeX, int sizeZ)
    {
        HashSet<Vector2Int> cells = new();
        for (int x = startX; x < startX + sizeX; x++)
        {
            for (int z = startZ; z < startZ + sizeZ; z++)
                cells.Add(new Vector2Int(x, z));
        }
        return cells;
    }

    static HashSet<Vector2Int> CollectOneCellRing(HashSet<Vector2Int> interior)
    {
        int[] dx = { 1, -1, 0, 0 };
        int[] dz = { 0, 0, 1, -1 };
        HashSet<Vector2Int> ring = new();

        foreach (Vector2Int cell in interior)
        {
            for (int i = 0; i < 4; i++)
            {
                Vector2Int nb = new(cell.x + dx[i], cell.y + dz[i]);
                if (!interior.Contains(nb))
                    ring.Add(nb);
            }
        }

        return ring;
    }

    bool TouchesBlockType(Vector2Int cell, BlockType type)
    {
        int[] dx = { 1, -1, 0, 0 };
        int[] dz = { 0, 0, 1, -1 };
        for (int i = 0; i < 4; i++)
        {
            int nx = cell.x + dx[i];
            int nz = cell.y + dz[i];
            if (!IsInsideMap(nx, nz))
                continue;
            if (cellTypes[nx, nz] == type)
                return true;
        }
        return false;
    }

    void BuildPreSiteRoomWalls(int startX, int startZ, int sizeX, int sizeZ, Vector2Int roadCell, Vector2Int siteCenter)
    {
        HashSet<Vector2Int> roomCells = BuildRectCells(startX, startZ, sizeX, sizeZ);
        HashSet<Vector2Int> ringCells = CollectOneCellRing(roomCells);

        Vector2Int? roadPassage = PickPassageCell(ringCells, roadCell, BlockType.Main);
        Vector2Int? sitePassage = PickPassageCell(ringCells, siteCenter, BlockType.Site);

        if (!roadPassage.HasValue || !sitePassage.HasValue)
            return;

        if (roadPassage.Value == sitePassage.Value)
            sitePassage = PickPassageCell(ringCells, siteCenter, BlockType.Site, roadPassage.Value);

        foreach (Vector2Int cell in ringCells)
        {
            if (cell == roadPassage.Value || cell == sitePassage.Value)
            {
                if (cellTypes[cell.x, cell.y] == BlockType.Empty)
                    TryMarkBlock(cell.x, cell.y, BlockType.Floor, trackZone: false);
                continue;
            }

            TryMarkBlock(cell.x, cell.y, BlockType.Wall, trackZone: false);
        }
    }

    Vector2Int? PickPassageCell(HashSet<Vector2Int> ringCells, Vector2Int target, BlockType neighborType, Vector2Int? exclude = null)
    {
        Vector2Int? best = null;
        int bestDist = int.MaxValue;

        foreach (Vector2Int cell in ringCells)
        {
            if (exclude.HasValue && cell == exclude.Value)
                continue;
            if (!TouchesBlockType(cell, neighborType))
                continue;

            int dist = Mathf.Abs(cell.x - target.x) + Mathf.Abs(cell.y - target.y);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = cell;
            }
        }

        if (best.HasValue)
            return best;

        foreach (Vector2Int cell in ringCells)
        {
            if (exclude.HasValue && cell == exclude.Value)
                continue;

            int dist = Mathf.Abs(cell.x - target.x) + Mathf.Abs(cell.y - target.y);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = cell;
            }
        }

        return best;
    }
}
