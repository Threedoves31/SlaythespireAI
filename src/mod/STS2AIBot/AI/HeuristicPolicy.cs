// Heuristic-based combat policy for Slay the Spire 2.
// Translated from Python version in src/training/baselines/heuristic_policy.py

using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Logging;
using STS2AIBot.StateExtractor;

namespace STS2AIBot.AI;

/// <summary>
/// Heuristic-based combat policy with rule prioritization.
/// 
/// Rules (priority order):
/// 1. One-shot kill enemies if possible
/// 2. Defense only when incoming damage > 4 (tolerate small damage)
/// 3. Attack vulnerable enemies first
/// 4. Apply Vulnerable (Bash) to high-HP enemies
/// 5. Play Power cards when enemy not attacking or have spare energy
/// 6. Draw cards when have spare energy
/// 7. Play 0-cost cards
/// 8. Regular attacks when enemy defending/buffing
/// 9. End turn when energy insufficient
///
/// Special rules:
/// - Don't use HP-for-benefit cards (Hemokinesis, Offering) when HP <= 20
/// </summary>
public class HeuristicPolicy : IPolicy
{
    public string Name => "Heuristic";
    public string Description => "Rule-based heuristic policy with priority queue";

    // Configuration
    private const int DAMAGE_TOLERANCE = 4;      // Tolerate up to 4 damage before defending
    private const int LOW_HP_THRESHOLD = 20;     // Avoid HP-cost cards below this

    // Card knowledge sets - HP cost cards categorized by benefit type
    private static readonly HashSet<string> HpCostCards = new()
    {
        "Hemokinesis", "Offering", "Bloodletting", "Rupture"
    };

    // HP-for-energy cards (gain energy)
    private static readonly HashSet<string> HpForEnergyCards = new()
    {
        "Offering", "Bloodletting"
    };

    // HP-for-draw cards (draw cards)
    private static readonly HashSet<string> HpForDrawCards = new()
    {
        "Offering"  // Offering gives both energy AND draw
    };

    // HP-for-damage cards (high damage attack)
    private static readonly HashSet<string> HpForDamageCards = new()
    {
        "Hemokinesis"  // 15 damage for 2 HP
    };

    // Card benefit data: (hpCost, energyGain, drawAmount, damageBonus)
    private static readonly Dictionary<string, (int hpCost, int energyGain, int drawCards, int bonusDamage)> HpCostCardData = new()
    {
        { "Offering", (6, 2, 3, 0) },       // Lose 6 HP, gain 2 energy, draw 3 cards
        { "Bloodletting", (3, 2, 0, 0) },   // Lose 3 HP, gain 2 energy
        { "Hemokinesis", (2, 0, 0, 15) },   // Lose 2 HP, deal 15 damage (instead of normal)
        { "Rupture", (1, 0, 0, 0) }         // Lose 1 HP when attacked (passive power)
    };

    private static readonly HashSet<string> VulnerableCards = new()
    {
        "Bash", "Clothesline"
    };

    private static readonly HashSet<string> StrengthPowerCards = new()
    {
        "Inflame", "SpotWeakness", "DemonForm", "LimitBreak"
    };

    private static readonly HashSet<string> DrawCards = new()
    {
        "BattleTrance", "ShrugItOff", "BurningPact", "Warcry", "ThinkingAhead"
    };

    private bool _debugMode = true;
    private int _turnCount = 0;

    public HeuristicPolicy(bool debugMode = true)
    {
        _debugMode = debugMode;
    }

