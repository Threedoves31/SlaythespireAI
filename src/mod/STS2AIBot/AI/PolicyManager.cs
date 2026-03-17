// Policy Manager - Central management for AI policies.
// Allows switching between policies via hotkey or console command.
// Also controls AI pause/manual state globally.

using System;
using MegaCrit.Sts2.Core.Logging;
using STS2AIBot.StateExtractor;

namespace STS2AIBot.AI;

/// <summary>
/// Central manager for AI policies and control state.
/// Supports hot-swapping policies at runtime.
/// </summary>
public class PolicyManager
{
    private static PolicyManager? _instance;
    public static PolicyManager Instance => _instance ??= new PolicyManager();

    private IPolicy _currentPolicy;
    private PolicyType _currentType;

    // Global control state (works even outside combat)
    private bool _paused = false;
    private bool _manualMode = false;

    public IPolicy CurrentPolicy => _currentPolicy;
    public PolicyType CurrentType => _currentType;
    
    public bool Paused 
    { 
        get => _paused; 
        set 
        {
            _paused = value;
            Log.Info($"[PolicyManager] Paused: {_paused}");
        }
    }
    
    public bool ManualMode 
    { 
        get => _manualMode; 
        set 
        {
            _manualMode = value;
            Log.Info($"[PolicyManager] Manual Mode: {_manualMode}");
        }
    }

    public event Action<PolicyType>? OnPolicyChanged;

    private PolicyManager()
    {
        _currentPolicy = new HeuristicPolicy(debugMode: false);
        _currentType = PolicyType.Heuristic;
    }

    /// <summary>
    /// Make a decision using current policy.
    /// </summary>
    public PolicyDecision? MakeDecision(CombatSnapshot state)
    {
        // Don't make decisions when paused or in manual mode
        if (_paused || _manualMode) return null;
        return _currentPolicy.MakeDecision(state);
    }

    /// <summary>
    /// Cycle to next policy in order: Heuristic -> Simulation -> Random -> Heuristic
    /// </summary>
    public void CyclePolicy()
    {
        var next = _currentType switch
        {
            PolicyType.Heuristic => PolicyType.Simulation,
            PolicyType.Simulation => PolicyType.Random,
            PolicyType.Random => PolicyType.Heuristic,
            _ => PolicyType.Heuristic
        };
        SetPolicy(next);
    }

    /// <summary>
    /// Set specific policy type.
    /// </summary>
    public void SetPolicy(PolicyType type)
    {
        if (type == _currentType) return;

        _currentPolicy = type switch
        {
            PolicyType.Heuristic => new HeuristicPolicy(debugMode: false),
            PolicyType.Simulation => new SimulationPolicy(),
            PolicyType.Random => new RandomPolicy(),
            _ => new HeuristicPolicy(debugMode: false)
        };
        _currentType = type;

        OnPolicyChanged?.Invoke(type);
        Log.Info($"[PolicyManager] Switched to {type} policy");
    }

    public void TogglePause()
    {
        Paused = !Paused;
    }

    public void ToggleManualMode()
    {
        ManualMode = !ManualMode;
    }

    public string GetStatusString()
    {
        return $"Policy: {_currentPolicy.Name} ({_currentType}) | Paused: {_paused} | Manual: {_manualMode}";
    }
}