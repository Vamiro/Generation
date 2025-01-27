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
        return new Vector3(
            Random.Range(boxCollider.bounds.min.x, boxCollider.bounds.max.x),
            boxCollider.bounds.center.y,
            Random.Range(boxCollider.bounds.min.z, boxCollider.bounds.max.z)
        );
    }
}
