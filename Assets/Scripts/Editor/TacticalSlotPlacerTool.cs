using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

[InitializeOnLoad]
public static class TacticalSlotPlacerTool
{
    private const string MenuPath = "Tools/Tactical Slots/Enable Placer Tool";
    private const string PrefKey  = "TacticalSlotPlacerTool.Enabled";

    private static readonly KeyCode[] HoldKeys = { KeyCode.Minus, KeyCode.KeypadMinus };
    private static readonly KeyCode[] PeekKeys = { KeyCode.Equals, KeyCode.Plus, KeyCode.KeypadPlus, KeyCode.KeypadEquals };
    private const float RayMarchStep = 0.25f;
    private const float NavMeshSnapRadius = 0.5f;
    private const float MaxRayDistance = 200f;
    private const float RayStartOffset = 0.1f;

    private const float ZoneSearchRadius = 0.2f;

    static TacticalSlotPlacerTool()
    {
        if (Enabled) SubscribeSceneGUI();
    }

    private static bool Enabled
    {
        get => EditorPrefs.GetBool(PrefKey, false);
        set
        {
            EditorPrefs.SetBool(PrefKey, value);
            Menu.SetChecked(MenuPath, value);
            if (value) SubscribeSceneGUI();
            else       UnsubscribeSceneGUI();
        }
    }

    [MenuItem(MenuPath)]
    private static void ToggleEnabled()
    {
        Enabled = !Enabled;
        SceneView.RepaintAll();
    }

    [MenuItem(MenuPath, true)]
    private static bool ToggleEnabledValidate()
    {
        Menu.SetChecked(MenuPath, Enabled);
        return true;
    }

    private static void SubscribeSceneGUI()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        SceneView.duringSceneGui += OnSceneGUI;
    }

    private static void UnsubscribeSceneGUI()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
    }

    private static void OnSceneGUI(SceneView sceneView)
    {
        Event e = Event.current;

        DrawOverlay(sceneView);

        if (e.type != EventType.KeyDown) return;

        TacticalSlotKind? kind = null;
        if (System.Array.IndexOf(HoldKeys, e.keyCode) >= 0) kind = TacticalSlotKind.HoldDefender;
        else if (System.Array.IndexOf(PeekKeys, e.keyCode) >= 0) kind = TacticalSlotKind.PeekAttacker;
        if (kind == null) return;

        e.Use();

        if (EditorApplication.isPlayingOrWillChangePlaymode || Application.isPlaying)
        {
            Debug.LogWarning("TacticalSlotPlacerTool: инструмент работает только в Edit Mode. Выйдите из Play Mode для расстановки слотов.");
            return;
        }

        if (!TryResolvePlacement(sceneView, e.mousePosition, out Vector3 worldPos, out Vector3 facing, out string error))
        {
            Debug.LogWarning($"TacticalSlotPlacerTool: {error}");
            return;
        }

        MapZoneComponent zone = FindZoneAtPosition(worldPos);
        if (zone == null)
        {
            Debug.LogWarning(
                $"TacticalSlotPlacerTool: в точке {worldPos} не найдена MapZoneComponent. " +
                "Маркер не создан — поставьте курсор внутрь триггер-коллайдера какой-нибудь зоны (Site/Neutral/Room/Main/Link).");
            return;
        }

        CreateMarker(kind.Value, worldPos, facing, zone);
    }

    private static bool TryResolvePlacement(
        SceneView sceneView, Vector2 mousePos,
        out Vector3 worldPos, out Vector3 facing, out string error)
    {
        worldPos = default;
        facing   = Vector3.forward;
        error    = null;

        if (sceneView == null || sceneView.camera == null)
        {
            error = "нет активного Scene View.";
            return false;
        }

        Ray ray = HandleUtility.GUIPointToWorldRay(mousePos);

        Vector3 rayPoint = default;
        bool hitFound = false;
        for (float t = RayStartOffset; t <= MaxRayDistance; t += RayMarchStep)
        {
            Vector3 p = ray.GetPoint(t);
            if (NavMesh.SamplePosition(p, out NavMeshHit navHit, NavMeshSnapRadius, NavMesh.AllAreas))
            {
                rayPoint = navHit.position;
                hitFound = true;
                break;
            }
        }

        if (!hitFound)
        {
            error = $"луч из Scene-камеры через курсор не пересекает NavMesh в пределах {MaxRayDistance} м " +
                    $"(шаг {RayMarchStep} м, радиус снэпа {NavMeshSnapRadius} м). " +
                    "Запеките NavMesh или прицельтесь точнее в проходимую область.";
            return false;
        }

        worldPos = rayPoint;

        Vector3 camForward = sceneView.camera.transform.forward;
        camForward.y = 0f;
        if (camForward.sqrMagnitude < 0.0001f) camForward = Vector3.forward;
        facing = camForward.normalized;
        return true;
    }

    private static MapZoneComponent FindZoneAtPosition(Vector3 worldPos)
    {
        Collider[] hits = Physics.OverlapSphere(worldPos, ZoneSearchRadius, ~0, QueryTriggerInteraction.Collide);
        if (hits == null || hits.Length == 0) return null;

        // Берём первую попавшуюся MapZoneComponent. Если зон несколько в одной точке
        // (вложенные коллайдеры), пользователь может потом перепривязать поле zone вручную.
        foreach (Collider c in hits)
        {
            if (c == null) continue;
            MapZoneComponent z = c.GetComponentInParent<MapZoneComponent>();
            if (z != null) return z;
        }
        return null;
    }

    private static void CreateMarker(TacticalSlotKind kind, Vector3 worldPos, Vector3 facing, MapZoneComponent zone)
    {
        string namePrefix = kind == TacticalSlotKind.HoldDefender ? "HoldSlot" : "PeekSlot";
        GameObject go = new GameObject($"{namePrefix}_{zone.name}");

        // Родителем делаем зону — так маркеры группируются под зоной в иерархии,
        // их легко найти и массово удалить вместе с зоной.
        go.transform.SetParent(zone.transform, worldPositionStays: true);
        go.transform.position = worldPos;
        go.transform.rotation = Quaternion.LookRotation(facing, Vector3.up);

        TacticalSlotMarker marker = go.AddComponent<TacticalSlotMarker>();
        marker.kind = kind;
        marker.zone = zone;
        // coverHeight и influenceRadius оставляем дефолтными — настраиваются в инспекторе.

        Undo.RegisterCreatedObjectUndo(go, $"Create {namePrefix}");
        EditorSceneManager.MarkSceneDirty(go.scene);

        Debug.Log(
            $"TacticalSlotPlacerTool: создан {kind} в {worldPos:F2} (зона: {zone.name}). " +
            "Стрелка gizmo показывает facingDir; крутите Y-поворот объекта в инспекторе, чтобы изменить направление взгляда.",
            go);
    }

    private static void DrawOverlay(SceneView sceneView)
    {
        Handles.BeginGUI();
        var style = new GUIStyle(GUI.skin.label)
        {
            normal = { textColor = new Color(0.2f, 1f, 0.4f, 1f) },
            fontStyle = FontStyle.Bold,
        };
        GUILayout.BeginArea(new Rect(10, 10, 320, 60));
        GUILayout.Label("Tactical Slot Placer: ON", style);
        GUILayout.Label("[-]  HoldDefender   |   [=]  PeekAttacker", EditorStyles.miniLabel);
        GUILayout.Label("facingDir = forward Scene-камеры (XZ)", EditorStyles.miniLabel);
        GUILayout.EndArea();
        Handles.EndGUI();
    }
}
