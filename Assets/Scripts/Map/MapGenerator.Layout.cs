using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

public partial class MapGenerator
{
    void PlaceSpawnZones(LayoutSettings layout)
    {
        int centerX = layout.UsableWidth / 2;
        int topBandEnd = Mathf.Max(1, layout.SpawnBandSize);
        int bottomBandStart = Mathf.Max(0, layout.UsableHeight - layout.SpawnBandSize);

        int spawnAX = ClampGridX(centerX + Random.Range(-layout.HorizontalJitter, layout.HorizontalJitter + 1));
        int spawnBX = ClampGridX(spawnAX + Random.Range(-spawnHorizontalOffset, spawnHorizontalOffset + 1));

        spawnA = new Vector2Int(
            spawnAX,
            ClampGridZ(Random.Range(0, topBandEnd)));

        spawnB = new Vector2Int(
            spawnBX,
            ClampGridZ(Random.Range(bottomBandStart, layout.UsableHeight)));

        int spawnSizeA = Random.Range(spawnZoneSizeMin, spawnZoneSizeMax + 1);
        MarkRectAround(spawnA, spawnSizeA / 2, spawnSizeA - spawnSizeA / 2, BlockType.Spawn);

        int spawnSizeB = Random.Range(spawnZoneSizeMin, spawnZoneSizeMax + 1);
        MarkRectAround(spawnB, spawnSizeB / 2, spawnSizeB - spawnSizeB / 2, BlockType.Spawn);
    }

    void PlaceSiteZones(LayoutSettings layout)
    {
        int sideBandSize = Mathf.Max(1, layout.SpawnBandSize + siteZoneWidth);
        int rightBandStart = Mathf.Max(0, layout.UsableWidth - sideBandSize);
        bool foundPlacement = false;

        for (int attempt = 0; attempt < Mathf.Max(1, sitePlacementAttempts); attempt++)
        {
            int siteAZ = ClampGridZ(layout.SiteDepth + Random.Range(-layout.HorizontalJitter, layout.HorizontalJitter + 1));
            int siteBZ = ClampGridZ(layout.SiteDepth + Random.Range(-layout.HorizontalJitter, layout.HorizontalJitter + 1));

            Vector2Int candidateA = new Vector2Int(
                ClampGridX(Random.Range(0, sideBandSize)),
                siteAZ);
            Vector2Int candidateB = new Vector2Int(
                ClampGridX(Random.Range(rightBandStart, layout.UsableWidth)),
                siteBZ);

            if (!IsSitePlacementValid(candidateA, candidateB))
                continue;

            siteA = candidateA;
            siteB = candidateB;
            foundPlacement = true;
            break;
        }

        if (!foundPlacement)
        {
            int fallbackSiteZ = ClampGridZ(layout.SiteDepth);
            siteA = new Vector2Int(ClampGridX(1), fallbackSiteZ);
            siteB = new Vector2Int(ClampGridX(width - siteZoneWidth - 2), fallbackSiteZ);
            Debug.LogWarning("MapGenerator: не удалось найти идеальное размещение сайтов, применен fallback.");
        }

        ClearZone(siteA.x, siteA.y, siteZoneWidth, siteZoneHeight, BlockType.Site);
        ClearZone(siteB.x, siteB.y, siteZoneWidth, siteZoneHeight, BlockType.Site);
    }

    bool IsSitePlacementValid(Vector2Int candidateA, Vector2Int candidateB)
    {
        if (!IsRectInsideMap(candidateA, siteZoneWidth, siteZoneHeight) ||
            !IsRectInsideMap(candidateB, siteZoneWidth, siteZoneHeight))
            return false;

        if (RectsOverlap(candidateA, siteZoneWidth, siteZoneHeight, candidateB, siteZoneWidth, siteZoneHeight))
            return false;

        Vector2Int centerA = GetSiteCenter(candidateA);
        Vector2Int centerB = GetSiteCenter(candidateB);

        if (ManhattanDistance(centerA, centerB) < sitePairDistanceMin)
            return false;

        if (!MeetsSiteDistanceConstraints(centerA) || !MeetsSiteDistanceConstraints(centerB))
            return false;

        if (!IsRectFreeForSite(candidateA) || !IsRectFreeForSite(candidateB))
            return false;

        return true;
    }

    bool IsRectFreeForSite(Vector2Int origin)
    {
        for (int x = origin.x; x < origin.x + siteZoneWidth; x++)
        {
            for (int z = origin.y; z < origin.y + siteZoneHeight; z++)
            {
                if (!IsInsideMap(x, z))
                    return false;

                if (mapGrid[x, z].blockType.Current != BlockType.Floor)
                    return false;
            }
        }

        return true;
    }

