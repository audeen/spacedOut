using System;
using System.Linq;
using Godot;
using SpacedOut.Agents;
using SpacedOut.LevelGen;
using SpacedOut.State;
using TC = SpacedOut.Shared.ThemeColors;
using UI = SpacedOut.Shared.UiFactory;

namespace SpacedOut.MainScreen;

/// <summary>F12-toggled debug panel with categorized command buttons and status indicators.</summary>
public class HudDebugPanel
{
    private PanelContainer _panel = null!;
    private VBoxContainer _buttons = null!;
    private Label? _levelGenInfoLabel;
    private Label? _godmodeStatusLabel;
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
        _panel.Position = new Vector2(-310, 10);
        _panel.Size = new Vector2(300, 0);
        _panel.Visible = false;
        _parent.AddChild(_panel);

        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(280, 0) };
        scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _panel.AddChild(scroll);

        _buttons = new VBoxContainer();
        _buttons.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(_buttons);

        _buttons.AddChild(UI.CreateLabel("DEBUG  ·  F12", 16, TC.Cyan));

        // ── Godmode ─────────────────────────────────────────────
        AddSection("GODMODE", TC.Red);
        AddButtonRow(("GODMODE", "godmode_toggle", TC.Red));
        _godmodeStatusLabel = UI.CreateLabel("Godmode: AUS", 11, TC.DimWhite);
        _buttons.AddChild(_godmodeStatusLabel);
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

        // ── Zeit ────────────────────────────────────────────────
        AddSection("ZEIT", TC.Cyan);
        AddButtonRow(
            ("1×", "time_1"), ("2×", "time_2"),
            ("5×", "time_5"), ("10×", "time_10"));
        AddButtonRow(("Einfrieren", "time_freeze"));

        // ── Mission ─────────────────────────────────────────────
        AddSection("MISSION", TC.Green);
        AddButtonRow(
            ("Start", "mission_start"),
            ("Reset", "mission_reset"),
            ("Pause", "mission_pause"));
        _buttons.AddChild(UI.CreateLabel("Ergebnis:", 11, TC.DimWhite));
        AddButtonRow(
            ("OK", "mission_end_ok"),
            ("Teil", "mission_end_partial"),
            ("Fail", "mission_end_fail"),
            ("Zeit", "mission_end_timeout"));

        // ── Schiff & Systeme ────────────────────────────────────
        AddSection("SCHIFF", TC.Yellow);
        _buttons.AddChild(UI.CreateLabel("Hülle:", 11, TC.DimWhite));
        AddButtonRow(
            ("−20", "hull_minus20"),
            ("+20", "hull_plus20"),
            ("Max", "hull_max"),
            ("Krit", "hull_critical"));
        _buttons.AddChild(UI.CreateLabel("Systeme:", 11, TC.DimWhite));
        AddButtonRow(
            ("Reparieren", "repair_all"),
            ("Beschädigen", "break_random"));
        AddButtonRow(
            ("Alle kaputt", "break_all"));
        _buttons.AddChild(UI.CreateLabel("Hitze:", 11, TC.DimWhite));
        AddButtonRow(
            ("Reset", "reset_heat"),
            ("Max", "max_heat"));
        _buttons.AddChild(UI.CreateLabel("Energie:", 11, TC.DimWhite));
        AddButtonRow(
            ("Gleich", "energy_balanced"),
            ("Waffen", "energy_max_weapons"),
            ("Schilde", "energy_max_shields"));

        // ── Kontakte & Sensoren ─────────────────────────────────
        AddSection("KONTAKTE", TC.Orange);
        AddButtonRow(
            ("Alle aufdecken", "reveal_all"),
            ("Alle scannen", "scan_all"));
        AddButtonRow(
            ("Zonen sondieren", "reveal_all_resource_zones"));
        _buttons.AddChild(UI.CreateLabel("NPC-Agent:", 11, TC.DimWhite));
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
        _buttons.AddChild(agentSpawnRow);
        AddButtonRow(
            ("Feinde töten", "kill_all_hostiles"));
        AddButtonRow(
            ("Sonden max", "probes_max"),
            ("Akt.Sensor", "toggle_active_sensors"));

        // ── Gunner ──────────────────────────────────────────────
        AddSection("GUNNER", TC.Purple);
        AddButtonRow(
            ("Sofort-Lock", "gunner_instant_lock"),
            ("CD Reset", "gunner_reset_cooldown"));
        AddButtonRow(
            ("Ziel töten", "gunner_kill_target"));

        // ── Events ──────────────────────────────────────────────
        AddSection("EVENTS", TC.Blue);
        AddButtonRow(
            ("Sensor", "event_sensor"),
            ("Schild", "event_shield"));
        AddButtonRow(
            ("Kontakt", "event_contact"),
            ("Bergung", "event_recovery"));

        // ── Phasen ──────────────────────────────────────────────
        AddSection("PHASEN", TC.Blue);
        AddButtonRow(
            ("Anflug", "phase_anflug"),
            ("Störung", "phase_stoerung"));
        AddButtonRow(
            ("Krise", "phase_krise"),
            ("Abschluss", "phase_abschluss"));

        // ── Level / Biom ────────────────────────────────────────
        AddSection("LEVEL / BIOM", TC.Cyan);
        AddButtonRow(
            ("FlyCam", "toggle_fly_camera"),
            ("Neues Level", "regen_level"));
        AddButtonRow(
            ("Asteroid", "biome_asteroid"),
            ("Wrack", "biome_wreck"),
            ("Station", "biome_station"));

        // ── Run ─────────────────────────────────────────────────
        AddSection("RUN", TC.Green);
        AddButtonRow(
            ("Neuer Run", "run_new"),
            ("Seed 42", "run_seed42"));
        AddButtonRow(
            ("+5 Ressourcen", "run_add_resources"),
            ("Knoten zeigen", "run_reveal_nodes"));

        // ── Info ────────────────────────────────────────────────
        AddSeparator();
        _levelGenInfoLabel = UI.CreateLabel("Level: —", 11, TC.DimWhite);
        _levelGenInfoLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _levelGenInfoLabel.CustomMinimumSize = new Vector2(240, 0);
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
        _levelGenInfoLabel.Text =
            $"Seed: {gen.CurrentSeed}\n" +
            $"Biom: {biome.DisplayName}\n" +
            $"Objekte: {gen.SpawnedObjects.Count}\n" +
            $"Valide: {(gen.IsValid ? "Ja" : "Nein")}";
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

    private void AddSection(string title, Color color)
    {
        AddSeparator();
        _buttons.AddChild(UI.CreateLabel(title, 13, color));
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
        _buttons.AddChild(row);
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
        _buttons.AddChild(row);
    }

    private void AddSeparator()
    {
        _buttons.AddChild(new HSeparator());
    }
}
