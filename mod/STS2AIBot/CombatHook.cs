using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using STS2AIBot.StateExtractor;
using STS2AIBot.Communication;

namespace STS2AIBot;

/// <summary>
/// Hooks into CombatManager.TurnStarted to intercept each player turn
/// and drive the AI decision loop.
/// </summary>
public static class CombatHook
{
    private static bool _isRunning = false;
    private static GameEnvironment? _gameEnv;

    public static void Register()
    {
        CombatManager.Instance.TurnStarted += OnTurnStarted;
        Log.Info("[STS2AIBot] CombatHook registered (heuristic mode)");
    }

    public static void Register(GameEnvironment gameEnv)
    {
        _gameEnv = gameEnv;
        CombatManager.Instance.TurnStarted += OnTurnStarted;
        Log.Info("[STS2AIBot] CombatHook registered (training mode)");
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

        // Heuristic mode (original code)
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

            // Log the current state
            var snapshot = GameStateReader.TryRead();
            if (snapshot != null)
                Log.Info("[STS2AIBot]\n" + snapshot.ToString());

            // Play cards until no more playable cards or energy runs out
            int cardsPlayed = 0;
            var attempted = new HashSet<string>();

            while (cardsPlayed < 50 && CombatManager.Instance.IsPlayPhase && CombatManager.Instance.IsInProgress)
            {
                var hand = PileType.Hand.GetPile(player);
                var playable = hand.Cards
                    .Where(c =>
                    {
                        c.CanPlay(out var reason, out _);
                        return reason == UnplayableReason.None && !attempted.Contains(c.Id.Entry + c.GetHashCode());
                    })
                    .ToList();

                if (playable.Count == 0)
                {
                    Log.Info("[STS2AIBot] No playable cards, ending turn");
                    break;
                }

                // Simple heuristic: prefer attacks, then skills, then powers
                var card = PickCard(playable, state);
                var target = PickTarget(card, state);

                attempted.Add(card.Id.Entry + card.GetHashCode());
                Log.Info($"[STS2AIBot] Playing {card.Id.Entry} -> target: {target?.Monster?.Id.Entry ?? "none"}");

                await CardCmd.AutoPlay(new BlockingPlayerChoiceContext(), card, target);
                cardsPlayed++;
                await Task.Delay(50);
            }

            // End turn
            if (CombatManager.Instance.IsPlayPhase && CombatManager.Instance.IsInProgress)
            {
                Log.Info("[STS2AIBot] Ending turn");
                PlayerCmd.EndTurn(player, canBackOut: false);
            }
        }
        catch (Exception ex)
        {
            Log.Info($"[STS2AIBot] PlayTurnAsync error: {ex}");
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

    /// <summary>
    /// Simple card priority: Attacks > Skills > Powers.
    /// Within each type, prefer lower energy cost first (greedy).
    /// </summary>
    private static CardModel PickCard(List<CardModel> playable, CombatState state)
    {
        // Prioritize attacks when enemies are alive
        bool hasLivingEnemies = state.HittableEnemies.Any();

        if (hasLivingEnemies)
        {
            var attacks = playable.Where(c => c.Type == CardType.Attack).ToList();
            if (attacks.Count > 0)
                return attacks.OrderBy(c => c.EnergyCost.GetAmountToSpend()).First();
        }

        var skills = playable.Where(c => c.Type == CardType.Skill).ToList();
        if (skills.Count > 0)
            return skills.OrderBy(c => c.EnergyCost.GetAmountToSpend()).First();

        return playable.OrderBy(c => c.EnergyCost.GetAmountToSpend()).First();
    }

    /// <summary>
    /// Pick the lowest-HP hittable enemy as target (focus fire).
    /// </summary>
    private static Creature? PickTarget(CardModel card, CombatState state)
    {
        if (card.TargetType != TargetType.AnyEnemy)
            return null;

        return state.HittableEnemies
            .OrderBy(e => e.CurrentHp)
            .FirstOrDefault();
    }
}
