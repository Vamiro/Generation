using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

public partial class MapGenerator
{
    [Header("NavMesh")]
    [SerializeField, Tooltip("Запекать NavMesh после генерации карты. Нужен для ботов (NavMeshAgent).")]
    private bool buildNavMesh = true;
    [SerializeField, Tooltip("ID типа агента (0 = Humanoid по умолчанию). Должен совпадать с NavMeshAgent у Bot.prefab.")]
    private int navMeshAgentTypeId = 0;

    private NavMeshSurface navMeshSurface;

    void RebuildNavMesh()
    {
        if (!buildNavMesh) return;
        if (geometryRoot == null)
        {
            Debug.LogWarning("MapGenerator.RebuildNavMesh: geometryRoot не инициализирован.");
            return;
        }

        // NavMeshSurface живёт на контейнере Geometry. Этого достаточно, чтобы
        // collectObjects=Children собирало только пол/стены/укрытия, и ничего из
        // Zones/ или из менеджеров матча не попадало в бейк.
        navMeshSurface = geometryRoot.GetComponent<NavMeshSurface>();
        if (navMeshSurface == null)
            navMeshSurface = geometryRoot.gameObject.AddComponent<NavMeshSurface>();

        navMeshSurface.agentTypeID = navMeshAgentTypeId;
        navMeshSurface.collectObjects = CollectObjects.Children;
        navMeshSurface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        navMeshSurface.layerMask = ~0; // все слои внутри Geometry; зоны живут отдельно

        navMeshSurface.BuildNavMesh();
    }

    public bool HasNavMesh => navMeshSurface != null && navMeshSurface.navMeshData != null;
}
