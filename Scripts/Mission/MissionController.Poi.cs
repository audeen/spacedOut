using System;
using System.Linq;
using Godot;
using SpacedOut.Poi;
using SpacedOut.Run;
using SpacedOut.State;

namespace SpacedOut.Mission;

public partial class MissionController
{
    private float _sensorPenaltyTimer;
    private float _sensorPenaltyAmount;

    public void UpdatePoiInteractions(float delta)
    {
        if (_sensorPenaltyTimer > 0)
            _sensorPenaltyTimer = Math.Max(0, _sensorPenaltyTimer - delta);

        foreach (var contact in _state.Contacts)
        {
            if (string.IsNullOrEmpty(contact.PoiType)) continue;
            if (contact.PoiPhase is PoiPhase.Complete or PoiPhase.Failed) continue;

            var bp = PoiBlueprintCatalog.GetOrNull(contact.PoiType);
            if (bp == null) continue;

            TickPoiAnalysis(contact, bp, delta);
            TickPoiDrill(contact, bp, delta);
            TickPoiExtraction(contact, bp, delta);
            TickPoiInstability(contact, bp, delta);
        }
    }

    /// <summary>Active sensor range penalty from anomaly POIs.</summary>
    public float GetPoiSensorRangePenalty() =>
        _sensorPenaltyTimer > 0 ? _sensorPenaltyAmount : 0f;

    private void TickPoiAnalysis(Contact contact, PoiBlueprint bp, float delta)
    {
        if (!contact.PoiAnalyzing) return;
        if (contact.PoiPhase != PoiPhase.None) { contact.PoiAnalyzing = false; return; }

        float dx = contact.PositionX - _state.Ship.PositionX;
        float dy = contact.PositionY - _state.Ship.PositionY;
        float dist = MathF.Sqrt(dx * dx + dy * dy);
        if (dist > bp.AnalyzeRange * 1.2f)
        {
            contact.PoiAnalyzing = false;
            contact.PoiProgress = 0;
            AddLog("Tactical", $"Tiefenscan abgebrochen — außer Reichweite: {contact.DisplayName}");
            return;
        }

        float sensorEff = _state.Ship.Systems[SystemId.Sensors].GetHeatEfficiencyMultiplier();
        float energyMult = _state.Ship.Energy.Sensors / 25f;
        float speed = 100f / bp.AnalyzeDuration * sensorEff * energyMult;
        contact.PoiProgress = Math.Min(100f, contact.PoiProgress + speed * delta);

        if (contact.PoiProgress >= 100f)
        {
            contact.PoiAnalyzing = false;
            contact.PoiPhase = PoiPhase.Analyzed;
            contact.PoiProgress = 0;

            string analyzedName = !string.IsNullOrEmpty(bp.AnalyzedName)
                ? bp.AnalyzedName : contact.DisplayName;
            contact.DisplayName = analyzedName;

            if (bp.TrapChance > 0 && contact.PoiRewardProfile == "trap")
                contact.PoiTrapRevealed = true;

            string trapInfo = contact.PoiTrapRevealed ? " ⚠ FALLE ERKANNT!" : "";
            AddLog("Tactical", $"Tiefenscan abgeschlossen: {analyzedName} — {bp.AnalysisDescription}{trapInfo}");

            if (bp.SensorRangePenalty > 0)
            {
                _sensorPenaltyAmount = bp.SensorRangePenalty;
                _sensorPenaltyTimer = bp.SensorPenaltyDuration;
                AddLog("Tactical", $"Sensor-Interferenz: Reichweite -{bp.SensorRangePenalty * 100:F0}% für {bp.SensorPenaltyDuration:F0}s");
            }

            if (!bp.RequiresDrill && !bp.RequiresExtraction)
                CompletePoiReward(contact, bp, dist);
        }
    }

