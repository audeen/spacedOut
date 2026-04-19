using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpacedOut.Mission;
using SpacedOut.Run;
using SpacedOut.State;
using TC = SpacedOut.Shared.ThemeColors;
using UI = SpacedOut.Shared.UiFactory;

namespace SpacedOut.MainScreen;

/// <summary>Run HUD: captain panel, navigator panel, run map prompt, run map display integration.</summary>
public class RunPanel
{
    private PanelContainer? _captainPanel;
    private Label? _resLabel;
    private Label? _flagsLabel;
    private Label? _nodeInfoLabel;
    private Button? _scanButton;

    private PanelContainer? _navigatorPanel;
    private Label? _navDepthLabel;
    private Label? _navVisitedLabel;
    private Label? _navPreviewLabel;
    private Label? _mapPromptLabel;

    private string? _selectedNodeId;

    private readonly Control _parent;
    private readonly RunMapDisplay _runMapDisplay;
    private readonly Action _onEnterPressed;
    private readonly Action<int> _onResolvePressed;
    private readonly Action<string>? _onScanPressed;

    public RunPanel(Control parent, RunMapDisplay runMapDisplay,
                    Action onEnterPressed, Action<int> onResolvePressed,
                    Action<string>? onScanPressed = null)
    {
        _parent = parent;
        _runMapDisplay = runMapDisplay;
        _onEnterPressed = onEnterPressed;
        _onResolvePressed = onResolvePressed;
        _onScanPressed = onScanPressed;
    }

    public string? SelectedNodeId
    {
        get => _selectedNodeId;
        set => _selectedNodeId = value;
    }

    public void Build()
    {
        _captainPanel = UI.CreatePanel(TC.PanelBg);
        _captainPanel.Position = new Vector2(20, 280);
        _captainPanel.Size = new Vector2(280, 420);
        _captainPanel.Visible = false;
        _captainPanel.Name = "RunCaptainPanel";
        _parent.AddChild(_captainPanel);

        var capBox = new VBoxContainer { Position = new Vector2(10, 8) };
        capBox.AddThemeConstantOverride("separation", 4);
        _captainPanel.AddChild(capBox);

        capBox.AddChild(UI.CreateLabel("KAPITÄN — Run", 14, TC.Cyan));
        _resLabel = UI.CreateLabel("Ressourcen: ---", 11, TC.DimWhite);
        capBox.AddChild(_resLabel);
        _flagsLabel = UI.CreateLabel("Flags: ---", 11, TC.DimWhite);
        _flagsLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        capBox.AddChild(_flagsLabel);
        _nodeInfoLabel = UI.CreateLabel("Knoten: ---", 11, TC.White);
        _nodeInfoLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _nodeInfoLabel.CustomMinimumSize = new Vector2(250, 0);
        capBox.AddChild(_nodeInfoLabel);

        _scanButton = new Button { Text = "Scan", CustomMinimumSize = new Vector2(250, 26), Disabled = true };
        _scanButton.Pressed += OnScanButtonPressed;
        capBox.AddChild(_scanButton);

        var enterBtn = new Button { Text = "Knoten betreten (Auswahl)", CustomMinimumSize = new Vector2(250, 28) };
        enterBtn.Pressed += () => _onEnterPressed();
        capBox.AddChild(enterBtn);

        capBox.AddChild(UI.CreateLabel("Auflösen (Test)", 12, TC.Yellow));
        var resGrid = new HBoxContainer();
        resGrid.AddThemeConstantOverride("separation", 4);
        capBox.AddChild(resGrid);
        AddResolveBtn(resGrid, "OK", NodeResolution.Success);
        AddResolveBtn(resGrid, "Teil", NodeResolution.PartialSuccess);
        var resGrid2 = new HBoxContainer();
        resGrid2.AddThemeConstantOverride("separation", 4);
        capBox.AddChild(resGrid2);
        AddResolveBtn(resGrid2, "Fail", NodeResolution.Failure);
        AddResolveBtn(resGrid2, "Skip", NodeResolution.Skipped);

        _navigatorPanel = UI.CreatePanel(TC.PanelBg);
        _navigatorPanel.AnchorLeft = 1;
        _navigatorPanel.AnchorRight = 1;
        _navigatorPanel.Position = new Vector2(-300, 280);
        _navigatorPanel.Size = new Vector2(280, 380);
        _navigatorPanel.Visible = false;
        _navigatorPanel.Name = "RunNavigatorPanel";
        _parent.AddChild(_navigatorPanel);

        var navBox = new VBoxContainer { Position = new Vector2(10, 8) };
        navBox.AddThemeConstantOverride("separation", 4);
        _navigatorPanel.AddChild(navBox);
        navBox.AddChild(UI.CreateLabel("NAVIGATOR — Vorschau", 14, TC.Cyan));
        _navDepthLabel = UI.CreateLabel("Tiefe: ---", 11, TC.DimWhite);
        navBox.AddChild(_navDepthLabel);
        _navVisitedLabel = UI.CreateLabel("Verlauf: ---", 11, TC.DimWhite);
        _navVisitedLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _navVisitedLabel.CustomMinimumSize = new Vector2(250, 0);
        navBox.AddChild(_navVisitedLabel);
        _navNavPreviewLabel(navBox);

        _mapPromptLabel = UI.CreateLabel(
            "RUN-KARTE — Auswahl beim Kommandanten", 16, TC.Cyan);
        _mapPromptLabel.AnchorLeft = 0.5f; _mapPromptLabel.AnchorRight = 0.5f;
        _mapPromptLabel.AnchorTop = 0.15f; _mapPromptLabel.AnchorBottom = 0.15f;
        _mapPromptLabel.Position = new Vector2(-300, -15);
        _mapPromptLabel.Size = new Vector2(600, 30);
        _mapPromptLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _mapPromptLabel.Visible = false;
        _parent.AddChild(_mapPromptLabel);
    }

