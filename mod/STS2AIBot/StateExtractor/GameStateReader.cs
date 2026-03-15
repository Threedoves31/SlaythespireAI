using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Logging;

namespace STS2AIBot.StateExtractor;

/// <summary>
/// Snapshot of a single enemy's state for AI consumption.
/// </summary>
public record EnemyInfo(
    string Id,
    int Hp,
    int MaxHp,
    int Block,
    string IntentType,   // "Attack", "Defend", "Buff", "Debuff", "Unknown", etc.
    int IntentDamage,    // 0 if not an attack intent
    int IntentHits,      // number of hits (multi-attack)
    List<PowerInfo> Powers
);

/// <summary>
/// Snapshot of a power/buff/debuff.
/// </summary>
public record PowerInfo(string Id, int Amount);

/// <summary>
/// Snapshot of a card in hand/draw/discard.
/// </summary>
public record CardInfo(
    string Id,
    int EnergyCost,
    string CardType,     // "Attack", "Skill", "Power", "Status", "Curse"
    string TargetType,   // "AnyEnemy", "AllEnemies", "Self", "None", etc.
    bool IsPlayable
);

/// <summary>
/// Full combat state snapshot — everything the AI needs to make a decision.
/// </summary>
public class CombatSnapshot
{
    // Player
    public int PlayerHp { get; set; }
    public int PlayerMaxHp { get; set; }
    public int PlayerBlock { get; set; }
    public int PlayerEnergy { get; set; }
    public int PlayerMaxEnergy { get; set; }
    public List<PowerInfo> PlayerPowers { get; set; } = new();

    // Cards
    public List<CardInfo> Hand { get; set; } = new();
    public int DrawPileCount { get; set; }
    public int DiscardPileCount { get; set; }
    public int ExhaustPileCount { get; set; }

    // Enemies
    public List<EnemyInfo> Enemies { get; set; } = new();

    // Run context
    public int TurnNumber { get; set; }
    public int FloorNumber { get; set; }
    public string CharacterId { get; set; } = "";

    public override string ToString()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"=== Combat State (Turn {TurnNumber}, Floor {FloorNumber}) ===");
        sb.AppendLine($"Player [{CharacterId}]: HP={PlayerHp}/{PlayerMaxHp} Block={PlayerBlock} Energy={PlayerEnergy}/{PlayerMaxEnergy}");
        if (PlayerPowers.Count > 0)
            sb.AppendLine($"  Powers: {string.Join(", ", PlayerPowers.Select(p => $"{p.Id}({p.Amount})"))}");
        sb.AppendLine($"Hand ({Hand.Count} cards): {string.Join(", ", Hand.Select(c => $"{c.Id}[{c.EnergyCost}]"))}");
        sb.AppendLine($"Draw={DrawPileCount} Discard={DiscardPileCount} Exhaust={ExhaustPileCount}");
        foreach (var e in Enemies)
        {
            sb.AppendLine($"Enemy [{e.Id}]: HP={e.Hp}/{e.MaxHp} Block={e.Block} Intent={e.IntentType}({e.IntentDamage}x{e.IntentHits})");
            if (e.Powers.Count > 0)
                sb.AppendLine($"  Powers: {string.Join(", ", e.Powers.Select(p => $"{p.Id}({p.Amount})"))}");
        }
        return sb.ToString();
    }
}

/// <summary>
/// Reads the current game state and produces a CombatSnapshot.
/// </summary>
public static class GameStateReader
{
    public static CombatSnapshot? TryRead()
    {
        try
        {
            if (!CombatManager.Instance.IsInProgress || !CombatManager.Instance.IsPlayPhase)
                return null;

            var runState = RunManager.Instance.DebugOnlyGetState();
            if (runState == null) return null;

            var player = LocalContext.GetMe(runState);
            if (player == null) return null;

            var combatState = CombatManager.Instance.DebugOnlyGetState();
            if (combatState == null) return null;

            var snapshot = new CombatSnapshot
            {
                PlayerHp        = player.Creature.CurrentHp,
                PlayerMaxHp     = player.Creature.MaxHp,
                PlayerBlock     = player.Creature.Block,
                PlayerEnergy    = player.PlayerCombatState?.Energy ?? 0,
                PlayerMaxEnergy = player.MaxEnergy,
                PlayerPowers    = ReadPowers(player.Creature),
                TurnNumber      = combatState.RoundNumber,
                FloorNumber     = runState.TotalFloor,
                CharacterId     = player.Character.Id.Entry,
                DrawPileCount    = PileType.Draw.GetPile(player).Cards.Count,
                DiscardPileCount = PileType.Discard.GetPile(player).Cards.Count,
                ExhaustPileCount = PileType.Exhaust.GetPile(player).Cards.Count,
                Hand    = ReadHand(player),
                Enemies = ReadEnemies(combatState),
            };

            return snapshot;
        }
        catch (Exception ex)
        {
            Log.Info($"[STS2AIBot] GameStateReader error: {ex.Message}");
            return null;
        }
    }

    private static List<CardInfo> ReadHand(Player player)
    {
        var hand = PileType.Hand.GetPile(player);
        return hand.Cards.Select(c =>
        {
            c.CanPlay(out var reason, out _);
            return new CardInfo(
                Id:         c.Id.Entry,
                EnergyCost: c.EnergyCost.GetAmountToSpend(),
                CardType:   c.Type.ToString(),
                TargetType: c.TargetType.ToString(),
                IsPlayable: reason == UnplayableReason.None
            );
        }).ToList();
    }

    private static List<EnemyInfo> ReadEnemies(CombatState combatState)
    {
        return combatState.Enemies
            .Where(e => e.IsAlive)
            .Select(e =>
            {
                var (intentType, intentDmg, intentHits) = ReadIntent(e);
                return new EnemyInfo(
                    Id:           e.Monster?.Id.Entry ?? "unknown",
                    Hp:           e.CurrentHp,
                    MaxHp:        e.MaxHp,
                    Block:        e.Block,
                    IntentType:   intentType,
                    IntentDamage: intentDmg,
                    IntentHits:   intentHits,
                    Powers:       ReadPowers(e)
                );
            }).ToList();
    }

    private static (string type, int dmg, int hits) ReadIntent(Creature enemy)
    {
        if (enemy.Monster?.NextMove == null)
            return ("Unknown", 0, 1);

        var intents = enemy.Monster.NextMove.Intents;
        if (intents == null || intents.Count == 0)
            return ("Unknown", 0, 1);

        // Find the primary intent
        var first = intents[0];
        string typeName = first.GetType().Name
            .Replace("Intent", "")
            .Replace("Single", "")
            .Replace("Multi", "");

        int dmg = 0, hits = 1;

        // Try to read damage from SingleAttackIntent or MultiAttackIntent via reflection
        try
        {
            var dmgProp = first.GetType().GetProperty("Damage")
                       ?? first.GetType().GetProperty("DamageAmount");
            if (dmgProp != null)
                dmg = Convert.ToInt32(dmgProp.GetValue(first));

            var hitsProp = first.GetType().GetProperty("Hits")
                        ?? first.GetType().GetProperty("HitCount");
            if (hitsProp != null)
                hits = Convert.ToInt32(hitsProp.GetValue(first));
        }
        catch { /* ignore reflection errors */ }

        return (typeName, dmg, hits);
    }

    private static List<PowerInfo> ReadPowers(Creature creature)
    {
        return creature.Powers
            .Select(p => new PowerInfo(p.Id.Entry, p.Amount))
            .ToList();
    }
}