    bool MeetsSiteDistanceConstraints(Vector2Int siteCenter)
    {
        int distanceFromSpawnA = ManhattanDistance(spawnA, siteCenter);
        int distanceFromSpawnB = ManhattanDistance(spawnB, siteCenter);
        if (distanceFromSpawnA < siteDistanceFromSpawnMin || distanceFromSpawnA > siteDistanceFromSpawnMax)
            return false;
        if (distanceFromSpawnB < siteDistanceFromSpawnMin || distanceFromSpawnB > siteDistanceFromSpawnMax)
            return false;

        return Mathf.Abs(distanceFromSpawnA - distanceFromSpawnB) <= siteFairnessTolerance;
    }

    void BuildStructuredRoutes()
    {
        Vector2Int defenderSpawn = spawnAIsDefender ? spawnA : spawnB;
        Vector2Int attackerSpawn = spawnAIsDefender ? spawnB : spawnA;

        TeamRoutePlan defenderPlan = BuildTeamRoutePlan(defenderSpawn);
        TeamRoutePlan attackerPlan = BuildTeamRoutePlan(attackerSpawn);

        List<TeamRoutePlan> linkPlans = new() { defenderPlan, attackerPlan };

        int linksToBuild = Mathf.Clamp(linkConnectionCount, 1, 2);
        if (preferShortestConnections)
        {
            linkPlans.Sort((left, right) =>
                ManhattanDistance(left.Spawn, GetSiteCenter(left.LinkSite))
                    .CompareTo(ManhattanDistance(right.Spawn, GetSiteCenter(right.LinkSite))));
        }
        else
        {
            Shuffle(linkPlans);
        }

        for (int i = 0; i < linksToBuild; i++)
        {
            TeamRoutePlan plan = linkPlans[i];
            Vector2Int oppositeSpawn = plan.Spawn == defenderSpawn ? attackerSpawn : defenderSpawn;
            CreateTeamLinkConnection(plan, oppositeSpawn);
        }

        // У каждого спавна всегда есть main в оба сайта.
        CreateMainConnection(defenderSpawn, siteA);
        CreateMainConnection(defenderSpawn, siteB);
        CreateMainConnection(attackerSpawn, siteA);
        CreateMainConnection(attackerSpawn, siteB);
    }

    TeamRoutePlan BuildTeamRoutePlan(Vector2Int spawnPoint)
    {
        Vector2Int linkSite = GetFarthestSiteOrigin(spawnPoint);
        return new TeamRoutePlan(spawnPoint, linkSite);
    }

    Vector2Int GetFarthestSiteOrigin(Vector2Int spawnPoint)
    {
        int distToA = ManhattanDistance(spawnPoint, GetSiteCenter(siteA));
        int distToB = ManhattanDistance(spawnPoint, GetSiteCenter(siteB));
        return distToA >= distToB ? siteA : siteB;
    }

    void MarkRoomZone()
    {
        if (!generateRoomZone)
            return;

        int midX = (spawnA.x + spawnB.x + siteA.x + siteB.x) / 4;
        int midZ = (spawnA.y + spawnB.y + siteA.y + siteB.y) / 4;
        int sizeX = Mathf.Max(1, roomSize.x);
        int sizeZ = Mathf.Max(1, roomSize.y);
        ClearZone(midX - sizeX / 2, midZ - sizeZ / 2, sizeX, sizeZ, BlockType.Room);
    }

    void GrowRoomZones()
    {
        if (!generateRoomZone || roomGrowthSteps <= 0)
            return;

        List<Vector2Int> growthFront = new();
        AppendZoneCellsToFront(BlockType.Room, growthFront);
        AppendZoneCellsToFront(BlockType.Main, growthFront);
        AppendZoneCellsToFront(BlockType.Link, growthFront);
        if (growthFront.Count == 0)
            return;

        for (int step = 0; step < roomGrowthSteps; step++)
        {
            Vector2Int seed = growthFront[Random.Range(0, growthFront.Count)];
            Vector2Int candidate = seed + RandomDirection4();
            if (!IsInsideMap(candidate.x, candidate.y) || IsBorder(candidate.x, candidate.y))
                continue;

            if (mapGrid[candidate.x, candidate.y].blockType.Current != BlockType.Floor)
                continue;

            int contactCount = CountNonFloorContacts(candidate.x, candidate.y);
            if (contactCount > roomAntiMergeContactThreshold)
                continue;

            float normalizedDistance = Vector2Int.Distance(candidate, seed) / Mathf.Max(1f, Mathf.Max(width, height));
            float chance = roomGrowthBaseChance + normalizedDistance * roomGrowthDistanceFactor;
            chance -= CountNeighborType(candidate.x, candidate.y, BlockType.Floor) * roomEmptyPenaltyFactor;
            chance = Mathf.Clamp01(chance);
            if (Random.value > chance)
                continue;

            if (TryMarkBlock(candidate.x, candidate.y, BlockType.Room))
                growthFront.Add(candidate);
        }
    }

