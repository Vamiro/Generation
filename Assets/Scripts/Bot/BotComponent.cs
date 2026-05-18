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
    
    public void SetAgentSpeed(float speed)
    {
        agent.speed = speed;
    }
    
    // remainingDistance валиден только пока агент на NavMesh, путь рассчитан и не в pendingPath.
    // Иначе Unity бросает варнинг "GetRemainingDistance can only be called on an active agent...".
    public bool IsOnPosition
    {
        get
        {
            if (agent == null || !agent.isOnNavMesh) return false;
            if (agent.pathPending) return false;
            return agent.remainingDistance < 0.01f;
        }
    }

    private void Start()
    {
    }

    public void Init(TeamManager teamManager)
    {
        _teamManager = teamManager;
    }

    private void Update()
    {
        // Все ссылки проверяем Unity-null-safe (==), потому что MonoBehaviour может
        // быть "фейково" уничтожен между кадрами.
        if (_teamManager == null) return;
        if (_target == null) return;
        _currentReactionTime += Time.deltaTime;

        if (!(_currentReactionTime >= reactionTime)) return;
        _currentReactionTime = 0;

        Shoot();
    }

    private void FixedUpdate()
    {
        if (_teamManager == null) return;
        TeamManager other = _teamManager.OtherTeam;
        if (other == null) return;

        var enemyBots = other.Bots;
        if (enemyBots == null) return;

        for (int i = 0; i < enemyBots.Count; i++)
        {
            BotComponent bot = enemyBots[i];
            // Destroyed-боты могут лежать в списке между смертью и Bots.Remove(this).
            if (bot == null) continue;

            var ray = new Ray(transform.position, bot.transform.position - transform.position);
            if (!Physics.Raycast(ray, out var hit, 1000f)) continue;
            if (hit.collider == null || hit.collider.gameObject != bot.gameObject) continue;

            _target = bot;
            Debug.DrawLine(transform.position, bot.transform.position, Color.red);
        }
    }
    
    public void MoveToZone(MapZoneComponent zone)
    {
        _currentZone = zone;
        if (zone == null || agent == null || !agent.isOnNavMesh) return;
        agent.SetDestination(zone.GetRandomPointInZone());
    }

    public void AssignRole(BotRole newRole, MapZoneComponent initialZone = null)
    {
        Role = newRole;
        SetAgentSpeed(Role == BotRole.Scout ? 3.5f : 5f);

        if (initialZone != null)
        {
            MoveToZone(initialZone);
        }
    }
    
    private void Shoot()
    {
        if (_target == null) return;

        var ray = new Ray(transform.position, _target.transform.position - transform.position);

        if (!Physics.Raycast(ray, out var hit, 1000f)) return;
        if (hit.collider == null || hit.collider.gameObject != _target.gameObject)
        {
            _target = null;
            return;
        }

        if (Random.value > chanceToShoot) return;

        BotComponent victim = _target;
        _target = null;
        victim.Die();
    }

    private void Die()
    {
        // Уведомляем оппонента/свою команду — но безопасно: к этому моменту
        // MatchManager мог уже начать DestroyTeams, и любая ссылка может оказаться Unity-null.
        TeamManager team = _teamManager;
        TeamManager other = team != null ? team.OtherTeam : null;

        if (team != null && other != null)
        {
            switch (role)
            {
                case BotRole.Defender:
                    team.NotifyDefenders(other.targetSite);
                    break;
                case BotRole.Attacker:
                    other.NotifyDefenders(team.targetSite);
                    break;
            }
        }

        var gm = GameManager.Instance;
        if (gm != null)
        {
            gm.SaveDeathPosition(transform.position);
            gm.IncreaseDeathCount();
        }

        if (team != null && team.Bots != null)
            team.Bots.Remove(this);

        if (deathEffect != null)
        {
            var spawnDeathEffect = new Vector3(transform.position.x, 50f, transform.position.z);
            var obj = Instantiate(deathEffect, spawnDeathEffect, Quaternion.identity);
            DontDestroyOnLoad(obj);
        }
        Destroy(gameObject);
    }
}