// Policy Manager - Central management for AI policies.
// Allows switching between policies via hotkey or config.

using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Logging;
using STS2AIBot.StateExtractor;

namespace STS2AIBot.AI;

/// <summary>
/// Central manager for AI policies.
/// Supports hot-swapping policies at runtime via F2 key.
/// </summary>
public class PolicyManager
{
    private static PolicyManager? _instance;
    public static PolicyManager Instance => _instance ??= new PolicyManager();

    private IPolicy _currentPolicy;
    private PolicyType _currentType;
    private readonly Dictionary<PolicyType, IPolicy> _policies = new();

    // Policy cycle order
    private static readonly PolicyType[] PolicyCycle = new[]
    {
        PolicyType.Heuristic,
        PolicyType.Simulation,
        PolicyType.Random,
        // PolicyType.MCTS,  // TODO
        // PolicyType.PPO,   // TODO
    };

    private int _cycleIndex = 0;

    public PolicyType CurrentType => _currentType;
    public IPolicy CurrentPolicy => _currentPolicy;
    public event Action<PolicyType>? OnPolicyChanged;

    private PolicyManager()
    {
        // Initialize with heuristic by default
        _currentType = PolicyType.Heuristic;
        _currentPolicy = CreatePolicy(PolicyType.Heuristic);
        _policies[PolicyType.Heuristic] = _currentPolicy;

        Log.Info($"[PolicyManager] Initialized with { _currentPolicy.Name} policy");
        Log.Info("[PolicyManager] Press F2 to cycle policies");
    }

    /// <summary>
    /// Get or create a policy instance.
    /// </summary>
    private IPolicy CreatePolicy(PolicyType type)
    {
        if (_policies.TryGetValue(type, out var existing))
            return existing;

        IPolicy policy = type switch
        {
            PolicyType.Heuristic => new HeuristicPolicy(),
            PolicyType.Simulation => new SimulationPolicy(),
            PolicyType.Random => new RandomPolicy(),
            PolicyType.Remote => new RemotePolicy(),
            _ => new HeuristicPolicy(),
        };

        _policies[type] = policy;
        return policy;
    }

    /// <summary>
    /// Switch to a specific policy type.
    /// </summary>
    public void SetPolicy(PolicyType type)
    {
        if (type == _currentType) return;

        var newPolicy = CreatePolicy(type);
        _currentType = type;
        _currentPolicy = newPolicy;

        Log.Info($"[PolicyManager] Switched to {newPolicy.Name} policy");
        OnPolicyChanged?.Invoke(type);
    }

    /// <summary>
    /// Cycle to the next policy in the list.
    /// Called by F2 hotkey.
    /// </summary>
    public void CyclePolicy()
    {
        _cycleIndex = (_cycleIndex + 1) % PolicyCycle.Length;
        SetPolicy(PolicyCycle[_cycleIndex]);
    }

    /// <summary>
    /// Make a decision using the current policy.
    /// </summary>
    public PolicyDecision MakeDecision(CombatSnapshot state)
    {
        return _currentPolicy.MakeDecision(state);
    }

    /// <summary>
    /// Get available policy types.
    /// </summary>
    public static IEnumerable<PolicyType> GetAvailablePolicies()
    {
        return PolicyCycle;
    }

    /// <summary>
    /// Get policy status string for UI display.
    /// </summary>
    public string GetStatusString()
    {
        return $"Policy: {_currentPolicy.Name} - {_currentPolicy.Description}";
    }
}