// Combat Hook - Intercepts combat turns and executes AI decisions.
// Uses PolicyManager for pluggable AI strategies.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using STS2AIBot.StateExtractor;
using STS2AIBot.Communication;
using STS2AIBot.AI;
using STS2AIBot.UI;
using Godot;

namespace STS2AIBot;

/// <summary>
/// Enhanced combat hook with pluggable AI policies.
/// Supports multiple strategies via PolicyManager, hot-swappable at runtime.
/// </summary>
public static class CombatHook
{
    private static bool _isRunning = false;
    private static GameEnvironment? _gameEnv;
    private static DecisionEngine? _decisionEngine;  // For potion logic

    private static int _turnCount = 0;
    private static List<CardModel> _playedThisTurn = new();
    private static int _previousPlayerHp = 0;

    public static void Register()
    {
        // Initialize components
        _decisionEngine = new DecisionEngine();

        // Register combat hook
        CombatManager.Instance.TurnStarted += OnTurnStarted;

        // Register policy change callback
        PolicyManager.Instance.OnPolicyChanged += OnPolicyChanged;

        Log.Info("[CombatHook] Combat hook registered");
        Log.Info($"[CombatHook] Current policy: {PolicyManager.Instance.GetStatusString()}");
    }

    public static void Register(GameEnvironment gameEnv)
    {
        _gameEnv = gameEnv;
        CombatManager.Instance.TurnStarted += OnTurnStarted;
        Log.Info("[CombatHook] CombatHook registered (training mode)");
    }

    private static void OnPolicyChanged(PolicyType newType)
    {
        Log.Info($"[CombatHook] Policy changed to: {newType}");
    }

    private static void OnTurnStarted(CombatState state)
    {
        // Only act on player turns
        if (state.CurrentSide != CombatSide.Player) return;

        // Training mode: notify game environment
        if (_gameEnv != null)
        {
            _gameEnv.OnTurnStarted();
            return;
        }

        // Check if paused (uses PolicyManager - works even outside combat)
        if (PolicyManager.Instance.Paused)
        {
            Log.Info("[CombatHook] AI paused, waiting...");
            _isRunning = false;  // Reset flag so AI can resume when unpaused
            return;
        }

        // Manual mode: don't auto-play
        if (PolicyManager.Instance.ManualMode)
        {
            Log.Info("[CombatHook] Manual mode active, no auto-play");
            return;
        }

        // AI mode
        // Note: _isRunning is reset in finally block of PlayTurnAsync
        // If paused during PlayTurnAsync, it breaks out and resets _isRunning
        if (_isRunning) 
        {
            Log.Info("[CombatHook] AI already running, skipping");
            return;
        }
        _isRunning = true;

        // Fire-and-forget async task
        var task = PlayTurnAsync(state);
        MegaCrit.Sts2.Core.Helpers.TaskHelper.RunSafely(task);
    }