    private void TickPoiDrill(Contact contact, PoiBlueprint bp, float delta)
    {
        if (!contact.PoiDrilling) return;
        if (contact.PoiPhase != PoiPhase.Analyzed) { contact.PoiDrilling = false; return; }

        var gunner = _state.Gunner;
        if (gunner.Tool != ToolMode.Mining || gunner.DrillTargetId != contact.Id)
        {
            contact.PoiDrilling = false;
            contact.PoiProgress = 0;
            return;
        }

        if (bp.BarrageCausesFailure && gunner.Mode == WeaponMode.Barrage)
        {
            FailPoi(contact, bp, "Barrage hat den Hohlraum zum Einsturz gebracht!");
            gunner.DrillTargetId = null;
            return;
        }

        float dx = contact.PositionX - _state.Ship.PositionX;
        float dy = contact.PositionY - _state.Ship.PositionY;
        float dist = MathF.Sqrt(dx * dx + dy * dy);
        if (dist > bp.DrillRange * 1.2f)
        {
            contact.PoiDrilling = false;
            contact.PoiProgress = 0;
            AddLog("Gunner", $"Bohrung abgebrochen — außer Reichweite: {contact.DisplayName}");
            gunner.DrillTargetId = null;
            return;
        }

        var weaponSys = _state.Ship.Systems[bp.DrillHeatTarget];
        weaponSys.Heat = Math.Clamp(weaponSys.Heat + bp.DrillHeatRate * delta, 0, ShipSystem.MaxHeat);

        float effMult = weaponSys.GetHeatEfficiencyMultiplier();
        float energyMult = _state.Ship.Energy.Weapons / 25f;
        float speed = 100f / bp.DrillDuration * effMult * energyMult;
        contact.PoiProgress = Math.Min(100f, contact.PoiProgress + speed * delta);

        if (contact.PoiProgress >= 100f)
        {
            contact.PoiDrilling = false;
            contact.PoiPhase = PoiPhase.Opened;
            contact.PoiProgress = 0;
            gunner.DrillTargetId = null;
            AddLog("Gunner", $"Bohrung abgeschlossen: {contact.DisplayName}");

            if (bp.InstabilityTimer > 0)
            {
                contact.PoiInstabilityTimer = bp.InstabilityTimer;
                AddLog("System", $"Instabilität erkannt — {bp.InstabilityTimer:F0}s bis zum Kollaps!");
            }
        }
    }

    private void TickPoiExtraction(Contact contact, PoiBlueprint bp, float delta)
    {
        if (!contact.PoiExtracting) return;
        if (contact.PoiPhase != PoiPhase.Extracting) { contact.PoiExtracting = false; return; }

        float dx = contact.PositionX - _state.Ship.PositionX;
        float dy = contact.PositionY - _state.Ship.PositionY;
        float dist = MathF.Sqrt(dx * dx + dy * dy);
        if (dist > bp.ExtractRange * 1.3f)
        {
            contact.PoiExtracting = false;
            contact.PoiPhase = PoiPhase.Opened;
            contact.PoiProgress = 0;
            AddLog("Engineer", $"Extraktion abgebrochen — außer Reichweite: {contact.DisplayName}");
            return;
        }

        var targetSys = _state.Ship.Systems[bp.ExtractHeatTarget];
        targetSys.Heat = Math.Clamp(targetSys.Heat + bp.ExtractHeatRate * delta, 0, ShipSystem.MaxHeat);

        if (bp.ReducedRewards.Length > 0 && targetSys.Heat >= ShipSystem.SevereEfficiencyThreshold)
        {
            contact.PoiExtracting = false;
            contact.PoiPhase = PoiPhase.Complete;
            GrantRewards(contact, bp.ReducedRewards, bp);
            AddLog("Engineer", $"Überhitzung! Reduzierter Ertrag: {contact.DisplayName}");
            if (bp.Id == "navigation_relay")
            {
                AddLog("Engineer", "Sprungdatenpaket vom Relais extrahiert (unter erschwerten Bedingungen).");
                MaybeRevealExitAfterRelayPipeline(contact, bp);
            }

            return;
        }

        float effMult = targetSys.GetHeatEfficiencyMultiplier();
        float speed = 100f / bp.ExtractDuration * effMult;
        contact.PoiProgress = Math.Min(100f, contact.PoiProgress + speed * delta);

        if (contact.PoiProgress >= 100f)
        {
            contact.PoiExtracting = false;
            contact.PoiPhase = PoiPhase.Complete;
            CompletePoiReward(contact, bp, dist);
        }
    }

    private void TickPoiInstability(Contact contact, PoiBlueprint bp, float delta)
    {
        if (contact.PoiInstabilityTimer <= 0) return;
        if (contact.PoiPhase is PoiPhase.Complete or PoiPhase.Failed) return;

        contact.PoiInstabilityTimer -= delta;
        if (contact.PoiInstabilityTimer <= 0)
            FailPoi(contact, bp, "Strukturkollaps — Zeitlimit überschritten!");
    }

