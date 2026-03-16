using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Logging;
using STS2AIBot.StateExtractor;

namespace STS2AIBot.AI;

/// <summary>
/// AI decision engine with multiple strategies.
/// Supports heuristic, rule-based, and future ML-based decisions.
/// </summary>
public class DecisionEngine
{
    public enum DecisionStrategy
    {
        SimpleHeuristic,      // Current: prefer attacks > skills
        ThreatBased,          // Assess enemy threat level
        ResourceOptimization,  // Optimize energy/card usage
        ComboOriented,       // Look for card synergies
        RiskManagement,       // Play defensively when low HP
        Adaptive             // Learn from past decisions
    }

    public enum ActionType
    {
        PlayCard,
        EndTurn,
        UsePotion,
        SkipReward
    }

    public record Decision(
        ActionType Type,
        CardModel? Card,
        Creature? Target,
        float Score,
        string Reason
    );

    private DecisionStrategy _strategy = DecisionStrategy.SimpleHeuristic;
    private bool _debugMode = true;
    private int _turnCount = 0;
    private List<DecisionRecord> _history = new();

    public DecisionEngine()
    {
        Log.Info("[DecisionEngine] Initialized with SimpleHeuristic strategy");
    }

    /// <summary>
    /// Set the decision strategy.
    /// </summary>
    public void SetStrategy(DecisionStrategy strategy)
    {
        _strategy = strategy;
        Log.Info($"[DecisionEngine] Strategy changed to: {strategy}");
    }

    /// <summary>
    /// Enable/disable debug mode.
    /// </summary>
    public void SetDebugMode(bool enabled)
    {
        _debugMode = enabled;
        Log.Info($"[DecisionEngine] Debug mode: {enabled}");
    }

    /// <summary>
    /// Make a decision based on current combat state.
    /// </summary>
    public Decision MakeDecision(CombatSnapshot state)
    {
        if (state == null)
        {
            return new Decision(ActionType.EndTurn, null, null, 0f, "No state available");
        }

        _turnCount++;

        switch (_strategy)
        {
            case DecisionStrategy.SimpleHeuristic:
                return SimpleHeuristicDecision(state);
            case DecisionStrategy.ThreatBased:
                return ThreatBasedDecision(state);
            case DecisionStrategy.ResourceOptimization:
                return ResourceOptimizedDecision(state);
            case DecisionStrategy.ComboOriented:
                return ComboOrientedDecision(state);
            case DecisionStrategy.RiskManagement:
                return RiskManagedDecision(state);
            case DecisionStrategy.Adaptive:
                return AdaptiveDecision(state);
            default:
                return SimpleHeuristicDecision(state);
        }
    }

    /// <summary>
    /// Simple heuristic: prefer attacks > skills, low cost first.
    /// </summary>
    private Decision SimpleHeuristicDecision(CombatSnapshot state)
    {
        var playableCards = state.Hand
            .Where(c => c.IsPlayable && c.EnergyCost <= state.PlayerEnergy)
            .ToList();

        if (!playableCards.Any())
        {
            return new Decision(ActionType.EndTurn, null, null, 0f, "No playable cards");
        }

        // Check enemies and attacks
        var livingEnemies = state.Enemies.Where(e => e.Hp > 0).ToList();
        bool hasAttacks = playableCards.Any(c => c.CardType == "Attack");

        if (livingEnemies.Any() && hasAttacks)
        {
            var attacks = playableCards.Where(c => c.CardType == "Attack").ToList();
            var bestAttack = attacks.OrderBy(c => c.EnergyCost).FirstOrDefault();
            var target = SelectTarget(bestAttack, livingEnemies);

            return new Decision(
                ActionType.PlayCard,
                bestAttack,
                target,
                ScorePlayCard(bestAttack, state),
                $"Attack {bestAttack.Id} on {target.Id} (HP {target.Hp})"
            );
        }

        // Use skills
        var skills = playableCards.Where(c => c.CardType == "Skill").ToList();
        if (skills.Any())
        {
            var bestSkill = skills.OrderBy(c => c.EnergyCost).FirstOrDefault();
            return new Decision(
                ActionType.PlayCard,
                bestSkill,
                null,
                ScoreSkillCard(bestSkill, state),
                $"Use {bestSkill.Id} for defense/utility"
            );
        }

        return new Decision(ActionType.EndTurn, null, null, 0f, "No suitable cards");
    }

