using System;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

public enum BotRole
{
    Attacker,
    Defender,
    Flanker,
    Scout
}

public class BotComponent : MonoBehaviour
{
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private float reactionTime = 0.3f;
    [SerializeField] private float chanceToShoot = 0.5f;
    [SerializeField] private GameObject deathEffect;
    [SerializeField] private BotRole role;

    private TeamManager _teamManager;
    private BotComponent _target;
    private float _currentReactionTime;
    private MapZoneComponent _currentZone;
    
    public BotRole Role
    {
        get => role;
        private set => role = value;
    }
    
    public bool IsOnPosition => agent.remainingDistance < 0.01f;

    private void Start()
    {
    }

    public void Init(TeamManager teamManager)
    {
        _teamManager = teamManager;
    }

    private void Update()
    {
        if (agent.remainingDistance < 0.01f && _currentZone)
            agent.destination = _currentZone.GetRandomPointInZone();

        if (!_target) return;
        _currentReactionTime += Time.deltaTime;

        if (!(_currentReactionTime >= reactionTime)) return;
        _currentReactionTime = 0;

        Shoot();
    }

    private void FixedUpdate()
    {
        foreach (var bot in _teamManager.OtherTeam.Bots)
        {
            var ray = new Ray(transform.position, bot.transform.position - transform.position);
            if (!Physics.Raycast(ray, out var hit, 1000f)) continue;

            if (hit.collider.gameObject != bot.gameObject) continue;

            _target = bot;

            Debug.DrawLine(transform.position, bot.transform.position, Color.red);
        }
    }
    
    public void MoveToZone(MapZoneComponent zone)
    {
        _currentZone = zone;
        if (zone != null)
        {
            agent.SetDestination(zone.GetRandomPointInZone());
        }
    }

    public void AssignRole(BotRole newRole, MapZoneComponent initialZone = null)
    {
        Role = newRole;
        if (initialZone != null)
        {
            MoveToZone(initialZone);
        }
    }
    
    private void Shoot()
    {
        var ray = new Ray(transform.position, _target.transform.position - transform.position);

        if (!Physics.Raycast(ray, out var hit, 1000f)) return;
        if (hit.collider.gameObject != _target.gameObject)
        {
            _target = null;
            return;
        }

        if (Random.value > chanceToShoot) return;

        _target.Die();
        _target = null;
    }

    private void Die()
    {
        switch (role)
        {
            //Notify defenders witch site is under attack
            case BotRole.Defender:
                _teamManager.NotifyDefenders(_teamManager.OtherTeam.targetSite);
                break;
            case BotRole.Attacker:
                _teamManager.OtherTeam.NotifyDefenders(_teamManager.targetSite);
                break;
            default:
                break;
        }

        GameManager.Instance.SaveDeathPosition(transform.position);
        _teamManager.Bots.Remove(this);
        var obj = Instantiate(deathEffect, transform.position, Quaternion.identity);
        DontDestroyOnLoad(obj);
        Destroy(gameObject);
    }
}