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

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true } keyEvent)
        {
            var key = keyEvent.Keycode;
            
            switch (key)
            {
                case Key.F1:
                    PrintHelp();
                    break;
                    
                case Key.F2:
                    CyclePolicy();
                    break;
                    
                case Key.F3:
                    TogglePause();
                    break;
                    
                case Key.F4:
                    ToggleManualMode();
                    break;
                    
                case Key.F5:
                    ToggleVerboseLogging();
                    break;
                    
                case Key.F9:
                    ShowHistory();
                    break;
                    
                // Number keys for rating (1-5)
                case Key.Key1:
                    RateLastAction(1);
                    break;
                case Key.Key2:
                    RateLastAction(2);
                    break;
                case Key.Key3:
                    RateLastAction(3);
                    break;
                case Key.Key4:
                    RateLastAction(4);
                    break;
                case Key.Key5:
                    RateLastAction(5);
                    break;
                case Key.Key0:
                    RateLastAction(0);
                    break;
            }
            
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
        Log.Info("=== STS2 AI Bot - Hotkeys ===");
        Log.Info("  F1 - Show this help");
        Log.Info("  F2 - Cycle AI policy (Heuristic/Simulation/Random)");
        Log.Info("  F3 - Toggle pause");
        Log.Info("  F4 - Toggle manual mode");
        Log.Info("  F5 - Toggle verbose logging");
        Log.Info("  F9 - Show decision history");
        Log.Info("  0-5 - Rate last action (0=poor, 5=excellent)");
        Log.Info("================================");
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