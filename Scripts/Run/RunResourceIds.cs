namespace SpacedOut.Run;

/// <summary>
/// String keys for run-level resources used for meta-progress within a single run.
/// Hüllenintegrität ist KEINE Run-Ressource — sie lebt als <c>ShipState.HullIntegrity</c>.
/// </summary>
/// <remarks>
/// Semantik:
/// <list type="bullet">
///   <item><description><c>SpareParts</c> — Reparatur und Ausbau (Upgrades) des Schiffs.</description></item>
///   <item><description><c>ScienceData</c> — Karte weiter aufdecken, neue Upgrades erforschen.</description></item>
///   <item><description><c>Fuel</c> — Sprünge zwischen Sektoren.</description></item>
///   <item><description><c>Credits</c> — Universalwährung; kauft die drei anderen Ressourcen und weitere Dinge.</description></item>
/// </list>
/// </remarks>
public static class RunResourceIds
{
    public const string SpareParts = "SpareParts";
    public const string ScienceData = "ScienceData";
    public const string Fuel = "Fuel";
    public const string Credits = "Credits";
}
