using System.Collections.Generic;
using UnityEngine;

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

    // Жизненный цикл матча (старт/стоп/проверка победы/респавн) принадлежит MatchManager.
    // TeamManager.Update раньше сам звал GameManager.RestartGame при пустом bots — это
    // конфликтовало с loop-логикой, поэтому теперь Update ничего не делает.
    public virtual void Update()
    {
    }

    // Удалит из bots всех уже уничтоженных компонентов (на случай гонки между Die() и Update).
    public int LiveBotsCount
    {
        get
        {
            if (bots == null) return 0;
            int n = 0;
            foreach (var b in bots)
                if (b != null) n++;
            return n;
        }
    }
    
    public void NotifyDefenders(MapZoneComponent site)
    {
        if (bots == null) return;
        for (int i = 0; i < bots.Count; i++)
        {
            BotComponent defender = bots[i];
            if (defender == null) continue;
            defender.AssignRole(BotRole.Defender, site);
        }
    }

    public void NotifyDefendersAboutRotate(MapZoneComponent site)
    {
        if (bots == null) return;
        int upTo = Mathf.Min(bots.Count, bots.Count / 2 + 1);
        for (var i = 0; i < upTo; i++)
        {
            BotComponent bot = bots[i];
            if (bot == null) continue;
            bot.AssignRole(BotRole.Defender, site);
        }
    }

    // Чистит из списка все уничтоженные ссылки. Зовём перед массовыми итерациями
    // в Update командных менеджеров, чтобы LINQ/индексы не падали на Unity-null.
    public void PruneDeadBots()
    {
        if (bots == null) return;
        for (int i = bots.Count - 1; i >= 0; i--)
        {
            if (bots[i] == null)
                bots.RemoveAt(i);
        }
    }
}