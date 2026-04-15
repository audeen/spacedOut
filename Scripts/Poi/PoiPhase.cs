using System.Text.Json.Serialization;

namespace SpacedOut.Poi;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PoiPhase
{
    None,
    Analyzed,
    Opened,
    Extracting,
    Complete,
    Failed,
}
