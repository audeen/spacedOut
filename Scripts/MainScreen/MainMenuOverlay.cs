using System;
using System.Collections.Generic;
using Godot;
using SpacedOut.Meta;
using TC = SpacedOut.Shared.ThemeColors;
using UI = SpacedOut.Shared.UiFactory;

namespace SpacedOut.MainScreen;

/// <summary>
/// M7: Main menu shown before any run. Hosts the Sternenstaub header, a loadout selector
/// (only purchased perks are eligible), the "Profil &amp; Upgrades" entry, and the
/// "Neuer Run"/"Beenden" buttons.
/// </summary>
public class MainMenuOverlay
{
    private readonly Control _parent;
    private PanelContainer _panel = null!;
    private Label _stardustLabel = null!;
    private OptionButton _perkSelector = null!;
    private Label _perkDescription = null!;

    private readonly Action<string?> _onNewRun;
    private readonly Action _onQuit;
    private readonly Action _onProfile;
    private readonly Func<MetaProfile?> _profileGetter;

    private readonly List<string?> _perkIds = new();

    public MainMenuOverlay(Control parent, Action<string?> onNewRun, Action onQuit,
        Action onProfile, Func<MetaProfile?> profileGetter)
    {
        _parent = parent;
        _onNewRun = onNewRun;
        _onQuit = onQuit;
        _onProfile = onProfile;
        _profileGetter = profileGetter;
    }

    public bool IsVisible => _panel?.Visible == true;

    public void Build()
    {
        _panel = UI.CreatePanel(new Color(0.02f, 0.03f, 0.06f, 0.97f));
        _panel.Name = "MainMenuOverlay";
        _panel.AnchorLeft = 0f; _panel.AnchorTop = 0f;
        _panel.AnchorRight = 1f; _panel.AnchorBottom = 1f;
        _panel.OffsetLeft = 0; _panel.OffsetTop = 0;
        _panel.OffsetRight = 0; _panel.OffsetBottom = 0;
        _panel.MouseFilter = Control.MouseFilterEnum.Stop;
        _panel.Visible = true;
        _parent.AddChild(_panel);

        var center = new CenterContainer
        {
            AnchorLeft = 0f, AnchorTop = 0f,
            AnchorRight = 1f, AnchorBottom = 1f,
        };
        _panel.AddChild(center);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 14);
        center.AddChild(box);

        var title = UI.CreateLabel("SPACED OUT", 48, TC.Cyan);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(title);

        var subtitle = UI.CreateLabel("Br\u00fcckenspiel \u2014 Roguelike-Prototyp", 16, TC.DimWhite);
        subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(subtitle);

        _stardustLabel = UI.CreateLabel("\u2728 Sternenstaub: 0", 18, TC.Yellow);
        _stardustLabel.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(_stardustLabel);

        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 14) });

        var loadoutLabel = UI.CreateLabel("Loadout", 14, TC.DimWhite);
        loadoutLabel.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(loadoutLabel);

        _perkSelector = new OptionButton { CustomMinimumSize = new Vector2(280, 36) };
        _perkSelector.AddThemeFontSizeOverride("font_size", 14);
        _perkSelector.ItemSelected += OnPerkSelected;
        box.AddChild(_perkSelector);

        _perkDescription = UI.CreateLabel("", 12, TC.DimWhite);
        _perkDescription.HorizontalAlignment = HorizontalAlignment.Center;
        _perkDescription.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _perkDescription.CustomMinimumSize = new Vector2(360, 0);
        box.AddChild(_perkDescription);

        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 18) });

        var newRunBtn = new Button { Text = "Neuer Run", CustomMinimumSize = new Vector2(280, 56) };
        newRunBtn.AddThemeFontSizeOverride("font_size", 22);
        newRunBtn.Pressed += () => _onNewRun(SelectedPerkId());
        box.AddChild(newRunBtn);

        var profileBtn = new Button { Text = "Profil & Upgrades", CustomMinimumSize = new Vector2(280, 40) };
        profileBtn.AddThemeFontSizeOverride("font_size", 16);
        profileBtn.Pressed += () => _onProfile();
        box.AddChild(profileBtn);

        var quitBtn = new Button { Text = "Beenden", CustomMinimumSize = new Vector2(280, 36) };
        quitBtn.AddThemeFontSizeOverride("font_size", 16);
        quitBtn.Pressed += () => _onQuit();
        box.AddChild(quitBtn);

        Refresh();
    }

    /// <summary>Rebuilds the perk options + stardust counter from the current <see cref="MetaProfile"/>.</summary>
    public void Refresh()
    {
        var profile = _profileGetter();
        _stardustLabel.Text = $"\u2728 Sternenstaub: {profile?.Stardust ?? 0}";

        string? previous = SelectedPerkId() ?? profile?.SelectedPerkId;

        _perkSelector.Clear();
        _perkIds.Clear();

        _perkSelector.AddItem("\u2014 ohne Perk \u2014");
        _perkIds.Add(null);

        int selectIndex = 0;
        if (profile != null)
        {
            foreach (var perk in UnlockCatalog.Perks)
            {
                if (!profile.UnlockedIds.Contains(perk.Id)) continue;
                _perkSelector.AddItem(perk.Name);
                _perkIds.Add(perk.Id);
                if (previous == perk.Id)
                    selectIndex = _perkIds.Count - 1;
            }
        }

        _perkSelector.Selected = selectIndex;
        UpdatePerkDescription();
    }

    private void OnPerkSelected(long _) => UpdatePerkDescription();

    private string? SelectedPerkId()
    {
        int idx = _perkSelector.Selected;
        if (idx < 0 || idx >= _perkIds.Count) return null;
        return _perkIds[idx];
    }

    private void UpdatePerkDescription()
    {
        var id = SelectedPerkId();
        if (id == null)
        {
            _perkDescription.Text = "Kein Perk aktiv.";
            return;
        }
        var def = UnlockCatalog.GetById(id);
        _perkDescription.Text = def?.Description ?? "";
    }

    public void Show()
    {
        Refresh();
        _panel.Visible = true;
    }

    public void Hide() => _panel.Visible = false;
}
