using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Logging;
using STS2AIBot.StateExtractor;

namespace STS2AIBot.Communication;

/// <summary>
/// JSON serializable game state for communication with Python.
/// </summary>
public record GameState(
    // Player state
    int player_hp,
    int player_max_hp,
    int player_block,
    int player_energy,
    int player_max_energy,
    int turn_number,
    int floor_number,

    // Cards
    List<CardData> hand,
    int draw_pile_count,
    int discard_pile_count,
    int exhaust_pile_count,

    // Enemies
    List<EnemyData> enemies,

    // Metadata
    bool is_done,
    bool is_player_turn,
    float reward,

    // Result (if done)
    bool? won,
    int? turns,
    int? hp_remaining
);

public record CardData(
    string id,
    int cost,
    string type,      // "Attack", "Skill", "Power"
    string target,    // "AnyEnemy", "AllEnemies", "Self", "None"
    bool is_playable,
    int hand_index
);

public record EnemyData(
    string id,
    string name,
    int hp,
    int max_hp,
    int block,
    bool is_alive,
    string intent_type,   // "Attack", "Defend", "Buff", "Debuff", "Unknown"
    int intent_damage,
    int intent_hits,
    List<PowerData> powers,
    int enemy_index
);

public record PowerData(
    string id,
    int amount
);

/// <summary>
/// Manages the game environment for RL training.
/// Handles state serialization, action execution, and reward calculation.
/// </summary>
public class GameEnvironment
{
    private CombatManager CombatManager => MegaCrit.Sts2.Core.Combat.CombatManager.Instance;
    private RunManager RunManager => MegaCrit.Sts2.Core.Runs.RunManager.Instance;

    // Episode tracking
    private int _currentEpisode = 0;
    private int _turnsInCurrentEpisode = 0;
    private float _episodeReward = 0;
    private int _playerStartHp = 0;
    private int _prevEnemyHpSum = 0;

    // State
    private bool _isRunning = false;
    private bool _waitingForAction = false;
    private bool _episodeDone = false;
    private bool _episodeWon = false;

    // Constants
    private const int MAX_HAND_SIZE = 10;
    private const int MAX_ENEMIES = 5;
    private const int MAX_POWERS = 10;

    // Rewards
    private const float REWARD_WIN = 100.0f;
    private const float REWARD_LOSS = -100.0f;
    private const float REWARD_DAMAGE_DEALT = 0.1f;
    private const float REWARD_DAMAGE_TAKEN = -0.2f;
    private const float REWARD_KILL_ENEMY = 5.0f;
    private const float REWARD_TURN_STEP = -0.01f;

    public bool IsRunning => _isRunning;
    public bool WaitingForAction => _waitingForAction;
    public bool EpisodeDone => _episodeDone;

    /// <summary>
    /// Initialize environment. Should be called when entering combat.
    /// </summary>
    public void Initialize()
    {
        Log.Info("[GameEnv] Initializing environment...");
        _currentEpisode++;
        _turnsInCurrentEpisode = 0;
        _episodeReward = 0;
        _episodeDone = false;
        _episodeWon = false;

        var runState = RunManager.Instance.DebugOnlyGetState();
        if (runState != null)
        {
            var player = LocalContext.GetMe(runState);
            _playerStartHp = player?.Creature.CurrentHp ?? 80;
        }

        _isRunning = true;
        _waitingForAction = false;
        Log.Info($"[GameEnv] Environment initialized (Episode {_currentEpisode})");
    }

    /// <summary>
    /// Wait for player turn and signal readiness.
    /// </summary>
    public void OnTurnStarted()
    {
        if (!_isRunning || _episodeDone)
            return;

        _turnsInCurrentEpisode++;

        var state = CombatManager.Instance.DebugOnlyGetState();
        if (state?.CurrentSide == CombatSide.Player)
        {
            _waitingForAction = true;
            Log.Info($"[GameEnv] Player turn started (Turn {_turnsInCurrentEpisode})");
        }
    }

