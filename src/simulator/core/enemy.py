"""
Enemy state and AI move patterns for Act 1 enemies.
"""
from __future__ import annotations
import random
from dataclasses import dataclass, field
from enum import Enum
from typing import List, Optional, Callable, TYPE_CHECKING
from .power import Power, make_power

if TYPE_CHECKING:
    from .combat import CombatState


class IntentType(Enum):
    ATTACK = "Attack"
    DEFEND = "Defend"
    BUFF = "Buff"
    DEBUFF = "Debuff"
    ATTACK_DEBUFF = "AttackDebuff"
    ATTACK_BUFF = "AttackBuff"
    SLEEP = "Sleep"
    UNKNOWN = "Unknown"


@dataclass
class Move:
    name: str
    intent: IntentType
    damage: int = 0       # per hit
    hits: int = 1
    block: int = 0
    effects: List[tuple] = field(default_factory=list)  # [(power_id, amount, target)]


@dataclass
class EnemyState:
    id: str
    name: str
    hp: int
    max_hp: int
    block: int = 0
    powers: List[Power] = field(default_factory=list)
    move_history: List[str] = field(default_factory=list)
    current_move: Optional[Move] = None
    _move_fn: Optional[Callable] = field(default=None, repr=False)

    @property
    def is_alive(self) -> bool:
        return self.hp > 0

    def get_power(self, power_id: str) -> Optional[Power]:
        for p in self.powers:
            if p.id == power_id:
                return p
        return None

    def add_power(self, power: Power):
        existing = self.get_power(power.id)
        if existing:
            existing.stack(power.amount)
        else:
            self.powers.append(power)

    def gain_block(self, amount: int):
        self.block = min(999, self.block + amount)

    def lose_hp(self, amount: int) -> int:
        """Take damage after block. Returns actual HP lost."""
        blocked = min(self.block, amount)
        self.block = max(0, self.block - amount)
        hp_loss = max(0, amount - blocked)
        self.hp = max(0, self.hp - hp_loss)
        return hp_loss

    def heal(self, amount: int):
        self.hp = min(self.max_hp, self.hp + amount)

    def roll_move(self, rng: random.Random, turn: int):
        if self._move_fn:
            self.current_move = self._move_fn(self, rng, turn, self.move_history)
            if self.current_move:
                self.move_history.append(self.current_move.name)

    def start_turn(self):
        self.block = 0
        for p in list(self.powers):
            p.at_turn_start(self)
        self.powers = [p for p in self.powers if p.amount > 0 or not p.is_debuff]

    def end_turn(self):
        for p in list(self.powers):
            p.at_turn_end(self)
        self.powers = [p for p in self.powers if p.amount > 0 or not p.is_debuff]

    def calc_attack_damage(self, base_damage: int) -> int:
        strength = self.get_power("Strength")
        dmg = base_damage + (strength.amount if strength else 0)
        weak = self.get_power("Weak")
        if weak and weak.amount > 0:
            dmg = int(dmg * 0.75)
        return max(0, dmg)

    def __repr__(self) -> str:
        intent_str = ""
        if self.current_move:
            m = self.current_move
            if m.damage > 0:
                intent_str = f" [{m.intent.value} {m.damage}x{m.hits}]"
            else:
                intent_str = f" [{m.intent.value}]"
        return f"{self.name}(hp={self.hp}/{self.max_hp} block={self.block}{intent_str})"


# ─── Move helper functions ────────────────────────────────────────────────────

def _last_n(history: List[str], n: int, move: str) -> bool:
    """Check if the last n moves were all the same move."""
    return len(history) >= n and all(h == move for h in history[-n:])


# ─── Act 1 Enemy Definitions ─────────────────────────────────────────────────

def _cultist_move(self, rng, turn, history):
    if turn == 1:
        return Move("Incantation", IntentType.BUFF,
                    effects=[("Ritual", 3, "self")])
    return Move("DarkStrike", IntentType.ATTACK, damage=6)


def _jaw_worm_move(self, rng, turn, history):
    if turn == 1:
        return Move("Chomp", IntentType.ATTACK, damage=11)
    roll = rng.random()
    if roll < 0.45 and not _last_n(history, 2, "Chomp"):
        return Move("Chomp", IntentType.ATTACK, damage=11)
    elif roll < 0.45 + 0.30 and not _last_n(history, 3, "Thrash"):
        return Move("Thrash", IntentType.ATTACK_DEBUFF, damage=7,
                    effects=[("Weak", 1, "player")])
    else:
        return Move("Bellow", IntentType.ATTACK_BUFF, damage=0,
                    effects=[("Strength", 3, "self"), ("Block", 6, "self")])


def _louse_move(self, rng, turn, history):
    # Curl Up on first hit (handled in combat)
    if rng.random() < 0.5 and not _last_n(history, 2, "Bite"):
        return Move("Bite", IntentType.ATTACK, damage=7)
    return Move("Grow", IntentType.BUFF, effects=[("Strength", 3, "self")])


def _acid_slime_m_move(self, rng, turn, history):
    roll = rng.random()
    if roll < 0.3 and not _last_n(history, 2, "Corrosive Spit"):
        return Move("Corrosive Spit", IntentType.ATTACK_DEBUFF, damage=11,
                    effects=[("Slimed", 1, "deck")])
    elif roll < 0.6 and not _last_n(history, 2, "Tackle"):
        return Move("Tackle", IntentType.ATTACK, damage=16)
    else:
        return Move("Lick", IntentType.DEBUFF, effects=[("Weak", 1, "player")])


