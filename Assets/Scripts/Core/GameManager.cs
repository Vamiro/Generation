using System.Collections.Generic;
using UnityEngine;

public class DeathData : StorageData<DeathData>
{
    public List<Vector3> DeathPositions = new List<Vector3>();
}

// Лёгкий компонент: только сбор метрики смертей + сохранение на выход.
// Жизненным циклом матчей, time scale и таймаутами раундов теперь владеет MatchManager.
// Старая логика SceneManager.LoadScene по таймеру убрана — она конфликтовала с
// match loop (приводила к спонтанной перезагрузке сцены между матчами).
public class GameManager : MonoSingleton<GameManager>
{
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

    protected override void OnDestroy()
    {
        SaveDeathPositions();
        base.OnDestroy();
    }

    // Совместимость со старыми вызовами BotComponent.Die — счётчик больше нигде не читается,
    // но метод оставлен, чтобы не править все Die() и не плодить null-чеки.
    public void IncreaseDeathCount()
    {
    }
}
