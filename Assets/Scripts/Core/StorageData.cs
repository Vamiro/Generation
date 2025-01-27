public class StorageData<T> where T : StorageData<T>, new()
{
    public static T Instance = Storage.Load<T>();
    public void Save() { Storage.Save(Instance); }
}