def _spike_slime_m_move(self, rng, turn, history):
    if rng.random() < 0.3 and not _last_n(history, 2, "Flame Tackle"):
        return Move("Flame Tackle", IntentType.ATTACK_DEBUFF, damage=8,
                    effects=[("Slimed", 1, "deck")])
    return Move("Lick", IntentType.DEBUFF, effects=[("Frail", 1, "player")])


def _gremlin_nob_move(self, rng, turn, history):
    if turn == 1:
        return Move("Bellow", IntentType.BUFF, effects=[("Enrage", 2, "self")])
    if rng.random() < 0.33 and not _last_n(history, 2, "Skull Bash"):
        return Move("Skull Bash", IntentType.ATTACK_DEBUFF, damage=6,
                    effects=[("Vulnerable", 2, "player")])
    return Move("Rush", IntentType.ATTACK, damage=14)


def _lagavulin_move(self, rng, turn, history):
    if turn <= 3:
        return Move("Sleep", IntentType.SLEEP)
    if turn == 4:
        return Move("Wake Up", IntentType.BUFF,
                    effects=[("Strength", 18, "self"), ("Dexterity", -2, "self")])
    if rng.random() < 0.45 and not _last_n(history, 2, "Attack"):
        return Move("Attack", IntentType.ATTACK, damage=18)
    return Move("Siphon Soul", IntentType.DEBUFF,
                effects=[("Strength", -1, "player"), ("Dexterity", -1, "player")])


def _sentries_move(self, rng, turn, history):
    # Alternates between Beam and Bolt
    if turn % 2 == 1:
        return Move("Beam", IntentType.ATTACK, damage=9)
    return Move("Bolt", IntentType.DEBUFF, effects=[("Dazed", 1, "deck")])


def _hexaghost_move(self, rng, turn, history):
    if turn == 1:
        return Move("Activate", IntentType.BUFF)
    if turn % 7 == 0:
        return Move("Inferno", IntentType.ATTACK, damage=6, hits=6)
    if turn % 2 == 0:
        return Move("Divider", IntentType.ATTACK, damage=6, hits=6)
    return Move("Sear", IntentType.ATTACK_DEBUFF, damage=6,
                effects=[("Burn", 1, "deck")])


# ─── Enemy Factory ────────────────────────────────────────────────────────────

def make_enemy(enemy_id: str, rng: random.Random) -> EnemyState:
    """Create an enemy with randomized HP."""
    configs = {
        "Cultist":      ("Cultist",     rng.randint(48, 54),  _cultist_move),
        "JawWorm":      ("Jaw Worm",    rng.randint(40, 44),  _jaw_worm_move),
        "AcidSlimeM":   ("Acid Slime",  rng.randint(28, 32),  _acid_slime_m_move),
        "SpikeSlimeM":  ("Spike Slime", rng.randint(28, 32),  _spike_slime_m_move),
        "RedLouse":     ("Red Louse",   rng.randint(10, 15),  _louse_move),
        "GreenLouse":   ("Green Louse", rng.randint(10, 15),  _louse_move),
        "GremlinNob":   ("Gremlin Nob", rng.randint(82, 86),  _gremlin_nob_move),
        "Lagavulin":    ("Lagavulin",   rng.randint(109, 111),_lagavulin_move),
        "Sentry":       ("Sentry",      rng.randint(38, 42),  _sentries_move),
        "Hexaghost":    ("Hexaghost",   rng.randint(250, 250),_hexaghost_move),
    }
    if enemy_id not in configs:
        raise ValueError(f"Unknown enemy: {enemy_id}")
    name, hp, move_fn = configs[enemy_id]
    e = EnemyState(id=enemy_id, name=name, hp=hp, max_hp=hp, _move_fn=move_fn)
    # CurlUp for louses
    if enemy_id in ("RedLouse", "GreenLouse"):
        e.add_power(make_power("CurlUp", rng.randint(3, 7)))
    return e


# Act 1 encounter pools
ACT1_NORMAL_ENCOUNTERS = [
    ["Cultist"],
    ["JawWorm"],
    ["RedLouse", "GreenLouse"],
    ["AcidSlimeM"],
    ["SpikeSlimeM"],
    ["RedLouse", "RedLouse"],
    ["GreenLouse", "GreenLouse"],
]

ACT1_ELITE_ENCOUNTERS = [
    ["GremlinNob"],
    ["Lagavulin"],
    ["Sentry", "Sentry", "Sentry"],
]

ACT1_BOSS_ENCOUNTERS = [
    ["Hexaghost"],
]


def random_encounter(encounter_type: str, rng: random.Random) -> List[EnemyState]:
    if encounter_type == "normal":
        pool = ACT1_NORMAL_ENCOUNTERS
    elif encounter_type == "elite":
        pool = ACT1_ELITE_ENCOUNTERS
    elif encounter_type == "boss":
        pool = ACT1_BOSS_ENCOUNTERS
    else:
        raise ValueError(f"Unknown encounter type: {encounter_type}")
    enemy_ids = rng.choice(pool)
    return [make_enemy(eid, rng) for eid in enemy_ids]
