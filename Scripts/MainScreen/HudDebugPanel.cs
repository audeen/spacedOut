using System;
using System.Linq;
using Godot;
using SpacedOut.Agents;
using SpacedOut.LevelGen;
using SpacedOut.Mission;
using SpacedOut.Run;
using SpacedOut.State;
using TC = SpacedOut.Shared.ThemeColors;
using UI = SpacedOut.Shared.UiFactory;

namespace SpacedOut.MainScreen;

/// <summary>F12-toggled debug panel with categorized command buttons and status indicators.</summary>
public class HudDebugPanel
{
    private const int DebugPanelWidth = 460;

    private PanelContainer _panel = null!;
    private VBoxContainer _buttons = null!;
    private VBoxContainer _activeContainer = null!;
    private Label? _levelGenInfoLabel;
    private Label? _godmodeStatusLabel;
    private Label? _skyboxStatusLabel;
    private Label? _sectorContactsLabel;
    private Label? _directorPacingLabel;
    private Label? _directorIntentsLabel;
    private bool _visible;

    private readonly Control _parent;
    private readonly Action<string> _onCommand;
    private readonly DebugFlags _flags;

    public bool IsVisible => _visible;

    public HudDebugPanel(Control parent, Action<string> onCommand, DebugFlags flags)
    {
        _parent = parent;
        _onCommand = onCommand;
        _flags = flags;
    }

