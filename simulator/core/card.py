"""
Card definitions for Ironclad starting deck + common Act 1 cards.
Based on STS2 decompiled code.
"""
from dataclasses import dataclass, field
from enum import Enum
from typing import Optional, TYPE_CHECKING

if TYPE_CHECKING:
    from .combat import CombatState


class CardType(Enum):
    ATTACK = "Attack"
    SKILL = "Skill"
    POWER = "Power"
    STATUS = "Status"
    CURSE = "Curse"


class TargetType(Enum):
    ANY_ENEMY = "AnyEnemy"
    ALL_ENEMIES = "AllEnemies"
    SELF = "Self"
    NONE = "None"


class CardRarity(Enum):
    STARTER = "Starter"
    COMMON = "Common"
    UNCOMMON = "Uncommon"
    RARE = "Rare"


@dataclass
class CardDef:
    """Static card definition (immutable template)."""
    id: str
    name: str
    card_type: CardType
    rarity: CardRarity
    target: TargetType
    cost: int          # base energy cost
    upgraded: bool = False

    # Effect parameters (base values)
    damage: int = 0
    block: int = 0
    draw: int = 0
    strength_gain: int = 0
    extra_damage_per_strength: int = 0  # for cards that scale with strength
    hits: int = 1      # number of hits for multi-hit attacks
    exhaust: bool = False
    innate: bool = False
    ethereal: bool = False

    def upgrade(self) -> "CardDef":
        """Return upgraded version of this card."""
        return UPGRADE_MAP.get(self.id, self)


@dataclass
class CardInstance:
    """A card in play — has a definition and runtime state."""
    definition: CardDef
    upgraded: bool = False

    @property
    def id(self) -> str:
        return self.definition.id + ("+" if self.upgraded else "")

    @property
    def cost(self) -> int:
        return self.definition.cost

    @property
    def card_type(self) -> CardType:
        return self.definition.card_type

    @property
    def target(self) -> TargetType:
        return self.definition.target

    def can_play(self, energy: int) -> bool:
        return energy >= self.cost

    def __repr__(self) -> str:
        return f"{self.id}[{self.cost}]"


# ─── Card Definitions ────────────────────────────────────────────────────────

def _card(id, name, ctype, rarity, target, cost, **kwargs) -> CardDef:
    return CardDef(id=id, name=name, card_type=ctype, rarity=rarity,
                   target=target, cost=cost, **kwargs)

A, S, P = CardType.ATTACK, CardType.SKILL, CardType.POWER
AE, ALL, SELF, NONE = TargetType.ANY_ENEMY, TargetType.ALL_ENEMIES, TargetType.SELF, TargetType.NONE
ST, C, U, R = CardRarity.STARTER, CardRarity.COMMON, CardRarity.UNCOMMON, CardRarity.RARE

CARD_DB: dict[str, CardDef] = {}

def _reg(*cards):
    for c in cards:
        CARD_DB[c.id] = c

# Starter cards
_reg(
    _card("Strike",       "Strike",       A, ST, AE,   1, damage=6),
    _card("Strike+",      "Strike+",      A, ST, AE,   1, damage=9),
    _card("Defend",       "Defend",       S, ST, SELF, 1, block=5),
    _card("Defend+",      "Defend+",      S, ST, SELF, 1, block=8),
    _card("Bash",         "Bash",         A, ST, AE,   2, damage=8),
    _card("Bash+",        "Bash+",        A, ST, AE,   2, damage=10),
)

# Common attacks
_reg(
    _card("Anger",        "Anger",        A, C, AE,    0, damage=6),
    _card("Anger+",       "Anger+",       A, C, AE,    0, damage=8),
    _card("Cleave",       "Cleave",       A, C, ALL,   1, damage=8),
    _card("Cleave+",      "Cleave+",      A, C, ALL,   1, damage=11),
    _card("Clothesline",  "Clothesline",  A, C, AE,    2, damage=12),
    _card("Clothesline+", "Clothesline+", A, C, AE,    2, damage=14),
    _card("IronWave",     "Iron Wave",    A, C, AE,    1, damage=5, block=5),
    _card("IronWave+",    "Iron Wave+",   A, C, AE,    1, damage=7, block=7),
    _card("PommelStrike", "Pommel Strike",A, C, AE,    1, damage=9, draw=1),
    _card("PommelStrike+","Pommel Strike+",A,C, AE,    1, damage=10,draw=2),
    _card("SwordBoomerang","Sword Boomerang",A,C,AE,   1, damage=3, hits=3),
    _card("SwordBoomerang+","Sword Boomerang+",A,C,AE, 1, damage=3, hits=4),
    _card("Thunderclap",  "Thunderclap",  A, C, ALL,   1, damage=4),
    _card("Thunderclap+", "Thunderclap+", A, C, ALL,   1, damage=7),
    _card("TwinStrike",   "Twin Strike",  A, C, AE,    1, damage=5, hits=2),
    _card("TwinStrike+",  "Twin Strike+", A, C, AE,    1, damage=7, hits=2),
    _card("WildStrike",   "Wild Strike",  A, C, AE,    1, damage=12),
    _card("WildStrike+",  "Wild Strike+", A, C, AE,    1, damage=17),
)

