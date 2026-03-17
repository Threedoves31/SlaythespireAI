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
