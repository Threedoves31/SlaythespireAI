using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Logging;
using STS2AIBot.AI;
using STS2AIBot.StateExtractor;

namespace STS2AIBot.UI;

/// <summary>
/// Debug window for monitoring AI decisions and providing manual control.
/// Provides real-time visualization of combat state and AI reasoning.
/// </summary>
public class DebugWindow
{
    private CombatSnapshot? _lastState;
    private Decision? _lastDecision;
    private int _turnCount = 0;
    private List<string> _actionLog = new();
    private List<RatingEntry> _ratings = new();
    private bool _paused = false;
    private bool _manualMode = false;

    public bool Paused => _paused;
    public bool ManualMode => _manualMode;

    /// <summary>
    /// Update the debug window with new state and decision.
    /// </summary>
    public void Update(CombatSnapshot? state, Decision? decision, int turnNumber)
    {
        if (state != null)
        {
            _lastState = state;
            _turnCount = turnNumber;
        }

        if (decision != null)
        {
            _lastDecision = decision;
            _actionLog.Add($"[Turn {turnNumber}] {decision.Type}: {decision.Reason}");

            if (_actionLog.Count > 50)
            {
                _actionLog = _actionLog.GetRange(_actionLog.Count - 50, 50);
            }
        }

        RenderToConsole();
    }

    /// <summary>
    /// Render the debug information to console.
    /// </summary>
    private void RenderToConsole()
    {
        Log.Info("═══════════════════════════════════════");
        Log.Info($"  STS2 AI Debug Window - Turn {_turnCount}");
        Log.Info($"  Paused: {_paused} | Manual Mode: {_manualMode}");
        Log.Info("─────────────────────────────────────────────────");

        if (_lastState != null)
        {
            RenderCombatState();
        }

        if (_lastDecision != null)
        {
            RenderDecision();
        }

        RenderActionLog();
        Log.Info("─────────────────────────────────────────────────");
        RenderCommands();
        Log.Info("═══════════════════════════════════════");
    }

    private void RenderCombatState()
    {
        if (_lastState == null) return;

        Log.Info($"  PLAYER: HP {_lastState.PlayerHp}/{_lastState.PlayerMaxHp} | " +
                  $"Block {_lastState.PlayerBlock} | " +
                  $"Energy {_lastState.PlayerEnergy}/{_lastState.PlayerMaxEnergy}");

        var powers = string.Join(", ", _lastState.PlayerPowers.Take(3).Select(p => $"{p.Id}({p.Amount})"));
        if (_lastState.PlayerPowers.Count > 0)
        {
            Log.Info($"          Powers: {powers}{(_lastState.PlayerPowers.Count > 3 ? "..." : "")}");
        }

        Log.Info($"  HAND ({_lastState.Hand.Count} cards):");
        for (int i = 0; i < _lastState.Hand.Count && i < 5; i++)
        {
            var card = _lastState.Hand[i];
            string playable = card.IsPlayable ? "✓" : "✗";
            Log.Info($"    [{i}] {playable} {card.Id}[{card.EnergyCost}] ({card.CardType})");
        }
        if (_lastState.Hand.Count > 5)
        {
            Log.Info($"    ... +{_lastState.Hand.Count - 5} more");
        }

        Log.Info($"  ENEMIES ({_lastState.Enemies.Count}):");
        for (int i = 0; i < _lastState.Enemies.Count; i++)
        {
            var enemy = _lastState.Enemies[i];
            string intent = enemy.IntentType switch
            {
                "Attack" => $"⚔ {enemy.IntentDamage}dmg",
                "Defend" => "🛡 defend",
                "Buff" => "⬆ buff",
                "Debuff" => "⬇ debuff",
                _ => "❓ unknown"
            };
            Log.Info($"    [{i}] {enemy.Id} HP {enemy.Hp}/{enemy.MaxHp} Block {enemy.Block} {intent}");
        }
    }

