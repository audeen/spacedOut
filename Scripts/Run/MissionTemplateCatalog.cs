using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SpacedOut.Run;

public static class MissionTemplateCatalog
{
    private static readonly List<MissionTemplate> GenericPoolList = CreateGenericPool();

    public static IReadOnlyList<MissionTemplate> GenericPool => GenericPoolList;

    private static readonly Dictionary<string, MissionTemplate> ById = BuildDictionary();

    private static Dictionary<string, MissionTemplate> BuildDictionary()
    {
        var list = new List<MissionTemplate>();
        list.AddRange(GenericPoolList);
        list.AddRange(CreateStoryPlaceholders());
        return list.ToDictionary(t => t.Id);
    }

    public static bool TryGet(string id, out MissionTemplate? template) =>
        ById.TryGetValue(id, out template);

    public static MissionTemplate? GetOrNull(string id) =>
        TryGet(id, out var t) ? t : null;

    /// <summary>Maps template mission type to run map / encounter routing.</summary>
    public static RunNodeType MapToRunNodeType(MissionType type) => type switch
    {
        MissionType.Anomaly => RunNodeType.Anomaly,
        MissionType.Hostile => RunNodeType.Hostile,
        MissionType.Station => RunNodeType.Station,
        MissionType.Story => RunNodeType.Story,
        MissionType.Salvage or MissionType.Distress or MissionType.Hazard => RunNodeType.Side,
        _ => RunNodeType.Side,
    };

    public static string BuildBriefing(MissionTemplate t)
    {
        var sb = new StringBuilder();
        sb.AppendLine(t.Description.Trim());
        if (t.Objectives.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Aufgaben:");
            foreach (var o in t.Objectives)
                sb.AppendLine($"• {o}");
        }
        if (!string.IsNullOrWhiteSpace(t.Twist))
        {
            sb.AppendLine();
            sb.AppendLine($"Hinweis: {t.Twist.Trim()}");
        }
        sb.AppendLine();
        sb.Append($"Risiko: {t.Risk} · Ertrag: {t.Reward}");
        return sb.ToString().TrimEnd();
    }

    private static IEnumerable<MissionTemplate> CreateStoryPlaceholders()
    {
        yield return new MissionTemplate
        {
            Id = "tutorial_blindsprung",
            Title = "Blindsprung",
            Type = MissionType.Story,
            Category = "tutorial",
            Description = "Notsprung abgeschlossen. Position: unbekanntes Asteroidenfeld. "
                + "Systeme durch Sprung\u00fcberlastung beeintr\u00e4chtigt. "
                + "Langreichweiten-Sensoren haben w\u00e4hrend des Sprungs ein Navigationsrelais erfasst \u2014 grobe Position bekannt. "
                + "Ohne die Koordinaten dieses Relais ist ein kontrollierter Absprung unm\u00f6glich. "
                + "Scannen Sie das Relais und navigieren Sie zum Sektorausgang. "
                + "Achtung: Fragment\u00e4re Sensordaten deuten auf Schiffsverkehr im Sektor hin \u2014 Zuordnung unklar.",
            Objectives = new List<string>
            {
                "Navigationsrelais scannen",
                "Sektorausgang erreichen",
            },
            Twist = "",
            Risk = 2,
            Reward = "Sprungkoordinaten",
            StoryFunction = "tutorial",
        };

        for (var i = 1; i <= 3; i++)
        {
            yield return new MissionTemplate
            {
                Id = $"story_act_{i}",
                Title = $"Story-{i}",
                Type = MissionType.Story,
                Category = "story",
                Description = "Erzählsegment (Kampagne folgt).",
                Objectives = new List<string> { "Ziele des Segments abschließen." },
                Twist = "",
                Risk = 2,
                Reward = "Fortschritt",
                StoryFunction = $"act_{i}_beat",
            };
        }
    }