    public void Build()
    {
        _panel = UI.CreateDebugPanel(
            new Color(0.06f, 0.06f, 0.12f, 0.94f), TC.Cyan);
        _panel.AnchorTop = 0; _panel.AnchorBottom = 1;
        _panel.AnchorLeft = 1; _panel.AnchorRight = 1;
        _panel.Position = new Vector2(-(DebugPanelWidth + 10), 10);
        _panel.Size = new Vector2(DebugPanelWidth, 0);
        _panel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        _panel.Visible = false;
        _parent.AddChild(_panel);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(DebugPanelWidth - 20, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _panel.AddChild(scroll);

        _buttons = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _buttons.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(_buttons);
        _activeContainer = _buttons;

        _buttons.AddChild(UI.CreateLabel("DEBUG  ·  F12", 16, TC.Cyan));

        // ── Godmode ─────────────────────────────────────────────
        var godBody = AddCollapsibleSection("GODMODE", TC.Red, expanded: false);
        _activeContainer = godBody;
        AddButtonRow(("GODMODE", "godmode_toggle", TC.Red));
        _godmodeStatusLabel = UI.CreateLabel("Godmode: AUS", 11, TC.DimWhite);
        _activeContainer.AddChild(_godmodeStatusLabel);
        AddButtonRow(
            ("Unverw.", "god_invulnerable"),
            ("NoHeat", "god_noheat"),
            ("Scan++", "god_instantscan"));
        AddButtonRow(
            ("Lock++", "god_instantlock"),
            ("Sonden", "god_infprobes"),
            ("NoCD", "god_nocooldown"));
        AddButtonRow(
            ("Aufdecken", "god_reveal"));
        _activeContainer = _buttons;

        // ── Zeit ────────────────────────────────────────────────
        var timeBody = AddCollapsibleSection("ZEIT", TC.Cyan, expanded: false);
        _activeContainer = timeBody;
        AddButtonRow(
            ("1×", "time_1"), ("2×", "time_2"),
            ("5×", "time_5"), ("10×", "time_10"));
        AddButtonRow(("Einfrieren", "time_freeze"));
        _activeContainer = _buttons;

        // ── Mission ─────────────────────────────────────────────
        var missionBody = AddCollapsibleSection("MISSION", TC.Green, expanded: false);
        _activeContainer = missionBody;
        AddButtonRow(
            ("Start", "mission_start"),
            ("Reset", "mission_reset"),
            ("Pause", "mission_pause"));
        _activeContainer.AddChild(UI.CreateLabel("Ergebnis:", 11, TC.DimWhite));
        AddButtonRow(
            ("OK", "mission_end_ok"),
            ("Teil", "mission_end_partial"),
            ("Fail", "mission_end_fail"),
            ("Zeit", "mission_end_timeout"));
        _activeContainer = _buttons;

        // ── Schiff & Systeme ────────────────────────────────────
        var shipBody = AddCollapsibleSection("SCHIFF", TC.Yellow, expanded: false);
        _activeContainer = shipBody;
        shipBody.AddChild(UI.CreateLabel("Hülle:", 11, TC.DimWhite));
        AddButtonRow(
            ("−20", "hull_minus20"),
            ("+20", "hull_plus20"),
            ("Max", "hull_max"),
            ("Krit", "hull_critical"));
        shipBody.AddChild(UI.CreateLabel("Systeme:", 11, TC.DimWhite));
        AddButtonRow(
            ("Reparieren", "repair_all"),
            ("Beschädigen", "break_random"));
        AddButtonRow(
            ("Alle kaputt", "break_all"));
        shipBody.AddChild(UI.CreateLabel("Hitze:", 11, TC.DimWhite));
        AddButtonRow(
            ("Reset", "reset_heat"),
            ("Max", "max_heat"));
        shipBody.AddChild(UI.CreateLabel("Energie:", 11, TC.DimWhite));
        AddButtonRow(
            ("Gleich", "energy_balanced"),
            ("Waffen", "energy_max_weapons"),
            ("Schilde", "energy_max_shields"));
        _activeContainer = _buttons;

        // ── Kontakte & Sensoren ─────────────────────────────────
        var contactBody = AddCollapsibleSection("KONTAKTE", TC.Orange, expanded: false);
        _activeContainer = contactBody;
        AddButtonRow(
            ("Alle aufdecken", "reveal_all"),
            ("Alle scannen", "scan_all"));
        AddButtonRow(
            ("Zonen sondieren", "reveal_all_resource_zones"));
        _activeContainer.AddChild(UI.CreateLabel("NPC-Agent:", 11, TC.DimWhite));
        var agentSpawnRow = new HBoxContainer();
        agentSpawnRow.AddThemeConstantOverride("separation", 4);
        var agentIds = AgentDefinition.GetAll().OrderBy(kvp => kvp.Value.DisplayName).Select(kvp => kvp.Key).ToList();
        var agentDropdown = new OptionButton
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 28),
        };
        for (int ai = 0; ai < agentIds.Count; ai++)
        {
            var def = AgentDefinition.Get(agentIds[ai]);
            agentDropdown.AddItem($"{def.DisplayName}  ({agentIds[ai]})", ai);
        }
        agentSpawnRow.AddChild(agentDropdown);
        var spawnAgentBtn = new Button
        {
            Text = "Spawn",
            CustomMinimumSize = new Vector2(72, 28),
        };
        spawnAgentBtn.Pressed += () =>
        {
            int sel = agentDropdown.Selected;
            if (sel < 0 || sel >= agentIds.Count) return;
            _onCommand($"spawn_agent:{agentIds[sel]}");
        };
        agentSpawnRow.AddChild(spawnAgentBtn);
        _activeContainer.AddChild(agentSpawnRow);
        _activeContainer.AddChild(UI.CreateLabel("Kontakte im Sektor:", 11, TC.DimWhite));
        _sectorContactsLabel = UI.CreateLabel("(keine)", 10, TC.DimWhite);
        _sectorContactsLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _sectorContactsLabel.CustomMinimumSize = new Vector2(DebugPanelWidth - 60, 0);
        _activeContainer.AddChild(_sectorContactsLabel);
        AddButtonRow(
            ("Feinde töten", "kill_all_hostiles"));
        AddButtonRow(
            ("Sonden max", "probes_max"),
            ("Akt.Sensor", "toggle_active_sensors"));
        _activeContainer = _buttons;

        // ── Gunner ──────────────────────────────────────────────
        var gunBody = AddCollapsibleSection("GUNNER", TC.Purple, expanded: false);
        _activeContainer = gunBody;
        AddButtonRow(
            ("Sofort-Lock", "gunner_instant_lock"),
            ("CD Reset", "gunner_reset_cooldown"));
        AddButtonRow(
            ("Ziel töten", "gunner_kill_target"),
            ("FX Test-Schuss", "fx_test_shot"));
        _activeContainer = _buttons;

        // ── Events ──────────────────────────────────────────────
        var eventsBody = AddCollapsibleSection("EVENTS", TC.Blue, expanded: false);
        _activeContainer = eventsBody;
        AddButtonRow(
            ("Sensor", "event_sensor"),
            ("Schild", "event_shield"));
        AddButtonRow(
            ("Kontakt", "event_contact"),
            ("Bergung", "event_recovery"));
        eventsBody.AddChild(UI.CreateLabel("Katalog (Run-Map / Mission):", 11, TC.DimWhite));
        var preEvents = NodeEventCatalog.All
            .Where(e => e.Trigger == NodeEventTrigger.PreSector)
            .OrderBy(e => e.Id)
            .ToList();
        var inEvents = NodeEventCatalog.All
            .Where(e => e.Trigger == NodeEventTrigger.InSector)
            .OrderBy(e => e.Id)
            .ToList();

