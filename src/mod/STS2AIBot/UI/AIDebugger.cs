// Unified AI Debugger - Handles keyboard input, state display, and control.
// Uses Godot's _UnhandledKeyInput for hotkey capture.

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using STS2AIBot.AI;
using STS2AIBot.StateExtractor;

namespace STS2AIBot.UI;

/// <summary>
/// Unified debugger for AI control and monitoring.
/// Captures F1-F5 hotkeys via Godot's input system.
/// </summary>
public partial class AIDebugger : Node
{
    // State
    private CombatSnapshot? _lastState;
    private PolicyDecision? _lastDecision;
    private int _turnCount = 0;
    private List<string> _actionLog = new();
    private List<RatingEntry> _ratings = new();
    
    // Control flags
    private bool _paused = false;
    private bool _manualMode = false;
    private bool _verboseLogging = true;
    
    // Singleton access
    public static AIDebugger? Instance { get; private set; }
    
    public bool Paused => _paused;
    public bool ManualMode => _manualMode;
    public bool VerboseLogging => _verboseLogging;

    public override void _Ready()
    {
        Instance = this;
        SetProcessUnhandledKeyInput(true);
        Log.Info("[AIDebugger] Initialized - Press F1 for help");
        PrintHelp();
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true } keyEvent)
        {
            var key = keyEvent.Keycode;
            bool handled = true;
            
            switch (key)
            {
                // Ctrl+Shift+key combinations to avoid game conflicts
                case Key.F1:
                    if (keyEvent.ShiftPressed)
                        PrintHelp();
                    else
                        handled = false;
                    break;
                    
                case Key.P:
                    if (keyEvent.CtrlPressed && keyEvent.ShiftPressed)
                        TogglePause();
                    else
                        handled = false;
                    break;
                    
                case Key.M:
                    if (keyEvent.CtrlPressed && keyEvent.ShiftPressed)
                        ToggleManualMode();
                    else
                        handled = false;
                    break;
                    
                case Key.L:
                    if (keyEvent.CtrlPressed && keyEvent.ShiftPressed)
                        ToggleVerboseLogging();
                    else
                        handled = false;
                    break;
                    
                case Key.C:
                    if (keyEvent.CtrlPressed && keyEvent.ShiftPressed)
                        CyclePolicy();
                    else
                        handled = false;
                    break;
                    
                case Key.H:
                    if (keyEvent.CtrlPressed && keyEvent.ShiftPressed)
                        ShowHistory();
                    else
                        handled = false;
                    break;
                    
                // Number keys for rating (Ctrl+Shift+1-5)
                case Key.Key1:
                    if (keyEvent.CtrlPressed && keyEvent.ShiftPressed)
                        RateLastAction(1);
                    else
                        handled = false;
                    break;
                case Key.Key2:
                    if (keyEvent.CtrlPressed && keyEvent.ShiftPressed)
                        RateLastAction(2);
                    else
                        handled = false;
                    break;
                case Key.Key3:
                    if (keyEvent.CtrlPressed && keyEvent.ShiftPressed)
                        RateLastAction(3);
                    else
                        handled = false;
                    break;
                case Key.Key4:
                    if (keyEvent.CtrlPressed && keyEvent.ShiftPressed)
                        RateLastAction(4);
                    else
                        handled = false;
                    break;
                case Key.Key5:
                    if (keyEvent.CtrlPressed && keyEvent.ShiftPressed)
                        RateLastAction(5);
                    else
                        handled = false;
                    break;
                case Key.Key0:
                    if (keyEvent.CtrlPressed && keyEvent.ShiftPressed)
                        RateLastAction(0);
                    else
                        handled = false;
                    break;
                    
                default:
                    handled = false;
                    break;
            }
            
            if (handled)
                GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>
    /// Update debugger with new combat state and decision.
    /// Called by CombatHook after each AI decision.
    /// </summary>
    public void Update(CombatSnapshot? state, PolicyDecision? decision, int turnNumber)
    {
        if (state != null)
        {
            _lastState = state;
            _turnCount = turnNumber;
        }

        if (decision != null)
        {
            _lastDecision = decision;
            _actionLog.Add($"[T{turnNumber}] {decision.Type}: {decision.Reason}");

            if (_actionLog.Count > 100)
            {
                _actionLog = _actionLog.GetRange(_actionLog.Count - 100, 100);
            }
        }

        if (_verboseLogging)
        {
            RenderToConsole();
        }
    }

    #region Control Methods

    private void CyclePolicy()
    {
        PolicyManager.Instance.CyclePolicy();
        var current = PolicyManager.Instance.CurrentPolicy;
        Log.Info($"[AIDebugger] Policy: {current.Name}");
    }

    private void TogglePause()
    {
        _paused = !_paused;
        Log.Info($"[AIDebugger] Paused: {_paused}");
    }

    private void ToggleManualMode()
    {
        _manualMode = !_manualMode;
        Log.Info($"[AIDebugger] Manual Mode: {_manualMode}");
    }

    private void ToggleVerboseLogging()
    {
        _verboseLogging = !_verboseLogging;
        Log.Info($"[AIDebugger] Verbose Logging: {_verboseLogging}");
    }

    private void RateLastAction(int stars)
    {
        if (_lastDecision == null || _lastDecision.Type != ActionType.PlayCard)
        {
            Log.Info("[AIDebugger] No card action to rate");
            return;
        }

        _ratings.Add(new RatingEntry
        {
            CardId = _lastDecision.Card?.Id ?? "",
            Stars = stars,
            Timestamp = DateTime.UtcNow
        });

        Log.Info($"[AIDebugger] Rated '{_lastDecision.Card?.Id}' as {stars}/5");
    }

    private void ShowHistory()
    {
        Log.Info("=== Decision History (last 10) ===");
        int start = Math.Max(0, _actionLog.Count - 10);
        for (int i = start; i < _actionLog.Count; i++)
        {
            Log.Info($"  {i - start + 1}. {_actionLog[i]}");
        }
    }

    public void ShowMessage(string message)
    {
        Log.Info($"[AIDebugger] {message}");
    }

    #endregion

    #region Rendering

    private void PrintHelp()
    {
        Log.Info("=== STS2 AI Bot - Hotkeys (Ctrl+Shift+Key) ===");
        Log.Info("  Shift+F1     - Show this help");
        Log.Info("  Ctrl+Shift+P - Toggle pause");
        Log.Info("  Ctrl+Shift+M - Toggle manual mode");
        Log.Info("  Ctrl+Shift+L - Toggle verbose logging");
        Log.Info("  Ctrl+Shift+C - Cycle AI policy");
        Log.Info("  Ctrl+Shift+H - Show decision history");
        Log.Info("  Ctrl+Shift+0-5 - Rate last action");
        Log.Info("==============================================");
        Log.Info($"Current: {PolicyManager.Instance.GetStatusString()}");
    }

    private void RenderToConsole()
    {
        if (_lastState == null) return;

        Log.Info($"--- Turn {_turnCount} | HP:{_lastState.PlayerHp}/{_lastState.PlayerMaxHp} | " +
                 $"Energy:{_lastState.PlayerEnergy} | Block:{_lastState.PlayerBlock} ---");

        // Show decision
        if (_lastDecision != null)
        {
            string action = _lastDecision.Type == ActionType.PlayCard 
                ? $"Play {_lastDecision.Card?.Id} -> {_lastDecision.Target?.Id ?? "self"}"
                : "End Turn";
            Log.Info($"  AI: {action} | {_lastDecision.Reason}");
        }

        // Show enemies
        if (_lastState.Enemies.Count > 0)
        {
            var enemy = _lastState.Enemies[0];
            Log.Info($"  Enemy: {enemy.Id} HP:{enemy.Hp}/{enemy.MaxHp} Intent:{enemy.IntentType}");
        }
    }

    #endregion

    #region Stats

    public DecisionStats GetStats()
    {
        return new DecisionStats
        {
            TotalActions = _actionLog.Count,
            TurnNumber = _turnCount,
            Ratings = _ratings.ToList(),
            AverageRating = _ratings.Count > 0 ? (float)_ratings.Average(r => r.Stars) : 0f
        };
    }

    public record DecisionStats
    {
        public int TotalActions;
        public int TurnNumber;
        public List<RatingEntry> Ratings = new();
        public float AverageRating;
    }

    public record RatingEntry
    {
        public string CardId = "";
        public int Stars;
        public DateTime Timestamp;
    }

    #endregion
}