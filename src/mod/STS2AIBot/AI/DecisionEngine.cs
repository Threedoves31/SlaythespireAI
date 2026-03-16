using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Logging;
using STS2AIBot.StateExtractor;

namespace STS2AIBot.AI;

/// <summary>
/// AI decision engine with multiple strategies.
/// Operates purely on snapshot types (CardInfo, EnemyInfo) — no game-native types.
/// CombatHook is responsible for resolving IDs back to game objects.
/// </summary>
public class DecisionEngine
{
    public enum DecisionStrategy
    {
        SimpleHeuristic,
        ThreatBased,
        ResourceOptimization,
        ComboOriented,
        RiskManagement,
        Adaptive
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
        CardInfo? Card,
        EnemyInfo? Target,
        float Score,
        string Reason,
        PotionInfo? Potion = null
    );

    private DecisionStrategy _strategy = DecisionStrategy.SimpleHeuristic;
    private bool _debugMode = true;
    private int _turnCount = 0;
    private List<DecisionRecord> _history = new();

    public DecisionEngine()
    {
        Log.Info("[DecisionEngine] Initialized with SimpleHeuristic strategy");
    }

    public void SetStrategy(DecisionStrategy strategy)
    {
        _strategy = strategy;
        Log.Info($"[DecisionEngine] Strategy changed to: {strategy}");
    }

    public void SetDebugMode(bool enabled)
    {
        _debugMode = enabled;
    }

    /// <summary>
    /// Make a decision based on current combat state.
    /// </summary>
    public Decision MakeDecision(CombatSnapshot state)
    {
        if (state == null)
            return new Decision(ActionType.EndTurn, null, null, 0f, "No state available");

        _turnCount++;

        return _strategy switch
        {
            DecisionStrategy.SimpleHeuristic => SimpleHeuristicDecision(state),
            DecisionStrategy.ThreatBased => ThreatBasedDecision(state),
            DecisionStrategy.ResourceOptimization => ResourceOptimizedDecision(state),
            DecisionStrategy.ComboOriented => ComboOrientedDecision(state),
            DecisionStrategy.RiskManagement => RiskManagedDecision(state),
            DecisionStrategy.Adaptive => AdaptiveDecision(state),
            _ => SimpleHeuristicDecision(state)
        };
    }

    /// <summary>
    /// Decide whether to use a potion before playing cards.
    /// </summary>
    public Decision? ConsiderPotion(CombatSnapshot state)
    {
        if (state.Potions.Count == 0) return null;

        float hpRatio = (float)state.PlayerHp / state.PlayerMaxHp;
        float incomingDamage = state.Enemies
            .Where(e => e.Hp > 0 && e.IntentType == "Attack")
            .Sum(e => e.IntentDamage * e.IntentHits);

        foreach (var potion in state.Potions)
        {
            if (IsHealingPotion(potion.Id) && hpRatio < 0.5f)
                return new Decision(ActionType.UsePotion, null, null, 20f,
                    $"Use {potion.Id}: low HP ({state.PlayerHp}/{state.PlayerMaxHp})", potion);

            if (potion.TargetType == "AnyEnemy" && state.Enemies.Any(e => e.Hp > 0))
                return new Decision(ActionType.UsePotion, null, null, 15f,
                    $"Use {potion.Id}: attack potion on enemy", potion);

            if (IsDefensivePotion(potion.Id) && (incomingDamage > state.PlayerHp * 0.4f || hpRatio < 0.4f))
                return new Decision(ActionType.UsePotion, null, null, 18f,
                    $"Use {potion.Id}: incoming {incomingDamage} dmg", potion);

            if (IsStrengthPotion(potion.Id) && hpRatio > 0.3f && state.Enemies.Any(e => e.Hp > 0))
                return new Decision(ActionType.UsePotion, null, null, 12f,
                    $"Use {potion.Id}: buff before attacking", potion);
        }

        return null;
    }

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