# Common skills
_reg(
    _card("Armaments",    "Armaments",    S, C, SELF,  1, block=5),
    _card("Armaments+",   "Armaments+",   S, C, SELF,  1, block=5),
    _card("FlexCard",     "Flex",         S, C, SELF,  0, strength_gain=2),
    _card("FlexCard+",    "Flex+",        S, C, SELF,  0, strength_gain=4),
    _card("Havoc",        "Havoc",        S, C, NONE,  1),
    _card("Havoc+",       "Havoc+",       S, C, NONE,  0),
    _card("Headbutt",     "Headbutt",     A, C, AE,    1, damage=9),
    _card("Headbutt+",    "Headbutt+",    A, C, AE,    1, damage=12),
    _card("HeavyBlade",   "Heavy Blade",  A, C, AE,    2, damage=14),
    _card("HeavyBlade+",  "Heavy Blade+", A, C, AE,    2, damage=14),
    _card("ShrugItOff",   "Shrug It Off", S, C, SELF,  1, block=8, draw=1),
    _card("ShrugItOff+",  "Shrug It Off+",S, C, SELF,  1, block=11,draw=1),
    _card("TrueGrit",     "True Grit",    S, C, SELF,  1, block=7),
    _card("TrueGrit+",    "True Grit+",   S, C, SELF,  1, block=9),
    _card("Warcry",       "Warcry",       S, C, SELF,  0, draw=1),
    _card("Warcry+",      "Warcry+",      S, C, SELF,  0, draw=2),
)

# Common powers
_reg(
    _card("Combust",      "Combust",      P, C, SELF,  1),
    _card("DarkEmbrace",  "Dark Embrace", P, C, SELF,  2),
    _card("Evolve",       "Evolve",       P, C, SELF,  1),
    _card("FeelNoPain",   "Feel No Pain", P, C, SELF,  1),
    _card("FireBreathing","Fire Breathing",P,C, SELF,  1),
    _card("Inflame",      "Inflame",      P, C, SELF,  1, strength_gain=2),
    _card("Inflame+",     "Inflame+",     P, C, SELF,  1, strength_gain=3),
    _card("Metallicize",  "Metallicize",  P, C, SELF,  1),
    _card("Rupture",      "Rupture",      P, C, SELF,  1),
    _card("Spot Weakness","Spot Weakness",P, C, SELF,  1),
)

# Uncommon attacks
_reg(
    _card("Carnage",      "Carnage",      A, U, AE,    2, damage=20, ethereal=True),
    _card("Carnage+",     "Carnage+",     A, U, AE,    2, damage=28, ethereal=True),
    _card("Dropkick",     "Dropkick",     A, U, AE,    1, damage=5),
    _card("Dropkick+",    "Dropkick+",    A, U, AE,    1, damage=8),
    _card("Hemokinesis",  "Hemokinesis",  A, U, AE,    1, damage=15),
    _card("Hemokinesis+", "Hemokinesis+", A, U, AE,    1, damage=20),
    _card("Pummel",       "Pummel",       A, U, AE,    1, damage=2, hits=4, exhaust=True),
    _card("Pummel+",      "Pummel+",      A, U, AE,    1, damage=2, hits=5, exhaust=True),
    _card("Rampage",      "Rampage",      A, U, AE,    1, damage=8),
    _card("Rampage+",     "Rampage+",     A, U, AE,    1, damage=8),
    _card("RecklessCharge","Reckless Charge",A,U,AE,   0, damage=7),
    _card("Whirlwind",    "Whirlwind",    A, U, ALL,   -1, damage=5),  # X cost
    _card("Whirlwind+",   "Whirlwind+",   A, U, ALL,   -1, damage=8),
)

