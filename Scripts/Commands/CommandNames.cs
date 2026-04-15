namespace SpacedOut.Commands;

public static class CommandNames
{
    // CaptainNav (merged Captain + Navigator)
    public const string ApproveOverlay = "ApproveOverlay";
    public const string DismissOverlay = "DismissOverlay";
    public const string SetMissionPriority = "SetMissionPriority";
    public const string ResolveDecision = "ResolveDecision";
    public const string RequestStatus = "RequestStatus";
    public const string SetEngagementRule = "SetEngagementRule";
    public const string SetWaypoint = "SetWaypoint";
    public const string RemoveWaypoint = "RemoveWaypoint";
    public const string ChangeFlightMode = "ChangeFlightMode";
    public const string HighlightRoute = "HighlightRoute";
    public const string ToggleStarMapOnMainScreen = "ToggleStarMapOnMainScreen";
    public const string SetCourseToContact = "SetCourseToContact";

    // Run / Map
    public const string SelectNode = "SelectNode";
    public const string ResolveRunNode = "ResolveRunNode";

    // Engineer
    public const string SetEnergyDistribution = "SetEnergyDistribution";
    public const string StartRepair = "StartRepair";
    public const string TriggerEmergencyShutdown = "TriggerEmergencyShutdown";
    public const string RaiseSystemWarning = "RaiseSystemWarning";
    public const string CoolantPulse = "CoolantPulse";
    public const string OverchargeSystem = "OverchargeSystem";
    public const string ConvertSparesToAmmo = "ConvertSparesToAmmo";

    // Tactical
    public const string ScanContact = "ScanContact";
    public const string MarkContact = "MarkContact";
    public const string RaiseTacticalWarning = "RaiseTacticalWarning";
    public const string ToggleTacticalOnMainScreen = "ToggleTacticalOnMainScreen";
    public const string DeployProbe = "DeployProbe";
    public const string ReleaseToNavigator = "ReleaseToNavigator";
    public const string DesignateTarget = "DesignateTarget";
    public const string AnalyzeWeakness = "AnalyzeWeakness";
    public const string SetSensorMode = "SetSensorMode";
    public const string PinContact = "PinContact";
    public const string UnpinContact = "UnpinContact";

    // Tactical – POI
    public const string AnalyzePoi = "AnalyzePoi";

    // Engineer – POI
    public const string ActivateTractor = "ActivateTractor";
    public const string ExtractResource = "ExtractResource";

    // Gunner
    public const string SelectTarget = "SelectTarget";
    public const string Fire = "Fire";
    public const string CeaseFire = "CeaseFire";
    public const string SetWeaponMode = "SetWeaponMode";
    public const string SetDefensiveMode = "SetDefensiveMode";
    public const string SetAutofire = "SetAutofire";
    public const string SetToolMode = "SetToolMode";
    public const string DrillTarget = "DrillTarget";
}