    /// <summary>
    /// Threat-based: assess enemy threat and respond accordingly.
    /// </summary>
    private Decision ThreatBasedDecision(CombatSnapshot state)
    {
        var playableCards = state.Hand
            .Where(c => c.IsPlayable && c.EnergyCost <= state.PlayerEnergy)
            .ToList();

        if (!playableCards.Any())
        {
            return new Decision(ActionType.EndTurn, null, null, 0f, "No playable cards");
        }

        // Calculate threat level
        float threatLevel = CalculateThreatLevel(state.Enemies);
        bool underThreat = state.PlayerBlock < threatLevel;

        // If under threat, prioritize defense
        if (underThreat && state.PlayerHp < state.PlayerMaxHp * 0.5f)
        {
            var defenses = playableCards.Where(c => c.CardType == "Skill").ToList();
            if (defenses.Any())
            {
                var bestDefense = defenses.OrderByDescending(c => EstimateCardDefense(c)).FirstOrDefault();
                return new Decision(
                    ActionType.PlayCard,
                    bestDefense,
                    null,
                    ScoreSkillCard(bestDefense, state),
                    $"Defensive play: {bestDefense.Id} (threat: {threatLevel})"
                );
            }
        }

        // Otherwise, focus damage on highest threat enemy
        var highestThreatEnemy = state.Enemies
            .Where(e => e.Hp > 0)
            .OrderByDescending(e => e.Hp) // Simplified threat proxy
            .FirstOrDefault();

        if (highestThreatEnemy != null)
        {
            var attacks = playableCards.Where(c => c.CardType == "Attack").ToList();
            if (attacks.Any())
            {
                var bestAttack = attacks.OrderByDescending(c => EstimateCardDamage(c)).FirstOrDefault();
                return new Decision(
                    ActionType.PlayCard,
                    bestAttack,
                    highestThreatEnemy,
                    ScorePlayCard(bestAttack, state),
                    $"Focus high threat: {bestAttack.Id} -> {highestThreatEnemy.Id}"
                );
            }
        }

        return SimpleHeuristicDecision(state);
    }

    /// <summary>
    /// Resource optimization: maximize energy and card efficiency.
    /// </summary>
    private Decision ResourceOptimizedDecision(CombatSnapshot state)
    {
        var playableCards = state.Hand
            .Where(c => c.IsPlayable && c.EnergyCost <= state.PlayerEnergy)
            .ToList();

        if (!playableCards.Any())
        {
            return new Decision(ActionType.EndTurn, null, null, 0f, "No playable cards");
        }

        // Find cards that use most energy without waste
        var bestCombination = FindBestCardCombination(playableCards, state.PlayerEnergy);

        if (bestCombination != null && bestCombination.Any())
        {
            var card = bestCombination.First();
            var target = card.CardType == "Attack" ? SelectTarget(card, state.Enemies) : null;

            return new Decision(
                ActionType.PlayCard,
                card,
                target,
                ScorePlayCard(card, state),
                $"Energy efficient: {card.Id}"
            );
        }

        return SimpleHeuristicDecision(state);
    }

    /// <summary>
    /// Combo-oriented: look for card synergies.
    /// </summary>
    private Decision ComboOrientedDecision(CombatSnapshot state)
    {
        var playableCards = state.Hand
            .Where(c => c.IsPlayable && c.EnergyCost <= state.PlayerEnergy)
            .ToList();

        if (!playableCards.Any())
        {
            return new Decision(ActionType.EndTurn, null, null, 0f, "No playable cards");
        }

        // Check for known combos
        var combo = FindBestCombo(playableCards, state);

        if (combo != null)
        {
            return new Decision(
                ActionType.PlayCard,
                combo.Card,
                combo.Target,
                combo.Score,
                $"Combo: {combo.Reason}"
            );
        }

        return SimpleHeuristicDecision(state);
    }

