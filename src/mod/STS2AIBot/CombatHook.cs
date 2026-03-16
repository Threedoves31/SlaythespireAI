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
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using STS2AIBot.StateExtractor;
using STS2AIBot.Communication;
using STS2AIBot.AI;
using STS2AIBot.UI;

namespace STS2AIBot;

/// <summary>
/// Enhanced combat hook with improved AI decision engine and debug controls.
/// Supports multiple strategies, manual rating, and in-game controls.
/// </summary>
public static class CombatHook
{
    private static bool _isRunning = false;
    private static GameEnvironment? _gameEnv;
    private static DecisionEngine? _decisionEngine;
    private static DebugWindow? _debugWindow;
    private static AIController? _aiController;
    private static DecisionEngine.DecisionStrategy _strategy = DecisionEngine.DecisionStrategy.SimpleHeuristic;

    private static int _turnCount = 0;
    private static List<CardModel> _playedThisTurn = new();
    private static int _previousPlayerHp = 0;

    public static void Register()
    {
        // Initialize components
        _debugWindow = new DebugWindow();
        _decisionEngine = new DecisionEngine();
        _aiController = new AIController(_debugWindow, _decisionEngine);

        // Register combat hook
        CombatManager.Instance.TurnStarted += OnTurnStarted;

        // Initialize controller (for manual override)
        _aiController.Initialize();

        Log.Info("[CombatHook] Enhanced combat hook registered");
        Log.Info("[CombatHook] Press F2 in-game to cycle strategies");
        Log.Info("[CombatHook] Press F3 to toggle pause");
        Log.Info("[CombatHook] Press F4 to toggle manual mode");
    }

    public static void Register(GameEnvironment gameEnv)
    {
        _gameEnv = gameEnv;
        CombatManager.Instance.TurnStarted += OnTurnStarted;
        Log.Info("[CombatHook] CombatHook registered (training mode)");
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

        // Check if paused
        if (_debugWindow != null && _debugWindow.Paused)
        {
            Log.Info("[CombatHook] AI paused, waiting for manual input");
            return;
        }

        // Manual mode: don't auto-play
        if (_debugWindow != null && _debugWindow.ManualMode)
        {
            Log.Info("[CombatHook] Manual mode active, no auto-play");
            return;
        }

        // AI mode
        if (_isRunning) return;
        _isRunning = true;

        // Fire-and-forget async task — same pattern as AutoSlayer
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

            // Update debug window with initial state
            _debugWindow?.Update(snapshot, null, _turnCount);

            // Use potions before playing cards
            await UsePotionsAsync(player, snapshot);

            // Refresh state after potions
            snapshot = GameStateReader.TryRead();
            if (snapshot == null) return;

            // Play cards until decision says to end
            int cardsPlayed = 0;
            var attempted = new HashSet<string>();

            while (cardsPlayed < 20 && CombatManager.Instance.IsPlayPhase && CombatManager.Instance.IsInProgress)
            {
                if (_debugWindow != null && _debugWindow.Paused) break;

                // Get AI decision
                var decision = _decisionEngine?.MakeDecision(snapshot);

                if (decision == null)
                {
                    Log.Info("[CombatHook] No decision available, ending turn");
                    break;
                }

                // Execute decision
                if (decision.Type == DecisionEngine.ActionType.PlayCard &&
                    decision.Card != null)
                {
                    var card = FindCardById(player, decision.Card.Id);
                    if (card != null && decision.Card.EnergyCost <= player.PlayerCombatState?.Energy)
                    {
                        attempted.Add(decision.Card.Id + card.GetHashCode());

                        // Get target
                        Creature? target = null;
                        if (decision.Target != null)
                        {
                            target = FindEnemyById(state, decision.Target.Id);
                        }

                        // Log action
                        Log.Info($"[CombatHook] Playing {decision.Card.Id} -> {decision.Target?.Id ?? "none"}");
                        Log.Info($"[CombatHook] Reason: {decision.Reason}");

                        // Play card
                        await CardCmd.AutoPlay(new BlockingPlayerChoiceContext(), card, target);

                        _playedThisTurn.Add(card);
                        cardsPlayed++;

                        // Wait for animation
                        await Task.Delay(100);

                        // Refresh state
                        snapshot = GameStateReader.TryRead();
                        if (snapshot == null) break;
                    }
                }
                else if (decision.Type == DecisionEngine.ActionType.EndTurn)
                {
                    Log.Info("[CombatHook] Ending turn");
                    break;
                }
                else
                {
                    Log.Info("[CombatHook] Unknown action type, ending turn");
                    break;
                }
            }

            // Record turn outcome
            var finalSnapshot = GameStateReader.TryRead();
            if (finalSnapshot != null)
            {
                bool turnSuccessful = finalSnapshot.PlayerHp > _previousPlayerHp ||
                                    !finalSnapshot.Enemies.Any(e => e.Hp > 0);
                _decisionEngine?.RecordDecision(
                    new DecisionEngine.Decision(DecisionEngine.ActionType.EndTurn, null, null, 0f, "Turn ended"),
                    finalSnapshot,
                    turnSuccessful
                );
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
