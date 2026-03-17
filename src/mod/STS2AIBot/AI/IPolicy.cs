// AI Policy Interface for pluggable decision-making algorithms.
// Implement this interface to create custom AI policies.

using System.Collections.Generic;
using STS2AIBot.StateExtractor;

namespace STS2AIBot.AI;

/// <summary>
/// Action types for AI decisions.
/// </summary>
public enum ActionType
{
    PlayCard,
    EndTurn,
    UsePotion,
    SkipReward
}

/// <summary>
/// Decision record returned by policies.
/// </summary>
public record PolicyDecision(
    ActionType Type,
    CardInfo? Card,
    EnemyInfo? Target,
    float Score,
    string Reason,
    PotionInfo? Potion = null
);

/// <summary>
/// Interface for combat decision policies.
/// Implementations: HeuristicPolicy, SimulationPolicy, MCTSPolicy, PPOPolicy, etc.
/// </summary>
public interface IPolicy
{
    /// <summary>
    /// Unique identifier for this policy.
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// Human-readable description.
    /// </summary>
    string Description { get; }
    
    /// <summary>
    /// Make a combat decision given the current state.
    /// Called repeatedly each turn until EndTurn is returned.
    /// </summary>
    PolicyDecision MakeDecision(CombatSnapshot state);
    
    /// <summary>
    /// Called at the start of each combat.
    /// </summary>
    void OnCombatStart(CombatSnapshot state) { }
    
    /// <summary>
    /// Called at the end of each combat.
    /// </summary>
    void OnCombatEnd(CombatSnapshot state, bool victory) { }
    
    /// <summary>
    /// Called at the start of each turn.
    /// </summary>
    void OnTurnStart(CombatSnapshot state, int turnNumber) { }
    
    /// <summary>
    /// Called at the end of each turn.
    /// </summary>
    void OnTurnEnd(CombatSnapshot state, int turnNumber) { }
}

/// <summary>
/// Policy types available in the system.
/// </summary>
public enum PolicyType
{
    /// <summary>Simple rule-based heuristic (fast, no simulation)</summary>
    Heuristic,
    
    /// <summary>Full turn simulation with DFS search</summary>
    Simulation,
    
    /// <summary>Monte Carlo Tree Search (planned)</summary>
    MCTS,
    
    /// <summary>PPO neural network via ONNX (planned)</summary>
    PPO,
    
    /// <summary>Python-controlled via named pipe</summary>
    Remote,
    
    /// <summary>Random valid actions (for testing)</summary>
    Random,
}

/// <summary>
/// Factory for creating policy instances.
/// </summary>
public static class PolicyFactory
{
    public static IPolicy Create(PolicyType type)
    {
        return type switch
        {
            PolicyType.Heuristic => new HeuristicPolicy(),
            PolicyType.Simulation => new SimulationPolicy(),
            PolicyType.Random => new RandomPolicy(),
            PolicyType.Remote => new RemotePolicy(),
            // PolicyType.MCTS => new MCTSPolicy(),  // TODO
            // PolicyType.PPO => new PPOPolicy(),    // TODO
            _ => new HeuristicPolicy(),
        };
    }
}