    /// <summary>
    /// Risk management: play defensively when HP is low.
    /// </summary>
    private Decision RiskManagedDecision(CombatSnapshot state)
    {
        var playableCards = state.Hand
            .Where(c => c.IsPlayable && c.EnergyCost <= state.PlayerEnergy)
            .ToList();

        if (!playableCards.Any())
        {
            return new Decision(ActionType.EndTurn, null, null, 0f, "No playable cards");
        }

        float hpRatio = (float)state.PlayerHp / state.PlayerMaxHp;
        bool highRisk = hpRatio < 0.3f;
        bool mediumRisk = hpRatio < 0.6f;

        // High risk: prioritize blocking and healing
        if (highRisk)
        {
            var defensive = playableCards
                .Where(c => c.CardType == "Skill" || c.Id.Contains("Defend") || c.Id.Contains("Bash"))
                .OrderByDescending(c => EstimateCardDefense(c))
                .FirstOrDefault();

            if (defensive != null)
            {
                return new Decision(
                    ActionType.PlayCard,
                    defensive,
                    null,
                    ScoreSkillCard(defensive, state),
                    $"Risk mitigation: {defensive.Id} (HP {state.PlayerHp}/{state.PlayerMaxHp})"
                );
            }
        }

        // Medium risk: balance offense and defense
        if (mediumRisk)
        {
            var balanced = playableCards
                .OrderByDescending(c => EstimateCardValue(c, hpRatio))
                .FirstOrDefault();

            if (balanced != null)
            {
                var target = balanced.CardType == "Attack" ? SelectTarget(balanced, state.Enemies) : null;
                return new Decision(
                    ActionType.PlayCard,
                    balanced,
                    target,
                    ScorePlayCard(balanced, state),
                    $"Balanced: {balanced.Id}"
                );
            }
        }

        return SimpleHeuristicDecision(state);
    }

    /// <summary>
    /// Adaptive: learn from past decisions and adjust.
    /// </summary>
    private Decision AdaptiveDecision(CombatSnapshot state)
    {
        // Simple adaptive: if same situation happened before, repeat winning decision
        var similarHistory = _history
            .Where(h => h.HpRatio < 0.8f && h.Success)
            .OrderByDescending(h => h.Timestamp)
            .Take(3)
            .ToList();

        if (similarHistory.Any())
        {
            var cardId = similarHistory.First().CardId;
            var card = playableCards.FirstOrDefault(c => c.Id == cardId);

            if (card != null)
            {
                var target = card.CardType == "Attack" ? SelectTarget(card, state.Enemies) : null;
                return new Decision(
                    ActionType.PlayCard,
                    card,
                    target,
                    ScorePlayCard(card, state),
                    $"Adaptive: Based on past success"
                );
            }
        }

        return SimpleHeuristicDecision(state);
    }

    #region Helper Methods

    private float CalculateThreatLevel(List<EnemyInfo> enemies)
    {
        float totalThreat = 0f;
        foreach (var enemy in enemies)
        {
            if (enemy.Hp <= 0) continue;
            // Estimate threat based on HP and intent
            float intentThreat = enemy.IntentType switch
            {
                "Attack" => enemy.IntentDamage * 1.5f,
                "Buff" => enemy.Hp * 0.3f,
                "Debuff" => enemy.Hp * 0.2f,
                _ => enemy.Hp * 0.1f
            };
            totalThreat += intentThreat;
        }
        return totalThreat / enemies.Count;
    }

    private Creature? SelectTarget(CardModel card, List<EnemyInfo> enemies)
    {
        if (card.CardType != "Attack") return null;

        var alive = enemies.Where(e => e.Hp > 0).ToList();
        if (!alive.Any()) return null;

        // Strategy: focus fire on weakest enemy
        return alive.OrderBy(e => e.Hp).FirstOrDefault();
    }

    private float ScorePlayCard(CardModel card, CombatSnapshot state)
    {
        float score = 0f;

        // Damage dealing is good
        if (card.CardType == "Attack")
        {
            score += 10f; // Base attack value
        }

        // Defense is situational
        if (card.CardType == "Skill")
        {
            // If under attack, defense is more valuable
            bool underAttack = state.Enemies.Any(e => e.IntentType == "Attack" && e.Hp > 0);
            if (underAttack && state.PlayerBlock < 10)
                score += 8f;
        }

        // Low cost is good
        score += (3 - card.EnergyCost) * 2f;

        return score;
    }

    private float ScoreSkillCard(CardModel card, CombatSnapshot state)
    {
        float score = 5f; // Base skill value

        // Block is valuable
        if (card.Id.Contains("Defend") || card.Id.Contains("Bash"))
        {
            score += 7f;
        }

        // Draw cards are valuable
        if (card.Id.Contains("Draw") || card.Id.Contains("Pommel"))
        {
            score += 6f;
        }

        // Energy gain is valuable
        if (card.Id.Contains("Offering"))
        {
            score += 5f;
        }

        return score;
    }

