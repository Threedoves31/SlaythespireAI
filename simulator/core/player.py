"""
Player state for the combat simulator.
"""
from __future__ import annotations
import random
from dataclasses import dataclass, field
from typing import List, Optional, TYPE_CHECKING
from .card import CardInstance, make_card, IRONCLAD_STARTER_DECK
from .power import Power, make_power, VulnerablePower, WeakPower, FrailPower, BarricadePower

if TYPE_CHECKING:
    from .combat import CombatState


@dataclass
class PlayerState:
    hp: int
    max_hp: int
    block: int = 0
    energy: int = 3
    max_energy: int = 3
    powers: List[Power] = field(default_factory=list)

    # Card piles
    deck: List[CardInstance] = field(default_factory=list)
    draw_pile: List[CardInstance] = field(default_factory=list)
    hand: List[CardInstance] = field(default_factory=list)
    discard_pile: List[CardInstance] = field(default_factory=list)
    exhaust_pile: List[CardInstance] = field(default_factory=list)

    @classmethod
    def ironclad(cls, hp: int = 80) -> "PlayerState":
        p = cls(hp=hp, max_hp=hp)
        p.deck = [make_card(c) for c in IRONCLAD_STARTER_DECK]
        return p

    # ── HP ────────────────────────────────────────────────────────────────────

    @property
    def is_alive(self) -> bool:
        return self.hp > 0

    def lose_hp(self, amount: int) -> int:
        """Apply damage after block. Returns actual HP lost."""
        blocked = min(self.block, amount)
        self.block = max(0, self.block - amount)
        hp_loss = max(0, amount - blocked)
        self.hp = max(0, self.hp - hp_loss)
        return hp_loss

    def heal(self, amount: int):
        self.hp = min(self.max_hp, self.hp + amount)

    def gain_block(self, amount: int):
        frail = self.get_power("Frail")
        if frail and frail.amount > 0:
            amount = int(amount * 0.75)
        dex = self.get_power("Dexterity")
        if dex:
            amount += dex.amount
        self.block = min(999, self.block + max(0, amount))

    # ── Powers ────────────────────────────────────────────────────────────────

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
        # Remove zero/negative duration powers
        self.powers = [p for p in self.powers if p.amount != 0 or not p.is_debuff]

    def apply_debuff(self, power_id: str, amount: int):
        """Apply a debuff, respecting Artifact."""
        artifact = self.get_power("Artifact")
        if artifact and artifact.amount > 0:
            artifact.amount -= 1
            return
        self.add_power(make_power(power_id, amount))

    # ── Cards ─────────────────────────────────────────────────────────────────

    def start_combat(self, rng: random.Random):
        """Shuffle deck into draw pile at combat start."""
        self.draw_pile = list(self.deck)
        rng.shuffle(self.draw_pile)
        self.hand.clear()
        self.discard_pile.clear()
        self.exhaust_pile.clear()

    def draw_cards(self, n: int, rng: random.Random):
        for _ in range(n):
            if not self.draw_pile:
                if not self.discard_pile:
                    break
                self.draw_pile = list(self.discard_pile)
                self.discard_pile.clear()
                rng.shuffle(self.draw_pile)
            if self.draw_pile:
                self.hand.append(self.draw_pile.pop())

    def discard_hand(self):
        self.discard_pile.extend(self.hand)
        self.hand.clear()

    def exhaust_card(self, card: CardInstance):
        if card in self.hand:
            self.hand.remove(card)
        self.exhaust_pile.append(card)
        # FeelNoPain trigger
        fnp = self.get_power("FeelNoPain")
        if fnp:
            self.gain_block(fnp.amount)

    # ── Turn lifecycle ────────────────────────────────────────────────────────

    def start_turn(self, rng: random.Random, draw_count: int = 5):
        # Clear block unless Barricade
        if not self.get_power("Barricade"):
            self.block = 0
        # Reset energy
        self.energy = self.max_energy
        # Tick powers
        for p in list(self.powers):
            p.at_turn_start(self)
        self.powers = [p for p in self.powers if p.amount > 0 or not p.is_debuff]
        # Draw cards
        self.draw_cards(draw_count, rng)

    def end_turn(self):
        # Tick end-of-turn powers
        for p in list(self.powers):
            p.at_turn_end(self)
        self.powers = [p for p in self.powers if p.amount > 0 or not p.is_debuff]
        self.discard_hand()

    # ── Damage calculation ────────────────────────────────────────────────────

    def calc_attack_damage(self, base_damage: int) -> int:
        """Calculate outgoing attack damage with Strength and Weak."""
        strength = self.get_power("Strength")
        dmg = base_damage + (strength.amount if strength else 0)
        weak = self.get_power("Weak")
        if weak and weak.amount > 0:
            dmg = int(dmg * 0.75)
        return max(0, dmg)

    def __repr__(self) -> str:
        return (f"Player(hp={self.hp}/{self.max_hp} block={self.block} "
                f"energy={self.energy} hand={len(self.hand)} "
                f"draw={len(self.draw_pile)} discard={len(self.discard_pile)})")