        eventsBody.AddChild(UI.CreateLabel("Pre-Sektor:", 11, TC.DimWhite));
        var preRow = new HBoxContainer();
        preRow.AddThemeConstantOverride("separation", 4);
        var preDropdown = new OptionButton
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 28),
        };
        for (int i = 0; i < preEvents.Count; i++)
            preDropdown.AddItem($"{preEvents[i].Title}  ({preEvents[i].Id})", i);
        preRow.AddChild(preDropdown);
        var preBtn = new Button
        {
            Text = "Auslösen",
            CustomMinimumSize = new Vector2(88, 28),
            Disabled = preEvents.Count == 0,
            TooltipText = "Staged wie vor Sektorstart (CaptainNav-Entscheidung auf der Run-Map).",
        };
        preBtn.Pressed += () =>
        {
            int sel = preDropdown.Selected;
            if (sel < 0 || sel >= preEvents.Count) return;
            _onCommand($"event_trigger:{preEvents[sel].Id}");
        };
        preRow.AddChild(preBtn);
        _activeContainer.AddChild(preRow);

        eventsBody.AddChild(UI.CreateLabel("In-Sektor:", 11, TC.DimWhite));
        var inRow = new HBoxContainer();
        inRow.AddThemeConstantOverride("separation", 4);
        var inDropdown = new OptionButton
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 28),
        };
        for (int j = 0; j < inEvents.Count; j++)
            inDropdown.AddItem($"{inEvents[j].Title}  ({inEvents[j].Id})", j);
        inRow.AddChild(inDropdown);
        var inNowBtn = new Button
        {
            Text = "Sofort",
            CustomMinimumSize = new Vector2(72, 28),
            Disabled = inEvents.Count == 0,
            TooltipText = "Sofort im laufenden Sektor (ignoriert Einmal-Guard).",
        };
        inNowBtn.Pressed += () =>
        {
            int sel = inDropdown.Selected;
            if (sel < 0 || sel >= inEvents.Count) return;
            _onCommand($"event_fire_now:{inEvents[sel].Id}");
        };
        inRow.AddChild(inNowBtn);
        var inQueueBtn = new Button
        {
            Text = "+0.1s",
            CustomMinimumSize = new Vector2(56, 28),
            Disabled = inEvents.Count == 0,
            TooltipText = "Als Zeit-Trigger einreihen (markiert Event als gefeuert für diesen Run).",
        };
        inQueueBtn.Pressed += () =>
        {
            int sel = inDropdown.Selected;
            if (sel < 0 || sel >= inEvents.Count) return;
            _onCommand($"event_trigger:{inEvents[sel].Id}");
        };
        inRow.AddChild(inQueueBtn);
        _activeContainer.AddChild(inRow);
        AddButtonRow(("Katalog → Konsole", "event_list"));
        _activeContainer = _buttons;

        // ── Phasen ──────────────────────────────────────────────
        var phaseBody = AddCollapsibleSection("PHASEN", TC.Blue, expanded: false);
        _activeContainer = phaseBody;
        AddButtonRow(
            ("Anflug", "phase_anflug"),
            ("Störung", "phase_stoerung"));
        AddButtonRow(
            ("Krise", "phase_krise"),
            ("Abschluss", "phase_abschluss"));
        _activeContainer = _buttons;

        // ── Level / Biom ────────────────────────────────────────
        var levelBody = AddCollapsibleSection("LEVEL / BIOM", TC.Cyan, expanded: false);
        _activeContainer = levelBody;
        AddButtonRow(
            ("FlyCam", "toggle_fly_camera"),
            ("Neues Level", "regen_level"));
        AddButtonRow(
            ("Schiff-Vergleich", "ship_scale_compare"));
        AddButtonRow(
            ("Asteroid", "biome_asteroid"),
            ("Wrack", "biome_wreck"),
            ("Station", "biome_station"));
        AddButtonRow(
            ("Skybox", "skybox_toggle"));
        _skyboxStatusLabel = UI.CreateLabel("Skybox: AUS", 11, TC.DimWhite);
        _activeContainer.AddChild(_skyboxStatusLabel);
        _activeContainer = _buttons;

        // ── POI (3D-Assets) ─────────────────────────────────────
        var poiBody = AddCollapsibleSection("POI / ASSETS", TC.Orange, expanded: false);
        _activeContainer = poiBody;
        _activeContainer.AddChild(UI.CreateLabel("POI-Marker:", 11, TC.DimWhite));
        var poiRow = new HBoxContainer();
        poiRow.AddThemeConstantOverride("separation", 4);
        var poiAssets = AssetLibrary.GetAll()
            .Where(a => a.Category == AssetCategory.PoiMarker)
            .OrderBy(a => a.DisplayName)
            .ToList();
        var poiDropdown = new OptionButton
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 28),
        };
        for (int pi = 0; pi < poiAssets.Count; pi++)
            poiDropdown.AddItem($"{poiAssets[pi].DisplayName}  ({poiAssets[pi].Id})", pi);
        poiRow.AddChild(poiDropdown);
        var spawnPoiBtn = new Button
        {
            Text = "Spawn",
            CustomMinimumSize = new Vector2(72, 28),
        };
        spawnPoiBtn.Pressed += () =>
        {
            int sel = poiDropdown.Selected;
            if (sel < 0 || sel >= poiAssets.Count) return;
            _onCommand($"spawn_poi:{poiAssets[sel].Id}");
        };
        poiRow.AddChild(spawnPoiBtn);
        _activeContainer.AddChild(poiRow);
        _activeContainer = _buttons;

        // ── Run ─────────────────────────────────────────────────
        var runBody = AddCollapsibleSection("RUN", TC.Green, expanded: false);
        _activeContainer = runBody;
        AddButtonRow(
            ("Neuer Run", "run_new"),
            ("Seed 42", "run_seed42"));
        AddButtonRow(
            ("+5 Ressourcen", "run_add_resources"),
            ("Knoten zeigen", "run_reveal_nodes"));
        _activeContainer = _buttons;

        // ── Director (Pacing / Beats) ───────────────────────────
        var directorBody = AddCollapsibleSection("DIRECTOR", TC.Cyan, expanded: false);
        _activeContainer = directorBody;
        _directorPacingLabel = UI.CreateLabel("Run inaktiv.", 11, TC.DimWhite);
        _directorPacingLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _directorPacingLabel.CustomMinimumSize = new Vector2(DebugPanelWidth - 60, 0);
        _activeContainer.AddChild(_directorPacingLabel);
        _activeContainer.AddChild(UI.CreateLabel("Geplante Beats (NodeIntent):", 11, TC.DimWhite));
        _directorIntentsLabel = UI.CreateLabel("—", 10, TC.DimWhite);
        _directorIntentsLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _directorIntentsLabel.CustomMinimumSize = new Vector2(DebugPanelWidth - 60, 0);
        _activeContainer.AddChild(_directorIntentsLabel);
        _activeContainer = _buttons;

        // ── Info ────────────────────────────────────────────────
        AddSeparator();
        _levelGenInfoLabel = UI.CreateLabel("Level: —", 11, TC.DimWhite);
        _levelGenInfoLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _levelGenInfoLabel.CustomMinimumSize = new Vector2(DebugPanelWidth - 60, 0);
        _buttons.AddChild(_levelGenInfoLabel);
    }

    public void Toggle()
    {
        _visible = !_visible;
        _panel.Visible = _visible;
    }

    public void UpdateLevelGenInfo(LevelGenerator? gen)
    {
        if (_levelGenInfoLabel == null || gen == null) return;
        var biome = BiomeDefinition.Get(gen.CurrentBiomeId);
        string radiusLine = gen.CurrentSectorData != null
            ? $"Radius: {gen.CurrentSectorData.LevelRadius:F0}"
            : "Radius: —";
        _levelGenInfoLabel.Text =
            $"Seed: {gen.CurrentSeed}\n" +
            $"Biom: {biome.DisplayName}\n" +
            $"{radiusLine}\n" +
            $"Objekte: {gen.SpawnedObjects.Count}\n" +
            $"Valide: {(gen.IsValid ? "Ja" : "Nein")}";
    }

    public void UpdateSkyboxStatus()
    {
        if (_skyboxStatusLabel == null) return;

        if (GameFeatures.SkyboxEnabled)
        {
            _skyboxStatusLabel.Text = "Skybox: AN";
            _skyboxStatusLabel.AddThemeColorOverride("font_color", TC.Cyan);
        }
        else
        {
            _skyboxStatusLabel.Text = "Skybox: AUS";
            _skyboxStatusLabel.AddThemeColorOverride("font_color", TC.DimWhite);
        }
    }

    /// <summary>Lists all non-destroyed sector contacts for debugging; refreshed from HUD each frame.</summary>
    public void UpdateSectorContactsDebugList(GameState? state)
    {
        if (_sectorContactsLabel == null) return;
        if (state == null)
        {
            _sectorContactsLabel.Text = "—";
            return;
        }

        var list = state.Contacts.Where(c => !c.IsDestroyed).OrderBy(c => c.Id).ToList();
        if (list.Count == 0)
        {
            _sectorContactsLabel.Text = "(keine Kontakte)";
            return;
        }

        static string Line(Contact c)
        {
            string name = string.IsNullOrEmpty(c.DisplayName) ? c.Id : c.DisplayName;
            string asset = string.IsNullOrEmpty(c.AssetId) ? "—" : c.AssetId;
            var sb = new System.Text.StringBuilder();
            sb.Append("• ").Append(name).Append("  (").Append(c.Type).Append(")  id=").Append(c.Id);
            sb.Append("\n   disc=").Append(c.Discovery).Append("  ·  asset=").Append(asset);
            if (c.Agent != null)
            {
                var a = c.Agent;
                string typeLabel = AgentDefinition.TryGet(a.AgentType, out var def)
                    ? $"{def.DisplayName}  ({a.AgentType})"
                    : a.AgentType;
                sb.Append("\n   NPC: ").Append(typeLabel).Append("  ·  ").Append(a.Mode);
            }

            return sb.ToString();
        }

        _sectorContactsLabel.Text = string.Join("\n\n", list.Select(Line));
    }

    /// <summary>
    /// Refreshes the DIRECTOR section: pacing counters (hostile streak, breather/station, tension),
    /// scan horizon, count of out-of-scan nodes, and the per-node <see cref="NodeIntent"/> map.
    /// Pass <c>null</c> outside an active run.
    /// </summary>
    public void UpdateDirectorInfo(RunController? run)
    {
        if (_directorPacingLabel == null || _directorIntentsLabel == null) return;
        if (run == null || string.IsNullOrEmpty(run.CurrentRun.RunId))
        {
            _directorPacingLabel.Text = "Run inaktiv.";
            _directorIntentsLabel.Text = "—";
            return;
        }

        var pacing = run.CurrentRun.Pacing;
        int curDepth = run.CurrentDepth();
        int horizon = curDepth + RunController.MaxScanDepthAhead;
        int outOfScan = run.CurrentDefinition.Nodes.Values.Count(n => !run.IsWithinScanRange(n.Id));

        string lastDrain = string.IsNullOrEmpty(pacing.LastDrainReason)
            ? "(noch nichts)"
            : $"{pacing.LastDrainReason} (-{pacing.LastDrainAmount:0.#})";
        string lastRefill = string.IsNullOrEmpty(pacing.LastRefillReason)
            ? "(noch nichts)"
            : $"+{pacing.LastRefillAmount:0.#} ({pacing.LastRefillReason})";

        string lastWave = pacing.LastWaveAtElapsed < 0f
            ? "(noch keine)"
            : $"{(string.IsNullOrEmpty(pacing.LastWaveReason) ? "?" : pacing.LastWaveReason)} @ {pacing.LastWaveAtElapsed:0}s";

        _directorPacingLabel.Text =
            $"Tension: {pacing.TensionLevel:0.00}\n" +
            $"Hostile-Streak: {pacing.RecentHostileStreak}\n" +
            $"Seit Breather: {pacing.NodesSinceBreather}\n" +
            $"Seit Station: {pacing.NodesSinceStation}\n" +
            $"Hülle: {run.CurrentRun.CurrentHull:0}\n" +
            $"ThreatPool: {pacing.ThreatPool:0.0} / {pacing.ThreatCapacity:0.0}\n" +
            $"Last Drain: {lastDrain}\n" +
            $"Last Refill: {lastRefill}\n" +
            $"Wellen (Sektor): {pacing.InSectorWaveCount} / {SpacedOut.Run.EscalatingDirector.MaxWavesPerSector}\n" +
            $"Letzte Welle: {lastWave}\n" +
            $"Nächster Heartbeat: @ {pacing.NextHeartbeatAtElapsed:0}s\n" +
            $"Scan-Horizont: Tiefe {curDepth}..{horizon}  (cap +{RunController.MaxScanDepthAhead})\n" +
            $"Out-of-scan Knoten: {outOfScan}";

        if (pacing.NodeIntent.Count == 0)
        {
            _directorIntentsLabel.Text = "(keine Director-Eingriffe bisher)";
            return;
        }

        var lines = pacing.NodeIntent
            .Select(kv =>
            {
                run.CurrentDefinition.Nodes.TryGetValue(kv.Key, out var nd);
                int depth = nd?.Depth ?? -1;
                string scope = run.IsWithinScanRange(kv.Key) ? "[scan]" : "[hidden]";
                return (depth, line: $"• d{depth} {kv.Key} → {kv.Value} {scope}");
            })
            .OrderBy(t => t.depth)
            .Select(t => t.line);

        _directorIntentsLabel.Text = string.Join("\n", lines);
    }

    public void UpdateGodmodeStatus()
    {
        if (_godmodeStatusLabel == null) return;

        if (_flags.GodMode)
        {
            _godmodeStatusLabel.Text = "Godmode: AN (alle Flags)";
            _godmodeStatusLabel.AddThemeColorOverride("font_color", TC.Red);
        }
        else
        {
            var active = new System.Collections.Generic.List<string>();
            if (_flags.Invulnerable) active.Add("Unverw");
            if (_flags.NoHeat) active.Add("NoHeat");
            if (_flags.InstantScans) active.Add("Scan");
            if (_flags.InstantLock) active.Add("Lock");
            if (_flags.InfiniteProbes) active.Add("Sonden");
            if (_flags.NoCooldowns) active.Add("NoCD");
            if (_flags.RevealContacts) active.Add("Reveal");

            if (active.Count == 0)
            {
                _godmodeStatusLabel.Text = "Godmode: AUS";
                _godmodeStatusLabel.AddThemeColorOverride("font_color", TC.DimWhite);
            }
            else
            {
                _godmodeStatusLabel.Text = $"Aktiv: {string.Join(", ", active)}";
                _godmodeStatusLabel.AddThemeColorOverride("font_color", TC.Yellow);
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Creates a collapsible section with a header button that toggles the
    /// body's visibility. Returns the body <see cref="VBoxContainer"/> so the
    /// caller can add buttons/labels into it. The header stays visible at all
    /// times; the body is shown/hidden based on <paramref name="expanded"/>.
    /// </summary>
    private VBoxContainer AddCollapsibleSection(string title, Color color, bool expanded)
    {
        AddSeparator();

        var header = new Button
        {
            Flat = true,
            Alignment = HorizontalAlignment.Left,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 22),
        };
        header.AddThemeColorOverride("font_color", color);
        header.AddThemeFontSizeOverride("font_size", 13);
        _activeContainer.AddChild(header);

        var body = new VBoxContainer { Visible = expanded };
        body.AddThemeConstantOverride("separation", 3);
        _activeContainer.AddChild(body);

        void Refresh()
        {
            header.Text = (body.Visible ? "▼ " : "▶ ") + title;
        }

        header.Pressed += () =>
        {
            body.Visible = !body.Visible;
            Refresh();
        };

        Refresh();
        return body;
    }

    private void AddButtonRow(params (string label, string command)[] items)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        foreach (var (label, cmd) in items)
        {
            var btn = new Button
            {
                Text = label,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 28),
            };
            btn.Pressed += () => _onCommand(cmd);
            row.AddChild(btn);
        }
        _activeContainer.AddChild(row);
    }

    private void AddButtonRow(params (string label, string command, Color color)[] items)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        foreach (var (label, cmd, color) in items)
        {
            var btn = new Button
            {
                Text = label,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 32),
            };
            var style = new StyleBoxFlat
            {
                BgColor = new Color(color.R * 0.3f, color.G * 0.3f, color.B * 0.3f, 0.8f),
                BorderColor = color,
                BorderWidthBottom = 1, BorderWidthTop = 1,
                BorderWidthLeft = 1, BorderWidthRight = 1,
                CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
                CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
                ContentMarginLeft = 4, ContentMarginRight = 4,
                ContentMarginTop = 2, ContentMarginBottom = 2,
            };
            btn.AddThemeStyleboxOverride("normal", style);
            btn.Pressed += () => _onCommand(cmd);
            row.AddChild(btn);
        }
        _activeContainer.AddChild(row);
    }

    private void AddSeparator()
    {
        _activeContainer.AddChild(new HSeparator());
    }
}
