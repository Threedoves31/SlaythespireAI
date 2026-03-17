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
/// Uses _UnhandledKeyInput to capture hotkeys without interfering with game.
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

    // Key masks for modifier detection
    private const long KeyMaskAlt = 0x1000000L;
    private const long KeyMaskShift = 0x2000000L;
    private const long KeyMaskCtrl = 0x4000000L;

    public override void _Ready()
    {
        Instance = this;
        SetProcessUnhandledKeyInput(true);
        Log.Info("[AIDebugger] Initialized");
        PrintHelp();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true } keyEvent)
        {
            // Ignore standalone modifier keys
            var key = keyEvent.Keycode;
            if (key is Key.Ctrl or Key.Shift or Key.Alt or Key.Meta) return;

            // Get key with modifiers as a single value
            long keyWithMods = (long)keyEvent.GetKeycodeWithModifiers();
            
            // Define hotkeys: Alt+Key combinations
            bool handled = true;
            
            // Alt+P = Pause
            if (keyWithMods == (KeyMaskAlt | (long)Key.P))
            {
                TogglePause();
            }
            // Alt+M = Manual mode
            else if (keyWithMods == (KeyMaskAlt | (long)Key.M))
            {
                ToggleManualMode();
            }
            // Alt+C = Cycle policy
            else if (keyWithMods == (KeyMaskAlt | (long)Key.C))
            {
                CyclePolicy();
            }
            // Alt+L = Toggle logging
            else if (keyWithMods == (KeyMaskAlt | (long)Key.L))
            {
                ToggleVerboseLogging();
            }
            // Alt+H = History
            else if (keyWithMods == (KeyMaskAlt | (long)Key.H))
            {
                ShowHistory();
            }
            // Shift+F1 = Help
            else if (keyWithMods == (KeyMaskShift | (long)Key.F1))
            {
                PrintHelp();
            }
            // Alt+0-5 = Rate last action
            else if (keyWithMods == (KeyMaskAlt | (long)Key.Key0))
            {
                RateLastAction(0);
            }
            else if (keyWithMods == (KeyMaskAlt | (long)Key.Key1))
            {
                RateLastAction(1);
            }
            else if (keyWithMods == (KeyMaskAlt | (long)Key.Key2))
            {
                RateLastAction(2);
            }
            else if (keyWithMods == (KeyMaskAlt | (long)Key.Key3))
            {
                RateLastAction(3);
            }
            else if (keyWithMods == (KeyMaskAlt | (long)Key.Key4))
            {
                RateLastAction(4);
            }
            else if (keyWithMods == (KeyMaskAlt | (long)Key.Key5))
            {
                RateLastAction(5);
            }
            else
            {
                handled = false;
            }
            
            if (handled)
            {
                GetViewport().SetInputAsHandled();
            }
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

    public void CyclePolicy()
    {
        PolicyManager.Instance.CyclePolicy();
        var current = PolicyManager.Instance.CurrentPolicy;
        Log.Info($"[AIDebugger] Policy: {current.Name}");
    }

    public void TogglePause()
    {
        _paused = !_paused;
        Log.Info($"[AIDebugger] Paused: {_paused}");
    }

    public void ToggleManualMode()
    {
        _manualMode = !_manualMode;
        Log.Info($"[AIDebugger] Manual Mode: {_manualMode}");
    }

    public void ToggleVerboseLogging()
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

    public void ShowHistory()
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
        Log.Info("=== STS2 AI Bot - Hotkeys ===");
        Log.Info("  Alt+P - Toggle pause");
        Log.Info("  Alt+M - Toggle manual mode");
        Log.Info("  Alt+C - Cycle AI policy");
        Log.Info("  Alt+L - Toggle verbose logging");
        Log.Info("  Alt+H - Show decision history");
        Log.Info("  Alt+0-5 - Rate last action");
        Log.Info("  Shift+F1 - Show this help");
        Log.Info("==============================");
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