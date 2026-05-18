using System;
using UnityEngine;

public enum BlockType
{
    Floor,
    Wall,
    Empty,
    None,
    Spawn,
    Main,
    Link,
    Neutral,
    Site,
    Road,
    Pocket, // Открытый карман / галерея вдоль дороги — без стен со стороны road
    Room    // Закрытая комната с choke-входом (Hookah-подобная)
}


[Serializable]
public class BlockData
{
    [SerializeField]
    private BlockType currentType = BlockType.None;
    
    public BlockData(BlockType initialType)
    {
        currentType = initialType;
    }

    // Свойство для чтения текущего типа
    public BlockType Current => currentType;

    // Метод для установки нового типа с учётом приоритета:
    // Если блок уже Spawn, Site или Neutral, то не разрешается менять на Main, Link или Road.
    public void Set(BlockType newType)
    {
        // Если текущий тип – Spawn/Site/Neutral, запрещаем смену на Main, Link или Road
        if ((currentType == BlockType.Spawn || currentType == BlockType.Site || currentType == BlockType.Neutral) &&
            (newType == BlockType.Main || newType == BlockType.Link || newType == BlockType.Road || newType == BlockType.Room || newType == BlockType.Pocket))
        {
            return;
        }
        currentType = newType;
    }

    public override string ToString()
    {
        return currentType.ToString();
    }
}
public class BlockComponent : MonoBehaviour
{
    public BlockData blockType = new BlockData(BlockType.None);
    public int weight = 0;
    private MeshRenderer cachedRenderer;

    public MeshRenderer Renderer
    {
        get
        {
            if (cachedRenderer == null)
                cachedRenderer = GetComponent<MeshRenderer>();
            return cachedRenderer;
        }
    }

    private void Awake()
    {
        cachedRenderer = GetComponent<MeshRenderer>();
    }
}
