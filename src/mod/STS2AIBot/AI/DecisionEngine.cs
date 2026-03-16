using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Logging;
using STS2AIBot.StateExtractor;

namespace STS2AIBot.AI;

/// <summary>
/// AI decision engine using turn simulation to evaluate card sequences.
/// Scoring priority: 1) maximize remaining HP, 2) preserve potions, 3) kill enemies fast.
/// Ironclad-specific logic built in.
/// </summary>
public class DecisionEngine
{
    public enum DecisionStrategy
    {
        Simulation,       // Full turn simulation (default)
        SimpleHeuristic,  // Fallback rule-based
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

    // Scoring weights
    private const float HP_WEIGHT         = 100f;  // per HP point saved
    private const float POTION_PENALTY    = 30f;   // per potion used
    private const float KILL_BONUS        = 50f;   // per enemy killed
    private const float DAMAGE_DEALT_BONUS = 0.5f; // per damage dealt (tiebreaker)

    private DecisionStrategy _strategy = DecisionStrategy.Simulation;
    private bool _debugMode = true;
    private int _turnCount = 0;
    private List<DecisionRecord> _history = new();

    // Simulation timing
    private DateTime _simulationStart;
    private int _sequencesEvaluated = 0;
    private const int MAX_SIMULATION_MS = 2000; // 2 second timeout

    public DecisionEngine()
    {
        Log.Info("[DecisionEngine] Initialized with Simulation strategy");
    }

    public void SetStrategy(DecisionStrategy strategy)
    {
        _strategy = strategy;
        Log.Info($"[DecisionEngine] Strategy changed to: {strategy}");
    }

    public void SetDebugMode(bool enabled) => _debugMode = enabled;

    /// <summary>
    /// Returns the next card to play this turn, using simulation to find the best sequence.
    /// Call repeatedly until EndTurn is returned.
    /// </summary>
    public Decision MakeDecision(CombatSnapshot state)
    {
        if (state == null)
            return EndTurn("No state available");

        _turnCount++;

        if (_strategy == DecisionStrategy.SimpleHeuristic)
            return SimpleHeuristicDecision(state);

        return SimulationDecision(state);
    }

    /// <summary>
    /// Decide whether to use a potion. Only use when truly necessary to preserve HP.
    /// Potions are precious — only use healing when HP &lt; 40%, defensive when fatal hit incoming.
    /// </summary>
    public Decision? ConsiderPotion(CombatSnapshot state)
    {
        if (state.Potions.Count == 0) return null;

        float hpRatio = (float)state.PlayerHp / state.PlayerMaxHp;
        int incomingDamage = state.Enemies
            .Where(e => e.Hp > 0 && e.IntentType == "Attack")
            .Sum(e => e.IntentDamage * e.IntentHits);

        // Net HP after block
        int netDamage = Math.Max(0, incomingDamage - state.PlayerBlock);

        foreach (var potion in state.Potions)
        {
            // Healing: only when very low HP
            if (IsHealingPotion(potion.Id) && hpRatio < 0.4f)
                return new Decision(ActionType.UsePotion, null, null, 20f,
                    $"Use {potion.Id}: critical HP ({state.PlayerHp}/{state.PlayerMaxHp})", potion);

            // Defensive: only when incoming damage would be fatal or near-fatal
            if (IsDefensivePotion(potion.Id) && netDamage >= state.PlayerHp)
                return new Decision(ActionType.UsePotion, null, null, 25f,
                    $"Use {potion.Id}: fatal hit incoming ({netDamage} dmg, {state.PlayerHp} HP)", potion);
        }

        // Attack potions: only use if no healing/defensive need and enemies alive
        // (lower priority — save for elites/bosses ideally, but use if HP is safe)
        if (hpRatio > 0.6f)
        {
            foreach (var potion in state.Potions)
            {
                if (potion.TargetType == "AnyEnemy" && state.Enemies.Any(e => e.Hp > 0))
                    return new Decision(ActionType.UsePotion, null, null, 10f,
                        $"Use {potion.Id}: attack potion (HP safe)", potion);
            }
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
            Score = decision.Score,
            HpRatio = hpRatio,
            Success = success,
            Timestamp = DateTime.UtcNow
        });
        if (_history.Count > 100)
            _history = _history.TakeLast(100).ToList();