    public PolicyDecision MakeDecision(CombatSnapshot state)
    {
        if (state == null)
            return EndTurn("No state available");

        _turnCount++;

        var playable = GetPlayableCards(state);
        if (!playable.Any())
            return EndTurn("No playable cards");

        var aliveEnemies = state.Enemies.Where(e => e.Hp > 0).ToList();
        if (!aliveEnemies.Any())
            return EndTurn("No alive enemies");

        int incomingDamage = CalculateIncomingDamage(aliveEnemies);
        int needBlock = Math.Max(0, incomingDamage - state.PlayerBlock);
        bool lowHp = state.PlayerHp <= LOW_HP_THRESHOLD;

        if (_debugMode)
        {
            Log.Info($"[Heuristic] Turn {_turnCount}: HP={state.PlayerHp}/{state.PlayerMaxHp}, " +
                     $"Energy={state.PlayerEnergy}, Block={state.PlayerBlock}, " +
                     $"Incoming={incomingDamage}, Need={needBlock}");
        }

        // === Priority 0: One-shot kill ===
        var killDecision = TryOneShotKill(playable, aliveEnemies, state);
        if (killDecision != null) return killDecision;

        // === Priority 1: Defense (only if significant damage incoming) ===
        if (incomingDamage > DAMAGE_TOLERANCE && needBlock > 0)
        {
            var defendDecision = TryDefend(playable, needBlock, state, lowHp);
            if (defendDecision != null) return defendDecision;
        }

        // === Priority 2: Attack vulnerable enemies ===
        var vulnDecision = TryAttackVulnerable(playable, aliveEnemies, state, lowHp);
        if (vulnDecision != null) return vulnDecision;

        // === Priority 3: Apply Vulnerable to high-HP enemies ===
        var bashDecision = TryApplyVulnerable(playable, aliveEnemies, state, lowHp);
        if (bashDecision != null) return bashDecision;

        // === Priority 4: Play Power cards ===
        var powerDecision = TryPlayPower(playable, aliveEnemies, state, lowHp, incomingDamage);
        if (powerDecision != null) return powerDecision;

        // === Priority 5: Draw cards ===
        var drawDecision = TryDrawCards(playable, state, lowHp);
        if (drawDecision != null) return drawDecision;

        // === Priority 6: Play 0-cost cards ===
        var freeDecision = TryPlayZeroCost(playable, aliveEnemies, state, lowHp);
        if (freeDecision != null) return freeDecision;

        // === Priority 7: Regular attacks (enemy not attacking) ===
        if (incomingDamage <= DAMAGE_TOLERANCE)
        {
            var attackDecision = TryAttack(playable, aliveEnemies, state, lowHp);
            if (attackDecision != null) return attackDecision;
        }

        // === Priority 8: Consider HP-cost cards (smart decision) ===
        var hpCostDecision = TryHpCostCards(playable, aliveEnemies, state, incomingDamage);
        if (hpCostDecision != null) return hpCostDecision;

        // === Priority 9: Any remaining useful card ===
        var anyDecision = TryAnyUsefulCard(playable, aliveEnemies, state, lowHp);
        if (anyDecision != null) return anyDecision;

        return EndTurn("No beneficial action");
    }

    public void OnTurnStart(CombatSnapshot state, int turnNumber)
    {
        _turnCount = turnNumber;
    }

    #region Priority Methods

    private PolicyDecision? TryOneShotKill(List<CardInfo> playable, List<EnemyInfo> enemies, CombatSnapshot state)
    {
        var attacks = playable.Where(c => c.CardType == "Attack").ToList();
        int strength = GetStrength(state);

        foreach (var card in attacks)
        {
            int dmg = EstimateDamage(card, strength);
            foreach (var enemy in enemies)
            {
                if (dmg >= enemy.Hp)
                {
                    return PlayCard(card, enemy, 100f,
                        $"One-shot kill {enemy.Id} ({dmg} dmg vs {enemy.Hp} HP)");
                }
            }
        }
        return null;
    }

    private PolicyDecision? TryDefend(List<CardInfo> playable, int needBlock, CombatSnapshot state, bool lowHp)
    {
        var filtered = lowHp ? FilterOutHpCostCards(playable) : playable;

        var defendCards = new List<(CardInfo, int)>();
        foreach (var c in filtered)
        {
            int block = EstimateBlock(c);
            if (block > 0)
                defendCards.Add((c, block));
        }

        if (!defendCards.Any()) return null;

        // Sort by block value (best first)
        defendCards.Sort((a, b) => b.Item2.CompareTo(a.Item2));

        var (bestCard, bestBlock) = defendCards[0];

        // Only defend if it provides meaningful block (at least 50% of need)
        if (bestBlock >= needBlock * 0.5)
        {
            return PlayCard(bestCard, null, 50f,
                $"Defend: +{bestBlock} block (need {needBlock})");
        }

        return null;
    }

    private PolicyDecision? TryAttackVulnerable(List<CardInfo> playable, List<EnemyInfo> enemies,
                                           CombatSnapshot state, bool lowHp)
    {
        var filtered = lowHp ? FilterOutHpCostCards(playable) : playable;
        var attacks = filtered.Where(c => c.CardType == "Attack").ToList();

        if (!attacks.Any()) return null;

        // Find vulnerable enemies
        var vulnEnemies = enemies.Where(e =>
            e.Powers.Any(p => p.Id.Contains("Vulnerable"))).ToList();

        if (!vulnEnemies.Any()) return null;

        int strength = GetStrength(state);
        var bestAttack = attacks.OrderByDescending(c => EstimateDamage(c, strength)).First();
        int dmg = EstimateDamage(bestAttack, strength);

        // Target weakest vulnerable enemy
        var target = vulnEnemies.OrderBy(e => e.Hp).First();

        return PlayCard(bestAttack, target, 40f,
            $"Attack vulnerable {target.Id} ({dmg} dmg x1.5)");
    }