    void AppendZoneCellsToFront(BlockType zoneType, List<Vector2Int> front)
    {
        if (!zoneBlocks.TryGetValue(zoneType, out HashSet<BlockComponent> blocks))
            return;

        foreach (BlockComponent block in blocks)
        {
            int gridX = Mathf.RoundToInt(block.transform.position.x / blockSize);
            int gridZ = Mathf.RoundToInt(block.transform.position.z / blockSize);
            front.Add(new Vector2Int(gridX, gridZ));
        }
    }

    void CreateMainConnection(Vector2Int spawnPoint, Vector2Int sitePoint)
    {
        Vector2Int endpoint = GetClosestEdgePoint(sitePoint, siteZoneWidth, siteZoneHeight, spawnPoint);
        CreatePath(spawnPoint, endpoint, BlockType.Main, mainWidth, mainPathHorizontalChance, true);
    }

    void CreateTeamLinkConnection(TeamRoutePlan plan, Vector2Int oppositeSpawn)
    {
        Vector2Int start = GetRandomPointNear(plan.Spawn, spawnOffset);
        Vector2Int hub = ResolveLinkHub(start, oppositeSpawn);
        Vector2Int endpoint = GetClosestEdgePoint(plan.LinkSite, siteZoneWidth, siteZoneHeight, hub);

        if (hub == start)
        {
            CreatePath(start, endpoint, BlockType.Link, linkWidth, linkPathHorizontalChance, false);
            return;
        }

        CreatePath(start, hub, BlockType.Link, linkWidth, linkPathHorizontalChance, false);
        CreatePath(hub, endpoint, BlockType.Link, linkWidth, linkPathHorizontalChance, false);
    }

    Vector2Int ResolveLinkHub(Vector2Int spawnPoint, Vector2Int oppositeSpawn)
    {
        Vector2Int mapCenter = new Vector2Int(width / 2, height / 2);
        Vector2Int laneMid = new Vector2Int(
            (spawnPoint.x + oppositeSpawn.x) / 2,
            (spawnPoint.y + oppositeSpawn.y) / 2);
        Vector2Int hub = Vector2Int.RoundToInt(Vector2.Lerp((Vector2)laneMid, (Vector2)mapCenter, linkHubBlendToCenter));

        bool shouldRouteViaRoom = routeLinkViaRoom && Random.value <= linkViaRoomChance;
        if (shouldRouteViaRoom && TryGetRoomHubPoint(out Vector2Int roomHub))
            hub = Vector2Int.RoundToInt(Vector2.Lerp((Vector2)hub, (Vector2)roomHub, 0.75f));

        Vector2Int sideDirection = new Vector2Int(
            Mathf.Clamp(spawnPoint.x - oppositeSpawn.x, -1, 1),
            Mathf.Clamp(spawnPoint.y - oppositeSpawn.y, -1, 1));
        if (sideDirection != Vector2Int.zero && linkRoomExitOffset > 0)
            hub += sideDirection * linkRoomExitOffset;

        hub = new Vector2Int(ClampGridX(hub.x), ClampGridZ(hub.y));
        if (CanTraverseForPath(hub.x, hub.y))
            return hub;

        return FindNearestWalkablePoint(hub, spawnPoint);
    }

