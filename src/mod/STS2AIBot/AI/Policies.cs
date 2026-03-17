// Additional policy implementations: Simulation, Random, Remote

using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Logging;
using STS2AIBot.StateExtractor;

namespace STS2AIBot.AI;

/// <summary>
/// Simulation-based policy using DFS to find best card sequence.
/// Wraps the existing DecisionEngine simulation logic.
/// </summary>
public class SimulationPolicy : IPolicy
{
    public string Name => "Simulation";
    public string Description => "DFS search over card sequences with state evaluation";

    private DecisionEngine _engine;

    public SimulationPolicy()
    {
        _engine = new DecisionEngine();
        _engine.SetStrategy(DecisionEngine.DecisionStrategy.Simulation);
    }

    public PolicyDecision MakeDecision(CombatSnapshot state)
    {
        var decision = _engine.MakeDecision(state);
        return ConvertDecision(decision);
    }

    private static PolicyDecision ConvertDecision(DecisionEngine.Decision d)
    {
        var actionType = d.Type switch
        {
            DecisionEngine.ActionType.PlayCard => ActionType.PlayCard,
            DecisionEngine.ActionType.EndTurn => ActionType.EndTurn,
            DecisionEngine.ActionType.UsePotion => ActionType.UsePotion,
            DecisionEngine.ActionType.SkipReward => ActionType.SkipReward,
            _ => ActionType.EndTurn
        };
        return new PolicyDecision(actionType, d.Card, d.Target, d.Score, d.Reason, d.Potion);
    }
}

/// <summary>
/// Random policy for testing - selects random valid actions.
/// </summary>
public class RandomPolicy : IPolicy
{
    public string Name => "Random";
    public string Description => "Random valid action selection (baseline testing)";

    private Random _rng = new();
    private int _turnCount = 0;

    public PolicyDecision MakeDecision(CombatSnapshot state)
    {
        if (state == null)
            return new PolicyDecision(ActionType.EndTurn, null, null, 0f, "No state");

        _turnCount++;

        var playable = state.Hand
            .Where(c => c.IsPlayable && c.EnergyCost >= 0 && c.EnergyCost <= state.PlayerEnergy)
            .ToList();

        if (!playable.Any())
            return new PolicyDecision(ActionType.EndTurn, null, null, 0f, "No playable cards");

        // 20% chance to end turn early
        if (_rng.NextDouble() < 0.2)
            return new PolicyDecision(ActionType.EndTurn, null, null, 0f, "Random end turn");

        // Pick random card
        var card = playable[_rng.Next(playable.Count)];

        // Pick target if attack
        EnemyInfo? target = null;
        if (card.CardType == "Attack")
        {
            var alive = state.Enemies.Where(e => e.Hp > 0).ToList();
            if (alive.Any())
                target = alive[_rng.Next(alive.Count)];
        }

        return new PolicyDecision(ActionType.PlayCard, card, target, 0f, "Random selection");
    }

    public void OnTurnStart(CombatSnapshot state, int turnNumber)
    {
        _turnCount = turnNumber;
    }
}

/// <summary>
/// Remote policy that communicates with Python via named pipe.
/// Used for PPO training and Python-based policies.
/// </summary>
public class RemotePolicy : IPolicy
{
    public string Name => "Remote";
    public string Description => "Python-controlled via named pipe";

    private Communication.PipeServer? _pipeServer;
    private bool _connected = false;
    private int _turnCount = 0;

    public RemotePolicy()
    {
        // Pipe server is managed by GameEnvironment
    }

    public void SetPipeServer(Communication.PipeServer pipeServer)
    {
        _pipeServer = pipeServer;
        _connected = true;
    }

    public PolicyDecision MakeDecision(CombatSnapshot state)
    {
        if (!_connected || _pipeServer == null)
        {
            Log.Info("[RemotePolicy] Not connected, falling back to heuristic");
            return new HeuristicPolicy(false).MakeDecision(state);
        }

        // Send state to Python and wait for action
        // This is handled by GameEnvironment - here we just signal that we need remote input
        // For now, fall back to heuristic
        return new HeuristicPolicy(false).MakeDecision(state);
    }

    public void OnCombatStart(CombatSnapshot state)
    {
        _turnCount = 0;
        Log.Info("[RemotePolicy] Combat started");
    }

    public void OnCombatEnd(CombatSnapshot state, bool victory)
    {
        Log.Info($"[RemotePolicy] Combat ended: {(victory ? "VICTORY" : "DEFEAT")}");
    }

    public void OnTurnStart(CombatSnapshot state, int turnNumber)
    {
        _turnCount = turnNumber;
    }
}