    private static List<MissionTemplate> CreateGenericPool() =>
        new()
        {
            new MissionTemplate
            {
                Id = "generic_drifting_cargo",
                Title = "Treibende Fracht",
                Type = MissionType.Salvage,
                Category = "generic",
                Description = "Mehrere Container treiben im All, teilweise beschädigt.",
                Objectives = new List<string>
                {
                    "Container kartieren.",
                    "Brauchbare Ladung sichern.",
                },
                Twist = "Einige Container sind leer; einer weist eine seltsame Signatur auf.",
                Risk = 1,
                Reward = "Ersatzteile / Forschungsdaten",
                PossibleFlags = new List<string> { "cargo_strange_signature" },
            },
            new MissionTemplate
            {
                Id = "generic_weak_distress",
                Title = "Schwaches Notsignal",
                Type = MissionType.Distress,
                Category = "generic",
                Description = "Schwaches Notsignal — die Quelle wirkt instabil.",
                Objectives = new List<string>
                {
                    "Signal orten.",
                    "Überlebende oder Daten sichern.",
                },
                Twist = "Das Signal ist alt; die Quelle ist möglicherweise nicht mehr vor Ort.",
                Risk = 2,
                Reward = "Crew / Daten",
                PossibleFlags = new List<string> { "distress_stale" },
            },
            new MissionTemplate
            {
                Id = "generic_debris_field",
                Title = "Trümmerfeld",
                Type = MissionType.Salvage,
                Category = "generic",
                Description = "Zerstörtes Schiff, ausgedehntes Trümmerfeld.",
                Objectives = new List<string>
                {
                    "Wrackbereich durchqueren.",
                    "Wertvolle Komponenten bergen.",
                },
                Twist = "Instabile Trümmerbewegung erschwert die Navigation.",
                Risk = 2,
                Reward = "Munition / Teile",
                PossibleFlags = new List<string> { "debris_unstable" },
            },
            new MissionTemplate
            {
                Id = "generic_sensor_ghost",
                Title = "Sensor-Geist",
                Type = MissionType.Anomaly,
                Category = "generic",
                Description = "Sensoren erfassen ein Objekt, visuell aber nicht sichtbar.",
                Objectives = new List<string>
                {
                    "Anomalie klassifizieren.",
                    "Messdaten sichern.",
                },
                Twist = "Das Ziel verschwindet bei Annäherung aus den Sensoren.",
                Risk = 1,
                Reward = "Forschungsdaten",
                PossibleFlags = new List<string> { "ghost_faded" },
            },
            new MissionTemplate
            {
                Id = "generic_pirate_intercept",
                Title = "Piraten-Abfang",
                Type = MissionType.Hostile,
                Category = "generic",
                Description = "Ein kleines Piratenschiff blockiert den Kurs.",
                Objectives = new List<string>
                {
                    "Kontakt bewerten.",
                    "Durchbruch oder Kampf.",
                },
                Twist = "Die Piraten fordern Ressourcen oder greifen direkt an.",
                Risk = 4,
                Reward = "Munition / Teile",
                PossibleFlags = new List<string> { "pirate_demand" },
            },
            new MissionTemplate
            {
                Id = "generic_abandoned_relay",
                Title = "Verlassenes Relais",
                Type = MissionType.Anomaly,
                Category = "generic",
                Description = "Stillgelegte Kommunikationsstation.",
                Objectives = new List<string>
                {
                    "Relais sichern.",
                    "Datenspuren auslesen.",
                },
                Twist = "Die Datenlogs sind stark fragmentiert.",
                Risk = 1,
                Reward = "Daten",
                PossibleFlags = new List<string> { "relay_fragmented" },
            },
            new MissionTemplate
            {
                Id = "generic_micro_asteroids",
                Title = "Mikro-Asteroiden-Schwarm",
                Type = MissionType.Hazard,
                Category = "generic",
                Description = "Dichter Asteroidenschwarm — Navigation schwierig.",
                Objectives = new List<string>
                {
                    "Sicheren Korridor finden.",
                    "Schiff intakt durchqueren.",
                },
                Twist = "Hohe Kollisionsgefahr bei voller Geschwindigkeit.",
                Risk = 2,
                Reward = "gering",
                PossibleFlags = new List<string> { "asteroid_swarm" },
            },
            new MissionTemplate
            {
                Id = "generic_trading_outpost",
                Title = "Handelsaußenposten",
                Type = MissionType.Station,
                Category = "generic",
                Description = "Kleine Versorgungsstation mit begrenztem Angebot.",
                Objectives = new List<string>
                {
                    "Andocken und handeln.",
                    "Versorgungslage prüfen.",
                },
                Twist = "Das Sortiment ist knapp und wechselt selten.",
                Risk = 1,
                Reward = "Ressourcen tauschen",
                PossibleFlags = new List<string> { "outpost_limited_stock" },
            },
        };
}
