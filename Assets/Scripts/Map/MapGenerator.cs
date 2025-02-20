using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

public class MapGenerator : MonoBehaviour
{
    [Header("Размер карты")]
    [SerializeField] private int width = 20;    // Количество блоков по X
    [SerializeField] private int height = 20;   // Количество блоков по Z

    [Header("Параметры блока")]
    [SerializeField] private float blockSize = 1f; // Если увеличить, позиции масштабируются
    [SerializeField] private int mainWidth = 1;      // Толщина основных путей
    [SerializeField] private int linkWidth = 1;      // Толщина фланговых путей

    [Header("Настройки зон")]
    [SerializeField] private int spawnZoneSizeMin = 8;   // Мин. размер зоны спавна
    [SerializeField] private int spawnZoneSizeMax = 10;    // Макс. размер зоны спавна
    [SerializeField] private int siteZoneWidth = 4;        // Ширина зоны сайта
    [SerializeField] private int siteZoneHeight = 4;       // Высота зоны сайта

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

    // Словарь для хранения блоков по зонам (Spawn, Site, Main, Link, Room)
    private Dictionary<BlockType, List<BlockComponent>> zoneBlocks;

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
        foreach (Transform child in transform)
            Destroy(child.gameObject);
        GenerateMap();
    }

    void GenerateMap()
    {
        // Инициализируем словарь зон
        zoneBlocks = new Dictionary<BlockType, List<BlockComponent>>()
        {
            { BlockType.Spawn, new List<BlockComponent>() },
            { BlockType.Site, new List<BlockComponent>() },
            { BlockType.Main, new List<BlockComponent>() },
            { BlockType.Link, new List<BlockComponent>() },
            { BlockType.Room, new List<BlockComponent>() }
        };

        // Создаем нижний этаж (Y = 0)
        mapGrid = new BlockComponent[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                Vector3 pos = new Vector3(x * blockSize, 0, z * blockSize);
                var floorBlock = Instantiate(floorPrefab, pos, Quaternion.identity, transform);
                mapGrid[x, z] = floorBlock;
                floorBlock.blockType.Set(BlockType.Floor);
            }
        }

        // Разметка зон (Спавны, Сайты, Main, Link, Комната)
        MarkZones();

        // Заполняем границы карты стенами
        FillBorderWalls();

        // Обновляем материалы нижнего этажа
        UpdateMap();

        // Формируем уровень стен (дублирование блоков)
        DuplicateWallBlocks();

        // Расставляем укрытия по обновленной логике
        PlaceCovers();
    }

    void MarkZones()
    {
        int mwidth = width - 4;
        int mheight = height - 4;

        // 1. Спавны (ближе к центру, но не на границе)
        spawnA = new Vector2Int(mwidth / 2 + Random.Range(-mwidth / 10, mwidth / 10), Random.Range(0, mheight / 4));
        spawnB = new Vector2Int(mwidth / 2 + Random.Range(-mwidth / 10, mwidth / 10), Random.Range(mheight - mheight / 4, mheight));
        int spawnSize = Random.Range(spawnZoneSizeMin, spawnZoneSizeMax + 1);
        ClearZone(spawnA.x, spawnA.y, spawnSize / 2, spawnSize - spawnSize / 2, BlockType.Spawn);
        spawnSize = Random.Range(spawnZoneSizeMin, spawnZoneSizeMax + 1);
        ClearZone(spawnB.x, spawnB.y, spawnSize / 2, spawnSize - spawnSize / 2, BlockType.Spawn);

        // 2. Сайты
        siteA = new Vector2Int(Random.Range(0, mheight / 4), mwidth / 3 + Random.Range(-mwidth / 10, mwidth / 10));
        siteB = new Vector2Int(Random.Range(mheight - mheight / 4, mheight), mwidth / 3 + Random.Range(-mwidth / 10, mwidth / 10));
        ClearZone(siteA.x, siteA.y, siteZoneWidth, siteZoneHeight, BlockType.Site);
        ClearZone(siteB.x, siteB.y, siteZoneWidth, siteZoneHeight, BlockType.Site);

        // 4. Фланговые пути (Link)
        int spawnOffset = 2;
        Vector2Int linkStart1 = GetRandomPointNear(spawnA, spawnOffset);
        Vector2Int linkEndpoint1 = GetRandomEdgePoint(siteB, siteZoneWidth, siteZoneHeight, linkStart1);
        CreateLinkPath(linkStart1.x, linkStart1.y, linkEndpoint1.x, linkEndpoint1.y, BlockType.Link, linkWidth);
        Vector2Int linkStart2 = GetRandomPointNear(spawnB, spawnOffset);
        Vector2Int linkEndpoint2 = GetRandomEdgePoint(siteA, siteZoneWidth, siteZoneHeight, linkStart2);
        CreateLinkPath(linkStart2.x, linkStart2.y, linkEndpoint2.x, linkEndpoint2.y, BlockType.Link, linkWidth);
        Vector2Int linkStart3 = GetRandomPointNear(spawnA, spawnOffset);
        Vector2Int linkEndpoint3 = GetRandomEdgePoint(siteA, siteZoneWidth, siteZoneHeight, linkStart3);
        CreateLinkPath(linkStart3.x, linkStart3.y, linkEndpoint3.x, linkEndpoint3.y, BlockType.Link, linkWidth);
        Vector2Int linkStart4 = GetRandomPointNear(spawnB, spawnOffset);
        Vector2Int linkEndpoint4 = GetRandomEdgePoint(siteB, siteZoneWidth, siteZoneHeight, linkStart4);
        CreateLinkPath(linkStart4.x, linkStart4.y, linkEndpoint4.x, linkEndpoint4.y, BlockType.Link, linkWidth);
        
        // 3. Основные пути (Main)
        Vector2Int mainEndpoint1 = GetRandomEdgePoint(siteA, siteZoneWidth, siteZoneHeight, spawnA);
        CreateMainPath(spawnA.x, spawnA.y, mainEndpoint1.x, mainEndpoint1.y, BlockType.Main, mainWidth);
        Vector2Int mainEndpoint2 = GetRandomEdgePoint(siteB, siteZoneWidth, siteZoneHeight, spawnB);
        CreateMainPath(spawnB.x, spawnB.y, mainEndpoint2.x, mainEndpoint2.y, BlockType.Main, mainWidth);
        Vector2Int mainEndpoint3 = GetRandomEdgePoint(siteB, siteZoneWidth, siteZoneHeight, spawnA);
        CreateMainPath(spawnA.x, spawnA.y, mainEndpoint3.x, mainEndpoint3.y, BlockType.Main, mainWidth);
        Vector2Int mainEndpoint4 = GetRandomEdgePoint(siteA, siteZoneWidth, siteZoneHeight, spawnB);
        CreateMainPath(spawnB.x, spawnB.y, mainEndpoint4.x, mainEndpoint4.y, BlockType.Main, mainWidth);

        // 5. Комната (Room)
        int midX = (spawnA.x + spawnB.x + siteA.x + siteB.x) / 4;
        int midZ = (spawnA.y + spawnB.y + siteA.y + siteB.y) / 4;
            //ClearZone(midX - 2, midZ - 2, 5, 5, BlockType.Room);
    }

    /// <summary>
    /// Выбирает случайную точку на ребре прямоугольной зоны, ближайшую к referencePoint.
    /// </summary>
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

        string chosenEdge = "top";
        float min = dTop;
        if (dBottom < min) { min = dBottom; chosenEdge = "bottom"; }
        if (dLeft < min) { min = dLeft; chosenEdge = "left"; }
        if (dRight < min) { min = dRight; chosenEdge = "right"; }

        switch(chosenEdge)
        {
            case "top":
                return new Vector2Int(Random.Range(zoneOrigin.x, zoneOrigin.x + zoneWidth), zoneOrigin.y);
            case "bottom":
                return new Vector2Int(Random.Range(zoneOrigin.x, zoneOrigin.x + zoneWidth), zoneOrigin.y + zoneHeight - 1);
            case "left":
                return new Vector2Int(zoneOrigin.x, Random.Range(zoneOrigin.y, zoneOrigin.y + zoneHeight));
            case "right":
                return new Vector2Int(zoneOrigin.x + zoneWidth - 1, Random.Range(zoneOrigin.y, zoneOrigin.y + zoneHeight));
            default:
                return zoneOrigin;
        }
    }

    /// <summary>
    /// Возвращает случайную точку в пределах смещения от базовой точки.
    /// </summary>
    Vector2Int GetRandomPointNear(Vector2Int basePoint, int offsetRange)
    {
        int offsetX = Random.Range(-offsetRange, offsetRange + 1);
        int offsetY = Random.Range(-offsetRange, offsetRange + 1);
        return new Vector2Int(basePoint.x + offsetX, basePoint.y + offsetY);
    }

    void ClearZone(int startX, int startZ, int sizeX, int sizeZ, BlockType type)
    {
        for (int x = startX; x < startX + sizeX; x++)
        {
            for (int z = startZ; z < startZ + sizeZ; z++)
            {
                if (x >= 0 && x < width && z >= 0 && z < height)
                {
                    mapGrid[x, z].blockType.Set(type);
                    if (zoneBlocks.ContainsKey(type))
                        zoneBlocks[type].Add(mapGrid[x, z]);
                }
            }
        }
    }

    void CreateMainPath(int startX, int startZ, int endX, int endZ, BlockType blockType, int pathWidth = 1)
    {
        int x = startX, z = startZ;
        int weight = 1;
        while (x != endX || z != endZ)
        {
            for (int dx = -pathWidth; dx <= pathWidth; dx++)
            {
                for (int dz = -pathWidth; dz <= pathWidth; dz++)
                {
                    int nx = x + dx;
                    int nz = z + dz;
                    if (nx >= 0 && nx < width && nz >= 0 && nz < height)
                    {
                        mapGrid[nx, nz].blockType.Set(blockType);
                        mapGrid[nx, nz].weight = weight;
                        if (zoneBlocks.ContainsKey(blockType))
                            zoneBlocks[blockType].Add(mapGrid[nx, nz]);
                    }
                }
            }
            if (Random.value > 0.15f)
            {
                if (x < endX) x++;
                else if (x > endX) x--;
            }
            else
            {
                if (z < endZ) z++;
                else if (z > endZ) z--;
            }
            weight++;
        }
    }
    
    void CreateLinkPath(int startX, int startZ, int endX, int endZ, BlockType blockType, int pathWidth = 0)
    {
        int x = startX, z = startZ;
        while (x != endX || z != endZ)
        {
            for (int dx = -pathWidth; dx <= pathWidth; dx++)
            {
                for (int dz = -pathWidth; dz <= pathWidth; dz++)
                {
                    int nx = x + dx;
                    int nz = z + dz;
                    if (nx >= 0 && nx < width && nz >= 0 && nz < height)
                    {
                        mapGrid[nx, nz].blockType.Set(blockType);
                        if (zoneBlocks.ContainsKey(blockType))
                            zoneBlocks[blockType].Add(mapGrid[nx, nz]);
                    }
                }
            }
            if (Random.value > 0.85f)
            {
                if (x < endX) x++;
                else if (x > endX) x--;
            }
            else
            {
                if (z < endZ) z++;
                else if (z > endZ) z--;
            }
        }
    }

    void UpdateMap()
    {
        foreach (BlockComponent block in mapGrid)
        {
            switch (block.blockType.Current)
            {
                case BlockType.Floor:
                    block.GetComponent<MeshRenderer>().material = floorMaterial;
                    break;
                case BlockType.Wall:
                    block.GetComponent<MeshRenderer>().material = wallMaterial;
                    break;
                case BlockType.Spawn:
                    block.GetComponent<MeshRenderer>().material = spawnMaterial;
                    break;
                case BlockType.Main:
                    block.GetComponent<MeshRenderer>().material = mainMaterial;
                    break;
                case BlockType.Link:
                    block.GetComponent<MeshRenderer>().material = linkMaterial;
                    break;
                case BlockType.Site:
                    block.GetComponent<MeshRenderer>().material = siteMaterial;
                    break;
                case BlockType.Road:
                    block.GetComponent<MeshRenderer>().material = roadMaterial;
                    break;
                case BlockType.Room:
                    block.GetComponent<MeshRenderer>().material = roomMaterial;
                    break;
                default:
                    break;
            }
        }
    }

    bool IsBorder(int x, int z)
    {
        return (x == 0 || x == width - 1 || z == 0 || z == height - 1);
    }

    void FillBorderWalls()
    {
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (IsBorder(x, z))
                    mapGrid[x, z].blockType.Set(BlockType.Wall);
            }
        }
    }

    void DuplicateWallBlocks()
    {
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                Vector3 pos;
                if (IsBorder(x, z))
                {
                    pos = new Vector3(x * blockSize, 1 * blockSize, z * blockSize);
                    var dup1 = Instantiate(wallPrefab, pos, Quaternion.identity, transform);
                    dup1.blockType.Set(BlockType.Wall);
                    dup1.GetComponent<MeshRenderer>().material = wallMaterial;

                    pos = new Vector3(x * blockSize, 2 * blockSize, z * blockSize);
                    var dup2 = Instantiate(wallPrefab, pos, Quaternion.identity, transform);
                    dup2.blockType.Set(BlockType.Wall);
                    dup2.GetComponent<MeshRenderer>().material = wallMaterial;
                }
                else
                {
                    if (mapGrid[x, z].blockType.Current == BlockType.Floor)
                    {
                        pos = new Vector3(x * blockSize, 1 * blockSize, z * blockSize);
                        var dup = Instantiate(wallPrefab, pos, Quaternion.identity, transform);
                        dup.blockType.Set(BlockType.Wall);
                        dup.GetComponent<MeshRenderer>().material = wallMaterial;
                        
                        pos = new Vector3(x * blockSize, 2 * blockSize, z * blockSize);
                        dup = Instantiate(wallPrefab, pos, Quaternion.identity, transform);
                        dup.blockType.Set(BlockType.Wall);
                        dup.GetComponent<MeshRenderer>().material = wallMaterial;
                    }
                }
            }
        }
    }

    // Определяет, является ли блок краевым в своей зоне (проверка 4-х соседей)
    bool IsEdgeBlock(int gridX, int gridZ, BlockType zoneType)
    {
        int[] dx = { 0, 1, 0, -1 };
        int[] dz = { 1, 0, -1, 0 };
        for (int i = 0; i < 4; i++)
        {
            int nx = gridX + dx[i];
            int nz = gridZ + dz[i];
            if (nx >= 0 && nx < width && nz >= 0 && nz < height)
            {
                if (mapGrid[nx, nz].blockType.Current != zoneType)
                    return true;
            }
        }
        return false;
    }

    // Определяет, является ли блок узловым (стыком зон) – если среди 8 соседей есть несколько разных типов
    bool IsJunctionBlock(int gridX, int gridZ, BlockType zoneType)
    {
        int diffCount = 0;
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                if (dx == 0 && dz == 0) continue;
                int nx = gridX + dx;
                int nz = gridZ + dz;
                if (nx >= 0 && nx < width && nz >= 0 && nz < height)
                {
                    if (mapGrid[nx, nz].blockType.Current != zoneType)
                        diffCount++;
                }
            }
        }
        return diffCount >= 2;
    }

    // Улучшенная логика расстановки укрытий:
    // - В зонах Main и Link спавнятся только на краях (или в узлах) дороги (чтобы оставался проход).
    // - В зонах Spawn и Site укрытия могут спавниться в зависимости от открытости.
    // - В зоне Room – аналогично Spawn.
    void PlaceCovers()
    {
        BlockType[] coverZones = { BlockType.Spawn, BlockType.Site, BlockType.Main, BlockType.Link, BlockType.Room };
        foreach (BlockType zone in coverZones)
        {
            if (!zoneBlocks.ContainsKey(zone)) continue;
            List<BlockComponent> blocks = zoneBlocks[zone];
            foreach (BlockComponent block in blocks)
            {
                int gridX = Mathf.RoundToInt(block.transform.position.x / blockSize);
                int gridZ = Mathf.RoundToInt(block.transform.position.z / blockSize);
                float density = CalculateLocalDensity(gridX, gridZ, zone);

                float baseProb = 0f;
                // Для Main и Link: спавним укрытия только если блок является краевым или узловым
                if (zone == BlockType.Main || zone == BlockType.Link)
                {
                    if (!IsEdgeBlock(gridX, gridZ, zone) && !IsJunctionBlock(gridX, gridZ, zone))
                        continue; // оставляем центр дороги свободным
                    if (zone == BlockType.Main)
                        baseProb = Mathf.Lerp(coverMaxProbabilityMain, coverMinProbabilityMain, density);
                    else
                        baseProb = Mathf.Lerp(coverMaxProbabilityLink, coverMinProbabilityLink, density);
                }
                else if (zone == BlockType.Spawn)
                {
                    baseProb = Mathf.Lerp(coverMaxProbabilitySpawn, coverMinProbabilitySpawn, density);
                }
                else if (zone == BlockType.Site)
                {
                    baseProb = Mathf.Lerp(coverMaxProbabilitySite, coverMinProbabilitySite, density);
                }
                else if (zone == BlockType.Room)
                {
                    baseProb = Mathf.Lerp(coverMaxProbabilityRoom, coverMinProbabilityRoom, density);
                }
                baseProb *= coverSpawnMultiplier;
                if (Random.value < baseProb)
                {
                    Vector3 coverPos = block.transform.position + new Vector3(0, blockSize, 0);
                    Instantiate(coverPrefab, coverPos, Quaternion.identity, transform);
                }
            }
        }
    }

    // Рассчитывает локальную плотность соседних блоков данного типа (окрестность 3x3)
    float CalculateLocalDensity(int gridX, int gridZ, BlockType zoneType)
    {
        int count = 0, total = 0;
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                int nx = gridX + dx;
                int nz = gridZ + dz;
                if (nx >= 0 && nx < width && nz >= 0 && nz < height)
                {
                    total++;
                    if (mapGrid[nx, nz].blockType.Current == zoneType)
                        count++;
                }
            }
        }
        return (float)count / total;
    }
}