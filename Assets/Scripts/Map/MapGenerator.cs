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
    [SerializeField, Tooltip("Ширина карты в клетках по оси X.")] private int width = 20;
    [SerializeField, Tooltip("Высота карты в клетках по оси Z.")] private int height = 20;

    [Header("Параметры блока")]
    [SerializeField, Min(0.01f), Tooltip("Размер одной клетки в мировых координатах.")] private float blockSize = 1f;

    [Header("Настройки зон")]
    [SerializeField, Tooltip("Минимальный размер зоны спавна.")] private int spawnZoneSizeMin = 8;
    [SerializeField, Tooltip("Максимальный размер зоны спавна.")] private int spawnZoneSizeMax = 10;
    [SerializeField, Tooltip("Минимальный размер зоны сайта.")] private int siteZoneSizeMin = 4;
    [SerializeField, Tooltip("Максимальный размер зоны сайта.")] private int siteZoneSizeMax = 6;

    [Header("Позиционирование зон")]
    [SerializeField, Min(0), Tooltip("Внутренний отступ от границы карты для размещения зон.")] private int innerPadding = 4;
    [SerializeField, Min(0), Tooltip("Максимальный сдвиг каждого спавна по X от центра карты.")] private int spawnHorizontalOffset = 3;
    [SerializeField, Range(0f, 0.5f), Tooltip("Смещение горизонтальной линии сайтов от центра между спавнами в сторону защитника (доля половины расстояния).")] private float siteLineBiasToDefender = 0.25f;
    [SerializeField, Min(0), Tooltip("Максимальный сдвиг сайта по Z вдоль линии сайтов.")] private int siteVerticalJitter = 2;
    [SerializeField, Min(0), Tooltip("Максимальный сдвиг сайта от края карты вглубь к центру по X (в клетках).")] private int siteCenterDriftMax = 4;
    [SerializeField, Min(1), Tooltip("Множитель размера сайта, определяющий минимальное расстояние от центра карты по X.")] private int siteMinCenterDistanceMultiplier = 2;

    [Header("Геометрия дорог")]
    [SerializeField, Min(0), Tooltip("Базовая толщина основных путей.")] private int mainWidth = 1;
    [SerializeField, Min(0), Tooltip("Базовая толщина фланговых путей.")] private int linkWidth = 1;
    [SerializeField, Tooltip("Использовать круговую кисть при расширении толщины дороги.")] private bool useCircularPathBrush = false;
    [SerializeField, Min(1), Tooltip("Базовое смещение link-точки атакующего по X от центра карты (расстояние между двумя link).")] private int attackerLinkOffsetFromCenter = 4;
    [SerializeField, Min(0), Tooltip("Случайный jitter X-смещения link-точки атакующего относительно базового смещения.")] private int attackerLinkOffsetJitter = 2;
    [SerializeField, Min(0), Tooltip("Максимальный сдвиг link-точки защитника по X от центра карты.")] private int defenderLinkOffsetFromCenter = 2;

    [Header("Сид генерации")]
    [SerializeField, Tooltip("Использовать фиксированный seed вместо случайного.")] private bool useFixedSeed;
    [SerializeField, Tooltip("Seed генерации, если включен фиксированный seed.")] private int generationSeed = 42;
    [SerializeField, ReadOnlyInInspector, Tooltip("Seed, примененный в текущей генерации.")] private int currentGenerationSeed;

    [Header("A*")]
    [FormerlySerializedAs("mainPathHorizontalChance")]
    [SerializeField, Range(0f, 1f), Tooltip("Приоритет горизонтального движения для main-пути при равных узлах.")] private float astarMainHorizontalBias = 0.85f;
    [FormerlySerializedAs("linkPathHorizontalChance")]
    [SerializeField, Range(0f, 1f), Tooltip("Приоритет горизонтального движения для link-пути при равных узлах.")] private float astarLinkHorizontalBias = 0.15f;
    [SerializeField, Range(0f, 5f), Tooltip("Штраф за поворот маршрута.")] private float astarTurnPenalty = 0.35f;
    [SerializeField, Range(0f, 10f), Tooltip("Штраф за повторное использование уже занятых дорог.")] private float astarRoadReusePenalty = 1.5f;
    [FormerlySerializedAs("astarLinkAvoidMainPenalty")]
    [FormerlySerializedAs("astarMainAvoidLinkPenalty")]
    [SerializeField, Range(0f, 10f), Tooltip("Штраф за движение по дороге другого типа (Main<->Link).")] private float astarCrossTypePenalty = 2f;
    [SerializeField, Range(0f, 5f), Tooltip("Штраф за прохождение близко к границам карты.")] private float astarBorderPenalty = 1.5f;
    [SerializeField, Range(0f, 10f), Tooltip("Штраф main-пути за удаление от внешних границ карты (выше — сильнее прижимает к краям).")] private float astarMainOuterBias = 3f;
    [SerializeField, Range(0f, 5f), Tooltip("Штраф link за прохождение через центральную область.")] private float astarLinkCenterPenalty = 1f;
    [SerializeField, Range(0f, 2f), Tooltip("Случайный шум стоимости пути для вариативности.")] private float astarRandomJitter = 0.05f;

    [Header("Нейтральная зона")]
    [FormerlySerializedAs("generateRoomZone")]
    [SerializeField, Tooltip("Генерировать фиксированную нейтральную зону.")] private bool generateNeutralZone = true;
    [FormerlySerializedAs("roomSize")]
    [SerializeField, Tooltip("Размер нейтральной зоны.")] private Vector2Int neutralZoneSize = new Vector2Int(5, 5);
    [SerializeField, Range(0f, 0.5f), Tooltip("Смещение центра нейтральной зоны по Z от центра карты к линии сайтов (доля расстояния).")] private float neutralZoneBiasToSites = 0.2f;
    [SerializeField, Min(0), Tooltip("Максимальный случайный сдвиг центра нейтральной зоны по X от центра карты.")] private int neutralZoneHorizontalJitter = 2;
    [SerializeField, Min(0), Tooltip("Максимальный случайный сдвиг центра нейтральной зоны по Z от смещённой линии.")] private int neutralZoneVerticalJitter = 1;

    [Header("Префабы")]
    [SerializeField, Tooltip("Префаб базовой клетки пола.")] private BlockComponent floorPrefab;
    [SerializeField, Tooltip("Префаб клетки стены.")] private BlockComponent wallPrefab;
    [SerializeField, Tooltip("Префаб объекта укрытия.")] private GameObject coverPrefab;

    [Header("Материалы")]
    [SerializeField, Tooltip("Материал для зоны спавна.")] private Material spawnMaterial;
    [SerializeField, Tooltip("Резервный материал для дорожных зон.")] private Material roadMaterial;
    [SerializeField, Tooltip("Материал для зоны сайта.")] private Material siteMaterial;
    [SerializeField, Tooltip("Материал для main-дорог.")] private Material mainMaterial;
    [SerializeField, Tooltip("Материал для link-дорог.")] private Material linkMaterial;
    [SerializeField, Tooltip("Материал для обычного пола.")] private Material floorMaterial;
    [SerializeField, Tooltip("Материал для стен.")] private Material wallMaterial;
    [FormerlySerializedAs("roomMaterial")]
    [SerializeField, Tooltip("Материал для нейтральной зоны.")] private Material neutralMaterial;

    [Header("Настройки укрытий")]
    [SerializeField, Tooltip("Общий множитель вероятности появления укрытий.")] private float coverSpawnMultiplier = 1f;
    [SerializeField, Tooltip("Минимальная вероятность укрытия в spawn-зоне.")] private float coverMinProbabilitySpawn = 0.3f;
    [SerializeField, Tooltip("Максимальная вероятность укрытия в spawn-зоне.")] private float coverMaxProbabilitySpawn = 0.6f;
    [SerializeField, Tooltip("Минимальная вероятность укрытия в site-зоне.")] private float coverMinProbabilitySite = 0.2f;
    [SerializeField, Tooltip("Максимальная вероятность укрытия в site-зоне.")] private float coverMaxProbabilitySite = 0.5f;
    [SerializeField, Tooltip("Минимальная вероятность укрытия в main-зоне.")] private float coverMinProbabilityMain = 0.1f;
    [SerializeField, Tooltip("Максимальная вероятность укрытия в main-зоне.")] private float coverMaxProbabilityMain = 0.8f;
    [SerializeField, Tooltip("Минимальная вероятность укрытия в link-зоне.")] private float coverMinProbabilityLink = 0.1f;
    [SerializeField, Tooltip("Максимальная вероятность укрытия в link-зоне.")] private float coverMaxProbabilityLink = 0.8f;
    [FormerlySerializedAs("coverMinProbabilityRoom")]
    [SerializeField, Tooltip("Минимальная вероятность укрытия в neutral-зоне.")] private float coverMinProbabilityNeutral = 0.5f;
    [FormerlySerializedAs("coverMaxProbabilityRoom")]
    [SerializeField, Tooltip("Максимальная вероятность укрытия в neutral-зоне.")] private float coverMaxProbabilityNeutral = 0.7f;
    [SerializeField, Min(1), Tooltip("Порог ширины, ниже которого проход считается узким.")] private int narrowCorridorWidthThreshold = 2;
    [SerializeField, Range(0.5f, 2f), Tooltip("Множитель вероятности укрытий в узких коридорах.")] private float narrowCorridorCoverMultiplier = 1.3f;
    [SerializeField, Range(0.5f, 2f), Tooltip("Множитель вероятности укрытий на открытых участках.")] private float openAreaCoverMultiplier = 1.15f;

    private BlockComponent[,] mapGrid;
    private Vector2Int attackerSpawn, defenderSpawn;
    private Vector2Int siteA, siteB;
    private int siteASize, siteBSize;
    private Vector2Int neutralCenter;

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
        PlaceSpawnZones();
        PlaceSiteZones();
        MarkNeutralZone();
        BuildStructuredRoutes();
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