    private static async Task PlayTurnAsync(CombatState state)
    {
        try
        {
            await WaitForPlayPhase();
            if (!CombatManager.Instance.IsInProgress) return;

            var runState = RunManager.Instance.DebugOnlyGetState();
            if (runState == null) return;
            var player = LocalContext.GetMe(runState);
            if (player == null) return;

            // Start of turn
            _turnCount++;
            _previousPlayerHp = player.Creature.CurrentHp;
            _playedThisTurn.Clear();

            // Read combat state
            var snapshot = GameStateReader.TryRead();
            if (snapshot == null)
            {
                Log.Info("[CombatHook] Failed to read combat state");
                return;
            }

            // Use potions before playing cards (using legacy DecisionEngine for now)
            await UsePotionsAsync(player, snapshot);

            // Refresh state after potions
            snapshot = GameStateReader.TryRead();
            if (snapshot == null) return;

            // Notify policy of turn start
            PolicyManager.Instance.CurrentPolicy.OnTurnStart(snapshot, _turnCount);

            // Play cards using current policy
            int cardsPlayed = 0;

            while (cardsPlayed < 20 && CombatManager.Instance.IsPlayPhase && CombatManager.Instance.IsInProgress)
            {
                // Check pause/manual mode via PolicyManager
                if (PolicyManager.Instance.Paused || PolicyManager.Instance.ManualMode)
                    break;

                // Refresh state
                snapshot = GameStateReader.TryRead();
                if (snapshot == null) break;

                // Get AI decision from PolicyManager
                var decision = PolicyManager.Instance.MakeDecision(snapshot);

                if (decision == null)
                {
                    Log.Info("[CombatHook] No decision available, ending turn");
                    break;
                }

                // Update debugger
                AIDebuggerRegistrar.Debugger?.Update(snapshot, decision, _turnCount);

                // Execute decision
                if (decision.Type == ActionType.PlayCard && decision.Card != null)
                {
                    var card = FindCardById(player, decision.Card.Id);
                    if (card != null && decision.Card.EnergyCost <= player.PlayerCombatState?.Energy)
                    {
                        // Get target
                        Creature? target = null;
                        if (decision.Target != null)
                        {
                            target = FindEnemyById(state, decision.Target.Id);
                        }

                        // Log action
                        Log.Info($"[CombatHook] [{PolicyManager.Instance.CurrentType}] " +
                                 $"Playing {decision.Card.Id} -> {decision.Target?.Id ?? "none"}");

                        // Play card
                        await CardCmd.AutoPlay(new BlockingPlayerChoiceContext(), card, target);

                        _playedThisTurn.Add(card);
                        cardsPlayed++;

                        // Wait for animation
                        await Task.Delay(100);
                    }
                    else
                    {
                        Log.Info($"[CombatHook] Could not play {decision.Card.Id}: card not found or insufficient energy");
                    }
                }
                else if (decision.Type == ActionType.EndTurn)
                {
                    Log.Info($"[CombatHook] [{PolicyManager.Instance.CurrentType}] Ending turn: {decision.Reason}");
                    break;
                }
                else
                {
                    Log.Info("[CombatHook] Unknown action type, ending turn");
                    break;
                }
            }

            // Notify policy of turn end
            snapshot = GameStateReader.TryRead();
            if (snapshot != null)
            {
                PolicyManager.Instance.CurrentPolicy.OnTurnEnd(snapshot, _turnCount);
            }

            // End turn
            if (CombatManager.Instance.IsPlayPhase && CombatManager.Instance.IsInProgress)
            {
                Log.Info("[CombatHook] Ending turn");
                PlayerCmd.EndTurn(player, canBackOut: false);
            }
        }
        catch (Exception ex)
        {
            Log.Info($"[CombatHook] PlayTurnAsync error: {ex}");
        }
        finally
        {
            _isRunning = false;
        }
    }

    private static async Task WaitForPlayPhase()
    {
        int waited = 0;
        while (!CombatManager.Instance.IsPlayPhase && CombatManager.Instance.IsInProgress && waited < 100)
        {
            await Task.Delay(100);
            waited++;
        }
    }

    private static async Task UsePotionsAsync(Player player, CombatSnapshot snapshot)
    {
        if (_decisionEngine == null) return;

        var combatState = player.Creature.CombatState;
        if (combatState == null) return;

        // Keep asking DecisionEngine until no more potions should be used
        while (CombatManager.Instance.IsPlayPhase && CombatManager.Instance.IsInProgress)
        {
            var potionDecision = _decisionEngine.ConsiderPotion(snapshot);
            if (potionDecision == null) break;

            var potionInfo = potionDecision.Potion!;
            var potion = player.Potions.FirstOrDefault(p => p.Id.Entry == potionInfo.Id);
            if (potion == null) break;

            Creature? target = potionInfo.TargetType switch
            {
                "AnyEnemy" => combatState.HittableEnemies.OrderBy(e => e.CurrentHp).FirstOrDefault(),
                "AnyAlly" or "AnyPlayer" or "Self" => player.Creature,
                _ => null
            };

            if (target == null && potion.TargetType.IsSingleTarget())
            {
                Log.Info($"[CombatHook] Skipping potion {potionInfo.Id}: no valid target");
                break;
            }

            Log.Info($"[CombatHook] Using potion: {potionInfo.Id} -> {target?.Monster?.Id.Entry ?? "none"} | {potionDecision.Reason}");
            potion.EnqueueManualUse(target);
            await Task.Delay(300);

            // Refresh snapshot to re-evaluate
            snapshot = GameStateReader.TryRead() ?? snapshot;
        }
    }

    private static CardModel? FindCardById(Player player, string cardId)
    {
        var hand = PileType.Hand.GetPile(player);
        return hand.Cards.FirstOrDefault(c => c.Id.Entry == cardId);
    }

    private static Creature? FindEnemyById(CombatState combatState, string enemyId)
    {
        return combatState.Enemies.FirstOrDefault(e =>
            e.Monster?.Id.Entry == enemyId && e.IsAlive);
    }
}