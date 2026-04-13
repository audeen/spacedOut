using System.Collections.Generic;

namespace SpacedOut.State;

/// <summary>Groups contact tracking state.</summary>
public class ContactsState
{
    public List<Contact> Items { get; set; } = new();
    public List<GameEvent> ActiveEvents { get; set; } = new();

    public List<SensorProbe> ActiveProbes { get; set; } = new();
    public int ProbeCharges { get; set; } = 3;
    public float ProbeRechargeTimer { get; set; }

    /// <summary>When true, sensors are in active mode: +50% range/scan speed, but enemies detect the ship.</summary>
    public bool ActiveSensors { get; set; }

    public const int MaxProbeCharges = 5;
    public const float ProbeRechargeTime = 45f;
}