    private void CompletePoiReward(Contact contact, PoiBlueprint bp, float distance)
    {
        if (bp.RewardProfiles.Length > 0 && !string.IsNullOrEmpty(contact.PoiRewardProfile))
        {
            var profile = bp.RewardProfiles.FirstOrDefault(p => p.ProfileId == contact.PoiRewardProfile);
            if (profile != null)
            {
                if (profile.IsTrap && !contact.PoiTrapRevealed)
                {
                    _state.Ship.HullIntegrity = Math.Max(0, _state.Ship.HullIntegrity + profile.HullDelta);
                    contact.PoiPhase = PoiPhase.Failed;
                    AddLog("System", $"Sprengfalle ausgelöst! Hull {profile.HullDelta:+0;-0}");
                    return;
                }
                if (profile.IsTrap && contact.PoiTrapRevealed)
                {
                    contact.PoiPhase = PoiPhase.Complete;
                    AddLog("Engineer", "Falle entschärft — kein Schaden.");
                    return;
                }
                if (profile.HullDelta != 0)
                {
                    // M7: respect MaxHullOverride from perks (e.g. perk_armor) as the upper cap.
                    float cap = _state.ActiveRunState?.MaxHullOverride ?? 100f;
                    _state.Ship.HullIntegrity = Math.Clamp(
                        _state.Ship.HullIntegrity + profile.HullDelta, 0, cap);
                }
                GrantRewards(contact, profile.Rewards, bp);
                contact.PoiPhase = PoiPhase.Complete;
                string hullNote = profile.HullDelta != 0 ? $" | Hull {profile.HullDelta:+0;-0}" : "";
                AddLog("System", $"POI abgeschlossen: {contact.DisplayName} — {profile.Label}{hullNote}");
                return;
            }
        }

        var rewards = bp.Rewards;
        if (bp.RewardScalesWithDistance)
        {
            if (distance > bp.HalfRewardRange)
                rewards = bp.ReducedRewards;
            else if (distance > bp.FullRewardRange && bp.ReducedRewards.Length > 0)
            {
                float t = (distance - bp.FullRewardRange) / (bp.HalfRewardRange - bp.FullRewardRange);
                if (t > 0.5f) rewards = bp.ReducedRewards;
            }

            if (distance <= bp.FullRewardRange && bp.CloseApproachHeatSpike > 0)
            {
                foreach (var sys in _state.Ship.Systems.Values)
                    sys.Heat = Math.Clamp(sys.Heat + bp.CloseApproachHeatSpike, 0, ShipSystem.MaxHeat);
                AddLog("System", $"Nah-Approach: Heat-Spike +{bp.CloseApproachHeatSpike:F0} auf alle Systeme");
            }
        }

        GrantRewards(contact, rewards, bp);
        contact.PoiPhase = PoiPhase.Complete;
        if (bp.Id == "navigation_relay")
        {
            AddLog("Engineer", "Sprungdatenpaket vom Relais extrahiert.");
            MaybeRevealExitAfterRelayPipeline(contact, bp);
        }
        else
            AddLog("System", $"POI abgeschlossen: {contact.DisplayName}");
    }

    private void MaybeRevealExitAfterRelayPipeline(Contact contact, PoiBlueprint bp)
    {
        if (bp.Id != "navigation_relay") return;
        if (contact.Id != "primary_target") return;
        if (_script?.PrimaryObjective?.HideExitUntilScanned != true) return;
        if (contact.PoiPhase != PoiPhase.Complete) return;
        RevealExitCoordinatesFromRelay();
    }

    private void RevealExitCoordinatesFromRelay()
    {
        if (_state.Mission.JumpCoordinatesUnlocked) return;
        var exit = _state.Contacts.Find(c => c.Id == "sector_exit");
        if (exit == null) return;

        _state.Mission.JumpCoordinatesUnlocked = true;
        exit.PreRevealed = true;
        exit.Discovery = DiscoveryState.Scanned;
        exit.ReleasedToNav = true;
        exit.IsVisibleOnMainScreen = true;

        _state.AddMissionLogEntry(new MissionLogEntry
        {
            Timestamp = _state.Mission.ElapsedTime,
            Source = "Navigation",
            Message = "Sprungkoordinaten vom Relais eingespielt — Sektorausgang markiert.",
            WebToast = MissionLogWebToast.ToastProminent,
        });
    }

    private void GrantRewards(Contact contact, PoiRewardEntry[] rewards, PoiBlueprint bp)
    {
        var runState = _state.ActiveRunState;
        if (runState == null) return;

        var rng = new Random(contact.Id.GetHashCode() ^ (int)(_state.Mission.ElapsedTime * 100));
        foreach (var entry in rewards)
        {
            int amount = rng.Next(entry.Min, entry.Max + 1);
            if (amount <= 0) continue;

            runState.Resources.TryGetValue(entry.ResourceId, out int current);
            runState.Resources[entry.ResourceId] = current + amount;
            AddLog("System", $"+{amount} {entry.ResourceId}", MissionLogWebToast.LogOnly);
        }
    }

    private void FailPoi(Contact contact, PoiBlueprint bp, string reason)
    {
        contact.PoiPhase = PoiPhase.Failed;
        contact.PoiDrilling = false;
        contact.PoiExtracting = false;
        contact.PoiAnalyzing = false;
        contact.PoiProgress = 0;

        if (bp.FailureHullDelta != 0)
        {
            _state.Ship.HullIntegrity = Math.Max(0, _state.Ship.HullIntegrity + bp.FailureHullDelta);
            AddLog("System", $"{reason} Hull {bp.FailureHullDelta:+0;-0}");
        }
        else
        {
            AddLog("System", reason);
        }
    }

    private void AddLog(string source, string message, MissionLogWebToast webToast = MissionLogWebToast.Unspecified)
    {
        _state.AddMissionLogEntry(new MissionLogEntry
        {
            Timestamp = _state.Mission.ElapsedTime,
            Source = source,
            Message = message,
            WebToast = webToast,
        });
    }
}
