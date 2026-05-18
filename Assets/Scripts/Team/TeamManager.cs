using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;

public abstract class TeamManager : MonoBehaviour
{
    [SerializeField] private TeamManager otherTeam;
    [SerializeField] private List<BotComponent> bots;
    public SiteZoneComponent targetSite;

    public List<BotComponent> Bots => bots;
    public TeamManager OtherTeam => otherTeam;

    // Заполнение состава команды из кода (для рантайм-спавна через MatchManager).
    // Должно быть вызвано ДО первого Start() — то есть в том же кадре, что и AddComponent.
    public void SetBots(List<BotComponent> newBots)
    {
        bots = newBots ?? new List<BotComponent>();
    }

    public void SetOtherTeam(TeamManager team)
    {
        otherTeam = team;
    }

    public virtual void Start()
    {
        if (bots == null) return;
        foreach (var bot in bots)
        {
            if (bot != null)
                bot.Init(this);
        }
    }

    public virtual void Update()
    {
        if (bots.Count != 0) return;
        Debug.Log($"{otherTeam.name} has won!");
        GameManager.Instance.RestartGame();
    }
    
    public void NotifyDefenders(MapZoneComponent site)
    {
        foreach (var defender in Bots)
        {
            defender.AssignRole(BotRole.Defender, site);
        }
    }

    public void NotifyDefendersAboutRotate(MapZoneComponent site)
    {
        for (var i = 0; i < Bots.Count / 2 + 1; i++)
        {
            Bots[i].AssignRole(BotRole.Defender, site);
        }
    }
}