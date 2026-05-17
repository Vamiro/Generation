using System.Collections.Generic;
using UnityEngine;

public partial class MapGenerator
{
    void BuildAndRegisterZoneObjects()
    {
        MapManager mapManager = MapManager.Instance;
        if (mapManager == null)
            return;

        mapManager.ClearZones();
        nextZoneId = 1;

        List<ZoneRegion> regions = ExtractAllRegions();
        regions.Sort((a, b) =>
        {
            int typeCompare = a.Type.CompareTo(b.Type);
            if (typeCompare != 0)
                return typeCompare;

            int xCompare = a.Min.x.CompareTo(b.Min.x);
            return xCompare != 0 ? xCompare : a.Min.y.CompareTo(b.Min.y);
        });

        List<SiteZoneComponent> siteZones = new();
        List<RoadZoneComponent> roadZones = new();

        foreach (ZoneRegion region in regions)
        {
            MapZoneComponent zone = InstantiateZoneForRegion(region);
            if (zone == null)
                continue;

            mapManager.RegisterZone(zone);
            if (zone is SiteZoneComponent siteZone)
                siteZones.Add(siteZone);
            else if (zone is RoadZoneComponent roadZone)
                roadZones.Add(roadZone);
        }

        AssignRoadTargets(roadZones, siteZones);
    }

    List<ZoneRegion> ExtractAllRegions()
    {
        List<ZoneRegion> regions = new();
        foreach (BlockType zoneType in RuntimeZoneTypes)
            regions.AddRange(ExtractRegionsForType(zoneType));

        return regions;
    }

    List<ZoneRegion> ExtractRegionsForType(BlockType zoneType)
    {
        List<ZoneRegion> regions = new();
        bool[,] visited = new bool[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (visited[x, z] || GetCellType(x, z) != zoneType)
                    continue;

                regions.Add(FloodFillRegion(x, z, zoneType, visited));
            }
        }