        if (_debugMode)
            Log.Info($"[DecisionEngine] Turn {_turnCount}: {decision.Card?.Id ?? "EndTurn"} score={decision.Score:F1} hp={state.PlayerHp}/{state.PlayerMaxHp}");
    }

    #region Simulation

    /// <summary>
    /// Simulate all possible card play sequences for this turn, score each, return best first card.
    /// Uses DFS with pruning — max depth 10 cards.
    /// </summary>
    private Decision SimulationDecision(CombatSnapshot state)
    {
        var playable = GetPlayableCards(state);
        if (!playable.Any())
            return EndTurn("No playable cards");

        // Start simulation timer
        _simulationStart = DateTime.UtcNow;
        _sequencesEvaluated = 0;

        // Run simulation to find best sequence
        var bestSeq = FindBestSequence(state);

        var elapsed = (DateTime.UtcNow - _simulationStart).TotalMilliseconds;
        if (_debugMode)
            Log.Info($"[DecisionEngine] Sim: evaluated {_sequencesEvaluated} sequences in {elapsed:F0}ms");

        if (bestSeq == null || bestSeq.Count == 0)
            return EndTurn("Simulation: no beneficial sequence found");

        var firstCard = bestSeq[0].Card;
        var firstTarget = bestSeq[0].Target;
        float score = EvaluateEndState(SimulateSequence(state, bestSeq));

        if (_debugMode)
            Log.Info($"[DecisionEngine] Best sequence: {string.Join(" -> ", bestSeq.Select(s => s.Card.Id))} (score={score:F1})");

        return PlayCard(firstCard, firstTarget, score,
            $"Sim: {firstCard.Id} (seq len={bestSeq.Count}, score={score:F1})");
    }

    private record CardPlay(CardInfo Card, EnemyInfo? Target);

    /// <summary>
    /// DFS over all card play orderings, return the sequence with highest end-state score.
    /// </summary>
    private List<CardPlay>? FindBestSequence(CombatSnapshot state)
    {
        List<CardPlay>? bestSeq = null;
        float bestScore = EvaluateEndState(state); // baseline: play nothing

        DFS(state, new List<CardPlay>(), ref bestSeq, ref bestScore, depth: 0);

        return bestSeq;
    }

