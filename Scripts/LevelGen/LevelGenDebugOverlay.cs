using System.Linq;
using Godot;
using SpacedOut.Shared;
using TC = SpacedOut.Shared.ThemeColors;
using UI = SpacedOut.Shared.UiFactory;

namespace SpacedOut.LevelGen;

public partial class LevelGenDebugOverlay : Control
{
    private LevelGenerator? _gen;

    private Label _seedLabel = null!;
    private LineEdit _seedInput = null!;
    private Label _biomeLabel = null!;
    private Label _statsLabel = null!;
    private Label _validationLabel = null!;
    private Label _speedLabel = null!;

    public void Initialize(LevelGenerator generator)
    {
        _gen = generator;
        BuildUI();
    }

    // ── UI construction ─────────────────────────────────────────────

    private void BuildUI()
    {
        MouseFilter = MouseFilterEnum.Ignore;

        var panel = UI.CreateDebugPanel(TC.PanelBg, TC.Cyan);
        panel.Position = new Vector2(10, 10);
        panel.CustomMinimumSize = new Vector2(290, 0);
        AddChild(panel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 5);
        panel.AddChild(vbox);

        vbox.AddChild(UI.CreateLabel("LEVEL GENERATOR", 18, TC.Cyan));
        vbox.AddChild(new HSeparator());

        _seedLabel = UI.CreateLabel("Seed: ---", 14, TC.White);
        vbox.AddChild(_seedLabel);

        var seedRow = new HBoxContainer();
        seedRow.AddThemeConstantOverride("separation", 4);
        _seedInput = new LineEdit
        {
            PlaceholderText = "Seed eingeben...",
            CustomMinimumSize = new Vector2(150, 28),
        };
        seedRow.AddChild(_seedInput);

        var goBtn = Btn("Los", 60);
        goBtn.Pressed += OnSeedGo;
        seedRow.AddChild(goBtn);
        vbox.AddChild(seedRow);

        var newSeedBtn = Btn("Neuer Seed  (Ctrl+R)", 270);
        newSeedBtn.Pressed += () => _gen?.RegenerateWithNewSeed();
        vbox.AddChild(newSeedBtn);

        vbox.AddChild(new HSeparator());

        _biomeLabel = UI.CreateLabel("Biom: ---", 14, TC.White);
        vbox.AddChild(_biomeLabel);

        var biomeRow = new HBoxContainer();
        biomeRow.AddThemeConstantOverride("separation", 4);
        biomeRow.AddChild(BiomeBtn("Asteroid (1)", "asteroid_field"));
        biomeRow.AddChild(BiomeBtn("Wrack (2)", "wreck_zone"));
        biomeRow.AddChild(BiomeBtn("Station (3)", "station_periphery"));
        vbox.AddChild(biomeRow);

        vbox.AddChild(new HSeparator());

        vbox.AddChild(UI.CreateLabel("STATISTIK", 14, TC.Cyan));
        _statsLabel = UI.CreateLabel("...", 12, TC.DimWhite);
        vbox.AddChild(_statsLabel);

        vbox.AddChild(new HSeparator());

        vbox.AddChild(UI.CreateLabel("VALIDIERUNG", 14, TC.Cyan));
        _validationLabel = UI.CreateLabel("...", 12, TC.DimWhite);
        vbox.AddChild(_validationLabel);

        vbox.AddChild(new HSeparator());

        vbox.AddChild(UI.CreateLabel("STEUERUNG", 14, TC.Cyan));
        vbox.AddChild(UI.CreateLabel(
            "WASD / QE  –  Bewegen\n" +
            "Maus  –  Umsehen\n" +
            "Shift  –  Schnell\n" +
            "Scrollrad  –  Geschwindigkeit\n" +
            "Tab  –  Maus freigeben\n" +
            "Ctrl+R  –  Neuer Seed\n" +
            "R  –  Regenerieren\n" +
            "1 / 2 / 3  –  Biom wählen\n" +
            "F1  –  Debug ein/aus", 11, TC.DimWhite));
    }

    // ── State updates ───────────────────────────────────────────────

    public void UpdateStats()
    {
        if (_gen == null) return;

        _seedLabel.Text = $"Seed: {_gen.CurrentSeed}";
        _seedInput.Text = _gen.CurrentSeed.ToString();

        var biome = BiomeDefinition.Get(_gen.CurrentBiomeId);
        _biomeLabel.Text = $"Biom: {biome.DisplayName}";

        int lm = _gen.GetLandmarkCount();
        int mid = _gen.GetMidScaleCount();
        int sc = _gen.GetScatterCount();
        int mk = _gen.GetMarkerCount();

        _statsLabel.Text =
            $"Landmarken:  {lm}\n" +
            $"Mid-Scale:   {mid}\n" +
            $"Scatter:     {sc}\n" +
            $"Marker:      {mk}\n" +
            $"Gesamt:      {_gen.SpawnedObjects.Count}";
        _statsLabel.AddThemeColorOverride("font_color", TC.DimWhite);

        if (_gen.IsValid)
        {
            _validationLabel.Text = "✓ Validierung OK";
            _validationLabel.AddThemeColorOverride("font_color", TC.Green);
        }
        else
        {
            _validationLabel.Text = string.Join("\n",
                _gen.ValidationMessages.Select(m => $"✗ {m}"));
            _validationLabel.AddThemeColorOverride("font_color", TC.Red);
        }
    }

    // ── Callbacks ───────────────────────────────────────────────────

    private void OnSeedGo()
    {
        if (_gen == null) return;
        if (int.TryParse(_seedInput.Text, out int seed))
            _gen.GenerateLevel(seed, _gen.CurrentBiomeId);
    }

    // ── Tiny UI helpers ─────────────────────────────────────────────

    private static Button Btn(string text, float minW)
    {
        return new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(minW, 28),
        };
    }

    private Button BiomeBtn(string text, string biomeId)
    {
        var b = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(85, 26),
        };
        b.Pressed += () =>
        {
            if (_gen != null)
                _gen.GenerateLevel(_gen.CurrentSeed, biomeId);
        };
        return b;
    }
}
