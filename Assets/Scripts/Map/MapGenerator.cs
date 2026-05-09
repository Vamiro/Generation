using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;
#if UNITY_EDITOR
using UnityEditor;
#endif

public partial class MapGenerator : MonoBehaviour
{
    [Header("Размер карты")]
    [SerializeField] private int width = 20; // Ширина карты в клетках по оси X.
    [SerializeField] private int height = 20; // Высота карты в клетках по оси Z.

    [Header("Параметры блока")]
    [SerializeField, Min(0.01f)] private float blockSize = 1f; // Размер одной клетки в мировых координатах.

    [Header("Настройки зон")]
    [SerializeField] private int spawnZoneSizeMin = 8; // Минимальный размер зоны спавна.
    [SerializeField] private int spawnZoneSizeMax = 10; // Максимальный размер зоны спавна.
    [SerializeField] private int siteZoneWidth = 4; // Ширина зоны сайта.
    [SerializeField] private int siteZoneHeight = 4; // Высота зоны сайта.

    [Header("Позиционирование зон")]
    [SerializeField, Min(0)] private int innerPadding = 4; // Внутренний отступ от границы карты для размещения зон.
    [SerializeField, Range(0.05f, 0.5f)] private float spawnBandRatio = 0.25f; // Доля высоты карты для полос размещения спавнов.
    [SerializeField, Range(0f, 0.5f)] private float horizontalJitterRatio = 0.1f; // Доля ширины, задающая случайный сдвиг зон по X.
    [SerializeField, Range(0.1f, 0.9f)] private float siteDepthRatio = 0.33f; // Относительная глубина размещения сайтов от края.
    [SerializeField, Min(0)] private int spawnOffset = 2; // Радиус случайного смещения старта link от центра спавна.
    [SerializeField, Min(0)] private int spawnHorizontalOffset = 3; // Максимальный сдвиг спавна B относительно спавна A по X.
    [SerializeField, Min(1)] private int siteDistanceFromSpawnMin = 6; // Минимальная дистанция от спавна до сайта.
    [SerializeField, Min(1)] private int siteDistanceFromSpawnMax = 64; // Максимальная дистанция от спавна до сайта.
    [SerializeField, Min(0)] private int siteFairnessTolerance = 5; // Допустимая разница дистанций до сайта для fairness.
    [SerializeField, Min(1)] private int sitePlacementAttempts = 30; // Количество попыток подобрать валидные позиции сайтов.
    [SerializeField, Min(1)] private int siteEntryMin = 2; // Минимум внешних входов в зону сайта.
    [SerializeField, Min(0)] private int sitePairDistanceMin = 4; // Минимальная дистанция между центрами сайтов.

    [Header("Маршруты link")]
    [SerializeField, Range(1, 2)] private int linkConnectionCount = 2; // Базовое число link-маршрутов до fallback-покрытия сайтов.
    [SerializeField] private bool preferShortestConnections = true; // Приоритет более коротких link-маршрутов.
    [SerializeField] private bool spawnAIsDefender = true; // Назначить спавн A стороной defender.
    [FormerlySerializedAs("routeLinkViaRoom")]
    [SerializeField] private bool routeLinkViaNeutral = true; // Разрешить прокладку link через нейтральную зону.
    [FormerlySerializedAs("linkViaRoomChance")]
    [SerializeField, Range(0f, 1f)] private float linkViaNeutralChance = 1f; // Вероятность маршрутизации link через нейтральную зону.
    [FormerlySerializedAs("linkRoomExitOffset")]
    [SerializeField, Min(0)] private int linkNeutralExitOffset = 1; // Смещение точки выхода link из нейтральной зоны/хаба.
    [SerializeField, Range(0f, 1f)] private float linkHubBlendToCenter = 0.5f; // Насколько hub link тянется к центру карты.

    [Header("Геометрия дорог")]
    [SerializeField, Min(0)] private int mainWidth = 1; // Базовая толщина основных путей.
    [SerializeField, Min(0)] private int linkWidth = 1; // Базовая толщина фланговых путей.
    [SerializeField, Min(0)] private int roadWidthRandomDelta = 1; // Случайный разброс толщины дороги вокруг базовой.
    [SerializeField] private bool useCircularPathBrush = false; // Использовать круговую кисть при расширении толщины дороги.

    [Header("Сид генерации")]
    [SerializeField] private bool useFixedSeed; // Использовать фиксированный seed вместо случайного.
    [SerializeField] private int generationSeed = 42; // Seed генерации, если включен фиксированный seed.
    [SerializeField, ReadOnlyInInspector] private int currentGenerationSeed; // Seed, примененный в текущей генерации.