    /// <summary>
    /// Get current game state as JSON.
    /// </summary>
    public string GetState()
    {
        try
        {
            var snapshot = GameStateReader.TryRead();
            if (snapshot == null)
            {
                return JsonSerializer.Serialize(new GameState(
                    0, 0, 0, 0, 3, 0, 0,
                    new List<CardData>(), 0, 0, 0,
                    new List<EnemyData>(),
                    true, false, 0, null, null, null
                ));
            }

            var state = CombatManager.Instance.DebugOnlyGetState();
            if (state == null)
                return CreateErrorState();

            // Calculate current reward
            var enemyHpSum = state.Enemies.Where(e => e.IsAlive).Sum(e => e.CurrentHp);
            var reward = CalculateReward(enemyHpSum);

            _prevEnemyHpSum = enemyHpSum;

            // Check if episode is done
            var isDone = CheckEpisodeDone(state, out bool won);
            if (isDone)
            {
                _episodeDone = true;
                _episodeWon = won;
                _isRunning = false;
            }

            return JsonSerializer.Serialize(new GameState(
                snapshot.PlayerHp,
                snapshot.PlayerMaxHp,
                snapshot.PlayerBlock,
                snapshot.PlayerEnergy,
                snapshot.PlayerMaxEnergy,
                snapshot.TurnNumber,
                snapshot.FloorNumber,
                SerializeHand(snapshot.Hand),
                snapshot.DrawPileCount,
                snapshot.DiscardPileCount,
                snapshot.ExhaustPileCount,
                SerializeEnemies(snapshot.Enemies, state.Enemies.ToList()),
                isDone,
                _waitingForAction,
                isDone ? (won ? REWARD_WIN : REWARD_LOSS) : reward,
                isDone ? won : null,
                isDone ? _turnsInCurrentEpisode : null,
                isDone ? snapshot.PlayerHp : null
            ));
        }
        catch (Exception ex)
        {
            Log.Info($"[GameEnv] GetState error: {ex.Message}");
            return CreateErrorState();
        }
    }

    /// <summary>
    /// Get action mask (which actions are valid).
    /// Returns a comma-separated string of valid action indices.
    /// </summary>
    public string GetActionMask()
    {
        try
        {
            var snapshot = GameStateReader.TryRead();
            if (snapshot == null || !_waitingForAction)
                return "";

            var runState = RunManager.Instance.DebugOnlyGetState();
            if (runState == null) return "";

            var player = LocalContext.GetMe(runState);
            if (player == null) return "";

            var hand = PileType.Hand.GetPile(player);
            var combatState = CombatManager.Instance.DebugOnlyGetState();
            if (combatState == null) return "";

            var aliveEnemies = combatState.Enemies.Where(e => e.IsAlive).ToList();
            var validActions = new List<int>();

            // Action format: hand_idx * MAX_ENEMIES + enemy_idx
            // END_TURN_ACTION = MAX_HAND_SIZE * MAX_ENEMIES = 50

            for (int handIdx = 0; handIdx < Math.Min(hand.Cards.Count, MAX_HAND_SIZE); handIdx++)
            {
                var card = hand.Cards[handIdx];
                card.CanPlay(out var reason, out _);

                if (reason != UnplayableReason.None)
                    continue;

                if (card.TargetType == TargetType.AnyEnemy)
                {
                    for (int enemyIdx = 0; enemyIdx < aliveEnemies.Count; enemyIdx++)
                    {
                        validActions.Add(handIdx * MAX_ENEMIES + enemyIdx);
                    }
                }
                else
                {
                    // Self-targeting or AoE: use enemy_idx=0
                    validActions.Add(handIdx * MAX_ENEMIES + 0);
                }
            }

            // Always allow end turn
            validActions.Add(50); // END_TURN_ACTION

            return string.Join(",", validActions);
        }
        catch (Exception ex)
        {
            Log.Info($"[GameEnv] GetActionMask error: {ex.Message}");
            return "50"; // Only end turn on error
        }
    }

