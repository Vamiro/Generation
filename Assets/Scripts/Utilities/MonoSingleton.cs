using UnityEngine;

public class MonoSingleton<T> : MonoBehaviour where T : MonoSingleton<T>
{
    private static T _instance;

    public static T Instance
    {
        get
        {
            if (_instance == null)
                _instance = FindObjectOfType<T>();
            return _instance;
        }
    }

    protected virtual void Awake()
    {
        if (_instance == null)
        {
            _instance = (T)this;
            return;
        }
        if (_instance == this) return;

        Debug.LogWarning(
            $"MonoSingleton<{typeof(T).Name}>: в сцене найден второй экземпляр на '{gameObject.name}'. " +
            "Он будет уничтожен — оставьте ровно один.",
            this);
        Destroy(gameObject);
    }

    protected virtual void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }
}