    private PolicyDecision? TryApplyVulnerable(List<CardInfo> playable, List<EnemyInfo> enemies,
                                          CombatSnapshot state, bool lowHp)
    {
        var filtered = lowHp ? FilterOutHpCostCards(playable) : playable;

        var vulnCards = filtered.Where(c =>
        {
            string baseId = c.Id.Replace("+", "");
            return VulnerableCards.Contains(baseId);
        }).ToList();

        if (!vulnCards.Any()) return null;

        // Find enemies without vulnerable
        var nonVulnEnemies = enemies.Where(e =>
            !e.Powers.Any(p => p.Id.Contains("Vulnerable"))).ToList();

        if (!nonVulnEnemies.Any()) return null;

        // Target highest HP enemy (most value from vulnerable)
        var target = nonVulnEnemies.OrderByDescending(e => e.Hp).First();
        var card = vulnCards.First();

        return PlayCard(card, target, 35f,
            $"Apply Vulnerable to {target.Id} (HP: {target.Hp})");
    }

    private PolicyDecision? TryPlayPower(List<CardInfo> playable, List<EnemyInfo> enemies,
                                    CombatSnapshot state, bool lowHp, int incomingDamage)
    {
        var filtered = lowHp ? FilterOutHpCostCards(playable) : playable;
        var powerCards = filtered.Where(c => c.CardType == "Power").ToList();

        if (!powerCards.Any()) return null;

        foreach (var card in powerCards)
        {
            int energyAfter = state.PlayerEnergy - card.EnergyCost;

            // Check if already have this power
            string baseId = card.Id.Replace("+", "");
            bool alreadyHave = state.PlayerPowers.Any(p => p.Id == baseId);

            if (alreadyHave && baseId != "LimitBreak")
                continue;

            // Only play powers if:
            // 1. Enemy is not attacking (safe turn), OR
            // 2. Have spare energy after playing (>= 1)
            if (incomingDamage <= DAMAGE_TOLERANCE || energyAfter >= 1)
            {
                return PlayCard(card, null, 30f, $"Play Power: {card.Id}");
            }
        }

        return null;
    }

    private PolicyDecision? TryDrawCards(List<CardInfo> playable, CombatSnapshot state, bool lowHp)
    {
        var filtered = lowHp ? FilterOutHpCostCards(playable) : playable;

        var drawCards = filtered.Where(c =>
        {
            string baseId = c.Id.Replace("+", "");
            return DrawCards.Contains(baseId);
        }).ToList();

        if (!drawCards.Any()) return null;

        // Only draw if have spare energy after playing
        foreach (var card in drawCards)
        {
            int energyAfter = state.PlayerEnergy - card.EnergyCost;
            if (energyAfter >= 1)
            {
                return PlayCard(card, null, 25f, $"Draw cards: {card.Id}");
            }
        }

        return null;
    }

    private PolicyDecision? TryPlayZeroCost(List<CardInfo> playable, List<EnemyInfo> enemies,
                                       CombatSnapshot state, bool lowHp)
    {
        var filtered = lowHp ? FilterOutHpCostCards(playable) : playable;
        var zeroCost = filtered.Where(c => c.EnergyCost == 0).ToList();

        if (!zeroCost.Any()) return null;

        // Prioritize attacks if enemies alive
        foreach (var card in zeroCost)
        {
            if (card.CardType == "Attack")
            {
                var target = enemies.OrderBy(e => e.Hp).FirstOrDefault();
                return PlayCard(card, target, 20f, $"Free attack: {card.Id}");
            }
        }

        // Then skills/powers
        var firstCard = zeroCost.First();
        return PlayCard(firstCard, null, 15f, $"Free card: {firstCard.Id}");
    }

    private PolicyDecision? TryAttack(List<CardInfo> playable, List<EnemyInfo> enemies,
                                 CombatSnapshot state, bool lowHp)
    {
        var filtered = lowHp ? FilterOutHpCostCards(playable) : playable;
        var attacks = filtered.Where(c => c.CardType == "Attack").ToList();

        if (!attacks.Any()) return null;

        int strength = GetStrength(state);
        var best = attacks.OrderByDescending(c => EstimateDamage(c, strength)).First();
        int dmg = EstimateDamage(best, strength);

        // Target weakest enemy
        var target = enemies.OrderBy(e => e.Hp).First();

        return PlayCard(best, target, 10f, $"Attack {target.Id}: {dmg} damage");
    }