    Vector2Int FindNearestWalkablePoint(Vector2Int origin, Vector2Int fallback)
    {
        if (CanTraverseForPath(origin.x, origin.y))
            return origin;

        int maxRadius = Mathf.Max(width, height);
        for (int radius = 1; radius <= maxRadius; radius++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dz = -radius; dz <= radius; dz++)
                {
                    if (Mathf.Abs(dx) != radius && Mathf.Abs(dz) != radius)
                        continue;

                    int x = ClampGridX(origin.x + dx);
                    int z = ClampGridZ(origin.y + dz);
                    if (CanTraverseForPath(x, z))
                        return new Vector2Int(x, z);
                }
            }
        }

        return fallback;
    }

    bool TryGetRoomHubPoint(out Vector2Int waypoint)
    {
        waypoint = default;
        if (!zoneBlocks.TryGetValue(BlockType.Room, out HashSet<BlockComponent> roomBlocks) ||
            roomBlocks.Count == 0)
            return false;

        Vector2Int referenceCenter = new Vector2Int(
            (spawnA.x + spawnB.x + siteA.x + siteB.x) / 4,
            (spawnA.y + spawnB.y + siteA.y + siteB.y) / 4);

        float bestScore = float.PositiveInfinity;
        bool found = false;
        foreach (BlockComponent block in roomBlocks)
        {
            int x = Mathf.RoundToInt(block.transform.position.x / blockSize);
            int z = Mathf.RoundToInt(block.transform.position.z / blockSize);
            Vector2Int candidate = new Vector2Int(x, z);
            if (!CanTraverseForPath(candidate.x, candidate.y))
                continue;

            float dCenter = Vector2Int.Distance(referenceCenter, candidate);
            float dSpawnA = Vector2Int.Distance(spawnA, candidate);
            float dSpawnB = Vector2Int.Distance(spawnB, candidate);
            float score = dCenter + Mathf.Abs(dSpawnA - dSpawnB) * 0.3f;
            if (score >= bestScore)
                continue;

            bestScore = score;
            waypoint = candidate;
            found = true;
        }

        return found;
    }

    Vector2Int GetRandomEdgePoint(Vector2Int zoneOrigin, int zoneWidth, int zoneHeight, Vector2Int referencePoint)
    {
        Vector2Int topCenter = new Vector2Int(zoneOrigin.x + zoneWidth / 2, zoneOrigin.y);
        Vector2Int bottomCenter = new Vector2Int(zoneOrigin.x + zoneWidth / 2, zoneOrigin.y + zoneHeight - 1);
        Vector2Int leftCenter = new Vector2Int(zoneOrigin.x, zoneOrigin.y + zoneHeight / 2);
        Vector2Int rightCenter = new Vector2Int(zoneOrigin.x + zoneWidth - 1, zoneOrigin.y + zoneHeight / 2);

        float dTop = Vector2Int.Distance(referencePoint, topCenter);
        float dBottom = Vector2Int.Distance(referencePoint, bottomCenter);
        float dLeft = Vector2Int.Distance(referencePoint, leftCenter);
        float dRight = Vector2Int.Distance(referencePoint, rightCenter);

        ZoneEdge chosenEdge = ZoneEdge.Top;
        float min = dTop;
        if (dBottom < min) { min = dBottom; chosenEdge = ZoneEdge.Bottom; }
        if (dLeft < min) { min = dLeft; chosenEdge = ZoneEdge.Left; }
        if (dRight < min) { chosenEdge = ZoneEdge.Right; }

        switch (chosenEdge)
        {
            case ZoneEdge.Top:
                return new Vector2Int(
                    ClampGridX(Random.Range(zoneOrigin.x, zoneOrigin.x + zoneWidth)),
                    ClampGridZ(zoneOrigin.y));
            case ZoneEdge.Bottom:
                return new Vector2Int(
                    ClampGridX(Random.Range(zoneOrigin.x, zoneOrigin.x + zoneWidth)),
                    ClampGridZ(zoneOrigin.y + zoneHeight - 1));
            case ZoneEdge.Left:
                return new Vector2Int(
                    ClampGridX(zoneOrigin.x),
                    ClampGridZ(Random.Range(zoneOrigin.y, zoneOrigin.y + zoneHeight)));
            case ZoneEdge.Right:
                return new Vector2Int(
                    ClampGridX(zoneOrigin.x + zoneWidth - 1),
                    ClampGridZ(Random.Range(zoneOrigin.y, zoneOrigin.y + zoneHeight)));
            default:
                return new Vector2Int(ClampGridX(zoneOrigin.x), ClampGridZ(zoneOrigin.y));
        }
    }

    Vector2Int GetClosestEdgePoint(Vector2Int zoneOrigin, int zoneWidth, int zoneHeight, Vector2Int referencePoint)
    {
        Vector2Int topCenter = new Vector2Int(zoneOrigin.x + zoneWidth / 2, zoneOrigin.y);
        Vector2Int bottomCenter = new Vector2Int(zoneOrigin.x + zoneWidth / 2, zoneOrigin.y + zoneHeight - 1);
        Vector2Int leftCenter = new Vector2Int(zoneOrigin.x, zoneOrigin.y + zoneHeight / 2);
        Vector2Int rightCenter = new Vector2Int(zoneOrigin.x + zoneWidth - 1, zoneOrigin.y + zoneHeight / 2);

        float dTop = Vector2Int.Distance(referencePoint, topCenter);
        float dBottom = Vector2Int.Distance(referencePoint, bottomCenter);
        float dLeft = Vector2Int.Distance(referencePoint, leftCenter);
        float dRight = Vector2Int.Distance(referencePoint, rightCenter);

        Vector2Int closest = topCenter;
        float best = dTop;
        if (dBottom < best)
        {
            best = dBottom;
            closest = bottomCenter;
        }

        if (dLeft < best)
        {
            best = dLeft;
            closest = leftCenter;
        }

        if (dRight < best)
            closest = rightCenter;

        return new Vector2Int(ClampGridX(closest.x), ClampGridZ(closest.y));
    }

    Vector2Int GetRandomPointNear(Vector2Int basePoint, int offsetRange)
    {
        int offsetX = Random.Range(-offsetRange, offsetRange + 1);
        int offsetY = Random.Range(-offsetRange, offsetRange + 1);
        return new Vector2Int(ClampGridX(basePoint.x + offsetX), ClampGridZ(basePoint.y + offsetY));
    }

    void ClearZone(int startX, int startZ, int sizeX, int sizeZ, BlockType type)
    {
        for (int x = startX; x < startX + sizeX; x++)
        {
            for (int z = startZ; z < startZ + sizeZ; z++)
                TryMarkBlock(x, z, type);
        }
    }

    void MarkRectAround(Vector2Int center, int sizeX, int sizeZ, BlockType type)
    {
        int clampedSizeX = Mathf.Max(1, sizeX);
        int clampedSizeZ = Mathf.Max(1, sizeZ);
        int startX = center.x - clampedSizeX / 2;
        int startZ = center.y - clampedSizeZ / 2;
        ClearZone(startX, startZ, clampedSizeX, clampedSizeZ, type);
    }

    bool IsRectInsideMap(Vector2Int origin, int sizeX, int sizeZ)
    {
        return origin.x >= 0 && origin.y >= 0 &&
               origin.x + sizeX <= width && origin.y + sizeZ <= height;
    }

    bool RectsOverlap(Vector2Int aOrigin, int aWidth, int aHeight, Vector2Int bOrigin, int bWidth, int bHeight)
    {
        bool separated = aOrigin.x + aWidth <= bOrigin.x ||
                         bOrigin.x + bWidth <= aOrigin.x ||
                         aOrigin.y + aHeight <= bOrigin.y ||
                         bOrigin.y + bHeight <= aOrigin.y;
        return !separated;
    }

    Vector2Int GetSiteCenter(Vector2Int origin)
    {
        return new Vector2Int(origin.x + siteZoneWidth / 2, origin.y + siteZoneHeight / 2);
    }

    int ManhattanDistance(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }

    void Shuffle<T>(List<T> values)
    {
        for (int i = values.Count - 1; i > 0; i--)
        {
            int swapIndex = Random.Range(0, i + 1);
            (values[i], values[swapIndex]) = (values[swapIndex], values[i]);
        }
    }

    Vector2Int RandomDirection4()
    {
        return Random.Range(0, 4) switch
        {
            0 => Vector2Int.right,
            1 => Vector2Int.left,
            2 => Vector2Int.up,
            _ => Vector2Int.down
        };
    }

    int CountNeighborType(int gridX, int gridZ, BlockType blockType)
    {
        int count = 0;
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                if (dx == 0 && dz == 0)
                    continue;

                int nx = gridX + dx;
                int nz = gridZ + dz;
                if (!IsInsideMap(nx, nz))
                    continue;

                if (mapGrid[nx, nz].blockType.Current == blockType)
                    count++;
            }
        }

        return count;
    }

    int CountNonFloorContacts(int gridX, int gridZ)
    {
        int count = 0;
        int[] dx = { 0, 1, 0, -1 };
        int[] dz = { 1, 0, -1, 0 };
        for (int i = 0; i < 4; i++)
        {
            int nx = gridX + dx[i];
            int nz = gridZ + dz[i];
            if (!IsInsideMap(nx, nz))
                continue;

            BlockType type = mapGrid[nx, nz].blockType.Current;
            if (type != BlockType.Floor && type != BlockType.Wall && type != BlockType.Room)
                count++;
        }

        return count;
    }
}
