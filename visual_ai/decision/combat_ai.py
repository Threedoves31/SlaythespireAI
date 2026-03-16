"""
Combat AI decision module for STS2.
Decides which card to play and which target to select.
"""
import random
from typing import List, Optional, Tuple
from dataclasses import dataclass

from ..vision.combat_reader import CombatState, CardInfo, EnemyInfo


@dataclass
class Action:
    """AI action to execute."""
    type: str  # "play_card", "end_turn", "use_potion"
    card_index: int = -1  # Index in hand
    target_index: int = -1  # Enemy index
    potion_index: int = -1  # Potion slot


class CombatAI:
    """AI for making combat decisions."""

    def __init__(self, debug: bool = False):
        self.debug = debug
        self.actions_this_turn = 0
        self.max_actions_per_turn = 10

    def decide(self, state: CombatState) -> Action:
        """Decide what action to take based on current state."""
        if state is None:
            return Action(type="end_turn")

        # Reset counter if energy changed (new turn)
        # This is a simplification - proper turn tracking needed

        # Get playable cards
        playable_cards = [
            (i, card) for i, card in enumerate(state.hand)
            if card.is_playable and card.cost <= state.player_energy
        ]

        if not playable_cards:
            if self.debug:
                print("CombatAI: No playable cards, ending turn")
            return Action(type="end_turn")

        # Simple heuristic AI
        action = self._heuristic_decision(state, playable_cards)

        if self.debug:
            self._debug_action(action, state)

        return action

    def _heuristic_decision(self, state: CombatState, playable_cards: List[Tuple[int, CardInfo]]) -> Action:
        """Simple heuristic-based decision making."""
        # Priority order:
        # 1. Kill low HP enemies with attacks
        # 2. Block if under attack and taking damage
        # 3. Draw more cards if hand is small
        # 4. Play low-cost cards efficiently
        # 5. End turn when out of energy

        # Get alive enemies sorted by HP (ascending)
        alive_enemies = [e for e in state.enemies if e.hp > 0]
        enemies_sorted = sorted(alive_enemies, key=lambda e: e.hp)

        # Check if enemies are attacking this turn
        attacking_enemies = [e for e in alive_enemies if e.intent_type == "Attack"]

        # Player needs defense?
        needs_defense = (
            state.player_block == 0 and
            len(attacking_enemies) > 0 and
            state.player_energy > 0
        )

        # Get cards by type
        attack_cards = [(i, c) for i, c in playable_cards if c.card_type == "Attack"]
        skill_cards = [(i, c) for i, c in playable_cards if c.card_type == "Skill"]
        power_cards = [(i, c) for i, c in playable_cards if c.card_type == "Power"]

        # Decision logic
        if enemies_sorted and attack_cards:
            # Has enemies and attacks -> prioritize killing
            # Pick best target (lowest HP)
            target_enemy = enemies_sorted[0]
            target_idx = state.enemies.index(target_enemy)

            # Pick best attack card
            best_attack = self._pick_best_attack(attack_cards, state.player_energy)
            card_idx, card = best_attack

            return Action(
                type="play_card",
                card_index=card_idx,
                target_index=target_idx
            )

        elif needs_defense and skill_cards:
            # Under attack and have skills -> use defense
            best_skill = self._pick_best_defense(skill_cards, state.player_energy)
            card_idx, card = best_skill

            return Action(
                type="play_card",
                card_index=card_idx,
                target_index=-1  # Self-targeting
            )

        elif skill_cards:
            # Use skills (draw, block, buffs)
            best_skill = self._pick_best_skill(skill_cards, state)
            card_idx, card = best_skill

            return Action(
                type="play_card",
                card_index=card_idx,
                target_index=-1
            )

        elif power_cards:
            # Play powers
            best_power = power_cards[0]  # Just pick first for now
            card_idx, card = best_power

            return Action(
                type="play_card",
                card_index=card_idx,
                target_index=-1
            )

        else:
            # End turn
            return Action(type="end_turn")

    def _pick_best_attack(self, attack_cards: List[Tuple[int, CardInfo]], energy: int) -> Tuple[int, CardInfo]:
        """Pick the best attack card from playable attacks."""
        # Prioritize:
        # 1. Highest damage per energy
        # 2. Multi-hit cards for low HP enemies
        # 3. Low cost cards first

        best_score = -1
        best_attack = attack_cards[0]

        for idx, card in attack_cards:
            if card.cost > energy:
                continue

            # Simple scoring: damage / cost (placeholder)
            # In real implementation, would need actual card damage data
            score = 10 / max(card.cost, 1)  # Prefer low cost

            if score > best_score:
                best_score = score
                best_attack = (idx, card)

        return best_attack

    def _pick_best_defense(self, skill_cards: List[Tuple[int, CardInfo]], energy: int) -> Tuple[int, CardInfo]:
        """Pick the best defense card from playable skills."""
        # Prioritize:
        # 1. Highest block
        # 2. Lowest cost

        best_score = -1
        best_skill = skill_cards[0]

        for idx, card in skill_cards:
            if card.cost > energy:
                continue

            # Prefer low cost defense
            score = 10 / max(card.cost, 1)

            if score > best_score:
                best_score = score
                best_skill = (idx, card)

        return best_skill

    def _pick_best_skill(self, skill_cards: List[Tuple[int, CardInfo]], state: CombatState) -> Tuple[int, CardInfo]:
        """Pick the best skill card from playable skills."""
        # Prioritize:
        # 1. Draw cards (low hand)
        # 2. Block (under attack)
        # 3. Energy gain
        # 4. Low cost

        hand_size = len(state.hand)

        attacking_enemies = [e for e in state.enemies if e.intent_type == "Attack"]
        needs_block = len(attacking_enemies) > 0

        best_score = -1
        best_skill = skill_cards[0]

        for idx, card in skill_cards:
            score = 0

            # Prefer draw cards when hand is small
            if hand_size < 4:
                score += 5

            # Prefer block when under attack
            if needs_block:
                score += 3

            # Prefer low cost
            score += (4 - card.cost)

            # Add randomness for variety
            score += random.uniform(-1, 1)

            if score > best_score:
                best_score = score
                best_skill = (idx, card)

        return best_skill

    def _debug_action(self, action: Action, state: CombatState):
        """Print debug information about the action."""
        print(f"\nCombatAI Decision:")
        print(f"  Action Type: {action.type}")

        if action.type == "play_card":
            card = state.hand[action.card_index]
            print(f"  Playing: {card.card_type}[{card.cost}] at position {card.position}")

            if action.target_index >= 0 and action.target_index < len(state.enemies):
                target = state.enemies[action.target_index]
                print(f"  Targeting: {target.name} (HP {target.hp})")
        elif action.type == "end_turn":
            print(f"  Ending turn")


def create_combat_ai(debug: bool = False) -> CombatAI:
    """Factory function to create combat AI."""
    return CombatAI(debug)


if __name__ == "__main__":
    # Test combat AI
    from ..vision.combat_reader import CombatState, CardInfo, EnemyInfo

    # Create a mock state
    state = CombatState(
        player_hp=60,
        player_max_hp=80,
        player_block=0,
        player_energy=3,
        hand=[
            CardInfo(name="Strike", cost=1, card_type="Attack", position=(1000, 1200)),
            CardInfo(name="Defend", cost=1, card_type="Skill", position=(1200, 1200)),
            CardInfo(name="Bash", cost=2, card_type="Attack", position=(1400, 1200)),
        ],
        enemies=[
            EnemyInfo(name="Jaw Worm", hp=30, max_hp=44, intent_type="Attack", intent_value=12, position=(1280, 300)),
        ]
    )

    ai = create_combat_ai(debug=True)

    print("Testing combat AI with mock state...")
    action = ai.decide(state)
    print(f"\nSelected action: {action}")
