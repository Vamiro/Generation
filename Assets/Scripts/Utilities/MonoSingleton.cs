using UnityEngine;

public class MonoSingleton<T> : MonoBehaviour where T : MonoSingleton<T>, new()
{
    private static T _instance;
    public static T Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<T>();
                if (_instance == null)
                {
                    _instance = new GameObject(typeof(T).Name).AddComponent<T>();
                }
            }
            return _instance;
        }
    }
        
    protected virtual void Awake()
    {
        if (_instance == null) return;
        Destroy(gameObject);
    }
}