    private float EstimateCardDamage(CardModel card)
    {
        // Simplified - would need card database for accurate values
        float baseDamage = 6f; // Strike base
        float cost = Math.Max(1, card.EnergyCost);

        // Higher cost usually means higher damage
        if (card.Id.Contains("Bash")) baseDamage = 10f;
        if (card.Id.Contains("Cleave")) baseDamage = 9f;
        if (card.Id.Contains("Heavy")) baseDamage = 14f;

        return baseDamage / cost; // Damage per energy
    }

    private float EstimateCardDefense(CardModel card)
    {
        float defense = 0f;

        if (card.Id.Contains("Defend"))
        {
            defense = 5f + card.EnergyCost * 2f; // Base 5 + 2 per cost
        }
        else if (card.Id.Contains("Bash"))
        {
            defense = 8f + card.EnergyCost; // Bash adds block
        }

        return defense;
    }

    private float EstimateCardValue(CardModel card, float hpRatio)
    {
        // Balance offense and defense based on HP
        bool needDefense = hpRatio < 0.6f;

        if (needDefense && card.CardType == "Skill")
        {
            return EstimateCardDefense(card) * 1.2f;
        }

        if (!needDefense && card.CardType == "Attack")
        {
            return EstimateCardDamage(card) * 1.2f;
        }

        return 5f; // Default
    }

    private List<CardModel> FindBestCardCombination(List<CardModel> cards, int energy)
    {
        // Greedy: try to use all energy
        var result = new List<CardModel>();
        int remainingEnergy = energy;

        foreach (var card in cards.OrderByDescending(c => c.EnergyCost))
        {
            if (card.EnergyCost <= remainingEnergy)
            {
                result.Add(card);
                remainingEnergy -= card.EnergyCost;
            }
            if (remainingEnergy <= 0) break;
        }

        return result.Count > 0 ? result : null;
    }

    private (CardModel Card, Creature Target, float Score, string Reason)? FindBestCombo(List<CardModel> cards, CombatSnapshot state)
    {
        // Known Ironclad combos
        // 1. Strength + multi-hit attacks
        // 2. Defend + high damage single attack
        // 3. Bash vulnerable enemies

        var attacks = cards.Where(c => c.CardType == "Attack").ToList();
        var skills = cards.Where(c => c.CardType == "Skill").ToList();

        // Check for Bash + vulnerable enemy
        var vulnerableEnemy = state.Enemies.FirstOrDefault(e =>
            e.Hp > 0 && e.Powers.Any(p => p.Id.Contains("Vulnerable")));

        if (vulnerableEnemy != null)
        {
            var bash = skills.FirstOrDefault(c => c.Id.Contains("Bash"));
            if (bash != null && bash.EnergyCost <= state.PlayerEnergy)
            {
                return (bash, vulnerableEnemy, 15f, "Bash + Vulnerable combo");
            }
        }

        // Defend + attack combo
        var defense = skills.FirstOrDefault(c => c.Id.Contains("Defend"));
        var highDmgAttack = attacks.FirstOrDefault(c => c.EnergyCost >= 2);
        if (defense != null && highDmgAttack != null &&
            defense.EnergyCost + highDmgAttack.EnergyCost <= state.PlayerEnergy)
        {
            // First defend, then attack
            return (defense, null, 10f, "Defend + Attack combo");
        }

        return null;
    }

    /// <summary>
    /// Record a decision for learning.
    /// </summary>
    public void RecordDecision(Decision decision, CombatSnapshot state, bool success)
    {
        float hpRatio = state.PlayerMaxHp > 0 ? (float)state.PlayerHp / state.PlayerMaxHp : 1f;

        _history.Add(new DecisionRecord
        {
            TurnNumber = _turnCount,
            CardId = decision.Card?.Id ?? "",
            TargetEnemyId = decision.Target?.Id ?? "",
            Score = decision.Score,
            HpRatio = hpRatio,
            EnergyUsed = decision.Card?.EnergyCost ?? 0,
            Success = success,
            Timestamp = DateTime.UtcNow
        });

        // Keep only last 100 decisions
        if (_history.Count > 100)
        {
            _history = _history.TakeLast(100).ToList();
        }

        if (_debugMode)
        {
            Log.Info($"[DecisionEngine] Recorded: {decision.Card?.Id ?? "None"} = {success} (HP: {state.PlayerHp}/{state.PlayerMaxHp})");
        }
    }

    private record DecisionRecord
    {
        public int TurnNumber;
        public string CardId;
        public string TargetEnemyId;
        public float Score;
        public float HpRatio;
        public int EnergyUsed;
        public bool Success;
        public DateTime Timestamp;
    }
}

#endregion
