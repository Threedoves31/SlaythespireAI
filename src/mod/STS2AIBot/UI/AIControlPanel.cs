// In-game AI Control Panel - Floating window for AI control and monitoring.
// Displays over combat UI with controls and status.

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using STS2AIBot.AI;
using STS2AIBot.StateExtractor;

namespace STS2AIBot.UI;

/// <summary>
/// Floating control panel for AI management in combat.
/// Toggle with Alt+G hotkey.
/// </summary>
public partial class AIControlPanel : Control
{
    // Panel components
    private PanelContainer _panel = null!;
    private VBoxContainer _mainVBox = null!;
    private Label _titleLabel = null!;
    private Label _statusLabel = null!;
    private Label _stateLabel = null!;
    private TextEdit _logView = null!;
    private HBoxContainer _buttonRow = null!;
    
    // Buttons
    private Button _btnPause = null!;
    private Button _btnManual = null!;
    private Button _btnCycle = null!;
    private Button _btnClear = null!;
    private Button _btnClose = null!;
    
    // State
    private CombatSnapshot? _lastState;
    private PolicyDecision? _lastDecision;
    private int _turnCount = 0;
    private List<string> _logLines = new();
    private bool _visible = false;
    private DateTime _lastUpdate = DateTime.MinValue;
    
    // Constants
    private const int PanelWidth = 350;
    private const int PanelHeight = 400;
    private const int MaxLogLines = 50;

    public static AIControlPanel? Instance { get; private set; }

    public override void _Ready()
    {
        Instance = this;
        
        // Set up control properties
        SetAnchorsPreset(LayoutPreset.TopRight);
        OffsetLeft = -PanelWidth - 10;
        OffsetTop = 10;
        OffsetRight = -10;
        OffsetBottom = PanelHeight + 10;
        
        // Create panel structure
        BuildUI();
        
        // Initially hidden
        Visible = false;
        
        Log.Info("[AIControlPanel] Created (toggle with Alt+G)");
    }

    private void BuildUI()
    {
        // Main panel with background
        _panel = new PanelContainer();
        _panel.CustomMinimumSize = new Vector2(PanelWidth, PanelHeight);
        AddChild(_panel);
        
        // Style - semi-transparent dark background
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.1f, 0.1f, 0.15f, 0.9f);
        style.BorderColor = new Color(0.3f, 0.5f, 0.8f, 1.0f);
        style.SetBorderWidthAll(2);
        style.SetContentMarginAll(8);
        _panel.AddThemeStyleboxOverride("panel", style);
        
        // Main vertical layout
        _mainVBox = new VBoxContainer();
        _panel.AddChild(_mainVBox);
        
        // Title bar
        _titleLabel = new Label();
        _titleLabel.Text = "🤖 AI Control Panel";
        _titleLabel.AddThemeFontSizeOverride("font_size", 16);
        _titleLabel.AddThemeColorOverride("font_color", new Color(0.4f, 0.7f, 1.0f));
        _mainVBox.AddChild(_titleLabel);
        
        // Separator
        var sep1 = new HSeparator();
        _mainVBox.AddChild(sep1);
        
        // Status section
        _statusLabel = new Label();
        _statusLabel.Text = "Status: Ready";
        _statusLabel.AddThemeFontSizeOverride("font_size", 12);
        _statusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _mainVBox.AddChild(_statusLabel);
        
