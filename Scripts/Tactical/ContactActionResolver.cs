using System.Collections.Generic;
using SpacedOut.Poi;
using SpacedOut.State;

namespace SpacedOut.Tactical;

public static class ContactActionResolver
{
    public static List<ActionDescriptor> Resolve(Contact contact, GameState state)
    {
        var actions = new List<ActionDescriptor>();

        AddScanAction(actions, contact);
        AddReleaseToNavAction(actions, contact);
        AddMarkAction(actions, contact);
        AddDesignateAction(actions, contact);
        AddWeaknessAction(actions, contact);
        AddPoiAnalyzeAction(actions, contact);

        return actions;
    }

    private static void AddScanAction(List<ActionDescriptor> actions, Contact contact)
    {
        if (contact.ScanProgress >= 100)
        {
            actions.Add(new ActionDescriptor
            {
                Id = "scan",
                Command = "ScanContact",
                Label = "Scan komplett",
                Style = "primary",
                Disabled = true,
                Active = true,
                Group = "scan",
            });
        }
        else if (contact.IsScanning)
        {
            actions.Add(new ActionDescriptor
            {
                Id = "scan",
                Command = "ScanContact",
                Label = $"Scanne... {contact.ScanProgress:0}%",
                Style = "primary",
                Disabled = true,
                Progress = contact.ScanProgress,
                Group = "scan",
            });
        }
        else
        {
            actions.Add(new ActionDescriptor
            {
                Id = "scan",
                Command = "ScanContact",
                Label = "Scannen",
                Style = "primary",
                Group = "scan",
            });
        }
    }

    private static void AddReleaseToNavAction(List<ActionDescriptor> actions, Contact contact)
    {
        if (contact.PreRevealed)
        {
            actions.Add(new ActionDescriptor
            {
                Id = "release_nav",
                Command = "",
                Label = "Bekanntes Objekt",
                Style = "success",
                Disabled = true,
                Active = true,
                Group = "info",
            });
            return;
        }

        if (contact.Discovery != DiscoveryState.Scanned) return;

        if (contact.ReleasedToNav)
        {
            actions.Add(new ActionDescriptor
            {
                Id = "release_nav",
                Command = "ReleaseToNavigator",
                Label = "Kommandant: Freigegeben \u2713",
                Style = "success",
                Active = true,
                Group = "info",
            });
        }
        else
        {
            actions.Add(new ActionDescriptor
            {
                Id = "release_nav",
                Command = "ReleaseToNavigator",
                Label = "Für Kommandant freigeben",
                Style = "primary",
                Group = "info",
            });
        }
    }

    private static void AddMarkAction(List<ActionDescriptor> actions, Contact contact)
    {
        actions.Add(new ActionDescriptor
        {
            Id = "mark",
            Command = "MarkContact",
            Label = "Auf Hauptschirm markieren",
            Style = "primary",
            Group = "info",
        });
    }

    private static void AddDesignateAction(List<ActionDescriptor> actions, Contact contact)
    {
        if (contact.Discovery != DiscoveryState.Scanned) return;

        if (contact.IsDesignated)
        {
            actions.Add(new ActionDescriptor
            {
                Id = "designate",
                Command = "DesignateTarget",
                Label = "Designation aufheben",
                Style = "danger",
                Active = true,
                Group = "combat",
            });
        }
        else
        {
            actions.Add(new ActionDescriptor
            {
                Id = "designate",
                Command = "DesignateTarget",
                Label = "Ziel designieren (+25% DMG)",
                Style = "danger",
                Group = "combat",
            });
        }
    }

    private static void AddWeaknessAction(List<ActionDescriptor> actions, Contact contact)
    {
        if (contact.Discovery != DiscoveryState.Scanned) return;

        if (contact.HasWeakness)
        {
            actions.Add(new ActionDescriptor
            {
                Id = "weakness",
                Command = "AnalyzeWeakness",
                Label = "Schwachstelle identifiziert \u2713",
                Style = "success",
                Disabled = true,
                Active = true,
                Group = "combat",
            });
            return;
        }

        if (contact.IsAnalyzing)
        {
            actions.Add(new ActionDescriptor
            {
                Id = "weakness",
                Command = "AnalyzeWeakness",
                Label = $"Analysiere... {contact.WeaknessAnalysisProgress:0}%",
                Style = "warning",
                Disabled = true,
                Progress = contact.WeaknessAnalysisProgress,
                Group = "combat",
            });
        }
        else
        {
            actions.Add(new ActionDescriptor
            {
                Id = "weakness",
                Command = "AnalyzeWeakness",
                Label = "Schwachstelle analysieren (+50% DMG)",
                Style = "warning",
                Group = "combat",
            });
        }
    }

    private static void AddPoiAnalyzeAction(List<ActionDescriptor> actions, Contact contact)
    {
        bool isPoi = contact.Discovery == DiscoveryState.Scanned
                     && !string.IsNullOrEmpty(contact.PoiType);
        if (!isPoi) return;

        if (contact.PoiPhase == PoiPhase.None)
        {
            if (contact.PoiAnalyzing)
            {
                actions.Add(new ActionDescriptor
                {
                    Id = "poi_analyze",
                    Command = "AnalyzePoi",
                    Label = $"POI-Analyse... {contact.PoiProgress:0}%",
                    Style = "primary",
                    Disabled = true,
                    Progress = contact.PoiProgress,
                    Group = "poi",
                });
            }
            else
            {
                actions.Add(new ActionDescriptor
                {
                    Id = "poi_analyze",
                    Command = "AnalyzePoi",
                    Label = "POI analysieren",
                    Style = "primary",
                    Group = "poi",
                });
            }
        }
        else
        {
            var (label, style) = contact.PoiPhase switch
            {
                PoiPhase.Analyzed => ("Analysiert \u2713", "success"),
                PoiPhase.Opened => ("Geöffnet", "primary"),
                PoiPhase.Extracting => ("Extraktion...", "primary"),
                PoiPhase.Complete => ("Abgeschlossen \u2713", "success"),
                PoiPhase.Failed => ("Fehlgeschlagen \u2717", "danger"),
                _ => (contact.PoiPhase.ToString(), "primary"),
            };

            actions.Add(new ActionDescriptor
            {
                Id = "poi_analyze",
                Command = "",
                Label = label,
                Style = style,
                Disabled = true,
                Active = contact.PoiPhase is PoiPhase.Complete or PoiPhase.Analyzed,
                Group = "poi",
            });
        }
    }
}