    [Header("A*")]
    [FormerlySerializedAs("mainPathHorizontalChance")]
    [SerializeField, Range(0f, 1f)] private float astarMainHorizontalBias = 0.85f; // Приоритет горизонтального движения для main-пути при равных узлах.
    [FormerlySerializedAs("linkPathHorizontalChance")]
    [SerializeField, Range(0f, 1f)] private float astarLinkHorizontalBias = 0.15f; // Приоритет горизонтального движения для link-пути при равных узлах.
    [SerializeField, Range(0f, 5f)] private float astarTurnPenalty = 0.35f; // Штраф за поворот маршрута.
    [SerializeField, Range(0f, 10f)] private float astarRoadReusePenalty = 1.5f; // Штраф за повторное использование уже занятых дорог.
    [SerializeField, Range(0f, 10f)] private float astarLinkAvoidMainPenalty = 2f; // Дополнительный штраф link за движение по main.
    [SerializeField, Range(0f, 10f)] private float astarMainAvoidLinkPenalty = 1f; // Дополнительный штраф main за движение по link.
    [SerializeField, Range(0f, 5f)] private float astarBorderPenalty = 1.5f; // Штраф за прохождение близко к границам карты.
    [SerializeField, Range(0f, 5f)] private float astarLinkCenterPenalty = 1f; // Штраф link за прохождение через центральную область.
    [SerializeField] private bool astarAllowDiagonalMoves = false; // Разрешить диагональные шаги в A*.
    [SerializeField, Range(0f, 5f)] private float astarDiagonalPenalty = 1.25f; // Стоимость диагонального шага относительно прямого.
    [SerializeField, Range(0f, 2f)] private float astarRandomJitter = 0.05f; // Случайный шум стоимости пути для вариативности.
    [SerializeField] private bool linkCanMergeIntoMain = true; // Разрешить link использовать клетки main в A*.
    [SerializeField, Range(0f, 5f)] private float astarLinkMergeMainPenalty = 0.25f; // Штраф A* за слияние link в main.
    [SerializeField] private bool useFallbackPathWhenAstarFails = true; // Строить fallback-путь, если A* не нашел маршрут.

    [Header("Нейтральная зона")]
    [FormerlySerializedAs("generateRoomZone")]
    [SerializeField] private bool generateNeutralZone = true; // Генерировать фиксированную нейтральную зону.
    [FormerlySerializedAs("roomSize")]
    [SerializeField] private Vector2Int neutralZoneSize = new Vector2Int(5, 5); // Размер нейтральной зоны.

    [Header("Префабы")]
    [SerializeField] private BlockComponent floorPrefab; // Префаб базовой клетки пола.
    [SerializeField] private BlockComponent wallPrefab; // Префаб клетки стены.
    [SerializeField] private GameObject coverPrefab; // Префаб объекта укрытия.

    [Header("Материалы")]
    [SerializeField] private Material spawnMaterial; // Материал для зоны спавна.
    [SerializeField] private Material roadMaterial; // Резервный материал для дорожных зон.
    [SerializeField] private Material siteMaterial; // Материал для зоны сайта.
    [SerializeField] private Material mainMaterial; // Материал для main-дорог.
    [SerializeField] private Material linkMaterial; // Материал для link-дорог.
    [SerializeField] private Material floorMaterial; // Материал для обычного пола.
    [SerializeField] private Material wallMaterial; // Материал для стен.
    [FormerlySerializedAs("roomMaterial")]
    [SerializeField] private Material neutralMaterial; // Материал для нейтральной зоны.

    [Header("Настройки укрытий")]
    [SerializeField] private float coverSpawnMultiplier = 1f; // Общий множитель вероятности появления укрытий.
    [SerializeField] private float coverMinProbabilitySpawn = 0.3f; // Минимальная вероятность укрытия в spawn-зоне.
    [SerializeField] private float coverMaxProbabilitySpawn = 0.6f; // Максимальная вероятность укрытия в spawn-зоне.
    [SerializeField] private float coverMinProbabilitySite  = 0.2f; // Минимальная вероятность укрытия в site-зоне.
    [SerializeField] private float coverMaxProbabilitySite  = 0.5f; // Максимальная вероятность укрытия в site-зоне.
    [SerializeField] private float coverMinProbabilityMain  = 0.1f; // Минимальная вероятность укрытия в main-зоне.
    [SerializeField] private float coverMaxProbabilityMain  = 0.8f; // Максимальная вероятность укрытия в main-зоне.
    [SerializeField] private float coverMinProbabilityLink  = 0.1f; // Минимальная вероятность укрытия в link-зоне.
    [SerializeField] private float coverMaxProbabilityLink  = 0.8f; // Максимальная вероятность укрытия в link-зоне.
    [FormerlySerializedAs("coverMinProbabilityRoom")]
    [SerializeField] private float coverMinProbabilityNeutral  = 0.5f; // Минимальная вероятность укрытия в neutral-зоне.
    [FormerlySerializedAs("coverMaxProbabilityRoom")]
    [SerializeField] private float coverMaxProbabilityNeutral  = 0.7f; // Максимальная вероятность укрытия в neutral-зоне.
    [SerializeField, Min(1)] private int narrowCorridorWidthThreshold = 2; // Порог ширины, ниже которого проход считается узким.
    [SerializeField, Range(0.5f, 2f)] private float narrowCorridorCoverMultiplier = 1.3f; // Множитель вероятности укрытий в узких коридорах.
    [SerializeField, Range(0.5f, 2f)] private float openAreaCoverMultiplier = 1.15f; // Множитель вероятности укрытий на открытых участках.

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
        BlockType.Neutral
    };

    private static readonly BlockType[] CoverZones =
    {
        BlockType.Spawn,
        BlockType.Site,
        BlockType.Main,
        BlockType.Link,
        BlockType.Neutral
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
        currentGenerationSeed = ResolveGenerationSeed();
        Random.InitState(currentGenerationSeed);

        InitializeZoneCollections();
        CreateFloorGrid();

        // Разметка зон (Спавны, Сайты, Main, Link, Neutral)
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
        MarkNeutralZone();
        BuildStructuredRoutes();
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
            case BlockType.Neutral:
                return neutralMaterial;
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