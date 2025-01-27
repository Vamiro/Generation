using System;
using System.Collections.Generic;
using UnityEngine;

public class DeathData : StorageData<DeathData>
{
    public List<Vector3> DeathPositions = new List<Vector3>();
}

public class GameManager : MonoSingleton<GameManager>
{
    [SerializeField] private float timeScale = 1f;
    [SerializeField] private float roundTime = 120f;
    
    private float _currentTime;
    
    private void Start()
    {
        Time.timeScale = timeScale;
    }

    private void Update()
    {
        _currentTime += Time.deltaTime;
        if (!(_currentTime >= roundTime / timeScale)) return;
        _currentTime = 0;
        RestartGame();
    }

    public void RestartGame()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
    }
    
    public void SaveDeathPosition(Vector3 position)
    {
        DeathData.Instance.DeathPositions.Add(position);
    }
    
    public void SaveDeathPositions()
    {
        DeathData.Instance.Save();
    }
    
    private void OnApplicationQuit()
    {
        SaveDeathPositions();
    }

    private void OnDestroy()
    {
        SaveDeathPositions();
    }
}