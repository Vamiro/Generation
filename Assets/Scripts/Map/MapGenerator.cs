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
    [SerializeField, Min(1), Tooltip("Минимальный размер зоны спавна по любой стороне (рандомится независимо по X и Z).")] private int spawnZoneSizeMin = 8;
    [SerializeField, Min(1), Tooltip("Максимальный размер зоны спавна по любой стороне (рандомится независимо по X и Z).")] private int spawnZoneSizeMax = 10;
    [SerializeField, Min(1), Tooltip("Минимальный размер зоны сайта по любой стороне (рандомится независимо по X и Z).")] private int siteZoneSizeMin = 4;
    [SerializeField, Min(1), Tooltip("Максимальный размер зоны сайта по любой стороне (рандомится независимо по X и Z).")] private int siteZoneSizeMax = 6;
    [SerializeField, Min(1), Tooltip("Минимальный размер нейтральной зоны по любой стороне (рандомится независимо по X и Z).")] private int neutralZoneSizeMin = 4;
    [SerializeField, Min(1), Tooltip("Максимальный размер нейтральной зоны по любой стороне (рандомится независимо по X и Z).")] private int neutralZoneSizeMax = 6;

    [Header("Позиционирование зон")]
    [SerializeField, Min(0), Tooltip("Внутренний отступ от границы карты для размещения зон.")] private int innerPadding = 4;
    [SerializeField, Min(0), Tooltip("Максимальный сдвиг каждого спавна по X от центра карты.")] private int spawnHorizontalOffset = 3;
    [SerializeField, Range(0f, 0.5f), Tooltip("Смещение горизонтальной линии сайтов от центра между спавнами в сторону защитника (доля половины расстояния).")] private float siteLineBiasToDefender = 0.25f;
    [SerializeField, Min(0), Tooltip("Максимальный сдвиг сайта по Z вдоль линии сайтов.")] private int siteVerticalJitter = 2;
    [SerializeField, Min(0), Tooltip("Максимальный сдвиг сайта от края карты вглубь к центру по X (в клетках).")] private int siteCenterDriftMax = 4;
    [SerializeField, Min(1), Tooltip("Множитель размера сайта, определяющий минимальное расстояние от центра карты по X.")] private int siteMinCenterDistanceMultiplier = 2;

    [Header("Геометрия зон")]
    [SerializeField, Tooltip("Форма заливки зон Spawn/Site/Neutral. Square — прямоугольник. Brush — круглая/квадратная кисть.")] private ZoneShapeMode zoneShapeMode = ZoneShapeMode.Square;
    [SerializeField, Tooltip("Если включено и режим Brush — рисовать базовую форму кругом, иначе квадратом.")] private bool useCircularZoneBrush = true;

    [Header("Геометрия дорог")]
    [SerializeField, Min(0), Tooltip("Базовая толщина основных путей.")] private int mainWidth = 1;
    [SerializeField, Min(0), Tooltip("Базовая толщина фланговых путей.")] private int linkWidth = 1;
    [SerializeField, Tooltip("Использовать круговую кисть при расширении толщины дороги.")] private bool useCircularPathBrush = false;
    [SerializeField, Range(0.1f, 0.6f), Tooltip("Позиция ответвления link-пути атакующего от main: 0 = у спавна, 1 = у сайта. Рекомендуется 0.25–0.40 — link отходит в начале маршрута.")] private float attackerLinkBranchFraction = 0.33f;
    [SerializeField, Range(0.4f, 0.95f), Tooltip("Позиция ответвления link-пути защитника от main: 0 = у спавна, 1 = у сайта. Защитник ответвляется ближе к своему сайту — его mid-путь короче (тактическое преимущество).")] private float defenderLinkBranchFraction = 0.70f;

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
    [SerializeField, Tooltip("Генерировать нейтральную зону.")] private bool generateNeutralZone = true;
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
    [SerializeField, Tooltip("Материал для комнат (галереи, ниши вдоль дорог).")] private Material roomMaterial;

    [Header("Комнаты")]
    [SerializeField, Tooltip("Генерировать комнаты-галереи вдоль main-дорог.")] private bool enableRooms = true;
    [SerializeField, Range(0, 3), Tooltip("Максимальное количество галерей на одну main-дорогу (посередине пути).")] private int roomsPerMainRoad = 1;
    [SerializeField, Min(2), Tooltip("Минимальный размер галереи по любой стороне (клеток).")] private int roomSizeMin = 3;
    [SerializeField, Min(2), Tooltip("Максимальный размер галереи по любой стороне (клеток).")] private int roomSizeMax = 5;
    [SerializeField, Min(1), Tooltip("Отступ комнаты от края дороги по перпендикуляру (клеток).")] private int roomOffsetFromRoad = 1;
    [SerializeField, Tooltip("Генерировать pre-site комнату перед каждым входом в сайт (аналог Hookah/Showers в Valorant).")] private bool enablePreSiteRooms = true;
    [SerializeField, Min(2), Tooltip("Минимальный размер pre-site комнаты (клеток).")] private int preSiteRoomSizeMin = 2;
    [SerializeField, Min(2), Tooltip("Максимальный размер pre-site комнаты (клеток).")] private int preSiteRoomSizeMax = 4;
    [SerializeField, Tooltip("Генерировать кубби (маленькие ниши) вдоль link/mid-дорог — позиции для информации и фланков.")] private bool enableLinkRooms = true;
    [SerializeField, Min(1), Tooltip("Минимальный размер кубби на link-дороге (клеток). Обычно 1–2.")] private int linkRoomSizeMin = 1;
    [SerializeField, Min(1), Tooltip("Максимальный размер кубби на link-дороге (клеток). Обычно 2–3.")] private int linkRoomSizeMax = 2;

    [Header("Структурирование зон")]
    [SerializeField, Tooltip("Окружать Site и Neutral зоны стенами с ограниченными входами после построения дорог.")] private bool enableZoneEnclosures = true;
    [SerializeField, Min(1), Tooltip("Максимальная ширина одного входа в зону (в клетках). Более широкие проёмы будут сужены до этого значения.")] private int maxEntranceWidth = 2;
    [SerializeField, Tooltip("Чем закрывать излишек ширины проёма: глухой стеной (жёсткий choke) или укрытием (тактическая преграда).")] private EntranceFillMode entranceFillMode = EntranceFillMode.Wall;

    [Header("Внешние стены")]
    [SerializeField, Min(1), Tooltip("Толщина внешних стен вокруг карты (в клетках).")] private int outerWallThickness = 2;
    [SerializeField, Min(1), Tooltip("Высота стен в блоках (количество уровней колонны).")] private int outerWallHeight = 2;

    [Header("Настройки укрытий")]
    [SerializeField, Tooltip("Включить расстановку укрытий после генерации карты.")] private bool enableCovers = false;
    [SerializeField, Tooltip("Зоны, в которых выполняется расстановка укрытий.")] private CoverableZones coverableZones = CoverableZones.SiteAndNeutral;
    [SerializeField, Min(1), Tooltip("Высота укрытия в блоках (количество уровней).")] private int coverHeight = 1;
    [SerializeField, Range(0f, 1f), Tooltip("Максимальная доля клеток зоны, занимаемая укрытиями (жёсткий потолок).")] private float coverMaxFillRatio = 0.25f;
    [SerializeField, Min(0), Tooltip("Жёсткий лимит количества укрытий на одну зону. 0 — без лимита.")] private int coverMaxPerZone = 0;
    [SerializeField, Min(1), Tooltip("Минимальное расстояние Чебышёва между двумя укрытиями.")] private int coverMinSpacing = 2;
    [SerializeField, Range(1, 3), Tooltip("Сколько укрытий ставить максимум на одну ось атаки (входы↔центр, входы↔входы).")] private int coversPerAxis = 2;
    [SerializeField, Min(1), Tooltip("На какой клетке от входа ставится первое 'pocket' укрытие. 2 = через 1 пустую клетку от входа.")] private int axisPocketDistance = 2;
    [SerializeField, Range(0f, 1f), Tooltip("Вероятность сдвинуть укрытие на 1 клетку перпендикулярно оси (создаёт корнер-пик 'сбоку от линии').")] private float axisLateralOffsetChance = 0.6f;
    [SerializeField, Tooltip("Включать оси 'вход↔вход' (через всю зону) дополнительно к 'вход↔центр'.")] private bool useEntranceToEntranceAxes = true;

    // Логическая сетка карты: тип каждой клетки. Empty = снаружи карты (нет ничего).
    private BlockType[,] cellTypes;
    // Лениво создаваемые инстансы пола: только для клеток зон/дорог. Null для Empty/Wall.
    private BlockComponent[,] floorInstances;
    // Веса клеток (нужны Main для прогрессии вдоль пути и потенциально для covers).
    private int[,] cellWeights;
    // Пути main-дорог (заполняется в BuildMainRoutes, читается в PlaceRooms).
    private List<List<Vector2Int>> mainRoadPaths = new();
    // Пути main-дорог по команде — нужны для ответвления Link.
    private List<List<Vector2Int>> attackerMainPaths = new();
    private List<List<Vector2Int>> defenderMainPaths = new();
    // Пути link-дорог — нужны для комнат на mid/link.
    private List<List<Vector2Int>> linkPaths = new();

    private Vector2Int attackerSpawn, defenderSpawn;
    private Vector2Int siteA, siteB;
    // Размеры зон по X и Z (Vector2Int.x — ширина, Vector2Int.y — высота).
    private Vector2Int siteASize, siteBSize;
    private Vector2Int neutralSize;
    private Vector2Int neutralCenter;

    // Словарь хранит уникальные блоки по зонам (без дублей).
    private Dictionary<BlockType, HashSet<BlockComponent>> zoneBlocks;

    private static readonly BlockType[] TrackedZones =
    {
        BlockType.Spawn,
        BlockType.Site,
        BlockType.Main,
        BlockType.Link,
        BlockType.Neutral,
        BlockType.Room
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
        BlockType.Neutral,
        BlockType.Room
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
        InitializeCellGrid();

        // Разметка зон (Спавны, Сайты, Main, Link, Neutral) — рисуем "по живому", создаём пол под помеченными клетками.
        MarkZones();

        // Размещаем комнаты-галереи вдоль main-дорог (до ShapeZoneEnclosures, чтобы входы в комнаты тоже сужались)
        PlaceRooms();

        // Окружаем Site/Neutral/Spawn/Room стенами и сужаем входы (choke points)
        ShapeZoneEnclosures();

        ValidateGeneratedLayout();
        BuildAndRegisterZoneObjects();

        // Обновляем материалы пола
        UpdateMap();

        // Возводим внешние стены вокруг всего "острова" пола заданной толщины и высоты
        BuildOuterWalls();

        if (enableCovers)
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

    // Инициализирует логическую сетку без создания GameObject-ов. Все клетки изначально Empty.
    void InitializeCellGrid()
    {
        cellTypes = new BlockType[width, height];
        floorInstances = new BlockComponent[width, height];
        cellWeights = new int[width, height];
        mainRoadPaths.Clear();
        attackerMainPaths.Clear();
        defenderMainPaths.Clear();
        linkPaths.Clear();
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
                cellTypes[x, z] = BlockType.Empty;
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
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                BlockComponent floor = floorInstances[x, z];
                if (floor == null)
                    continue;

                Material material = GetMaterialForType(cellTypes[x, z]);
                if (material != null && floor.Renderer != null)
                    floor.Renderer.material = material;
            }
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
            case BlockType.Room:
                return roomMaterial;
            default:
                return null;
        }
    }

    // Возводит внешние стены вокруг "острова" пола.
    // Алгоритм двухфазный:
    //  Фаза 1 — "обвести контур": для каждой Floor-клетки у которой сосед — Empty или за
    //           краем карты, ставим Wall на ближайшей внутренней Empty-клетке в том направлении.
    //           Это гарантирует, что стена появляется ВСЕГДА — даже если зона вплотную к краю.
    //  Фаза 2 — "утолщение": классический flood-fill outward ещё (outerWallThickness-1) слоёв.
    //  Фаза 3 — создаём физические колонны для всех Wall-клеток.
    void BuildOuterWalls()
    {
        int thickness = Mathf.Max(1, outerWallThickness);
        int wallHeight = Mathf.Max(1, outerWallHeight);
        int[] dx = { 1, -1, 0, 0 };
        int[] dz = { 0, 0, 1, -1 };

        // Фаза 1: обход контура. Каждая Floor-клетка "просматривает" 4 стороны.
        // Если соседняя ячейка Empty — она становится Wall.
        // Если соседняя ячейка за краем карты — значит Floor-клетка сама находится
        // у границы: ставим Wall на единственной возможной позиции (другой сосед в обратном направлении).
        // Результат: Floor никогда не окажется с "голой" стороной у края карты.
        List<Vector2Int> contourWalls = new();
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (cellTypes[x, z] == BlockType.Empty || cellTypes[x, z] == BlockType.Wall)
                    continue;

                for (int i = 0; i < 4; i++)
                {
                    int nx = x + dx[i];
                    int nz = z + dz[i];

                    if (IsInsideMap(nx, nz))
                    {
                        // Обычный случай: Empty-сосед → ставим стену там.
                        if (cellTypes[nx, nz] == BlockType.Empty)
                        {
                            cellTypes[nx, nz] = BlockType.Wall;
                            contourWalls.Add(new Vector2Int(nx, nz));
                        }
                    }
                    else
                    {
                        // Сосед вне карты: Floor вплотную к краю. Стену ставить некуда снаружи,
                        // поэтому ищем противоположного соседа-Empty внутри карты и ставим там.
                        int ox = x - dx[i];
                        int oz = z - dz[i];
                        if (IsInsideMap(ox, oz) && cellTypes[ox, oz] == BlockType.Empty)
                        {
                            cellTypes[ox, oz] = BlockType.Wall;
                            contourWalls.Add(new Vector2Int(ox, oz));
                        }
                    }
                }
            }
        }

        // Фаза 2: flood-fill для утолщения стены. Первый слой уже заложен в Фазе 1.
        List<Vector2Int> currentFront = contourWalls;
        for (int layer = 1; layer < thickness; layer++)
        {
            List<Vector2Int> nextFront = new();
            foreach (Vector2Int cell in currentFront)
            {
                for (int i = 0; i < 4; i++)
                {
                    int nx = cell.x + dx[i];
                    int nz = cell.y + dz[i];
                    if (!IsInsideMap(nx, nz))
                        continue;
                    if (cellTypes[nx, nz] != BlockType.Empty)
                        continue;

                    cellTypes[nx, nz] = BlockType.Wall;
                    nextFront.Add(new Vector2Int(nx, nz));
                }
            }

            currentFront = nextFront;
            if (currentFront.Count == 0)
                break;
        }

        // Кроме того, инициализируем фронт из ранее поставленных внутренних стен
        // (ShapeZoneEnclosures) — чтобы вокруг choke-стен тоже выросли внешние слои.
        if (thickness > 1)
        {
            List<Vector2Int> existingWalls = new();
            for (int x = 0; x < width; x++)
                for (int z = 0; z < height; z++)
                    if (cellTypes[x, z] == BlockType.Wall && !contourWalls.Contains(new Vector2Int(x, z)))
                        existingWalls.Add(new Vector2Int(x, z));

            currentFront = existingWalls;
            for (int layer = 1; layer < thickness; layer++)
            {
                List<Vector2Int> nextFront = new();
                foreach (Vector2Int cell in currentFront)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        int nx = cell.x + dx[i];
                        int nz = cell.y + dz[i];
                        if (!IsInsideMap(nx, nz)) continue;
                        if (cellTypes[nx, nz] != BlockType.Empty) continue;

                        cellTypes[nx, nz] = BlockType.Wall;
                        nextFront.Add(new Vector2Int(nx, nz));
                    }
                }

                currentFront = nextFront;
                if (currentFront.Count == 0) break;
            }
        }

        // Фаза 3: строим физические колонны для всех Wall-клеток.
        for (int x = 0; x < width; x++)
            for (int z = 0; z < height; z++)
                if (cellTypes[x, z] == BlockType.Wall)
                    CreateWallColumn(x, z, wallHeight);
    }

    // Колонна стены строится с уровня 0 (вместо пола) на levels блоков вверх.
    void CreateWallColumn(int x, int z, int levels)
    {
        for (int level = 0; level < levels; level++)
        {
            Vector3 pos = new Vector3(x * blockSize, level * blockSize, z * blockSize);
            BlockComponent wallBlock = Instantiate(wallPrefab, pos, Quaternion.identity, transform);
            wallBlock.blockType.Set(BlockType.Wall);
            if (wallMaterial != null && wallBlock.Renderer != null)
                wallBlock.Renderer.material = wallMaterial;
        }
    }

    // Помечает клетку логически и при необходимости лениво создаёт инстанс пола.
    // Для Wall инстанс не создаётся — стены строятся отдельно колоннами в BuildOuterWalls/ShapeZoneEnclosures.
    bool TryMarkBlock(int x, int z, BlockType type, int? weight = null, bool trackZone = true)
    {
        if (!IsInsideMap(x, z))
            return false;

        // Приоритеты: Spawn/Site/Neutral нельзя перезаписать дорогами и комнатами.
        BlockType currentType = cellTypes[x, z];
        if ((currentType == BlockType.Spawn || currentType == BlockType.Site || currentType == BlockType.Neutral) &&
            (type == BlockType.Main || type == BlockType.Link || type == BlockType.Road || type == BlockType.Room))
        {
            return false;
        }

        if (currentType == type && weight == null)
            return true;

        cellTypes[x, z] = type;
        if (weight.HasValue)
            cellWeights[x, z] = weight.Value;

        // Уберём из старого набора zoneBlocks, если был там.
        if (floorInstances[x, z] != null)
            RemoveFromTrackedZones(floorInstances[x, z]);

        bool needsFloor = type != BlockType.Empty && type != BlockType.Wall;
        if (needsFloor)
        {
            if (floorInstances[x, z] == null)
            {
                Vector3 pos = new Vector3(x * blockSize, 0f, z * blockSize);
                floorInstances[x, z] = Instantiate(floorPrefab, pos, Quaternion.identity, transform);
            }

            BlockComponent floor = floorInstances[x, z];
            floor.blockType.Set(type);
            if (weight.HasValue)
                floor.weight = weight.Value;

            if (trackZone && zoneBlocks.TryGetValue(type, out HashSet<BlockComponent> blocks))
                blocks.Add(floor);
        }
        else if (floorInstances[x, z] != null)
        {
            // Стало стеной/пустотой — убираем визуальный пол.
            Destroy(floorInstances[x, z].gameObject);
            floorInstances[x, z] = null;
        }

        return true;
    }

    void RemoveFromTrackedZones(BlockComponent block)
    {
        if (zoneBlocks == null || block == null)
            return;

        foreach (HashSet<BlockComponent> blocks in zoneBlocks.Values)
            blocks.Remove(block);
    }

    bool IsInsideMap(int x, int z)
    {
        return x >= 0 && x < width && z >= 0 && z < height;
    }

    BlockType GetCellType(int x, int z)
    {
        return IsInsideMap(x, z) ? cellTypes[x, z] : BlockType.Empty;
    }

    int GetCellWeight(int x, int z)
    {
        return IsInsideMap(x, z) ? cellWeights[x, z] : 0;
    }

    // Клетка на самом краю массива карты (старое определение IsBorder).
    bool IsAtMapEdge(int x, int z)
    {
        return x == 0 || x == width - 1 || z == 0 || z == height - 1;
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