    private void DFS(CombatSnapshot state, List<CardPlay> current,
        ref List<CardPlay>? bestSeq, ref float bestScore, int depth)
    {
        // Check timeout
        if ((DateTime.UtcNow - _simulationStart).TotalMilliseconds > MAX_SIMULATION_MS)
            return;

        if (depth >= 10) return; // safety cap

        var playable = GetPlayableCards(state);

        // Deduplicate: same card ID + same target → skip duplicates
        var tried = new HashSet<string>();

        foreach (var card in playable)
        {
            var targets = GetTargetsForCard(card, state);
            foreach (var target in targets)
            {
                string key = card.Id + "|" + (target?.Id ?? "none");
                if (!tried.Add(key)) continue;

                _sequencesEvaluated++;

                var next = SimulatePlay(state, card, target);
                if (next == null) continue;

                current.Add(new CardPlay(card, target));

                float score = EvaluateEndState(next);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestSeq = new List<CardPlay>(current);
                }

                // Recurse
                DFS(next, current, ref bestSeq, ref bestScore, depth + 1);

                current.RemoveAt(current.Count - 1);
            }
        }
    }

    /// <summary>
    /// Simulate playing one card and return the resulting state snapshot.
    /// This is a lightweight approximation — not a full game engine.
    /// </summary>
    private CombatSnapshot? SimulatePlay(CombatSnapshot state, CardInfo card, EnemyInfo? target)
    {
        if (card.EnergyCost > state.PlayerEnergy) return null;

        // Clone state
        var newHand = state.Hand.Where(c => c != card).ToList();
        var newEnemies = state.Enemies.Select(e => e with { }).ToList();
        int newEnergy = state.PlayerEnergy - card.EnergyCost;
        int newBlock = state.PlayerBlock;
        int newHp = state.PlayerHp;
        var newPowers = state.PlayerPowers.ToList();

        // Get current strength
        int strength = state.PlayerPowers.FirstOrDefault(p => p.Id == "Strength")?.Amount ?? 0;

        // Apply card effects
        ApplyCardEffect(card, target, ref newHp, ref newBlock, ref newEnergy,
            ref strength, newEnemies, newHand, newPowers);

        // Update strength in powers
        var strPower = newPowers.FirstOrDefault(p => p.Id == "Strength");
        if (strPower != null)
            newPowers = newPowers.Select(p => p.Id == "Strength" ? p with { Amount = strength } : p).ToList();
        else if (strength > 0)
            newPowers.Add(new PowerInfo("Strength", strength));

        return new CombatSnapshot
        {
            PlayerHp = newHp,
            PlayerMaxHp = state.PlayerMaxHp,
            PlayerBlock = newBlock,
            PlayerEnergy = newEnergy,
            PlayerMaxEnergy = state.PlayerMaxEnergy,
            PlayerPowers = newPowers,
            Hand = newHand,
            DrawPileCount = state.DrawPileCount,
            DiscardPileCount = state.DiscardPileCount + 1,
            ExhaustPileCount = state.ExhaustPileCount,
            Enemies = newEnemies,
            Potions = state.Potions,
            TurnNumber = state.TurnNumber,
            FloorNumber = state.FloorNumber,
            CharacterId = state.CharacterId,
        };
    }

    private CombatSnapshot SimulateSequence(CombatSnapshot state, List<CardPlay> seq)
    {
        var cur = state;
        foreach (var play in seq)
        {
            var next = SimulatePlay(cur, play.Card, play.Target);
            if (next == null) break;
            cur = next;
        }
        return cur;
    }

    /// <summary>
    /// Apply card effects to simulated state. Ironclad-specific card knowledge.
    /// </summary>
    private static void ApplyCardEffect(
        CardInfo card, EnemyInfo? target,
        ref int hp, ref int block, ref int energy, ref int strength,
        List<EnemyInfo> enemies, List<CardInfo> hand, List<PowerInfo> powers)
    {
        string id = card.Id.Replace("+", "").ToLower();

        switch (id)
        {
            // ── Starter ──────────────────────────────────────────────
            case "strike":
                DealDamage(target, 6 + strength, enemies); break;
            case "defend":
                block += 5; break;
            case "bash":
                DealDamage(target, 8 + strength, enemies);
                ApplyVulnerable(target, 2, enemies); break;

            // ── Common Attacks ────────────────────────────────────────
            case "anger":
                DealDamage(target, 6 + strength, enemies); break;
            case "cleave":
                DealDamageAll(5 + strength, enemies); break;
            case "clothesline":
                DealDamage(target, 12 + strength, enemies); break;
            case "ironwave":
                DealDamage(target, 5 + strength, enemies);
                block += 5; break;
            case "pommelstrike":
                DealDamage(target, 9 + strength, enemies); break;
            case "swordboomerang":
                for (int i = 0; i < 3; i++) DealDamageRandom(3 + strength, enemies); break;
            case "thunderclap":
                DealDamageAll(4 + strength, enemies); break;
            case "twinstrike":
                DealDamage(target, 5 + strength, enemies);
                DealDamage(target, 5 + strength, enemies); break;
            case "wildstrike":
                DealDamage(target, 12 + strength, enemies); break;
            case "headbutt":
                DealDamage(target, 9 + strength, enemies); break;
            case "heavyblade":
                DealDamage(target, 14 + strength * 3, enemies); break; // scales 3x with strength

            // ── Common Skills ─────────────────────────────────────────
            case "armaments":
                block += 5; break;
            case "flexcard":
                strength += 2; break;
            case "shrugitoff":
                block += 8; break;
            case "truegrit":
                block += 7; break;
            case "warcry":
                break; // draw effect not simulated

            // ── Common Powers ─────────────────────────────────────────
            case "inflame":
                strength += 2; break;
            case "metallicize":
                block += 3; break; // approximate: +3 block per turn

            // ── Uncommon Attacks ──────────────────────────────────────
            case "carnage":
                DealDamage(target, 20 + strength, enemies); break;
            case "dropkick":
                DealDamage(target, 5 + strength, enemies); break;
            case "hemokinesis":
                hp -= 2;
                DealDamage(target, 15 + strength, enemies); break;
            case "pummel":
                for (int i = 0; i < 4; i++) DealDamage(target, 2 + strength, enemies); break;
            case "rampage":
                DealDamage(target, 8 + strength, enemies); break;
            case "recklesscharge":
                DealDamage(target, 7 + strength, enemies); break;
            case "whirlwind":
                // X cost: use all remaining energy
                for (int i = 0; i < energy; i++) DealDamageAll(5 + strength, enemies);
                energy = 0; break;

            // ── Uncommon Skills ───────────────────────────────────────
            case "battletrance":
                break; // draw not simulated
            case "bloodforblood":
                DealDamage(target, 18 + strength, enemies); break;
            case "burningpact":
                break; // exhaust + draw not simulated
            case "disarm":
                // reduce enemy strength by 2 (approximate)
                if (target != null)
                {
                    var idx = enemies.IndexOf(target);
                    if (idx >= 0)
                        enemies[idx] = enemies[idx] with {
                            IntentDamage = Math.Max(0, enemies[idx].IntentDamage - 2)
                        };
                } break;
            case "entrench":
                block *= 2; break;
            case "ghostlyarmor":
                block += 10; break;
            case "intimidate":
                break; // weak not simulated
            case "powerthrough":
                block += 15; break;
            case "secondwind":
                block += 5; break; // approximate
            case "seeinred":
                energy += 2; break;
            case "sentinel":
                block += 5; break;
            case "shockwave":
                break; // weak/vulnerable not simulated
            case "spotweakness":
                strength += 3; break;
            case "seeingred":
                energy += 2; break;

            // ── Rare Attacks ──────────────────────────────────────────
            case "bludgeon":
                DealDamage(target, 32 + strength, enemies); break;
            case "feed":
                DealDamage(target, 10 + strength, enemies); break;
            case "immolate":
                DealDamageAll(21 + strength, enemies); break;
            case "reaper":
                int reapDmg = 4 + strength;
                foreach (var e in enemies.Where(e => e.Hp > 0))
                {
                    int dealt = Math.Min(e.Hp, reapDmg);
                    DealDamage(e, reapDmg, enemies);
                    hp = Math.Min(hp + dealt, 999); // heal for damage dealt (approx)
                } break;

            // ── Rare Skills ───────────────────────────────────────────
            case "impervious":
                block += 30; break;
            case "limitbreak":
                strength *= 2; break;
            case "offering":
                hp -= 6;
                energy += 2; break;

            default:
                // Unknown card: do nothing in simulation
                break;
        }
    }

    private static void DealDamage(EnemyInfo? target, int damage, List<EnemyInfo> enemies)
    {
        if (target == null) return;
        int idx = enemies.IndexOf(target);
        if (idx < 0) return;

        // Apply vulnerable multiplier
        bool vulnerable = enemies[idx].Powers.Any(p => p.Id.Contains("Vulnerable"));
        int effective = vulnerable ? (int)(damage * 1.5f) : damage;

        int newHp = Math.Max(0, enemies[idx].Hp - Math.Max(0, effective - enemies[idx].Block));
        int newBlock = Math.Max(0, enemies[idx].Block - effective);
        enemies[idx] = enemies[idx] with { Hp = newHp, Block = newBlock };
    }

    private static void DealDamageAll(int damage, List<EnemyInfo> enemies)
    {
        for (int i = 0; i < enemies.Count; i++)
        {
            if (enemies[i].Hp <= 0) continue;
            bool vulnerable = enemies[i].Powers.Any(p => p.Id.Contains("Vulnerable"));
            int effective = vulnerable ? (int)(damage * 1.5f) : damage;
            int newHp = Math.Max(0, enemies[i].Hp - Math.Max(0, effective - enemies[i].Block));
            int newBlock = Math.Max(0, enemies[i].Block - effective);
            enemies[i] = enemies[i] with { Hp = newHp, Block = newBlock };
        }
    }

    private static void DealDamageRandom(int damage, List<EnemyInfo> enemies)
    {
        // For simulation: hit the weakest enemy (deterministic approximation)
        var alive = enemies.Where(e => e.Hp > 0).ToList();
        if (alive.Any()) DealDamage(alive.OrderBy(e => e.Hp).First(), damage, enemies);
    }

    private static void ApplyVulnerable(EnemyInfo? target, int stacks, List<EnemyInfo> enemies)
    {
        if (target == null) return;
        int idx = enemies.IndexOf(target);
        if (idx < 0) return;

        var powers = enemies[idx].Powers.ToList();
        var existing = powers.FirstOrDefault(p => p.Id == "Vulnerable");
        if (existing != null)
            powers = powers.Select(p => p.Id == "Vulnerable" ? p with { Amount = p.Amount + stacks } : p).ToList();
        else
            powers.Add(new PowerInfo("Vulnerable", stacks));

        enemies[idx] = enemies[idx] with { Powers = powers };
    }

    /// <summary>
    /// Score an end state. Higher = better.
    /// Priority: HP remaining > potions preserved > enemies killed.
    /// </summary>
    private static float EvaluateEndState(CombatSnapshot state)
    {
        float score = 0f;

        // 1. HP remaining (most important)
        score += state.PlayerHp * HP_WEIGHT;

        // 2. Potions preserved (don't want to use them)
        score += state.Potions.Count * POTION_PENALTY;

        // 3. Enemies killed
        int killed = state.Enemies.Count(e => e.Hp <= 0);
        score += killed * KILL_BONUS;

        // 4. Damage dealt to surviving enemies (tiebreaker)
        float totalEnemyHp = state.Enemies.Sum(e => e.Hp);
        score -= totalEnemyHp * DAMAGE_DEALT_BONUS;

        // 5. Block built (minor bonus)
        score += state.PlayerBlock * 0.5f;

        return score;
    }

    private static List<EnemyInfo?> GetTargetsForCard(CardInfo card, CombatSnapshot state)
    {
        var living = state.Enemies.Where(e => e.Hp > 0).ToList();

        return card.TargetType switch
        {
            "AnyEnemy" => living.Cast<EnemyInfo?>().ToList(),
            "AllEnemies" => new List<EnemyInfo?> { null }, // null = all enemies
            "Self" or "None" => new List<EnemyInfo?> { null },
            _ => new List<EnemyInfo?> { null }
        };
    }

    #endregion

    #region SimpleHeuristic (fallback)

    private Decision SimpleHeuristicDecision(CombatSnapshot state)
    {
        var playable = GetPlayableCards(state);
        if (!playable.Any()) return EndTurn("No playable cards");

        var living = state.Enemies.Where(e => e.Hp > 0).ToList();

        // 0-cost cards first (free value)
        var free = playable.Where(c => c.EnergyCost == 0).ToList();
        if (free.Any())
        {
            var card = free.First();
            var target = card.CardType == "Attack" ? SelectTarget(living) : null;
            return PlayCard(card, target, 15f, $"Free card: {card.Id}");
        }

        // Bash if enemy not vulnerable
        var bash = playable.FirstOrDefault(c => c.Id.StartsWith("Bash"));
        if (bash != null)
        {
            var nonVuln = living.FirstOrDefault(e => !e.Powers.Any(p => p.Id.Contains("Vulnerable")));
            if (nonVuln != null)
                return PlayCard(bash, nonVuln, 14f, "Bash to apply Vulnerable");
        }

        // Attack vulnerable enemy
        var vuln = living.FirstOrDefault(e => e.Powers.Any(p => p.Id.Contains("Vulnerable")));
        var attacks = playable.Where(c => c.CardType == "Attack").ToList();
        if (vuln != null && attacks.Any())
        {
            var best = attacks.OrderByDescending(EstimateDamage).First();
            return PlayCard(best, vuln, 13f, $"Hit vulnerable {vuln.Id}");
        }

        // Attack
        if (living.Any() && attacks.Any())
        {
            var best = attacks.OrderByDescending(EstimateDamage).First();
            return PlayCard(best, SelectTarget(living), 10f, $"Attack: {best.Id}");
        }

        // Skill
        var skills = playable.Where(c => c.CardType == "Skill").ToList();
        if (skills.Any())
        {
            var best = skills.OrderByDescending(EstimateDefense).First();
            return PlayCard(best, null, 8f, $"Skill: {best.Id}");
        }

        return EndTurn("No suitable cards");
    }

    #endregion

    #region Helpers

    private static List<CardInfo> GetPlayableCards(CombatSnapshot state) =>
        state.Hand.Where(c => c.IsPlayable && c.EnergyCost >= 0 && c.EnergyCost <= state.PlayerEnergy).ToList();

    private static Decision EndTurn(string reason) =>
        new(ActionType.EndTurn, null, null, 0f, reason);

    private static Decision PlayCard(CardInfo card, EnemyInfo? target, float score, string reason) =>
        new(ActionType.PlayCard, card, target, score, reason);

    private static EnemyInfo? SelectTarget(List<EnemyInfo> enemies)
    {
        if (!enemies.Any()) return null;
        // Focus weakest to remove threats faster
        return enemies.OrderBy(e => e.Hp).First();
    }

    private static float EstimateDamage(CardInfo card)
    {
        string id = card.Id.Replace("+", "").ToLower();
        return id switch
        {
            "bludgeon" => 32f,
            "immolate" => 21f,
            "carnage" => 20f,
            "hemokinesis" => 15f,
            "heavyblade" => 14f,
            "clothesline" => 12f,
            "wildstrike" => 12f,
            "headbutt" => 9f,
            "pommelstrike" => 9f,
            "bash" => 8f,
            "cleave" => 8f,
            "twinstrike" => 10f,
            "strike" => 6f,
            "anger" => 6f,
            _ => 5f
        };
    }

    private static float EstimateDefense(CardInfo card)
    {
        string id = card.Id.Replace("+", "").ToLower();
        return id switch
        {
            "impervious" => 30f,
            "powerthrough" => 15f,
            "entrench" => 12f,
            "ghostlyarmor" => 10f,
            "shrugitoff" => 8f,
            "truegrit" => 7f,
            "sentinel" => 5f,
            "defend" => 5f,
            "ironwave" => 5f,
            _ => 0f
        };
    }

    private static bool IsHealingPotion(string id) =>
        id.Contains("Health") || id.Contains("Fairy") || id.Contains("Regen");

    private static bool IsDefensivePotion(string id) =>
        id.Contains("Block") || id.Contains("Iron") || id.Contains("Dexterity");

    #endregion

    private record DecisionRecord
    {
        public int TurnNumber;
        public string CardId = "";
        public float Score;
        public float HpRatio;
        public bool Success;
        public DateTime Timestamp;
    }
}
