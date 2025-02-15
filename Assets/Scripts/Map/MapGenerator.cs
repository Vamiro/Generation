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
    [SerializeField] private int mainWidth = 1; // Размер блока (если увеличить, то позиции масштабируются)
    [SerializeField] private int linkWidth = 1; // Размер блока (если увеличить, то позиции масштабируются)

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

        int size = Random.Range(8, 10);
        ClearZone(spawnA.x, spawnA.y, size / 2, size - size / 2, BlockType.Spawn);
        size = Random.Range(8, 10);
        ClearZone(spawnB.x, spawnB.y, size / 2, size - size / 2, BlockType.Spawn);
        
        // 2. Сайты
        siteA = new Vector2Int(Random.Range(0, mheight / 4), mwidth / 2 + Random.Range(-mwidth / 10, mwidth / 10));
        siteB = new Vector2Int(Random.Range(mheight - mheight / 4, mheight), mwidth / 2 + Random.Range(-mwidth / 10, mwidth / 10));
        ClearZone(siteA.x, siteA.y, 4, 4, BlockType.Site);
        ClearZone(siteB.x, siteB.y, 4, 4, BlockType.Site);

        // 3. Основные пути (MainVolume)
        CreateMainPath(spawnA.x, spawnA.y, siteA.x, siteA.y, BlockType.Main, mainWidth);
        CreateMainPath(spawnB.x, spawnB.y, siteB.x, siteB.y, BlockType.Main, mainWidth);
        CreateMainPath(spawnA.x, spawnA.y, siteB.x, siteB.y, BlockType.Main, mainWidth);
        CreateMainPath(spawnB.x, spawnB.y, siteA.x, siteA.y, BlockType.Main, mainWidth);

        // 4. Фланговые пути (LinkVolume)
        CreateLinkPath(spawnA.x, spawnA.y, siteB.x, siteB.y, BlockType.Link, linkWidth);
        CreateLinkPath(spawnB.x, spawnB.y, siteA.x, siteA.y, BlockType.Link, linkWidth);
        CreateLinkPath(spawnA.x, spawnA.y, siteA.x, siteA.y, BlockType.Link, linkWidth);
        CreateLinkPath(spawnB.x, spawnB.y, siteB.x, siteB.y, BlockType.Link, linkWidth);

        // 5. Комната в центре пересечения путей
        int midX = (spawnA.x + spawnB.x + siteA.x + siteB.x) / 4;
        int midZ = (spawnA.y + spawnB.y + siteA.y + siteB.y) / 4;
        ClearZone(midX - 2, midZ - 2, 5, 5, BlockType.Room);
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
            if (Random.value > 0.1f)
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
            if (Random.value > 0.9f)
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
                    }
                }
            }
        }
    }
}