    private PolicyDecision? TryAnyUsefulCard(List<CardInfo> playable, List<EnemyInfo> enemies,
                                        CombatSnapshot state, bool lowHp)
    {
        var filtered = lowHp ? FilterOutHpCostCards(playable) : playable;

        if (!filtered.Any()) return null;

        // Prioritize by card type: Attack > Skill > Power
        string[] typePriority = { "Attack", "Skill", "Power" };

        foreach (var ctype in typePriority)
        {
            var cardsOfType = filtered.Where(c => c.CardType == ctype).ToList();
            if (cardsOfType.Any())
            {
                var card = cardsOfType.First();
                var target = (ctype == "Attack") ? enemies.OrderBy(e => e.Hp).FirstOrDefault() : null;
                return PlayCard(card, target, 5f, $"Play remaining: {card.Id}");
            }
        }

        return null;
    }

    /// <summary>
    /// Smart decision for HP-cost cards (Hemokinesis, Offering, Bloodletting).
    /// 
    /// Rules:
    /// 1. HP-for-energy: Only use if we need energy AND have good cards to play
    /// 2. HP-for-draw: Only use if hand is low AND enemy threat is manageable
    /// 3. HP-for-damage: Only use if it can kill or we're safe
    /// 4. Never use if HP would drop below 10 after cost
    /// </summary>
    private PolicyDecision? TryHpCostCards(List<CardInfo> playable, List<EnemyInfo> enemies,
                                          CombatSnapshot state, int incomingDamage)
    {
        // Never use HP-cost cards if HP is critical
        if (state.PlayerHp <= 10) return null;

        var hpCostCards = playable.Where(c =>
        {
            string baseId = c.Id.Replace("+", "");
            return HpCostCards.Contains(baseId);
        }).ToList();

        if (!hpCostCards.Any()) return null;

        foreach (var card in hpCostCards)
        {
            string baseId = card.Id.Replace("+", "");
            
            if (!HpCostCardData.TryGetValue(baseId, out var data))
                continue;

            // Check if HP after cost is safe
            int hpAfter = state.PlayerHp - data.hpCost;
            if (hpAfter <= 10) continue;

            // === HP-for-energy cards (Offering, Bloodletting) ===
            if (HpForEnergyCards.Contains(baseId))
            {
                // Don't use if we already have enough energy
                if (state.PlayerEnergy >= 3)
                {
                    if (_debugMode)
                        Log.Info($"[Heuristic] Skip {baseId}: already have {state.PlayerEnergy} energy");
                    continue;
                }

                // Check if we have expensive cards in hand that need the energy
                var expensiveCards = state.Hand.Where(c => 
                    c.IsPlayable && c.EnergyCost >= 2 && !HpCostCards.Contains(c.Id.Replace("+", ""))).ToList();
                
                if (!expensiveCards.Any() && state.PlayerEnergy >= 1)
                {
                    if (_debugMode)
                        Log.Info($"[Heuristic] Skip {baseId}: no expensive cards to play");
                    continue;
                }

                // Safe to use: enemy not attacking or we have block
                bool isSafe = incomingDamage <= DAMAGE_TOLERANCE || state.PlayerBlock >= incomingDamage;
                if (!isSafe && hpAfter <= 15)
                {
                    if (_debugMode)
                        Log.Info($"[Heuristic] Skip {baseId}: unsafe (incoming={incomingDamage}, hpAfter={hpAfter})");
                    continue;
                }

                return PlayCard(card, null, 8f, 
                    $"HP-for-energy: {baseId} (+{data.energyGain} energy, -{data.hpCost} HP)");
            }

            // === HP-for-draw cards (Offering) ===
            if (HpForDrawCards.Contains(baseId))
            {
                // Only draw if hand is low
                if (state.Hand.Count >= 5)
                {
                    if (_debugMode)
                        Log.Info($"[Heuristic] Skip {baseId}: hand already full ({state.Hand.Count} cards)");
                    continue;
                }

                // Evaluate if we need more options
                var usefulCards = state.Hand.Where(c => 
                    c.IsPlayable && c.EnergyCost <= state.PlayerEnergy).Count();
                
                if (usefulCards >= 3)
                {
                    if (_debugMode)
                        Log.Info($"[Heuristic] Skip {baseId}: enough useful cards ({usefulCards})");
                    continue;
                }

                // Safe to use: enemy not attacking or we have block
                bool isSafe = incomingDamage <= DAMAGE_TOLERANCE || state.PlayerBlock >= incomingDamage;
                if (!isSafe)
                {
                    if (_debugMode)
                        Log.Info($"[Heuristic] Skip {baseId}: enemy attacking, unsafe to burn HP");
                    continue;
                }

                return PlayCard(card, null, 7f,
                    $"HP-for-draw: {baseId} (+{data.drawCards} cards, -{data.hpCost} HP)");
            }

            // === HP-for-damage cards (Hemokinesis) ===
            if (HpForDamageCards.Contains(baseId))
            {
                int strength = GetStrength(state);
                int dmg = data.bonusDamage + strength;

                // Check if it can one-shot an enemy
                foreach (var enemy in enemies)
                {
                    if (dmg >= enemy.Hp)
                    {
                        return PlayCard(card, enemy, 90f,
                            $"HP-for-kill: {baseId} kills {enemy.Id} (-{data.hpCost} HP)");
                    }
                }

                // Safe to use for damage if enemy not attacking
                bool isSafe = incomingDamage <= DAMAGE_TOLERANCE || state.PlayerBlock >= incomingDamage;
                if (!isSafe)
                {
                    if (_debugMode)
                        Log.Info($"[Heuristic] Skip {baseId}: enemy attacking, save HP for defense");
                    continue;
                }

                // Use if enemy has high HP and we're safe
                var highestHpEnemy = enemies.OrderByDescending(e => e.Hp).First();
                if (highestHpEnemy.Hp >= 20)
                {
                    return PlayCard(card, highestHpEnemy, 6f,
                        $"HP-for-damage: {baseId} -> {highestHpEnemy.Id} ({dmg} dmg, -{data.hpCost} HP)");
                }
            }
        }

        return null;
    }