    /// <summary>
    /// Execute an action in the game.
    /// </summary>
    public void Step(int action)
    {
        if (!_waitingForAction)
        {
            Log.Info($"[GameEnv] Step called when not waiting for action: {action}");
            return;
        }

        Log.Info($"[GameEnv] Executing action: {action}");

        try
        {
            const int END_TURN_ACTION = 50;

            if (action == END_TURN_ACTION)
            {
                // End turn
                var runState = RunManager.Instance.DebugOnlyGetState();
                if (runState != null)
                {
                    var player = LocalContext.GetMe(runState);
                    if (player != null)
                    {
                        PlayerCmd.EndTurn(player, canBackOut: false);
                    }
                }
            }
            else
            {
                // Play card
                int handIdx = action / MAX_ENEMIES;
                int enemyIdx = action % MAX_ENEMIES;

                var runState = RunManager.Instance.DebugOnlyGetState();
                if (runState == null) return;

                var player = LocalContext.GetMe(runState);
                if (player == null) return;

                var hand = PileType.Hand.GetPile(player);
                if (handIdx >= hand.Cards.Count) return;

                var card = hand.Cards[handIdx];
                var combatState = CombatManager.Instance.DebugOnlyGetState();
                if (combatState == null) return;

                var aliveEnemies = combatState.Enemies.Where(e => e.IsAlive).ToList();
                Creature? target = null;

                if (card.TargetType == TargetType.AnyEnemy)
                {
                    if (enemyIdx < aliveEnemies.Count)
                    {
                        target = aliveEnemies[enemyIdx];
                    }
                }

                // Play the card
                Task.Run(async () =>
                {
                    try
                    {
                        await CardCmd.AutoPlay(new BlockingPlayerChoiceContext(), card, target);
                        Log.Info($"[GameEnv] Played {card.Id.Entry}");
                        await Task.Delay(100); // Small delay for game to process
                    }
                    catch (Exception ex)
                    {
                        Log.Info($"[GameEnv] Play card error: {ex.Message}");
                    }
                });
            }

            _waitingForAction = false;
        }
        catch (Exception ex)
        {
            Log.Info($"[GameEnv] Step error: {ex.Message}");
        }
    }

    private float CalculateReward(int currentEnemyHpSum)
    {
        // Dense reward shaping
        float reward = REWARD_TURN_STEP;

        // Damage dealt
        int damageDealt = _prevEnemyHpSum - currentEnemyHpSum;
        if (damageDealt > 0)
        {
            reward += damageDealt * REWARD_DAMAGE_DEALT;
        }

        // Check for kills
        var combatState = CombatManager.Instance.DebugOnlyGetState();
        if (combatState != null)
        {
            var aliveCount = combatState.Enemies.Count(e => e.IsAlive);
            var totalCount = combatState.Enemies.Count;
            if (totalCount > aliveCount)
            {
                reward += (totalCount - aliveCount) * REWARD_KILL_ENEMY;
            }
        }

        _episodeReward += reward;
        return reward;
    }

    private bool CheckEpisodeDone(CombatState combatState, out bool won)
    {
        won = false;

        if (!CombatManager.Instance.IsInProgress)
        {
            // Combat ended
            var runState = RunManager.Instance.DebugOnlyGetState();
            if (runState != null)
            {
                var playerEntity = LocalContext.GetMe(runState);
                if (playerEntity != null)
                {
                    won = playerEntity.Creature.CurrentHp > 0;
                }
            }
            return true;
        }

        if (combatState.Enemies.All(e => !e.IsAlive))
        {
            won = true;
            return true;
        }

        // Check player HP from snapshot
        var snapshot = GameStateReader.TryRead();
        if (snapshot != null && snapshot.PlayerHp <= 0)
        {
            won = false;
            return true;
        }

        return false;
    }

    private List<CardData> SerializeHand(List<CardInfo> hand)
    {
        var result = new List<CardData>();
        for (int i = 0; i < MAX_HAND_SIZE; i++)
        {
            if (i < hand.Count)
            {
                var card = hand[i];
                result.Add(new CardData(
                    card.Id,
                    card.EnergyCost,
                    card.CardType,
                    card.TargetType,
                    card.IsPlayable,
                    i
                ));
            }
        }
        return result;
    }

    private List<EnemyData> SerializeEnemies(List<EnemyInfo> enemies, List<MegaCrit.Sts2.Core.Entities.Creatures.Creature> combatEnemies)
    {
        var result = new List<EnemyData>();
        for (int i = 0; i < MAX_ENEMIES; i++)
        {
            if (i < enemies.Count && i < combatEnemies.Count)
            {
                var info = enemies[i];
                var powers = info.Powers.Take(MAX_POWERS)
                    .Select(p => new PowerData(p.Id, p.Amount))
                    .ToList();

                result.Add(new EnemyData(
                    info.Id,
                    info.Id,
                    info.Hp,
                    info.MaxHp,
                    info.Block,
                    info.Hp > 0,
                    info.IntentType,
                    info.IntentDamage,
                    info.IntentHits,
                    powers,
                    i
                ));
            }
            else
            {
                // Empty enemy slot
                result.Add(new EnemyData(
                    "", "", 0, 0, 0, false, "Unknown", 0, 0, new List<PowerData>(), i
                ));
            }
        }
        return result;
    }

    private string CreateErrorState()
    {
        return JsonSerializer.Serialize(new GameState(
            0, 0, 0, 0, 3, 0, 0,
            new List<CardData>(), 0, 0, 0,
            new List<EnemyData>(),
            true, false, 0, false, 0, 0
        ));
    }
}