    private void RenderDecision()
    {
        if (_lastDecision == null) return;

        string typeSymbol = _lastDecision.Type switch
        {
            ActionType.PlayCard => "🎴",
            ActionType.EndTurn => "⏹",
            ActionType.UsePotion => "🧪",
            _ => "❓"
        };

        Log.Info($"  AI DECISION: {typeSymbol} {_lastDecision.Type}");
        Log.Info($"    Action: {_lastDecision.Reason}");

        if (_lastDecision.Card != null)
        {
            Log.Info($"    Card: {_lastDecision.Card.Id} (Cost: {_lastDecision.Card.EnergyCost})");
        }
        if (_lastDecision.Target != null)
        {
            Log.Info($"    Target: {_lastDecision.Target.Id} (HP: {_lastDecision.Target.CurrentHp})");
        }
        Log.Info($"    Score: {_lastDecision.Score:F2}");
    }

    private void RenderActionLog()
    {
        Log.Info("  RECENT ACTIONS:");
        var recent = _actionLog.GetRange(Math.Max(0, _actionLog.Count - 5), Math.Min(5, _actionLog.Count));
        foreach (var action in recent)
        {
            Log.Info($"    {action}");
        }
    }

    private void RenderCommands()
    {
        Log.Info("  COMMANDS (in game console):");
        Log.Info($"    [1] Toggle Pause (Currently: {_paused})");
        Log.Info($"    [2] Toggle Manual Mode (Currently: {_manualMode})");
        Log.Info($"    [3] Rate Last Action (1-5 stars)");
        Log.Info($"    [4] View Decision History");
        Log.Info($"    [5] Change AI Strategy");
    }

    /// <summary>
    /// Toggle pause state.
    /// </summary>
    public void TogglePause()
    {
        _paused = !_paused;
        Log.Info($"[DebugWindow] Pause toggled: {_paused}");
        RenderToConsole();
    }

    /// <summary>
    /// Toggle manual mode.
    /// </summary>
    public void ToggleManualMode()
    {
        _manualMode = !_manualMode;
        Log.Info($"[DebugWindow] Manual mode toggled: {_manualMode}");
        RenderToConsole();
    }

    /// <summary>
    /// Add a manual rating for the last action.
    /// </summary>
    public void RateLastAction(int stars)
    {
        if (_lastDecision == null || _lastDecision.Type != ActionType.PlayCard) return;

        _ratings.Add(new RatingEntry
        {
            CardId = _lastDecision.Card?.Id ?? "",
            Stars = stars,
            Timestamp = DateTime.UtcNow
        });

        Log.Info($"[DebugWindow] Rated action {stars}/5 stars");
    }

    /// <summary>
    /// Get decision statistics.
    /// </summary>
    public DecisionStats GetStats()
    {
        return new DecisionStats
        {
            TotalActions = _actionLog.Count,
            TurnNumber = _turnCount,
            Ratings = _ratings.ToList(),
            AverageRating = _ratings.Count > 0 ? _ratings.Average(r => r.Stars) : 0f
        };
    }

    /// <summary>
    /// Change AI strategy interactively.
    /// </summary>
    public void CycleStrategy(DecisionEngine.DecisionStrategy current)
    {
        var strategies = Enum.GetValues(typeof(DecisionEngine.DecisionStrategy));
        int currentIndex = Array.IndexOf(strategies, current);
        int nextIndex = (currentIndex + 1) % strategies.Length;
        var nextStrategy = (DecisionEngine.DecisionStrategy)strategies.GetValue(nextIndex);

        Log.Info($"[DebugWindow] Strategy changed: {current} -> {nextStrategy}");
    }

    /// <summary>
    /// Show decision history.
    /// </summary>
    public void ShowHistory()
    {
        Log.Info("  DECISION HISTORY (last 10):");
        for (int i = Math.Max(0, _actionLog.Count - 10); i < _actionLog.Count; i++)
        {
            Log.Info($"    [{i}] {_actionLog[i]}");
        }
    }

    public record DecisionStats
    {
        public int TotalActions;
        public int TurnNumber;
        public List<RatingEntry> Ratings;
        public float AverageRating;
    }

    public record RatingEntry
    {
        public string CardId;
        public int Stars;
        public DateTime Timestamp;
    }
}