    private void _navNavPreviewLabel(VBoxContainer navBox)
    {
        _navPreviewLabel = UI.CreateLabel("Nächste: ---", 11, TC.DimWhite);
        _navPreviewLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        navBox.AddChild(_navPreviewLabel);
    }

    private void AddResolveBtn(HBoxContainer row, string text, NodeResolution res)
    {
        var btn = new Button { Text = text, CustomMinimumSize = new Vector2(118, 26) };
        btn.Pressed += () => _onResolvePressed((int)res);
        row.AddChild(btn);
    }

    private void OnScanButtonPressed()
    {
        if (string.IsNullOrEmpty(_selectedNodeId)) return;
        _onScanPressed?.Invoke(_selectedNodeId);
    }

    public void UpdateVisibility(bool runMapActive)
    {
        if (_mapPromptLabel != null) _mapPromptLabel.Visible = runMapActive;
        if (_captainPanel != null) _captainPanel.Visible = false;
        if (_navigatorPanel != null) _navigatorPanel.Visible = runMapActive;
    }

    public void UpdateRun(RunController run)
    {
        _runMapDisplay.UpdateRun(run, _selectedNodeId);

        var st = run.CurrentRun;
        string resLine = string.Join("  ",
            st.Resources.Select(kvp => $"{kvp.Key}:{kvp.Value}"));
        if (_resLabel != null) _resLabel.Text = $"Ressourcen: {resLine}";
        if (_flagsLabel != null)
            _flagsLabel.Text = st.Flags.Count > 0
                ? $"Flags: {string.Join(", ", st.Flags)}"
                : "Flags: —";

        string cur = st.CurrentNodeId ?? "—";
        string sel = _selectedNodeId ?? "—";

        st.Resources.TryGetValue(RunResourceIds.ScienceData, out int science);
        int scanCost = RunController.ScanCostScience;

        if (_nodeInfoLabel != null)
        {
            string detail = "";
            if (!string.IsNullOrEmpty(st.CurrentNodeId) &&
                run.CurrentDefinition.Nodes.TryGetValue(st.CurrentNodeId, out var active))
            {
                detail = FormatNodeInfo(active, run.GetNodeRuntime(st.CurrentNodeId).Knowledge,
                    label: "Aktiv");
            }

            if (!string.IsNullOrEmpty(_selectedNodeId) &&
                run.CurrentDefinition.Nodes.TryGetValue(_selectedNodeId, out var picked) &&
                (string.IsNullOrEmpty(st.CurrentNodeId) || _selectedNodeId != st.CurrentNodeId))
            {
                string pickedLine = FormatNodeInfo(picked,
                    run.GetNodeRuntime(_selectedNodeId).Knowledge, label: "Auswahl");
                detail += (detail.Length > 0 ? "\n\n" : "") + pickedLine;
            }

            _nodeInfoLabel.Text = $"Aktiv: {cur}   Auswahl: {sel}\n{detail}";
        }

        if (_scanButton != null)
        {
            bool hasSelection = !string.IsNullOrEmpty(_selectedNodeId);
            bool inRange = hasSelection && run.IsWithinScanRange(_selectedNodeId!);
            bool canScan = hasSelection && run.CanScanNode(_selectedNodeId!);
            _scanButton.Disabled = !canScan;
            if (hasSelection && !inRange)
                _scanButton.Text = $"Scan außer Reichweite (+{RunController.MaxScanDepthAhead})";
            else
                _scanButton.Text = $"Scan ({scanCost} Data, aktuell {science})";
        }

        if (_navDepthLabel != null)
            _navDepthLabel.Text = $"Tiefe (Run): {st.CurrentDepth}";
        if (_navVisitedLabel != null)
            _navVisitedLabel.Text = "Verlauf: " + (st.VisitedNodeIds.Count > 0
                ? string.Join(" → ", st.VisitedNodeIds)
                : "—");
        if (_navPreviewLabel != null)
        {
            if (!string.IsNullOrEmpty(st.CurrentNodeId) &&
                run.CurrentDefinition.Nodes.TryGetValue(st.CurrentNodeId, out var cn))
            {
                var parts = new List<string>();
                foreach (var nid in cn.NextNodeIds)
                {
                    if (!run.CurrentRun.NodeStates.TryGetValue(nid, out var nrt)) continue;
                    parts.Add($"{nid}:{nrt.State}");
                }
                _navPreviewLabel.Text = "Nächste: " + string.Join(", ", parts);
            }
            else
                _navPreviewLabel.Text = "Nächste: —";
        }
    }