        // State section
        _stateLabel = new Label();
        _stateLabel.Text = "HP: -- | Energy: -- | Block: --";
        _stateLabel.AddThemeFontSizeOverride("font_size", 11);
        _stateLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.8f));
        _stateLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _mainVBox.AddChild(_stateLabel);
        
        // Separator
        var sep2 = new HSeparator();
        _mainVBox.AddChild(sep2);
        
        // Log view
        _logView = new TextEdit();
        _logView.CustomMinimumSize = new Vector2(0, 200);
        _logView.Editable = false;
        _logView.ScrollFitContentHeight = true;
        _logView.WrapMode = TextEdit.LineWrappingMode.Boundary;
        
        var logStyle = new StyleBoxFlat();
        logStyle.BgColor = new Color(0.05f, 0.05f, 0.1f, 0.9f);
        logStyle.SetContentMarginAll(4);
        _logView.AddThemeStyleboxOverride("normal", logStyle);
        _logView.AddThemeColorOverride("font_readonly_color", new Color(0.9f, 0.9f, 0.9f));
        _logView.AddThemeFontSizeOverride("font_size", 11);
        _mainVBox.AddChild(_logView);
        
        // Separator
        var sep3 = new HSeparator();
        _mainVBox.AddChild(sep3);
        
        // Button row
        _buttonRow = new HBoxContainer();
        _buttonRow.AddThemeConstantOverride("separation", 4);
        _mainVBox.AddChild(_buttonRow);
        
        // Create buttons
        _btnPause = CreateButton("⏸ Pause", OnPauseClick);
        _btnManual = CreateButton("👤 Manual", OnManualClick);
        _btnCycle = CreateButton("🔄 Cycle", OnCycleClick);
        _btnClear = CreateButton("🗑 Clear", OnClearClick);
        _btnClose = CreateButton("✕ Close", OnCloseClick);
        
        _buttonRow.AddChild(_btnPause);
        _buttonRow.AddChild(_btnManual);
        _buttonRow.AddChild(_btnCycle);
        _buttonRow.AddChild(_btnClear);
        _buttonRow.AddChild(_btnClose);
        
        // Help label
        var helpLabel = new Label();
        helpLabel.Text = "Alt+G: Toggle | Alt+P: Pause | Alt+M: Manual | Alt+C: Cycle";
        helpLabel.AddThemeFontSizeOverride("font_size", 9);
        helpLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
        helpLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _mainVBox.AddChild(helpLabel);
    }

    private Button CreateButton(string text, Action onClick)
    {
        var btn = new Button();
        btn.Text = text;
        btn.CustomMinimumSize = new Vector2(60, 28);
        btn.AddThemeFontSizeOverride("font_size", 10);
        
        var btnStyle = new StyleBoxFlat();
        btnStyle.BgColor = new Color(0.2f, 0.3f, 0.5f, 0.8f);
        btnStyle.SetBorderWidthAll(1);
        btnStyle.BorderColor = new Color(0.3f, 0.5f, 0.7f);
        btnStyle.SetContentMarginAll(4);
        btn.AddThemeStyleboxOverride("normal", btnStyle);
        
        btn.Pressed += onClick;
        return btn;
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true } keyEvent)
        {
            // Alt+G = Toggle panel
            long keyWithMods = (long)keyEvent.GetKeycodeWithModifiers();
            const long keyMaskAlt = 0x1000000L;
            
            if (keyWithMods == (keyMaskAlt | (long)Key.G))
            {
                TogglePanel();
                GetViewport().SetInputAsHandled();
            }
        }
    }

    public void TogglePanel()
    {
        _visible = !_visible;
        Visible = _visible;
        Log.Info($"[AIControlPanel] Panel visible: {_visible}");
    }

    public void UpdatePanel(CombatSnapshot? state, PolicyDecision? decision, int turnNumber)
    {
        _lastState = state;
        _lastDecision = decision;
        _turnCount = turnNumber;
        
        if (!Visible) return;
        
        // Throttle updates
        if ((DateTime.UtcNow - _lastUpdate).TotalMilliseconds < 100) return;
        _lastUpdate = DateTime.UtcNow;
        
        UpdateStatus();
        UpdateStateDisplay();
        
        if (decision != null)
        {
            AddLog($"T{turnNumber}: {decision.Type} - {decision.Reason}");
        }
    }

    private void UpdateStatus()
    {
        var pm = PolicyManager.Instance;
        string status = $"Policy: {pm.CurrentPolicy.Name}";
        
        if (pm.Paused)
            status += " | ⏸ PAUSED";
        if (pm.ManualMode)
            status += " | 👤 MANUAL";
        
        _statusLabel.Text = status;
        
        // Update button states
        _btnPause.Text = pm.Paused ? "▶ Resume" : "⏸ Pause";
        _btnManual.Text = pm.ManualMode ? "🤖 Auto" : "👤 Manual";
    }

    private void UpdateStateDisplay()
    {
        if (_lastState == null)
        {
            _stateLabel.Text = "No combat state";
            return;
        }
        
        var s = _lastState;
        string text = $"HP: {s.PlayerHp}/{s.PlayerMaxHp} | Energy: {s.PlayerEnergy} | Block: {s.PlayerBlock}\n";
        text += $"Hand: {s.Hand.Count} cards | Draw: {s.DrawPileCount} | Discard: {s.DiscardPileCount}\n";
        
        if (s.Enemies.Count > 0)
        {
            var e = s.Enemies[0];
            text += $"Enemy: {e.Id} HP:{e.Hp}/{e.MaxHp} | Intent: {e.IntentType}";
        }
        
        _stateLabel.Text = text;
    }

    private void AddLog(string message)
    {
        _logLines.Add($"[{DateTime.UtcNow:HH:mm:ss}] {message}");
        
        while (_logLines.Count > MaxLogLines)
        {
            _logLines.RemoveAt(0);
        }
        
        _logView.Text = string.Join("\n", _logLines);
        _logView.ScrollVertical = Mathf.Inf; // Scroll to bottom
    }

    #region Button Handlers

    private void OnPauseClick()
    {
        PolicyManager.Instance.TogglePause();
        UpdateStatus();
        AddLog(PolicyManager.Instance.Paused ? "Paused" : "Resumed");
    }

    private void OnManualClick()
    {
        PolicyManager.Instance.ToggleManualMode();
        UpdateStatus();
        AddLog(PolicyManager.Instance.ManualMode ? "Manual mode ON" : "Auto mode ON");
    }

    private void OnCycleClick()
    {
        PolicyManager.Instance.CyclePolicy();
        AddLog($"Policy: {PolicyManager.Instance.CurrentPolicy.Name}");
        UpdateStatus();
    }

    private void OnClearClick()
    {
        _logLines.Clear();
        _logView.Text = "";
        AddLog("Log cleared");
    }

    private void OnCloseClick()
    {
        TogglePanel();
    }

    #endregion

    public void ShowMessage(string message)
    {
        AddLog(message);
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }
}