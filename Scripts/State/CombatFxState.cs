using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SpacedOut.State;

/// <summary>
/// Visual presentation style for a weapon shot. Pure cosmetic — does not influence
/// hit math, damage, or cooldowns. Consumed by <see cref="SpacedOut.Fx.CombatFxSystem"/>
/// to pick the right FX preset scene.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WeaponVisualKind
{
    LaserBeam,
    PlasmaBolt,
    KineticTracer,
}

/// <summary>
/// A single fired shot recorded for the FX layer. Enqueued in the simulation tick
/// (player gunner + enemy attacks) and consumed exactly once by the 3D FX system.
/// </summary>
public class ShotEvent
{
    /// <summary>"player" for the ship, otherwise a <see cref="Contact.Id"/>.</summary>
    public string ShooterId { get; set; } = "";

    /// <summary>"player" for the ship, otherwise a <see cref="Contact.Id"/>.</summary>
    public string TargetId { get; set; } = "";

    public WeaponVisualKind Visual { get; set; }

    public bool Hit { get; set; }

    /// <summary>Mission elapsed time when the shot was resolved.</summary>
    public float TimestampSec { get; set; }
}

/// <summary>
/// Ring buffer of pending shot events for the 3D combat FX system. Marked
/// <see cref="JsonIgnoreAttribute"/> so the web broadcast stays lean.
/// </summary>
public class CombatFxState
{
    [JsonIgnore]
    public List<ShotEvent> PendingShots { get; } = new();
}
