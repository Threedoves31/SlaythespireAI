"""
Core combat simulation engine.
Handles turn flow, card effects, damage calculation.
"""
from __future__ import annotations
import random
from dataclasses import dataclass, field
from typing import List, Optional, Tuple
from .card import CardInstance, CardType, TargetType, make_card
from .player import PlayerState
from .enemy import EnemyState
from .power import make_power


@dataclass
class CombatResult:
    won: bool
    turns: int
    hp_remaining: int
    hp_lost: int


class CombatState:
    def __init__(self, player: PlayerState, enemies: List[EnemyState], rng: random.Random):
        self.player = player
        self.enemies = enemies
        self.rng = rng
        self.turn = 0
        self.done = False
        self.result: Optional[CombatResult] = None
        self._initial_hp = player.hp

        # Start combat
        player.start_combat(rng)
        for e in enemies:
            e.roll_move(rng, 1)

    @property
    def alive_enemies(self) -> List[EnemyState]:
        return [e for e in self.enemies if e.is_alive]

    @property
    def is_over(self) -> bool:
        return self.done

    def start_player_turn(self):
        self.turn += 1
        self.player.start_turn(self.rng)

    def play_card(self, card: CardInstance, target: Optional[EnemyState] = None) -> bool:
        """
        Play a card. Returns True if successful.
        Handles damage, block, draw, powers, and special effects.
        """
        p = self.player
        if not card.can_play(p.energy):
            return False
        if card not in p.hand:
            return False

        # Spend energy (X-cost cards spend all energy)
        if card.cost == -1:
            x_val = p.energy
            p.energy = 0
        else:
            p.energy -= card.cost
            x_val = 0

        # Remove from hand
        p.hand.remove(card)

        d = card.definition
        ctype = d.card_type

        # ── Attack cards ──────────────────────────────────────────────────────
        if ctype == CardType.ATTACK:
            hits = d.hits
            if d.cost == -1:  # X-cost: hits = X
                hits = x_val

            for _ in range(hits):
                if target and target.is_alive:
                    self._deal_damage_to_enemy(d.damage, target)
                elif d.target == TargetType.ALL_ENEMIES:
                    for e in self.alive_enemies:
                        self._deal_damage_to_enemy(d.damage, e)

            # Special: Whirlwind hits all enemies X times
            if d.id.startswith("Whirlwind") and d.cost == -1:
                for _ in range(x_val - 1):  # already did 1 above
                    for e in self.alive_enemies:
                        self._deal_damage_to_enemy(d.damage, e)

        # ── Skill cards ───────────────────────────────────────────────────────
        elif ctype == CardType.SKILL:
            if d.block > 0:
                p.gain_block(d.block)
            if d.damage > 0 and target and target.is_alive:
                self._deal_damage_to_enemy(d.damage, target)

        # ── Power cards ───────────────────────────────────────────────────────
        elif ctype == CardType.POWER:
            if d.strength_gain > 0:
                p.add_power(make_power("Strength", d.strength_gain))
            # Specific power card effects
            self._apply_power_card(card, target)

        # Draw cards
        if d.draw > 0:
            p.draw_cards(d.draw, self.rng)

        # Exhaust
        if d.exhaust:
            p.exhaust_pile.append(card)
        else:
            p.discard_pile.append(card)

        # Check win condition
        self._check_win()
        return True

    def _deal_damage_to_enemy(self, base_damage: int, enemy: EnemyState):
        """Calculate and apply damage to an enemy."""
        dmg = self.player.calc_attack_damage(base_damage)
        # Vulnerable: take 50% more damage
        vuln = enemy.get_power("Vulnerable")
        if vuln and vuln.amount > 0:
            dmg = int(dmg * 1.5)
        # Intangible: reduce to 1
        intang = enemy.get_power("Intangible")
        if intang and intang.amount > 0:
            dmg = 1
        enemy.lose_hp(dmg)

    def _deal_damage_to_player(self, base_damage: int, enemy: EnemyState):
        """Calculate and apply damage to the player."""
        dmg = enemy.calc_attack_damage(base_damage)
        # Vulnerable: take 50% more damage
        vuln = self.player.get_power("Vulnerable")
        if vuln and vuln.amount > 0:
            dmg = int(dmg * 1.5)
        self.player.lose_hp(dmg)

    def _apply_power_card(self, card: CardInstance, target: Optional[EnemyState]):
        """Handle specific power card effects not covered by generic fields."""
        d = card.definition
        p = self.player
        # Metallicize, DemonForm, Barricade, etc. are handled by their Power classes
        # Here we just apply the power to the player
        power_map = {
            "Metallicize":   ("Metallicize",  d.block or 3),
            "DemonForm":     ("DemonForm",    2),
            "Barricade":     ("Barricade",    1),
            "FeelNoPain":    ("FeelNoPain",   3),
            "Inflame":       ("Strength",     d.strength_gain or 2),
            "Combust":       ("Combust",      1),
            "DarkEmbrace":   ("DarkEmbrace",  1),
            "Evolve":        ("Evolve",       1),
            "FireBreathing": ("FireBreathing",6),
            "Rupture":       ("Rupture",      1),
        }
        base_id = d.id.rstrip("+")
        if base_id in power_map:
            pid, amt = power_map[base_id]
            p.add_power(make_power(pid, amt))

    def end_player_turn(self):
        """End the player's turn and execute enemy turns."""
        self.player.end_turn()
        if self.done:
            return

        # Enemy turns
        for enemy in self.alive_enemies:
            if not self.player.is_alive:
                break
            self._execute_enemy_move(enemy)
            enemy.end_turn()

        if not self.player.is_alive:
            self.done = True
            self.result = CombatResult(
                won=False,
                turns=self.turn,
                hp_remaining=0,
                hp_lost=self._initial_hp
            )
            return

        # Roll next moves for alive enemies
        for enemy in self.alive_enemies:
            enemy.roll_move(self.rng, self.turn + 1)

    def _execute_enemy_move(self, enemy: EnemyState):
        """Execute the enemy's current move."""
        move = enemy.current_move
        if move is None:
            return

        p = self.player

        # Attack
        if move.damage > 0:
            for _ in range(move.hits):
                if p.is_alive:
                    self._deal_damage_to_player(move.damage, enemy)

        # Block
        if move.block > 0:
            enemy.gain_block(move.block)

        # Effects
        for effect_id, amount, target_str in move.effects:
            if target_str == "player":
                p.apply_debuff(effect_id, amount)
            elif target_str == "self":
                if effect_id == "Block":
                    enemy.gain_block(amount)
                else:
                    enemy.add_power(make_power(effect_id, amount))
            elif target_str == "deck":
                # Add status card to discard pile
                try:
                    status = make_card(effect_id)
                    p.discard_pile.append(status)
                except ValueError:
                    pass

    def _check_win(self):
        if not self.alive_enemies:
            self.done = True
            self.result = CombatResult(
                won=True,
                turns=self.turn,
                hp_remaining=self.player.hp,
                hp_lost=self._initial_hp - self.player.hp
            )

    def get_playable_cards(self) -> List[Tuple[int, CardInstance]]:
        """Return list of (hand_index, card) for playable cards."""
        return [
            (i, c) for i, c in enumerate(self.player.hand)
            if c.can_play(self.player.energy)
        ]

    def get_valid_targets(self, card: CardInstance) -> List[Optional[EnemyState]]:
        """Return valid targets for a card."""
        if card.target == TargetType.ANY_ENEMY:
            return self.alive_enemies
        return [None]  # self-targeting or AoE

    def __repr__(self) -> str:
        enemies_str = ", ".join(str(e) for e in self.alive_enemies)
        return (f"Combat(turn={self.turn} {self.player} | {enemies_str})")