    #endregion

    #region Helpers

    private static List<CardInfo> GetPlayableCards(CombatSnapshot state)
    {
        return state.Hand.Where(c => c.IsPlayable && c.EnergyCost >= 0 && c.EnergyCost <= state.PlayerEnergy).ToList();
    }

    private static int CalculateIncomingDamage(List<EnemyInfo> enemies)
    {
        int total = 0;
        foreach (var e in enemies)
        {
            if (e.IntentType == "Attack" || e.IntentType == "AttackDebuff" || e.IntentType == "AttackBuff")
            {
                total += e.IntentDamage * Math.Max(1, e.IntentHits);
            }
        }
        return total;
    }

    private static int GetStrength(CombatSnapshot state)
    {
        var strPower = state.PlayerPowers.FirstOrDefault(p => p.Id == "Strength");
        return strPower?.Amount ?? 0;
    }

    private static int EstimateDamage(CardInfo card, int strength)
    {
        string id = card.Id.Replace("+", "").ToLower();
        int baseDmg = id switch
        {
            "bludgeon" => 32,
            "immolate" => 21,
            "carnage" => 20,
            "hemokinesis" => 15,
            "heavyblade" => 14 + strength * 2, // scales 3x total
            "clothesline" => 12,
            "wildstrike" => 12,
            "headbutt" => 9,
            "pommelstrike" => 9,
            "bash" => 8,
            "cleave" => 8,
            "twinstrike" => 10, // 5x2
            "strike" => 6,
            "anger" => 6,
            "ironwave" => 5,
            _ => 5  // fallback
        };

        // Add strength for non-scaling cards
        if (id != "heavyblade")
            baseDmg += strength;

        return baseDmg;
    }

    private static int EstimateBlock(CardInfo card)
    {
        string id = card.Id.Replace("+", "").ToLower();
        return id switch
        {
            "impervious" => 30,
            "powerthrough" => 15,
            "entrench" => 12, // approximate
            "ghostlyarmor" => 10,
            "shrugitoff" => 8,
            "truegrit" => 7,
            "sentinel" => 5,
            "defend" => 5,
            "ironwave" => 5,
            "armaments" => 5,
            _ => 0  // fallback
        };
    }

    private List<CardInfo> FilterOutHpCostCards(List<CardInfo> cards)
    {
        return cards.Where(c =>
        {
            string baseId = c.Id.Replace("+", "");
            return !HpCostCards.Contains(baseId);
        }).ToList();
    }

    private static PolicyDecision EndTurn(string reason)
    {
        return new PolicyDecision(ActionType.EndTurn, null, null, 0f, reason);
    }

    private static PolicyDecision PlayCard(CardInfo card, EnemyInfo? target, float score, string reason)
    {
        return new PolicyDecision(ActionType.PlayCard, card, target, score, reason);
    }

    #endregion
}