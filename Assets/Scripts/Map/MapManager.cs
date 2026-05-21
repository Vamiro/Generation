using System;
using System.Collections.Generic;
using UnityEngine;

public class MapManager : MonoSingleton<MapManager>
{
    [SerializeField] private List<MapZoneComponent> zones = new();
    public List<MapZoneComponent> Zones => zones;
    public List<SiteZoneComponent> SiteZones => GetSortedSiteZones();
    public List<RoadZoneComponent> RoadZones => GetZonesOfType<RoadZoneComponent>();
    public List<SpawnZoneComponent> SpawnZones => GetZonesOfType<SpawnZoneComponent>();
    public List<NeutralZoneComponent> NeutralZones => GetZonesOfType<NeutralZoneComponent>();

    // Раньше веса хранились в каждом MapZoneComponent. Теперь все веса централизованы
    // здесь, в инспекторе MapManager.
    [Serializable]
    public struct ZoneRoleWeights
    {
        [Tooltip("Вес зоны для атакеров. Используется в AttackerTeamManager.PickWeightedSite (weighted random выбор атакуемого сайта).")]
        [Min(0f)] public float attack;
        [Tooltip("Вес зоны для защитников. Используется в DefenderTeamManager.ComputeDefendersDistribution (пропорциональное распределение защитников по сайтам).")]
        [Min(0f)] public float defense;
        [Tooltip("Вес зоны для фланкеров. Зарезервировано — пока в логике не читается.")]
        [Min(0f)] public float flank;
        [Tooltip("Вес зоны для скаутов. Зарезервировано — пока в логике не читается.")]
        [Min(0f)] public float scout;

        public static ZoneRoleWeights Default => new ZoneRoleWeights
        {
            attack = 1f, defense = 1f, flank = 1f, scout = 1f
        };
    }

    [Header("Веса сайтов (баланс матча)")]
    [Tooltip("Веса для siteA — сайт с меньшим SpawnId в списке зон. Если все веса 0 → команды используют uniform random / равное распределение (старое поведение).")]
    [SerializeField] private ZoneRoleWeights siteAWeights = ZoneRoleWeights.Default;
    [Tooltip("Веса для siteB — сайт со следующим SpawnId.")]
    [SerializeField] private ZoneRoleWeights siteBWeights = ZoneRoleWeights.Default;

    protected override void Awake()
    {
        base.Awake();

        if (zones == null)
            zones = new List<MapZoneComponent>();

        zones.RemoveAll(zone => zone == null);
    }

    public void ClearZones()
    {
        zones.Clear();
    }

    public void RegisterZone(MapZoneComponent zone)
    {
        if (zone == null || zones.Contains(zone))
            return;

        zones.Add(zone);
    }

    public void RegisterZones(IEnumerable<MapZoneComponent> newZones)
    {
        if (newZones == null)
            return;

        foreach (MapZoneComponent zone in newZones)
            RegisterZone(zone);
    }

    public float GetWeight(MapZoneComponent zone, BotRole role)
    {
        if (zone == null) return 0f;

        if (zone is SiteZoneComponent site)
        {
            List<SiteZoneComponent> sites = GetSortedSiteZones();
            int index = sites.IndexOf(site);
            if (index == 0) return PickFromWeights(siteAWeights, role);
            if (index == 1) return PickFromWeights(siteBWeights, role);
            return 0f;
        }

        return 0f;
    }

    private static float PickFromWeights(ZoneRoleWeights w, BotRole role)
    {
        return role switch
        {
            BotRole.Attacker => w.attack,
            BotRole.Defender => w.defense,
            BotRole.Flanker => w.flank,
            BotRole.Scout => w.scout,
            _ => 0f
        };
    }

    private List<SiteZoneComponent> GetSortedSiteZones()
    {
        List<SiteZoneComponent> sites = GetZonesOfType<SiteZoneComponent>();
        sites.Sort((a, b) => a.SpawnId.CompareTo(b.SpawnId));
        return sites;
    }

    private List<TZone> GetZonesOfType<TZone>() where TZone : MapZoneComponent
    {
        for (int i = zones.Count - 1; i >= 0; i--)
        {
            if (zones[i] == null)
                zones.RemoveAt(i);
        }

        List<TZone> result = new();
        foreach (MapZoneComponent zone in zones)
        {
            if (zone is TZone typedZone)
                result.Add(typedZone);
        }

        return result;
    }
}
