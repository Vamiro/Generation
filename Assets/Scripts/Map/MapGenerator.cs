using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;
#if UNITY_EDITOR
using UnityEditor;
#endif

public partial class MapGenerator : MonoBehaviour
{
    [Header("Размер карты")]
    [SerializeField] private int width = 20;    // Количество блоков по X
    [SerializeField] private int height = 20;   // Количество блоков по Z

    [Header("Параметры блока")]
    [SerializeField, Min(0.01f)] private float blockSize = 1f; // Если увеличить, позиции масштабируются

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
    [SerializeField, Min(0)] private int spawnHorizontalOffset = 3;
    [SerializeField, Min(1)] private int siteDistanceFromSpawnMin = 6;
    [SerializeField, Min(1)] private int siteDistanceFromSpawnMax = 64;
    [SerializeField, Min(0)] private int siteFairnessTolerance = 5;
    [SerializeField, Min(1)] private int sitePlacementAttempts = 30;
    [SerializeField, Min(1)] private int siteEntryMin = 2;
    [SerializeField, Min(0)] private int sitePairDistanceMin = 4;

    [Header("Поведение путей")]
    [SerializeField, Range(0f, 1f)] private float mainPathHorizontalChance = 0.85f;
    [SerializeField, Range(0f, 1f)] private float linkPathHorizontalChance = 0.15f;
    [SerializeField, Range(1, 2)] private int linkConnectionCount = 2;
    [SerializeField] private bool preferShortestConnections = true;
    [SerializeField] private bool useFixedSeed;
    [SerializeField] private int generationSeed = 42;
    [SerializeField, ReadOnlyInInspector] private int currentGenerationSeed;
    [SerializeField] private bool spawnAIsDefender = true;
    [SerializeField, Min(0)] private int mainWidth = 1;      // Толщина основных путей
    [SerializeField, Min(0)] private int linkWidth = 1;      // Толщина фланговых путей
    [SerializeField, Min(0)] private int roadWidthRandomDelta = 1;

    [Header("Детализация дорог")]
    [SerializeField] private bool addStraightRoadIndentations = true;
    [SerializeField, Min(3)] private int straightRoadIndentMinLength = 8;
    [SerializeField, Min(2)] private int straightRoadIndentInterval = 4;
    [SerializeField, Min(1)] private int straightRoadIndentDepth = 1;
    [SerializeField, Range(0f, 1f)] private float straightRoadIndentChance = 0.65f;

    [Header("Маршруты link")]
    [SerializeField] private bool routeLinkViaRoom = true;
    [SerializeField, Range(0f, 1f)] private float linkViaRoomChance = 1f;
    [SerializeField, Min(0)] private int linkRoomExitOffset = 1;
    [SerializeField, Range(0f, 1f)] private float linkHubBlendToCenter = 0.5f;
    [SerializeField] private bool linkCanMergeIntoMain = true;
    [SerializeField, Range(0f, 5f)] private float astarLinkMergeMainPenalty = 0.25f;

    [Header("A*")]
    [SerializeField, Range(0f, 5f)] private float astarTurnPenalty = 0.35f;
    [SerializeField, Range(0f, 10f)] private float astarRoadReusePenalty = 1.5f;
    [SerializeField, Range(0f, 10f)] private float astarLinkAvoidMainPenalty = 2f;
    [SerializeField, Range(0f, 10f)] private float astarMainAvoidLinkPenalty = 1f;
    [SerializeField, Range(0f, 5f)] private float astarBorderPenalty = 1.5f;
    [SerializeField, Range(0f, 5f)] private float astarLinkCenterPenalty = 1f;
    [SerializeField] private bool astarAllowDiagonalMoves = false;
    [SerializeField, Range(0f, 5f)] private float astarDiagonalPenalty = 1.25f;
    [SerializeField, Range(0f, 2f)] private float astarRandomJitter = 0.05f;
    [SerializeField] private bool useCircularPathBrush = false;
    [SerializeField] private bool useFallbackPathWhenAstarFails = true;

    [Header("Комната")]
    [SerializeField] private bool generateRoomZone = true;
    [SerializeField] private Vector2Int roomSize = new Vector2Int(5, 5);
    [SerializeField, Min(0)] private int roomGrowthSteps = 24;
    [SerializeField, Range(0f, 1f)] private float roomGrowthBaseChance = 0.35f;
    [SerializeField, Range(0f, 2f)] private float roomGrowthDistanceFactor = 0.45f;
    [SerializeField, Range(0f, 1f)] private float roomEmptyPenaltyFactor = 0.08f;
    [SerializeField, Min(0)] private int roomAntiMergeContactThreshold = 1;

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
    [SerializeField, Min(1)] private int narrowCorridorWidthThreshold = 2;
    [SerializeField, Range(0.5f, 2f)] private float narrowCorridorCoverMultiplier = 1.3f;
    [SerializeField, Range(0.5f, 2f)] private float openAreaCoverMultiplier = 1.15f;

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
        BlockType.Link
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
        currentGenerationSeed = ResolveGenerationSeed();
        Random.InitState(currentGenerationSeed);

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

    int ResolveGenerationSeed()
    {
        return useFixedSeed ? generationSeed : Guid.NewGuid().GetHashCode();
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

    private readonly struct TeamRoutePlan
    {
        public TeamRoutePlan(Vector2Int spawn, Vector2Int linkSite)
        {
            Spawn = spawn;
            LinkSite = linkSite;
        }

        public Vector2Int Spawn { get; }
        public Vector2Int LinkSite { get; }
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
        MarkRoomZone();
        BuildStructuredRoutes();

        GrowRoomZones();
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