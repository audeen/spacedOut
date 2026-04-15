namespace SpacedOut.Poi;

/// <summary>One possible resource grant from completing a POI.</summary>
public class PoiRewardEntry
{
    public string ResourceId { get; init; } = "";
    public int Min { get; init; }
    public int Max { get; init; }
}