        return regions;
    }

    ZoneRegion FloodFillRegion(int startX, int startZ, BlockType zoneType, bool[,] visited)
    {
        Queue<Vector2Int> queue = new();
        queue.Enqueue(new Vector2Int(startX, startZ));
        visited[startX, startZ] = true;

        ZoneRegion region = new ZoneRegion
        {
            Type = zoneType,
            Min = new Vector2Int(startX, startZ),
            Max = new Vector2Int(startX, startZ)
        };

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            region.Cells.Add(current);

            if (current.x < region.Min.x) region.Min.x = current.x;
            if (current.y < region.Min.y) region.Min.y = current.y;
            if (current.x > region.Max.x) region.Max.x = current.x;
            if (current.y > region.Max.y) region.Max.y = current.y;

            TryEnqueueRegionCell(current.x + 1, current.y, zoneType, visited, queue);
            TryEnqueueRegionCell(current.x - 1, current.y, zoneType, visited, queue);
            TryEnqueueRegionCell(current.x, current.y + 1, zoneType, visited, queue);
            TryEnqueueRegionCell(current.x, current.y - 1, zoneType, visited, queue);
        }

        return region;
    }

    void TryEnqueueRegionCell(int x, int z, BlockType zoneType, bool[,] visited, Queue<Vector2Int> queue)
    {
        if (!IsInsideMap(x, z) || visited[x, z] || GetCellType(x, z) != zoneType)
            return;

        visited[x, z] = true;
        queue.Enqueue(new Vector2Int(x, z));
    }

    MapZoneComponent InstantiateZoneForRegion(ZoneRegion region)
    {
        GameObject zoneObject = new GameObject($"{region.Type}Zone_{nextZoneId}");
        zoneObject.transform.SetParent(transform, false);
        zoneObject.transform.position = region.GetCenterWorld(blockSize);

        BoxCollider boxCollider = zoneObject.AddComponent<BoxCollider>();
        boxCollider.size = new Vector3(
            (region.Max.x - region.Min.x + 1) * blockSize,
            Mathf.Max(0.1f, blockSize * 0.5f),
            (region.Max.y - region.Min.y + 1) * blockSize);
        boxCollider.center = Vector3.zero;

        MapZoneComponent zoneComponent;
        switch (region.Type)
        {
            case BlockType.Spawn:
                zoneComponent = zoneObject.AddComponent<SpawnZoneComponent>();
                break;
            case BlockType.Site:
                zoneComponent = zoneObject.AddComponent<SiteZoneComponent>();
                break;
            case BlockType.Main:
            case BlockType.Link:
                RoadZoneComponent roadZone = zoneObject.AddComponent<RoadZoneComponent>();
                roadZone.roadType = region.Type == BlockType.Main ? RoadType.Main : RoadType.Link;
                zoneComponent = roadZone;
                break;
            case BlockType.Neutral:
                zoneComponent = zoneObject.AddComponent<NeutralZoneComponent>();
                break;
            case BlockType.Room:
                zoneComponent = zoneObject.AddComponent<RoomZoneComponent>();
                break;
            default:
                Destroy(zoneObject);
                return null;
        }

        zoneComponent.InitializeZone(nextZoneId, boxCollider);
        zoneComponent.SetSamplePoints(BuildSamplePoints(region));
        nextZoneId++;
        region.ZoneComponent = zoneComponent;
        return zoneComponent;
    }

    List<Vector3> BuildSamplePoints(ZoneRegion region)
    {
        List<Vector3> samplePoints = new(region.Cells.Count);
        foreach (Vector2Int cell in region.Cells)
            samplePoints.Add(new Vector3(cell.x * blockSize, 0f, cell.y * blockSize));

        return samplePoints;
    }

    void AssignRoadTargets(List<RoadZoneComponent> roadZones, List<SiteZoneComponent> siteZones)
    {
        if (roadZones.Count == 0 || siteZones.Count == 0)
        {
            Debug.LogWarning("MapGenerator: невозможно связать roadToSite — отсутствуют дороги или сайты.");
            return;
        }

        foreach (RoadZoneComponent road in roadZones)
        {
            road.roadToSite = FindClosestSite(road.transform.position, siteZones);
        }

        EnsureRoadCoverage(siteZones, roadZones, RoadType.Main);
        EnsureRoadCoverage(siteZones, roadZones, RoadType.Link);
    }

    SiteZoneComponent FindClosestSite(Vector3 position, List<SiteZoneComponent> siteZones)
    {
        SiteZoneComponent closestSite = null;
        float bestDistance = float.MaxValue;

        foreach (SiteZoneComponent site in siteZones)
        {
            float sqrDistance = (site.transform.position - position).sqrMagnitude;
            if (sqrDistance >= bestDistance)
                continue;

            bestDistance = sqrDistance;
            closestSite = site;
        }

        return closestSite;
    }

    void EnsureRoadCoverage(List<SiteZoneComponent> siteZones, List<RoadZoneComponent> roadZones, RoadType roadType)
    {
        bool hasRoadsOfType = false;
        foreach (RoadZoneComponent road in roadZones)
        {
            if (road.roadType == roadType)
            {
                hasRoadsOfType = true;
                break;
            }
        }

        if (!hasRoadsOfType)
        {
            Debug.LogWarning($"MapGenerator: не найдено дорог типа {roadType}.");
            return;
        }

        foreach (SiteZoneComponent site in siteZones)
        {
            bool isCovered = false;
            foreach (RoadZoneComponent road in roadZones)
            {
                if (road.roadType == roadType && road.roadToSite == site)
                {
                    isCovered = true;
                    break;
                }
            }

            if (isCovered)
                continue;

            RoadZoneComponent fallbackRoad = FindClosestRoad(site.transform.position, roadZones, roadType);
            if (fallbackRoad != null)
            {
                fallbackRoad.roadToSite = site;
                Debug.LogWarning($"MapGenerator: fallback-назначение {roadType} дороги для сайта {site.name}.");
            }
        }
    }

    RoadZoneComponent FindClosestRoad(Vector3 position, List<RoadZoneComponent> roadZones, RoadType roadType)
    {
        RoadZoneComponent closestRoad = null;
        float bestDistance = float.MaxValue;

        foreach (RoadZoneComponent road in roadZones)
        {
            if (road.roadType != roadType)
                continue;

            float sqrDistance = (road.transform.position - position).sqrMagnitude;
            if (sqrDistance >= bestDistance)
                continue;

            bestDistance = sqrDistance;
            closestRoad = road;
        }

        return closestRoad;
    }
}
