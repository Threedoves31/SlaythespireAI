// AI Card Selector - Smart card selection for cards that require choices.
// Handles: True Grit (exhaust), Burning Pact (exhaust), Headbutt (discard pile),
//          Armaments+ (upgrade), Dual Wield (duplicate), etc.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.TestSupport;

namespace STS2AIBot.AI;

/// <summary>
/// Smart card selector that makes intelligent choices for card selection prompts.
/// Implements ICardSelector interface used by the game for all card choices.
/// </summary>
public class AICardSelector : ICardSelector
{
    // Card value categories for exhaust priority (lowest value first)
    private static readonly HashSet<string> LowValueCards = new()
    {
        "Wound", "Dazed", "Slimed", "Burn", "Void",  // Status cards
        "Strike", "Strike+",                          // Basic attacks
    };

    private static readonly HashSet<string> MediumValueCards = new()
    {
        "Defend", "Defend+",                          // Basic defense
        "Anger", "Anger+",                            // 0-cost attack
    };

    private static readonly HashSet<string> HighValueCards = new()
    {
        "Bash", "Bash+",                              // Vulnerable application
        "PommelStrike", "PommelStrike+",              // Draw + damage
        "ShrugItOff", "ShrugItOff+",                  // Block + draw
        "BattleTrance", "BattleTrance+",              // Big draw
        "Inflame", "Inflame+",                        // Strength
        "LimitBreak", "LimitBreak+",                  // Double strength
    };

    // Cards that are good to duplicate
    private static readonly HashSet<string> DuplicateTargets = new()
    {
        "Bash", "Bash+",
        "PommelStrike", "PommelStrike+",
        "ShrugItOff", "ShrugItOff+",
        "BattleTrance", "BattleTrance+",
        "Inflame", "Inflame+",
        "HeavyBlade", "HeavyBlade+",
        "Feed", "Feed+",
        "Immolate", "Immolate+",
    };

    // Cards that are good to retrieve from discard pile
    private static readonly HashSet<string> RetrieveTargets = new()
    {
        "Bash", "Bash+",
        "PommelStrike", "PommelStrike+",
        "ShrugItOff", "ShrugItOff+",
        "BattleTrance", "BattleTrance+",
        "Inflame", "Inflame+",
        "LimitBreak", "LimitBreak+",
        "Impervious", "Impervious+",
    };

    public AICardSelector()
    {
        Log.Info("[AICardSelector] Initialized");
    }

    /// <summary>
    /// Called when the game needs card selection (exhaust, discard pile, etc.)
    /// </summary>
    public Task<IEnumerable<CardModel>> GetSelectedCards(
        IEnumerable<CardModel> options, 
        int minSelect, 
        int maxSelect)
    {
        var optionList = options.ToList();
        
        if (optionList.Count == 0)
        {
            Log.Info("[AICardSelector] No cards to select from");
            return Task.FromResult(Enumerable.Empty<CardModel>());
        }

        Log.Info($"[AICardSelector] Selecting {minSelect}-{maxSelect} from {optionList.Count} cards");

        // Sort cards by value (ascending - lowest first for exhaust, we'll reverse for retrieval)
        var sortedByValue = optionList
            .OrderBy(c => GetCardValue(c))
            .ToList();

        int selectCount = Math.Min(maxSelect, sortedByValue.Count);
        if (selectCount < minSelect)
            selectCount = Math.Min(minSelect, sortedByValue.Count);

        // Default: select lowest value cards (for exhaust effects like True Grit)
        var selected = sortedByValue.Take(selectCount).ToList();

        Log.Info($"[AICardSelector] Selected: {string.Join(", ", selected.Select(c => c.Id.Entry))}");

        return Task.FromResult((IEnumerable<CardModel>)selected);
    }

    /// <summary>
    /// Called when choosing a card reward after combat.
    /// </summary>
    public CardModel? GetSelectedCardReward(
        IReadOnlyList<CardCreationResult> options, 
        IReadOnlyList<CardRewardAlternative> alternatives)
    {
        if (options.Count == 0)
        {
            Log.Info("[AICardSelector] No card rewards to choose from");
            return null;
        }

        Log.Info($"[AICardSelector] Choosing from {options.Count} card rewards");

        // Score each card option
        var scored = options
            .Select((result, idx) => new { Card = result.Card, Score = ScoreRewardCard(result.Card), Index = idx })
            .OrderByDescending(x => x.Score)
            .ToList();

        var best = scored.First();
        Log.Info($"[AICardSelector] Selected reward: {best.Card.Id.Entry} (score: {best.Score})");

        return best.Card;
    }

    #region Card Evaluation

    /// <summary>
    /// Get card value for exhaust/discard decisions.
    /// Lower = better to exhaust, Higher = better to keep.
    /// </summary>
    private int GetCardValue(CardModel card)
    {
        string id = card.Id.Entry;

        // Status cards - always want to exhaust these
        if (LowValueCards.Contains(id))
            return 10;

        // Basic cards
        if (MediumValueCards.Contains(id))
            return 30;

        // High value cards - never want to exhaust
        if (HighValueCards.Contains(id))
            return 100;

        // Default: evaluate by card type
        return card.Type switch
        {
            CardType.Power => 80,      // Powers are usually valuable
            CardType.Attack => 40,     // Attacks are medium value
            CardType.Skill => 50,      // Skills are medium-high value
            CardType.Curse => 0,       // Always exhaust curses if possible
            CardType.Status => 5,      // Status cards are low value
            _ => 30
        };
    }

    /// <summary>
    /// Score a card for reward selection.
    /// Higher = better to pick.
    /// </summary>
    private int ScoreRewardCard(CardModel card)
    {
        string id = card.Id.Entry;
        int baseScore = 0;

        // Rarity bonus
        baseScore += card.Rarity switch
        {
            CardRarity.Rare => 50,
            CardRarity.Uncommon => 30,
            CardRarity.Common or CardRarity.Basic => 10,
            _ => 0
        };

        // Card type bonus
        baseScore += card.Type switch
        {
            CardType.Power => 40,     // Powers are often strong
            CardType.Attack => 20,
            CardType.Skill => 25,
            _ => 0
        };

        // Specific card bonuses
        if (HighValueCards.Contains(id) || DuplicateTargets.Contains(id))
            baseScore += 30;

        // Synergy cards (strength scaling, draw, etc.)
        if (id.Contains("Strength") || id.Contains("Draw") || id.Contains("Limit"))
            baseScore += 20;

        return baseScore;
    }

    /// <summary>
    /// Check if a card is good to duplicate (for Dual Wield).
    /// </summary>
    public static bool IsGoodDuplicateTarget(CardModel card)
    {
        string id = card.Id.Entry;
        return DuplicateTargets.Contains(id) || 
               card.Type == CardType.Power;
    }

    /// <summary>
    /// Check if a card is good to retrieve from discard pile (for Headbutt).
    /// </summary>
    public static bool IsGoodRetrieveTarget(CardModel card)
    {
        string id = card.Id.Entry;
        return RetrieveTargets.Contains(id);
    }

    #endregion
}