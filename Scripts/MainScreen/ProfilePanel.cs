using System;
using System.Collections.Generic;
using Godot;
using SpacedOut.Meta;
using TC = SpacedOut.Shared.ThemeColors;
using UI = SpacedOut.Shared.UiFactory;

namespace SpacedOut.MainScreen;

/// <summary>
/// M7: Full-screen "Profil &amp; Upgrades" overlay listing all <see cref="UnlockCatalog"/>
/// entries with cost, status, and a buy button. Surfaces the current Sternenstaub and a
/// "Zurück" button to close the panel.
/// </summary>
public class ProfilePanel
{
    private readonly Control _parent;
    private readonly Func<MetaProgressService?> _serviceGetter;
    private readonly Action _onClose;
    private readonly Action _onChanged;

    private PanelContainer _panel = null!;
    private Label _stardustLabel = null!;
    private VBoxContainer _entriesBox = null!;

    public ProfilePanel(Control parent, Func<MetaProgressService?> serviceGetter,
        Action onClose, Action onChanged)
    {
        _parent = parent;
        _serviceGetter = serviceGetter;
        _onClose = onClose;
        _onChanged = onChanged;
    }

    public bool IsVisible => _panel?.Visible == true;

    public void Build()
    {
        _panel = UI.CreatePanel(new Color(0.02f, 0.03f, 0.06f, 0.97f));
        _panel.Name = "ProfilePanel";
        _panel.AnchorLeft = 0f; _panel.AnchorTop = 0f;
        _panel.AnchorRight = 1f; _panel.AnchorBottom = 1f;
        _panel.OffsetLeft = 0; _panel.OffsetTop = 0;
        _panel.OffsetRight = 0; _panel.OffsetBottom = 0;
        _panel.MouseFilter = Control.MouseFilterEnum.Stop;
        _panel.Visible = false;
        _parent.AddChild(_panel);

        var center = new CenterContainer
        {
            AnchorLeft = 0f, AnchorTop = 0f,
            AnchorRight = 1f, AnchorBottom = 1f,
        };
        _panel.AddChild(center);

        var box = new VBoxContainer { CustomMinimumSize = new Vector2(720, 0) };
        box.AddThemeConstantOverride("separation", 12);
        center.AddChild(box);

        var title = UI.CreateLabel("PROFIL & UPGRADES", 32, TC.Cyan);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(title);

        _stardustLabel = UI.CreateLabel("\u2728 Sternenstaub: 0", 20, TC.Yellow);
        _stardustLabel.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(_stardustLabel);

        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });

        var perkHeader = UI.CreateLabel("Loadout-Perks (1 pro Run wählbar)", 14, TC.DimWhite);
        box.AddChild(perkHeader);

        _entriesBox = new VBoxContainer { CustomMinimumSize = new Vector2(700, 0) };
        _entriesBox.AddThemeConstantOverride("separation", 6);
        box.AddChild(_entriesBox);

        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 16) });

        var closeBtn = new Button { Text = "Zur\u00fcck", CustomMinimumSize = new Vector2(220, 40) };
        closeBtn.AddThemeFontSizeOverride("font_size", 16);
        closeBtn.Pressed += () => _onClose();
        box.AddChild(closeBtn);
    }

    public void Show()
    {
        Refresh();
        _panel.Visible = true;
    }

    public void Hide() => _panel.Visible = false;

    public void Refresh()
    {
        var svc = _serviceGetter();
        var profile = svc?.Profile;
        _stardustLabel.Text = $"\u2728 Sternenstaub: {profile?.Stardust ?? 0}";

        foreach (var child in _entriesBox.GetChildren())
            child.QueueFree();

        var groups = new List<(string Header, IEnumerable<UnlockDef> Entries)>
        {
            ("Loadout-Perks", UnlockCatalog.Perks),
            ("Inhalts-Pakete", UnlockCatalog.EventPacks),
        };

        foreach (var (header, entries) in groups)
        {
            var head = UI.CreateLabel(header, 14, TC.Cyan);
            _entriesBox.AddChild(head);

            foreach (var def in entries)
                _entriesBox.AddChild(BuildEntry(def, profile));
        }
    }

    private Control BuildEntry(UnlockDef def, MetaProfile? profile)
    {
        var row = new HBoxContainer { CustomMinimumSize = new Vector2(680, 48) };
        row.AddThemeConstantOverride("separation", 8);

        var info = new VBoxContainer { CustomMinimumSize = new Vector2(440, 0) };
        info.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        var name = UI.CreateLabel(def.Name, 14, TC.White);
        info.AddChild(name);

        var desc = UI.CreateLabel(def.Description, 12, TC.DimWhite);
        desc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        info.AddChild(desc);
        row.AddChild(info);

        var cost = UI.CreateLabel($"{def.Cost} \u2728", 14, TC.Yellow);
        cost.CustomMinimumSize = new Vector2(80, 0);
        cost.HorizontalAlignment = HorizontalAlignment.Right;
        row.AddChild(cost);

        bool owned = profile?.UnlockedIds.Contains(def.Id) == true;
        var button = new Button { CustomMinimumSize = new Vector2(120, 36) };
        if (owned)
        {
            button.Text = "Im Besitz";
            button.Disabled = true;
        }
        else
        {
            bool canAfford = (profile?.Stardust ?? 0) >= def.Cost;
            button.Text = canAfford ? "Kaufen" : "Zu teuer";
            button.Disabled = !canAfford;
            button.Pressed += () => OnPurchase(def.Id);
        }
        row.AddChild(button);

        return row;
    }

    private void OnPurchase(string id)
    {
        var svc = _serviceGetter();
        if (svc == null) return;
        if (svc.Purchase(id))
        {
            _onChanged();
            Refresh();
        }
    }
}
