using UnityEngine;

public enum RoadType
{
    Main,
    Link
}

public class RoadZoneComponent : MapZoneComponent
{
    [Header("Road to Site")]
    public SiteZoneComponent roadToSite;
    [Header("Road Type")]
    public RoadType roadType;
}