    private static string FormatNodeInfo(RunNodeData node, NodeKnowledgeState know, string label)
    {
        int fuelCost = NodeEncounterConfig.GetFuelCostFor(node.Type);
        string fuelLine = fuelCost <= 0
            ? "Sprungkosten: kostenlos"
            : $"Sprungkosten: {fuelCost} Fuel";

        switch (know)
        {
            case NodeKnowledgeState.Silhouette:
                return $"{label}: {node.Id}\nUnbekannt — Scan verfügbar (1 ScienceData).";

            case NodeKnowledgeState.Sighted:
                return $"{label}: {node.Title} ({node.Type})\n" +
                       $"Risiko: {node.RiskRating}\n" +
                       $"{fuelLine}\n" +
                       $"Scan für Briefing-Vorschau verfügbar.";

            case NodeKnowledgeState.Scanned:
                string briefing = GetBriefingShort(node);
                string rewards = FormatRewards(node.ResourceChangesOnSuccess);
                var rewardLine = string.IsNullOrEmpty(rewards) ? "" : $"\nErwartete Belohnung: {rewards}";
                var briefingLine = string.IsNullOrEmpty(briefing) ? "" : $"\nBriefing: {briefing}";
                return $"{label}: {node.Title} ({node.Type})\n" +
                       $"Risiko: {node.RiskRating}\n" +
                       $"{fuelLine}" +
                       briefingLine +
                       rewardLine;

            default:
                return $"{label}: {node.Id}";
        }
    }

    private static string FormatRewards(Dictionary<string, int>? rewards)
    {
        if (rewards == null || rewards.Count == 0) return "";
        return string.Join(", ", rewards
            .Where(kv => kv.Value != 0)
            .Select(kv => $"{kv.Key} {(kv.Value >= 0 ? "+" : "")}{kv.Value}"));
    }

    private static string GetBriefingShort(RunNodeData node)
    {
        string text = SpacedOut.Orchestration.MissionOrchestrator.GetBriefingPreviewForRunNode(node);
        if (string.IsNullOrEmpty(text)) return "";
        if (text.Length <= 120) return text;
        return text.Substring(0, 118) + "…";
    }
}