# Uncommon skills
_reg(
    _card("BattleTrance", "Battle Trance",S, U, SELF,  0, draw=3),
    _card("BattleTrance+","Battle Trance+",S,U, SELF,  0, draw=4),
    _card("BloodForBlood","Blood for Blood",A,U,AE,    4, damage=18),
    _card("BloodForBlood+","Blood for Blood+",A,U,AE,  3, damage=22),
    _card("BurningPact",  "Burning Pact", S, U, SELF,  1, draw=2, exhaust=True),
    _card("BurningPact+", "Burning Pact+",S, U, SELF,  1, draw=3, exhaust=True),
    _card("Disarm",       "Disarm",       S, U, AE,    1, exhaust=True),
    _card("Disarm+",      "Disarm+",      S, U, AE,    1, exhaust=True),
    _card("DualWield",    "Dual Wield",   S, U, SELF,  1),
    _card("Entrench",     "Entrench",     S, U, SELF,  2),
    _card("Entrench+",    "Entrench+",    S, U, SELF,  1),
    _card("Ghostly Armor","Ghostly Armor",S, U, SELF,  1, block=10, ethereal=True),
    _card("Infernal Blade","Infernal Blade",S,U,SELF,  1, exhaust=True),
    _card("Intimidate",   "Intimidate",   S, U, ALL,   0, exhaust=True),
    _card("PowerThrough", "Power Through",S, U, SELF,  1, block=15),
    _card("Rage",         "Rage",         S, U, SELF,  0),
    _card("SecondWind",   "Second Wind",  S, U, SELF,  1),
    _card("SeeingRed",    "Seeing Red",   S, U, SELF,  1, exhaust=True),
    _card("SeeingRed+",   "Seeing Red+",  S, U, SELF,  0, exhaust=True),
    _card("Sentinel",     "Sentinel",     S, U, SELF,  1, block=5),
    _card("Sentinel+",    "Sentinel+",    S, U, SELF,  1, block=8),
    _card("Shockwave",    "Shockwave",    S, U, ALL,   2, exhaust=True),
    _card("Shockwave+",   "Shockwave+",   S, U, ALL,   2, exhaust=True),
    _card("SpotWeakness", "Spot Weakness",S, U, AE,    1),
)

# Rare attacks
_reg(
    _card("Bludgeon",     "Bludgeon",     A, R, AE,    3, damage=32),
    _card("Bludgeon+",    "Bludgeon+",    A, R, AE,    3, damage=42),
    _card("Feed",         "Feed",         A, R, AE,    1, damage=10, exhaust=True),
    _card("Feed+",        "Feed+",        A, R, AE,    1, damage=12, exhaust=True),
    _card("FiendFire",    "Fiend Fire",   A, R, AE,    2, exhaust=True),
    _card("Immolate",     "Immolate",     A, R, ALL,   2, damage=21),
    _card("Immolate+",    "Immolate+",    A, R, ALL,   2, damage=28),
    _card("Impervious",   "Impervious",   S, R, SELF,  2, block=30, exhaust=True),
    _card("Impervious+",  "Impervious+",  S, R, SELF,  2, block=40, exhaust=True),
    _card("LimitBreak",   "Limit Break",  S, R, SELF,  1, exhaust=True),
    _card("LimitBreak+",  "Limit Break+", S, R, SELF,  1),
    _card("Offering",     "Offering",     S, R, SELF,  0, draw=3, exhaust=True),
    _card("Offering+",    "Offering+",    S, R, SELF,  0, draw=5, exhaust=True),
    _card("Reaper",       "Reaper",       A, R, ALL,   2, exhaust=True),
    _card("Reaper+",      "Reaper+",      A, R, ALL,   2, exhaust=True),
)

# Status cards (added to deck by enemies)
_reg(
    _card("Wound",        "Wound",        S, CardRarity.STARTER, NONE, -1),
    _card("Dazed",        "Dazed",        S, CardRarity.STARTER, NONE, -1, ethereal=True),
    _card("Slimed",       "Slimed",       S, CardRarity.STARTER, NONE, 1, exhaust=True),
    _card("Burn",         "Burn",         S, CardRarity.STARTER, NONE, -1),
    _card("Void",         "Void",         S, CardRarity.STARTER, NONE, -1, ethereal=True),
)

# Upgrade map: base_id -> upgraded CardDef
UPGRADE_MAP: dict[str, CardDef] = {
    c.id: CARD_DB[c.id + "+"]
    for c in CARD_DB.values()
    if not c.id.endswith("+") and (c.id + "+") in CARD_DB
}


def make_card(card_id: str, upgraded: bool = False) -> CardInstance:
    """Create a CardInstance from a card ID."""
    if upgraded and (card_id + "+") in CARD_DB:
        return CardInstance(CARD_DB[card_id + "+"], upgraded=True)
    if card_id not in CARD_DB:
        raise ValueError(f"Unknown card: {card_id}")
    return CardInstance(CARD_DB[card_id], upgraded=upgraded)


# Ironclad starting deck
IRONCLAD_STARTER_DECK = [
    "Strike", "Strike", "Strike", "Strike", "Strike",
    "Defend", "Defend", "Defend", "Defend",
    "Bash",
]
