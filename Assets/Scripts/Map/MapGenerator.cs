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
    [SerializeField] private float blockSize = 1f; // Размер блока (если увеличить, то позиции масштабируются)
    [SerializeField] private int mainWidth = 1; // Толщина основных путей
    [SerializeField] private int linkWidth = 1; // Толщина фланговых путей

    [Header("Настройки зон")]
    [SerializeField] private int spawnZoneSizeMin = 8;  // Минимальный размер зоны спавна
    [SerializeField] private int spawnZoneSizeMax = 10; // Максимальный размер зоны спавна
    [SerializeField] private int siteZoneWidth = 4;     // Ширина зоны сайта
    [SerializeField] private int siteZoneHeight = 4;    // Высота зоны сайта

    [Header("Префабы")]
    [SerializeField] private BlockComponent floorPrefab;
    [SerializeField] private BlockComponent wallPrefab;

    [Header("Материалы")]
    [SerializeField] private Material spawnMaterial;
    [SerializeField] private Material roadMaterial;
    [SerializeField] private Material siteMaterial;
    [SerializeField] private Material mainMaterial;
    [SerializeField] private Material linkMaterial;
    [SerializeField] private Material floorMaterial;
    [SerializeField] private Material wallMaterial;
    [SerializeField] private Material roomMaterial; // Материал для комнаты

    private BlockComponent[,] mapGrid;

    private Vector2Int spawnA, spawnB;
    private Vector2Int siteA, siteB;

    void Start()
    {
        GenerateMap();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.R))
        {
            DestroyMap();
        }
    }

    void DestroyMap()
    {
        foreach (Transform child in transform)
        {
            Destroy(child.gameObject);
        }
        GenerateMap();
    }
    
    void GenerateMap()
    {
        // Создаем нижний этаж (Y = 0) для всех внутренних ячеек карты.
        mapGrid = new BlockComponent[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                Vector3 pos = new Vector3(x * blockSize, 0, z * blockSize);
                var floorBlock = Instantiate(floorPrefab, pos, Quaternion.identity, transform);
                mapGrid[x, z] = floorBlock;
                // Изначально все ячейки считаем типом Floor
                mapGrid[x, z].blockType.Set(BlockType.Floor);
            }
        }

        // Разметка зон (спавны, сайты, основные и фланговые пути, комната)
        MarkZones();

        // Заполняем внешний слой карты: все ячейки на границе переопределяем как стены
        FillBorderWalls();

        // Обновляем визуальное отображение нижнего этажа (материалы)
        UpdateMap();

        // Дублируем блоки для формирования уровня стен:
        // Внутренние ячейки (не на границе) с типом Floor — один дубликат на Y = 1 (стена).
        // Граничные ячейки (Border) — дублируются на два этажа (Y = 1 и Y = 2).
        DuplicateWallBlocks();
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

        // 3. Основные пути (MainVolume)
        // Вместо фиксированной точки (например, siteA) выбираем случайную точку на ребре зоны site, которая ближе к спавну.
        Vector2Int mainEndpoint1 = GetRandomEdgePoint(siteA, siteZoneWidth, siteZoneHeight, spawnA);
        CreateMainPath(spawnA.x, spawnA.y, mainEndpoint1.x, mainEndpoint1.y, BlockType.Main, mainWidth);

        Vector2Int mainEndpoint2 = GetRandomEdgePoint(siteB, siteZoneWidth, siteZoneHeight, spawnB);
        CreateMainPath(spawnB.x, spawnB.y, mainEndpoint2.x, mainEndpoint2.y, BlockType.Main, mainWidth);

        Vector2Int mainEndpoint3 = GetRandomEdgePoint(siteB, siteZoneWidth, siteZoneHeight, spawnA);
        CreateMainPath(spawnA.x, spawnA.y, mainEndpoint3.x, mainEndpoint3.y, BlockType.Main, mainWidth);

        Vector2Int mainEndpoint4 = GetRandomEdgePoint(siteA, siteZoneWidth, siteZoneHeight, spawnB);
        CreateMainPath(spawnB.x, spawnB.y, mainEndpoint4.x, mainEndpoint4.y, BlockType.Main, mainWidth);

        // 4. Фланговые пути (LinkVolume)
        // Допустим, хотим смещать стартовую точку на 1-2 блока от базовой точки спавна.
        int spawnOffset = 2;

        Vector2Int linkStart1 = GetRandomPointNear(spawnA, spawnOffset);
        Vector2Int linkEndpoint1 = GetRandomEdgePoint(siteB, siteZoneWidth, siteZoneHeight, linkStart1);
        CreateLinkPath(linkStart1.x, linkStart1.y, linkEndpoint1.x, linkEndpoint1.y, BlockType.Link, linkWidth);

        Vector2Int linkStart2 = GetRandomPointNear(spawnB, spawnOffset);
        Vector2Int linkEndpoint2 = GetRandomEdgePoint(siteA, siteZoneWidth, siteZoneHeight, linkStart2);
        CreateLinkPath(linkStart2.x, linkStart2.y, linkEndpoint2.x, linkEndpoint2.y, BlockType.Link, linkWidth);

        // Если нужно ещё дополнительные пути, можно также случайно варьировать стартовые точки.
        Vector2Int linkStart3 = GetRandomPointNear(spawnA, spawnOffset);
        Vector2Int linkEndpoint3 = GetRandomEdgePoint(siteA, siteZoneWidth, siteZoneHeight, linkStart3);
        CreateLinkPath(linkStart3.x, linkStart3.y, linkEndpoint3.x, linkEndpoint3.y, BlockType.Link, linkWidth);

        Vector2Int linkStart4 = GetRandomPointNear(spawnB, spawnOffset);
        Vector2Int linkEndpoint4 = GetRandomEdgePoint(siteB, siteZoneWidth, siteZoneHeight, linkStart4);
        CreateLinkPath(linkStart4.x, linkStart4.y, linkEndpoint4.x, linkEndpoint4.y, BlockType.Link, linkWidth);


        // 5. Комната в центре пересечения путей
        int midX = (spawnA.x + spawnB.x + siteA.x + siteB.x) / 4;
        int midZ = (spawnA.y + spawnB.y + siteA.y + siteB.y) / 4;
        ClearZone(midX - 2, midZ - 2, 5, 5, BlockType.Room);
    }

    
    /// <summary>
    /// Выбирает случайную точку на ребре (границе) прямоугольной зоны, которая находится ближе всего к referencePoint.
    /// </summary>
    /// <param name="zoneOrigin">Левый верхний угол зоны</param>
    /// <param name="zoneWidth">Ширина зоны</param>
    /// <param name="zoneHeight">Высота зоны</param>
    /// <param name="referencePoint">Исходная точка, от которой выбирается ближайшее ребро</param>
    /// <returns>Случайная точка на ближайшем ребре зоны</returns>
    Vector2Int GetRandomEdgePoint(Vector2Int zoneOrigin, int zoneWidth, int zoneHeight, Vector2Int referencePoint)
    {
        // Вычисляем центры каждой из 4 граней зоны.
        Vector2Int topCenter = new Vector2Int(zoneOrigin.x + zoneWidth / 2, zoneOrigin.y);
        Vector2Int bottomCenter = new Vector2Int(zoneOrigin.x + zoneWidth / 2, zoneOrigin.y + zoneHeight - 1);
        Vector2Int leftCenter = new Vector2Int(zoneOrigin.x, zoneOrigin.y + zoneHeight / 2);
        Vector2Int rightCenter = new Vector2Int(zoneOrigin.x + zoneWidth - 1, zoneOrigin.y + zoneHeight / 2);

        // Вычисляем расстояния от исходной точки до центров граней.
        float dTop = Vector2Int.Distance(referencePoint, topCenter);
        float dBottom = Vector2Int.Distance(referencePoint, bottomCenter);
        float dLeft = Vector2Int.Distance(referencePoint, leftCenter);
        float dRight = Vector2Int.Distance(referencePoint, rightCenter);

        // Определяем, какая грань ближе всего.
        string chosenEdge = "top";
        float min = dTop;
        if (dBottom < min) { min = dBottom; chosenEdge = "bottom"; }
        if (dLeft < min) { min = dLeft; chosenEdge = "left"; }
        if (dRight < min) { min = dRight; chosenEdge = "right"; }

        // Выбираем случайную точку вдоль выбранного ребра.
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
    /// Возвращает случайную точку в пределах указанного смещения от базовой точки.
    /// </summary>
    /// <param name="basePoint">Базовая точка</param>
    /// <param name="offsetRange">Максимальное смещение по каждой оси</param>
    /// <returns>Новая случайная точка</returns>
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

    // Возвращает true, если ячейка (x,z) находится на границе карты
    bool IsBorder(int x, int z)
    {
        return (x == 0 || x == width - 1 || z == 0 || z == height - 1);
    }

    // Присваивает всем граничным ячейкам тип Wall
    void FillBorderWalls()
    {
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (IsBorder(x, z))
                {
                    mapGrid[x, z].blockType.Set(BlockType.Wall);
                }
            }
        }
    }

    // Дублирует блоки нижнего этажа для формирования уровня стен.
    // Для граничных ячеек (Border) создаются два дубликата (на Y = 1 и Y = 2).
    // Для остальных (если их тип остался Floor) создается один дубликат на Y = 1.
    void DuplicateWallBlocks()
    {
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                Vector3 pos;
                if (IsBorder(x, z))
                {
                    // Дублируем на первый этаж стен (Y = 1)
                    pos = new Vector3(x * blockSize, 1 * blockSize, z * blockSize);
                    var dup1 = Instantiate(wallPrefab, pos, Quaternion.identity, transform);
                    dup1.blockType.Set(BlockType.Wall);
                    dup1.GetComponent<MeshRenderer>().material = wallMaterial;

                    // Дублируем еще на второй этаж стен (Y = 2)
                    pos = new Vector3(x * blockSize, 2 * blockSize, z * blockSize);
                    var dup2 = Instantiate(wallPrefab, pos, Quaternion.identity, transform);
                    dup2.blockType.Set(BlockType.Wall);
                    dup2.GetComponent<MeshRenderer>().material = wallMaterial;
                }
                else
                {
                    // Для внутренних ячеек, если тип остался Floor, дублируем на Y = 1
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
}