        if (_history.Count > 100)
            _history = _history.TakeLast(100).ToList();

        if (_debugMode)
            Log.Info($"[DecisionEngine] Recorded: {decision.Card?.Id ?? "None"} = {success} (HP: {state.PlayerHp}/{state.PlayerMaxHp})");
    }

    #region Strategies

    private Decision SimpleHeuristicDecision(CombatSnapshot state)
    {
        var playable = GetPlayableCards(state);
        if (!playable.Any())
            return EndTurn("No playable cards");

        var livingEnemies = state.Enemies.Where(e => e.Hp > 0).ToList();

        // Prefer attacks
        var attacks = playable.Where(c => c.CardType == "Attack").ToList();
        if (livingEnemies.Any() && attacks.Any())
        {
            var best = attacks.OrderBy(c => c.EnergyCost).First();
            var target = SelectTarget(livingEnemies);
            return PlayCard(best, target, ScoreAttack(best, state),
                $"Attack {best.Id} on {target.Id} (HP {target.Hp})");
        }

        // Then skills
        var skills = playable.Where(c => c.CardType == "Skill").ToList();
        if (skills.Any())
        {
            var best = skills.OrderBy(c => c.EnergyCost).First();
            return PlayCard(best, null, ScoreSkill(best, state),
                $"Use {best.Id} for defense/utility");
        }

        // Then powers
        var powers = playable.Where(c => c.CardType == "Power").ToList();
        if (powers.Any())
        {
            var best = powers.OrderBy(c => c.EnergyCost).First();
            return PlayCard(best, null, 8f, $"Play power {best.Id}");
        }

        return EndTurn("No suitable cards");
    }

    private Decision ThreatBasedDecision(CombatSnapshot state)
    {
        var playable = GetPlayableCards(state);
        if (!playable.Any())
            return EndTurn("No playable cards");

        float threatLevel = CalculateThreatLevel(state.Enemies);
        bool underThreat = state.PlayerBlock < threatLevel;

        // Under threat + low HP → defend first
        if (underThreat && state.PlayerHp < state.PlayerMaxHp * 0.5f)
        {
            var defenses = playable.Where(c => c.CardType == "Skill").ToList();
            if (defenses.Any())
            {
                var best = defenses.OrderByDescending(EstimateDefense).First();
                return PlayCard(best, null, ScoreSkill(best, state),
                    $"Defensive play: {best.Id} (threat: {threatLevel:F0})");
            }
        }

        // Focus damage on highest threat enemy
        var highThreat = state.Enemies
            .Where(e => e.Hp > 0)
            .OrderByDescending(e => e.IntentType == "Attack" ? e.IntentDamage * e.IntentHits : 0)
            .ThenByDescending(e => e.Hp)
            .FirstOrDefault();

        if (highThreat != null)
        {
            var attacks = playable.Where(c => c.CardType == "Attack").ToList();
            if (attacks.Any())
            {
                var best = attacks.OrderByDescending(EstimateDamage).First();
                return PlayCard(best, highThreat, ScoreAttack(best, state),
                    $"Focus high threat: {best.Id} -> {highThreat.Id}");
            }
        }

        return SimpleHeuristicDecision(state);
    }

    private Decision ResourceOptimizedDecision(CombatSnapshot state)
    {
        var playable = GetPlayableCards(state);
        if (!playable.Any())
            return EndTurn("No playable cards");

        // Pick the card with best value-per-energy
        var best = playable
            .OrderByDescending(c => EstimateCardValue(c, state) / Math.Max(1, c.EnergyCost))
            .First();

        var target = best.CardType == "Attack"
            ? SelectTarget(state.Enemies.Where(e => e.Hp > 0).ToList())
            : null;

        return PlayCard(best, target, ScoreAttack(best, state),
            $"Energy efficient: {best.Id}");
    }

    private Decision ComboOrientedDecision(CombatSnapshot state)
    {
        var playable = GetPlayableCards(state);
        if (!playable.Any())
            return EndTurn("No playable cards");

        var combo = FindBestCombo(playable, state);
        if (combo != null)
            return combo;

        return SimpleHeuristicDecision(state);
    }

    private Decision RiskManagedDecision(CombatSnapshot state)
    {
        var playable = GetPlayableCards(state);
        if (!playable.Any())
            return EndTurn("No playable cards");

        float hpRatio = (float)state.PlayerHp / state.PlayerMaxHp;

        // High risk: block first
        if (hpRatio < 0.3f)
        {
            var defensive = playable
                .Where(c => c.CardType == "Skill" || c.Id.Contains("Defend"))
                .OrderByDescending(EstimateDefense)
                .FirstOrDefault();

            if (defensive != null)
                return PlayCard(defensive, null, ScoreSkill(defensive, state),
                    $"Risk mitigation: {defensive.Id} (HP {state.PlayerHp}/{state.PlayerMaxHp})");
        }

        // Medium risk: balance
        if (hpRatio < 0.6f)
        {
            var best = playable
                .OrderByDescending(c => EstimateCardValue(c, state))
                .First();

            var target = best.CardType == "Attack"
                ? SelectTarget(state.Enemies.Where(e => e.Hp > 0).ToList())
                : null;

            return PlayCard(best, target, ScoreAttack(best, state),
                $"Balanced: {best.Id}");
        }

        return SimpleHeuristicDecision(state);
    }

    private Decision AdaptiveDecision(CombatSnapshot state)
    {
        var playable = GetPlayableCards(state);

        var winningCardId = _history
            .Where(h => h.Success)
            .OrderByDescending(h => h.Timestamp)
            .Select(h => h.CardId)
            .FirstOrDefault();

        if (winningCardId != null)
        {
            var card = playable.FirstOrDefault(c => c.Id == winningCardId);
            if (card != null)
            {
                var target = card.CardType == "Attack"
                    ? SelectTarget(state.Enemies.Where(e => e.Hp > 0).ToList())
                    : null;
                return PlayCard(card, target, ScoreAttack(card, state),
                    "Adaptive: based on past success");
            }
        }

        return SimpleHeuristicDecision(state);
    }

    #endregion

    #region Helpers

    private static List<CardInfo> GetPlayableCards(CombatSnapshot state) =>
        state.Hand.Where(c => c.IsPlayable && c.EnergyCost <= state.PlayerEnergy).ToList();

    private static Decision EndTurn(string reason) =>
        new(ActionType.EndTurn, null, null, 0f, reason);

    private static Decision PlayCard(CardInfo card, EnemyInfo? target, float score, string reason) =>
        new(ActionType.PlayCard, card, target, score, reason);

    private static EnemyInfo? SelectTarget(List<EnemyInfo> enemies)
    {
        if (!enemies.Any()) return null;
        // Focus fire on weakest enemy to remove threats faster
        return enemies.OrderBy(e => e.Hp).First();
    }

    private float CalculateThreatLevel(List<EnemyInfo> enemies)
    {
        if (!enemies.Any(e => e.Hp > 0)) return 0f;

        float totalThreat = 0f;
        int count = 0;
        foreach (var enemy in enemies.Where(e => e.Hp > 0))
        {
            totalThreat += enemy.IntentType switch
            {
                "Attack" => enemy.IntentDamage * enemy.IntentHits * 1.5f,
                "Buff" => enemy.Hp * 0.3f,
                "Debuff" => enemy.Hp * 0.2f,
                _ => enemy.Hp * 0.1f
            };
            count++;
        }
        return totalThreat / count;
    }

    private static float ScoreAttack(CardInfo card, CombatSnapshot state)
    {
        float score = 0f;
        if (card.CardType == "Attack") score += 10f;
        if (card.CardType == "Skill")
        {
            bool underAttack = state.Enemies.Any(e => e.IntentType == "Attack" && e.Hp > 0);
            if (underAttack && state.PlayerBlock < 10) score += 8f;
        }
        score += (3 - card.EnergyCost) * 2f;
        return score;
    }

    private static float ScoreSkill(CardInfo card, CombatSnapshot state)
    {
        float score = 5f;
        if (card.Id.Contains("Defend") || card.Id.Contains("Bash")) score += 7f;
        if (card.Id.Contains("Draw") || card.Id.Contains("Pommel")) score += 6f;
        if (card.Id.Contains("Offering")) score += 5f;
        return score;
    }

    private static float EstimateDamage(CardInfo card)
    {
        float base_ = 6f;
        if (card.Id.Contains("Bash")) base_ = 10f;
        if (card.Id.Contains("Cleave")) base_ = 9f;
        if (card.Id.Contains("Heavy")) base_ = 14f;
        return base_ / Math.Max(1, card.EnergyCost);
    }

    private static float EstimateDefense(CardInfo card)
    {
        if (card.Id.Contains("Defend")) return 5f + card.EnergyCost * 2f;
        if (card.Id.Contains("Bash")) return 8f + card.EnergyCost;
        return 0f;
    }

    private static float EstimateCardValue(CardInfo card, CombatSnapshot state)
    {
        float hpRatio = (float)state.PlayerHp / Math.Max(1, state.PlayerMaxHp);
        if (hpRatio < 0.6f && card.CardType == "Skill") return EstimateDefense(card) * 1.2f;
        if (hpRatio >= 0.6f && card.CardType == "Attack") return EstimateDamage(card) * 1.2f;
        return 5f;
    }

    private Decision? FindBestCombo(List<CardInfo> cards, CombatSnapshot state)
    {
        var attacks = cards.Where(c => c.CardType == "Attack").ToList();
        var skills = cards.Where(c => c.CardType == "Skill").ToList();
        var livingEnemies = state.Enemies.Where(e => e.Hp > 0).ToList();

        // Bash on non-vulnerable enemy (apply vulnerable first, then follow up with attacks)
        var bash = cards.FirstOrDefault(c => c.Id.Contains("Bash"));
        if (bash != null)
        {
            var nonVulnerable = livingEnemies.FirstOrDefault(e =>
                !e.Powers.Any(p => p.Id.Contains("Vulnerable")));
            if (nonVulnerable != null)
                return PlayCard(bash, nonVulnerable, 15f, "Bash to apply Vulnerable");
        }

        // If enemy already vulnerable, prioritize heavy attacks
        var vulnerable = livingEnemies.FirstOrDefault(e =>
            e.Powers.Any(p => p.Id.Contains("Vulnerable")));
        if (vulnerable != null && attacks.Any())
        {
            var best = attacks.OrderByDescending(EstimateDamage).First();
            return PlayCard(best, vulnerable, 14f, $"Hit vulnerable {vulnerable.Id}");
        }

        // Defend + attack combo if enough energy
        var defend = skills.FirstOrDefault(c => c.Id.Contains("Defend"));
        var bigAttack = attacks.FirstOrDefault(c => c.EnergyCost >= 2);
        if (defend != null && bigAttack != null &&
            defend.EnergyCost + bigAttack.EnergyCost <= state.PlayerEnergy)
        {
            return PlayCard(defend, null, 10f, "Defend + Attack combo");
        }

        return null;
    }

    private static bool IsHealingPotion(string id) =>
        id.Contains("Health") || id.Contains("Fairy") || id.Contains("Regen");

    private static bool IsDefensivePotion(string id) =>
        id.Contains("Block") || id.Contains("Iron") || id.Contains("Dexterity");

    private static bool IsStrengthPotion(string id) =>
        id.Contains("Strength") || id.Contains("Power") || id.Contains("Elixir");

    #endregion

    private record DecisionRecord
    {
        public int TurnNumber;
        public string CardId = "";
        public string TargetEnemyId = "";
        public float Score;
        public float HpRatio;
        public int EnergyUsed;
        public bool Success;
        public DateTime Timestamp;
    }
}
