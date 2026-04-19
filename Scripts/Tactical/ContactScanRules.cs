using System;
using SpacedOut.Shared;
using SpacedOut.State;

namespace SpacedOut.Tactical;

/// <summary>Rules for when tactical may scan a contact (initial scan / progress).</summary>
public static class ContactScanRules
{
    /// <summary>
    /// Probed radar ghosts may only be scanned under active probe coverage or within ship sensor range.
    /// Other discovery states are unchanged.
    /// </summary>
    public static bool CanScanContact(Contact contact, GameState state)
    {
        if (contact.Discovery != DiscoveryState.Probed)
            return true;

        if (IsCoveredByActiveProbe(contact, state))
            return true;

        float sensorRange = ShipCalculations.CalculateSensorRange(state.Ship, state.ContactsState.ActiveSensors);
        float dx = contact.PositionX - state.Ship.PositionX;
        float dy = contact.PositionY - state.Ship.PositionY;
        return MathF.Sqrt(dx * dx + dy * dy) <= sensorRange;
    }

    private static bool IsCoveredByActiveProbe(Contact contact, GameState state)
    {
        foreach (var p in state.ContactsState.ActiveProbes)
        {
            float dx = contact.PositionX - p.X;
            float dy = contact.PositionY - p.Y;
            if (MathF.Sqrt(dx * dx + dy * dy) <= p.RevealRadius)
                return true;
        }

        return false;
    }
}
