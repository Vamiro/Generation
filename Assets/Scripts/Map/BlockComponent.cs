using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

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
    Room
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
    // Если блок уже Spawn или Site, то не разрешается менять на Main, Link или Road.
    public void Set(BlockType newType)
    {
        // Если текущий тип – Spawn или Site, запрещаем смену на Main, Link или Road
        if ((currentType == BlockType.Spawn || currentType == BlockType.Site) &&
            (newType == BlockType.Main || newType == BlockType.Link || newType == BlockType.Road))
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
}
