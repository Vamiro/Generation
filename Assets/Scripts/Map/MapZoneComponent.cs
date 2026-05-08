using System.Collections.Generic;
using UnityEngine;

public class MapZoneComponent : MonoBehaviour
{
    [Header("Zone ID")]
    [SerializeField] private int id;
    public int SpawnId => id;
    
    [Header("Zone Weights")]
    [SerializeField] private float attackWeight = 0f;
    [SerializeField] private float defenseWeight = 0f;
    [SerializeField] private float flankWeight = 0f;
    [SerializeField] private float scoutWeight = 0f;
    
    [Header("Zone Collider")]
    [SerializeField] private BoxCollider boxCollider;
    private List<Vector3> samplePoints = new();

    public void InitializeZone(int zoneId, BoxCollider collider)
    {
        id = zoneId;
        boxCollider = collider;
    }

    public void SetSamplePoints(List<Vector3> points)
    {
        samplePoints = points ?? new List<Vector3>();
    }

    public float GetWeight(BotRole role)
    {
        return role switch
        {
            BotRole.Attacker => attackWeight,
            BotRole.Defender => defenseWeight,
            BotRole.Flanker => flankWeight,
            BotRole.Scout => scoutWeight,
            _ => 1f
        };
    }

    public Vector3 GetRandomPointInZone()
    {
        if (samplePoints.Count > 0)
            return samplePoints[Random.Range(0, samplePoints.Count)];

        if (boxCollider == null)
            return transform.position;

        return new Vector3(
            Random.Range(boxCollider.bounds.min.x, boxCollider.bounds.max.x),
            boxCollider.bounds.center.y,
            Random.Range(boxCollider.bounds.min.z, boxCollider.bounds.max.z)
        );
    }
}
