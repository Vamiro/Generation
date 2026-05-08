using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class MapGenerator : MonoBehaviour
{
    [Header("Размер карты")]
    [SerializeField] private int width = 20;    // Количество блоков по X
    [SerializeField] private int height = 20;   // Количество блоков по Z

    [Header("Параметры блока")]
    [SerializeField, Min(0.01f)] private float blockSize = 1f; // Если увеличить, позиции масштабируются
    [SerializeField, Min(0)] private int mainWidth = 1;      // Толщина основных путей
    [SerializeField, Min(0)] private int linkWidth = 1;      // Толщина фланговых путей

    [Header("Настройки зон")]
    [SerializeField] private int spawnZoneSizeMin = 8;   // Мин. размер зоны спавна
    [SerializeField] private int spawnZoneSizeMax = 10;    // Макс. размер зоны спавна
    [SerializeField] private int siteZoneWidth = 4;        // Ширина зоны сайта
    [SerializeField] private int siteZoneHeight = 4;       // Высота зоны сайта

    [Header("Позиционирование зон")]
    [SerializeField, Min(0)] private int innerPadding = 4;
    [SerializeField, Range(0.05f, 0.5f)] private float spawnBandRatio = 0.25f;
    [SerializeField, Range(0f, 0.5f)] private float horizontalJitterRatio = 0.1f;
    [SerializeField, Range(0.1f, 0.9f)] private float siteDepthRatio = 0.33f;
    [SerializeField, Min(0)] private int spawnOffset = 2;

    [Header("Поведение путей")]
    [SerializeField, Range(0f, 1f)] private float mainPathHorizontalChance = 0.85f;
    [SerializeField, Range(0f, 1f)] private float linkPathHorizontalChance = 0.15f;
    [SerializeField] private bool useFixedSeed;
    [SerializeField] private int generationSeed = 42;

    [Header("Комната")]
    [SerializeField] private bool generateRoomZone;
    [SerializeField] private Vector2Int roomSize = new Vector2Int(5, 5);

    [Header("Префабы")]
    [SerializeField] private BlockComponent floorPrefab;
    [SerializeField] private BlockComponent wallPrefab;
    [SerializeField] private GameObject coverPrefab; // Префаб укрытия

    [Header("Материалы")]
    [SerializeField] private Material spawnMaterial;
    [SerializeField] private Material roadMaterial;
    [SerializeField] private Material siteMaterial;
    [SerializeField] private Material mainMaterial;
    [SerializeField] private Material linkMaterial;
    [SerializeField] private Material floorMaterial;
    [SerializeField] private Material wallMaterial;
    [SerializeField] private Material roomMaterial; // Материал для комнаты

    [Header("Настройки укрытий")]
    [SerializeField] private float coverSpawnMultiplier = 1f; // Общий множитель вероятности
    // Для каждой зоны можно задать минимальную и максимальную вероятность спавна укрытия:
    [SerializeField] private float coverMinProbabilitySpawn = 0.3f;
    [SerializeField] private float coverMaxProbabilitySpawn = 0.6f;
    [SerializeField] private float coverMinProbabilitySite  = 0.2f;
    [SerializeField] private float coverMaxProbabilitySite  = 0.5f;
    [SerializeField] private float coverMinProbabilityMain  = 0.1f; // На центральной части дороги вероятность мала
    [SerializeField] private float coverMaxProbabilityMain  = 0.8f; // На краях – высокая
    [SerializeField] private float coverMinProbabilityLink  = 0.1f;
    [SerializeField] private float coverMaxProbabilityLink  = 0.8f;
    [SerializeField] private float coverMinProbabilityRoom  = 0.5f;
    [SerializeField] private float coverMaxProbabilityRoom  = 0.7f;

    private BlockComponent[,] mapGrid;
    private Vector2Int spawnA, spawnB;
    private Vector2Int siteA, siteB;

    // Словарь хранит уникальные блоки по зонам (без дублей).
    private Dictionary<BlockType, HashSet<BlockComponent>> zoneBlocks;

    private static readonly BlockType[] TrackedZones =
    {
        BlockType.Spawn,
        BlockType.Site,
        BlockType.Main,
        BlockType.Link,
        BlockType.Room
    };

    private static readonly BlockType[] CoverZones =
    {
        BlockType.Spawn,
        BlockType.Site,
        BlockType.Main,
        BlockType.Link,
        BlockType.Room
    };

    private static readonly BlockType[] RuntimeZoneTypes =
    {
        BlockType.Spawn,
        BlockType.Site,
        BlockType.Main,
        BlockType.Link,
        BlockType.Neutral
    };

    private int nextZoneId;
    private bool isRegenerating;

    void Start()
    {
        GenerateMap();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.R))
            DestroyMap();
    }

    void DestroyMap()
    {
        if (isRegenerating)
            return;

        StartCoroutine(RegenerateMapRoutine());
    }

    IEnumerator RegenerateMapRoutine()
    {
        isRegenerating = true;

#if UNITY_EDITOR
        if (Selection.activeGameObject != null && Selection.activeGameObject.transform.IsChildOf(transform))
            Selection.activeGameObject = gameObject;
#endif

        MapManager mapManager = MapManager.Instance;
        if (mapManager != null)
            mapManager.ClearZones();

        List<Transform> children = new();
        foreach (Transform child in transform)
            children.Add(child);

        foreach (Transform child in children)
            Destroy(child.gameObject);

        yield return null;
        GenerateMap();
        isRegenerating = false;
    }

    void GenerateMap()
    {
        if (useFixedSeed)
            Random.InitState(generationSeed);

        InitializeZoneCollections();
        CreateFloorGrid();

        // Разметка зон (Спавны, Сайты, Main, Link, Комната)
        MarkZones();

        // Заполняем границы карты стенами
        FillBorderWalls();
        ValidateGeneratedLayout();
        BuildAndRegisterZoneObjects();

        // Обновляем материалы нижнего этажа
        UpdateMap();

        // Формируем уровень стен (дублирование блоков)
        DuplicateWallBlocks();

        // Расставляем укрытия по обновленной логике
        PlaceCovers();
    }

    void InitializeZoneCollections()
    {
        zoneBlocks = new Dictionary<BlockType, HashSet<BlockComponent>>(TrackedZones.Length);
        foreach (BlockType zone in TrackedZones)
            zoneBlocks[zone] = new HashSet<BlockComponent>();
    }

    void CreateFloorGrid()
    {
        mapGrid = new BlockComponent[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                Vector3 pos = new Vector3(x * blockSize, 0, z * blockSize);
                BlockComponent floorBlock = Instantiate(floorPrefab, pos, Quaternion.identity, transform);
                floorBlock.blockType.Set(BlockType.Floor);
                mapGrid[x, z] = floorBlock;
            }
        }
    }

    private struct LayoutSettings
    {
        public int UsableWidth;
        public int UsableHeight;
        public int SpawnBandSize;
        public int HorizontalJitter;
        public int SiteDepth;
    }

    private enum ZoneEdge
    {
        Top,
        Bottom,
        Left,
        Right
    }

    private sealed class ZoneRegion
    {
        public BlockType Type;
        public readonly List<Vector2Int> Cells = new();
        public Vector2Int Min;
        public Vector2Int Max;
        public MapZoneComponent ZoneComponent;

        public Vector3 GetCenterWorld(float cellSize)
        {
            return new Vector3(
                (Min.x + Max.x) * 0.5f * cellSize,
                0f,
                (Min.y + Max.y) * 0.5f * cellSize);
        }
    }

    void MarkZones()
    {
        LayoutSettings layout = BuildLayoutSettings();
        PlaceSpawnZones(layout);
        PlaceSiteZones(layout);

        CreateLinkConnection(spawnA, siteB);
        CreateLinkConnection(spawnB, siteA);
        CreateLinkConnection(spawnA, siteA);
        CreateLinkConnection(spawnB, siteB);

        CreateMainConnection(spawnA, siteA);
        CreateMainConnection(spawnB, siteB);
        CreateMainConnection(spawnA, siteB);
        CreateMainConnection(spawnB, siteA);

        MarkRoomZone();
    }

    LayoutSettings BuildLayoutSettings()
    {
        int usableWidth = Mathf.Max(1, width - innerPadding);
        int usableHeight = Mathf.Max(1, height - innerPadding);

        return new LayoutSettings
        {
            UsableWidth = usableWidth,
            UsableHeight = usableHeight,
            SpawnBandSize = Mathf.Max(1, Mathf.RoundToInt(usableHeight * spawnBandRatio)),
            HorizontalJitter = Mathf.Max(1, Mathf.RoundToInt(usableWidth * horizontalJitterRatio)),
            SiteDepth = Mathf.Clamp(Mathf.RoundToInt(usableHeight * siteDepthRatio), 0, usableHeight - 1)
        };
    }

    void PlaceSpawnZones(LayoutSettings layout)
    {
        int centerX = layout.UsableWidth / 2;
        int topBandEnd = Mathf.Max(1, layout.SpawnBandSize);
        int bottomBandStart = Mathf.Max(0, layout.UsableHeight - layout.SpawnBandSize);

        spawnA = new Vector2Int(
            ClampGridX(centerX + Random.Range(-layout.HorizontalJitter, layout.HorizontalJitter + 1)),
            ClampGridZ(Random.Range(0, topBandEnd)));

        spawnB = new Vector2Int(
            ClampGridX(centerX + Random.Range(-layout.HorizontalJitter, layout.HorizontalJitter + 1)),
            ClampGridZ(Random.Range(bottomBandStart, layout.UsableHeight)));

        int spawnSizeA = Random.Range(spawnZoneSizeMin, spawnZoneSizeMax + 1);
        ClearZone(spawnA.x, spawnA.y, spawnSizeA / 2, spawnSizeA - spawnSizeA / 2, BlockType.Spawn);

        int spawnSizeB = Random.Range(spawnZoneSizeMin, spawnZoneSizeMax + 1);
        ClearZone(spawnB.x, spawnB.y, spawnSizeB / 2, spawnSizeB - spawnSizeB / 2, BlockType.Spawn);
    }

    void PlaceSiteZones(LayoutSettings layout)
    {
        int sideBandSize = Mathf.Max(1, layout.SpawnBandSize);
        int rightBandStart = Mathf.Max(0, layout.UsableWidth - sideBandSize);
        int siteZ = ClampGridZ(layout.SiteDepth + Random.Range(-layout.HorizontalJitter, layout.HorizontalJitter + 1));

        siteA = new Vector2Int(ClampGridX(Random.Range(0, sideBandSize)), siteZ);
        siteB = new Vector2Int(ClampGridX(Random.Range(rightBandStart, layout.UsableWidth)), siteZ);

        ClearZone(siteA.x, siteA.y, siteZoneWidth, siteZoneHeight, BlockType.Site);
        ClearZone(siteB.x, siteB.y, siteZoneWidth, siteZoneHeight, BlockType.Site);
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

    void CreateMainConnection(Vector2Int spawnPoint, Vector2Int sitePoint)
    {
        Vector2Int endpoint = GetRandomEdgePoint(sitePoint, siteZoneWidth, siteZoneHeight, spawnPoint);
        CreatePath(spawnPoint, endpoint, BlockType.Main, mainWidth, mainPathHorizontalChance, true);
    }

    void CreateLinkConnection(Vector2Int spawnPoint, Vector2Int sitePoint)
    {
        Vector2Int start = GetRandomPointNear(spawnPoint, spawnOffset);
        Vector2Int endpoint = GetRandomEdgePoint(sitePoint, siteZoneWidth, siteZoneHeight, start);
        CreatePath(start, endpoint, BlockType.Link, linkWidth, linkPathHorizontalChance, false);
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

    void CreatePath(
        Vector2Int startPoint,
        Vector2Int endPoint,
        BlockType blockType,
        int pathWidth,
        float horizontalMoveChance,
        bool writeWeight)
    {
        int x = ClampGridX(startPoint.x);
        int z = ClampGridZ(startPoint.y);
        int endX = ClampGridX(endPoint.x);
        int endZ = ClampGridZ(endPoint.y);
        int weight = 1;

        while (x != endX || z != endZ)
        {
            PaintPathBrush(x, z, pathWidth, blockType, writeWeight ? (int?)weight : null);
            StepTowardsTarget(ref x, ref z, endX, endZ, horizontalMoveChance);
            if (writeWeight)
                weight++;
        }

        PaintPathBrush(endX, endZ, pathWidth, blockType, writeWeight ? (int?)weight : null);
    }

    void PaintPathBrush(int centerX, int centerZ, int pathWidth, BlockType blockType, int? weight)
    {
        for (int dx = -pathWidth; dx <= pathWidth; dx++)
        {
            for (int dz = -pathWidth; dz <= pathWidth; dz++)
                TryMarkBlock(centerX + dx, centerZ + dz, blockType, weight);
        }
    }

    void StepTowardsTarget(ref int x, ref int z, int targetX, int targetZ, float horizontalChance)
    {
        bool moveXFirst = Random.value < horizontalChance;
        if (moveXFirst)
        {
            if (MoveAxis(ref x, targetX))
                return;
            MoveAxis(ref z, targetZ);
            return;
        }

        if (MoveAxis(ref z, targetZ))
            return;
        MoveAxis(ref x, targetX);
    }

    bool MoveAxis(ref int current, int target)
    {
        if (current == target)
            return false;

        current += current < target ? 1 : -1;
        return true;
    }

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
                if (visited[x, z] || mapGrid[x, z].blockType.Current != zoneType)
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
        if (!IsInsideMap(x, z) || visited[x, z] || mapGrid[x, z].blockType.Current != zoneType)
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

    void UpdateMap()
    {
        foreach (BlockComponent block in mapGrid)
        {
            Material material = GetMaterialForType(block.blockType.Current);
            if (material != null && block.Renderer != null)
                block.Renderer.material = material;
        }
    }

    Material GetMaterialForType(BlockType blockType)
    {
        switch (blockType)
        {
            case BlockType.Floor:
                return floorMaterial;
            case BlockType.Wall:
                return wallMaterial;
            case BlockType.Spawn:
                return spawnMaterial;
            case BlockType.Main:
                return mainMaterial;
            case BlockType.Link:
                return linkMaterial;
            case BlockType.Site:
                return siteMaterial;
            case BlockType.Road:
                return roadMaterial;
            case BlockType.Room:
                return roomMaterial;
            default:
                return null;
        }
    }

    bool IsBorder(int x, int z)
    {
        return x == 0 || x == width - 1 || z == 0 || z == height - 1;
    }

    void FillBorderWalls()
    {
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (IsBorder(x, z))
                    TryMarkBlock(x, z, BlockType.Wall, trackZone: false);
            }
        }
    }

    void ValidateGeneratedLayout()
    {
        if (!zoneBlocks.TryGetValue(BlockType.Main, out HashSet<BlockComponent> mainBlocks) || mainBlocks.Count == 0)
            Debug.LogWarning("MapGenerator: после генерации отсутствуют Main пути.");

        if (!zoneBlocks.TryGetValue(BlockType.Link, out HashSet<BlockComponent> linkBlocks) || linkBlocks.Count == 0)
            Debug.LogWarning("MapGenerator: после генерации отсутствуют Link пути.");

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (mapGrid[x, z].blockType.Current == BlockType.Site && IsBorder(x, z))
                {
                    Debug.LogWarning($"MapGenerator: сайт попал на границу карты ({x}, {z}).");
                    break;
                }
            }
        }

        Vector2Int[] spawns = { spawnA, spawnB };
        Vector2Int[] sites = { siteA, siteB };
        foreach (Vector2Int spawn in spawns)
        {
            foreach (Vector2Int site in sites)
            {
                if (IsReachable(spawn, site))
                    continue;

                Debug.LogWarning($"MapGenerator: нет пути от спавна {spawn} к сайту {site}.");
            }
        }
    }

    bool IsReachable(Vector2Int start, Vector2Int end)
    {
        int startX = ClampGridX(start.x);
        int startZ = ClampGridZ(start.y);
        int endX = ClampGridX(end.x);
        int endZ = ClampGridZ(end.y);

        if (!IsWalkableCell(startX, startZ) || !IsWalkableCell(endX, endZ))
            return false;

        bool[,] visited = new bool[width, height];
        Queue<Vector2Int> queue = new();
        queue.Enqueue(new Vector2Int(startX, startZ));
        visited[startX, startZ] = true;

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            if (current.x == endX && current.y == endZ)
                return true;

            TryEnqueueReachable(current.x + 1, current.y, visited, queue);
            TryEnqueueReachable(current.x - 1, current.y, visited, queue);
            TryEnqueueReachable(current.x, current.y + 1, visited, queue);
            TryEnqueueReachable(current.x, current.y - 1, visited, queue);
        }

        return false;
    }

    void TryEnqueueReachable(int x, int z, bool[,] visited, Queue<Vector2Int> queue)
    {
        if (!IsInsideMap(x, z) || visited[x, z] || !IsWalkableCell(x, z))
            return;

        visited[x, z] = true;
        queue.Enqueue(new Vector2Int(x, z));
    }

    bool IsWalkableCell(int x, int z)
    {
        return IsInsideMap(x, z) && mapGrid[x, z].blockType.Current != BlockType.Wall;
    }

    void DuplicateWallBlocks()
    {
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (IsBorder(x, z) || mapGrid[x, z].blockType.Current == BlockType.Floor)
                    CreateWallColumn(x, z, 2);
            }
        }
    }

    void CreateWallColumn(int x, int z, int levels)
    {
        for (int level = 1; level <= levels; level++)
        {
            Vector3 pos = new Vector3(x * blockSize, level * blockSize, z * blockSize);
            BlockComponent wallBlock = Instantiate(wallPrefab, pos, Quaternion.identity, transform);
            wallBlock.blockType.Set(BlockType.Wall);
            if (wallMaterial != null && wallBlock.Renderer != null)
                wallBlock.Renderer.material = wallMaterial;
        }
    }

    bool IsEdgeBlock(int gridX, int gridZ, BlockType zoneType)
    {
        int[] dx = { 0, 1, 0, -1 };
        int[] dz = { 1, 0, -1, 0 };
        for (int i = 0; i < 4; i++)
        {
            int nx = gridX + dx[i];
            int nz = gridZ + dz[i];
            if (IsInsideMap(nx, nz) && mapGrid[nx, nz].blockType.Current != zoneType)
                return true;
        }

        return false;
    }

    bool IsJunctionBlock(int gridX, int gridZ, BlockType zoneType)
    {
        int diffCount = 0;
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                if (dx == 0 && dz == 0)
                    continue;

                int nx = gridX + dx;
                int nz = gridZ + dz;
                if (IsInsideMap(nx, nz) && mapGrid[nx, nz].blockType.Current != zoneType)
                    diffCount++;
            }
        }

        return diffCount >= 2;
    }

    void PlaceCovers()
    {
        if (coverPrefab == null)
            return;

        foreach (BlockType zone in CoverZones)
        {
            if (!zoneBlocks.TryGetValue(zone, out HashSet<BlockComponent> blocks))
                continue;

            foreach (BlockComponent block in blocks)
            {
                int gridX = Mathf.RoundToInt(block.transform.position.x / blockSize);
                int gridZ = Mathf.RoundToInt(block.transform.position.z / blockSize);
                float density = CalculateLocalDensity(gridX, gridZ, zone);

                if (!TryGetCoverProbability(zone, gridX, gridZ, density, out float baseProbability))
                    continue;

                float probability = Mathf.Clamp01(baseProbability * coverSpawnMultiplier);
                if (Random.value < probability)
                {
                    Vector3 coverPos = block.transform.position + new Vector3(0f, blockSize, 0f);
                    Instantiate(coverPrefab, coverPos, Quaternion.identity, transform);
                }
            }
        }
    }

    bool TryGetCoverProbability(BlockType zone, int gridX, int gridZ, float density, out float probability)
    {
        probability = 0f;
        if (zone == BlockType.Main || zone == BlockType.Link)
        {
            if (!IsEdgeBlock(gridX, gridZ, zone) && !IsJunctionBlock(gridX, gridZ, zone))
                return false;

            probability = zone == BlockType.Main
                ? Mathf.Lerp(coverMaxProbabilityMain, coverMinProbabilityMain, density)
                : Mathf.Lerp(coverMaxProbabilityLink, coverMinProbabilityLink, density);
            return true;
        }

        switch (zone)
        {
            case BlockType.Spawn:
                probability = Mathf.Lerp(coverMaxProbabilitySpawn, coverMinProbabilitySpawn, density);
                return true;
            case BlockType.Site:
                probability = Mathf.Lerp(coverMaxProbabilitySite, coverMinProbabilitySite, density);
                return true;
            case BlockType.Room:
                probability = Mathf.Lerp(coverMaxProbabilityRoom, coverMinProbabilityRoom, density);
                return true;
            default:
                return false;
        }
    }

    float CalculateLocalDensity(int gridX, int gridZ, BlockType zoneType)
    {
        int count = 0;
        int total = 0;
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                int nx = gridX + dx;
                int nz = gridZ + dz;
                if (!IsInsideMap(nx, nz))
                    continue;

                total++;
                if (mapGrid[nx, nz].blockType.Current == zoneType)
                    count++;
            }
        }

        return total == 0 ? 0f : (float)count / total;
    }

    bool TryMarkBlock(int x, int z, BlockType type, int? weight = null, bool trackZone = true)
    {
        if (!IsInsideMap(x, z))
            return false;

        BlockComponent block = mapGrid[x, z];
        block.blockType.Set(type);
        if (block.blockType.Current != type)
            return false;

        RemoveFromTrackedZones(block);

        if (weight.HasValue)
            block.weight = weight.Value;

        if (trackZone && zoneBlocks.TryGetValue(type, out HashSet<BlockComponent> blocks))
            blocks.Add(block);

        return true;
    }

    void RemoveFromTrackedZones(BlockComponent block)
    {
        if (zoneBlocks == null)
            return;

        foreach (HashSet<BlockComponent> blocks in zoneBlocks.Values)
            blocks.Remove(block);
    }

    bool IsInsideMap(int x, int z)
    {
        return x >= 0 && x < width && z >= 0 && z < height;
    }

    int ClampGridX(int x)
    {
        return Mathf.Clamp(x, 0, width - 1);
    }

    int ClampGridZ(int z)
    {
        return Mathf.Clamp(z, 0, height - 1);
    }
}