"""
Power/buff/debuff system for the simulator.
Covers the most common powers in Act 1.
"""
from dataclasses import dataclass, field
from enum import Enum
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from .combat import CombatState
    from .player import PlayerState
    from .enemy import EnemyState


class PowerStackType(Enum):
    STACKING = "Stacking"   # amount increases each application
    DURATION = "Duration"   # amount decreases each turn
    COUNTER  = "Counter"    # tracks a count


@dataclass
class Power:
    id: str
    amount: int
    stack_type: PowerStackType = PowerStackType.STACKING
    is_debuff: bool = False

    def stack(self, amount: int):
        self.amount += amount

    def at_turn_start(self, owner) -> None:
        """Called at the start of the owner's turn."""
        pass

    def at_turn_end(self, owner) -> None:
        """Called at the end of the owner's turn. Duration powers tick down."""
        if self.stack_type == PowerStackType.DURATION:
            self.amount -= 1


# ─── Player Powers ────────────────────────────────────────────────────────────

class StrengthPower(Power):
    def __init__(self, amount: int):
        super().__init__("Strength", amount)

class DexterityPower(Power):
    def __init__(self, amount: int):
        super().__init__("Dexterity", amount)

class BarricadePower(Power):
    """Block is not removed at start of turn."""
    def __init__(self):
        super().__init__("Barricade", 1)

class InflamePower(Power):
    def __init__(self, amount: int):
        super().__init__("Inflame", amount)
    def on_apply(self, owner):
        owner.add_power(StrengthPower(self.amount))

class MetallicizePower(Power):
    """Gain block at end of turn."""
    def __init__(self, amount: int):
        super().__init__("Metallicize", amount)
    def at_turn_end(self, owner):
        owner.gain_block(self.amount)

class FeelNoPainPower(Power):
    """Gain block when a card is exhausted."""
    def __init__(self, amount: int):
        super().__init__("FeelNoPain", amount)

class DemonFormPower(Power):
    """Gain strength at start of each turn."""
    def __init__(self, amount: int):
        super().__init__("DemonForm", amount)
    def at_turn_start(self, owner):
        owner.add_power(StrengthPower(self.amount))

# ─── Debuffs ──────────────────────────────────────────────────────────────────

class VulnerablePower(Power):
    """Take 50% more damage from attacks."""
    def __init__(self, amount: int):
        super().__init__("Vulnerable", amount, PowerStackType.DURATION, is_debuff=True)
    def at_turn_start(self, owner):
        if self.amount > 0:
            self.amount -= 1

class WeakPower(Power):
    """Deal 25% less damage with attacks."""
    def __init__(self, amount: int):
        super().__init__("Weak", amount, PowerStackType.DURATION, is_debuff=True)
    def at_turn_start(self, owner):
        if self.amount > 0:
            self.amount -= 1

class FrailPower(Power):
    """Gain 25% less block."""
    def __init__(self, amount: int):
        super().__init__("Frail", amount, PowerStackType.DURATION, is_debuff=True)
    def at_turn_start(self, owner):
        if self.amount > 0:
            self.amount -= 1

class PoisonPower(Power):
    """Lose HP equal to stacks at start of turn, then reduce by 1."""
    def __init__(self, amount: int):
        super().__init__("Poison", amount, PowerStackType.STACKING, is_debuff=True)
    def at_turn_start(self, owner):
        if self.amount > 0:
            owner.lose_hp(self.amount)
            self.amount -= 1

class StrengthDownPower(Power):
    """Lose strength at end of turn."""
    def __init__(self, amount: int):
        super().__init__("StrengthDown", amount, is_debuff=True)
    def at_turn_end(self, owner):
        owner.add_power(StrengthPower(-self.amount))
        self.amount = 0

class ArtifactPower(Power):
    """Negate the next debuff."""
    def __init__(self, amount: int):
        super().__init__("Artifact", amount)


# ─── Enemy Powers ─────────────────────────────────────────────────────────────

class CurlUpPower(Power):
    """Gain block when hit for the first time."""
    def __init__(self, amount: int):
        super().__init__("CurlUp", amount)

class PlatingPower(Power):
    """Gain block at start of turn."""
    def __init__(self, amount: int):
        super().__init__("Plating", amount)
    def at_turn_start(self, owner):
        owner.gain_block(self.amount)

class RegenPower(Power):
    """Heal HP at end of turn."""
    def __init__(self, amount: int):
        super().__init__("Regen", amount, PowerStackType.DURATION)
    def at_turn_end(self, owner):
        owner.heal(self.amount)
        super().at_turn_end(owner)

class IntangiblePower(Power):
    """Reduce all damage to 1."""
    def __init__(self, amount: int):
        super().__init__("Intangible", amount, PowerStackType.DURATION)
    def at_turn_start(self, owner):
        if self.amount > 0:
            self.amount -= 1


# ─── Power Registry ───────────────────────────────────────────────────────────

POWER_CLASSES = {
    "Strength":     StrengthPower,
    "Dexterity":    DexterityPower,
    "Barricade":    BarricadePower,
    "Inflame":      InflamePower,
    "Metallicize":  MetallicizePower,
    "FeelNoPain":   FeelNoPainPower,
    "DemonForm":    DemonFormPower,
    "Vulnerable":   VulnerablePower,
    "Weak":         WeakPower,
    "Frail":        FrailPower,
    "Poison":       PoisonPower,
    "StrengthDown": StrengthDownPower,
    "Artifact":     ArtifactPower,
    "CurlUp":       CurlUpPower,
    "Plating":      PlatingPower,
    "Regen":        RegenPower,
    "Intangible":   IntangiblePower,
}


def make_power(power_id: str, amount: int) -> Power:
    cls = POWER_CLASSES.get(power_id)
    if cls is None:
        return Power(power_id, amount)
    if power_id in ("Barricade",):
        return cls()
    return cls(amount)
