"""
Heuristic policy for STS2 combat.

Rules (priority order):
1. One-shot kill enemies if possible
2. Defense only when incoming damage > 4 (tolerate small damage)
3. Attack vulnerable enemies first
4. Apply Vulnerable (Bash) to high-HP enemies
5. Play Power cards when enemy not attacking or have spare energy
6. Draw cards when have spare energy
7. Play 0-cost cards
8. Regular attacks when enemy defending/buffing
9. End turn when energy insufficient

Special rules:
- Don't use HP-for-benefit cards (Hemokinesis, Offering) when HP <= 20
"""
from __future__ import annotations
import sys
import os
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))

from dataclasses import dataclass
from typing import List, Optional, Tuple
from enum import Enum
from simulator.core.card import CardInstance, CardType, TargetType
from simulator.core.player import PlayerState
from simulator.core.enemy import EnemyState, IntentType
from simulator.core.power import Power


class ActionType(Enum):
    PLAY_CARD = "play_card"
    END_TURN = "end_turn"


@dataclass
class Decision:
    action: ActionType
    card: Optional[CardInstance] = None
    target: Optional[EnemyState] = None
    reason: str = ""


# Cards that cost HP to play
HP_COST_CARDS = {"Hemokinesis", "Offering", "Bloodletting", "Rupture"}

# Cards that apply Vulnerable
VULNERABLE_CARDS = {"Bash", "Clothesline"}

# Power cards that give strength
STRENGTH_POWER_CARDS = {"Inflame", "SpotWeakness", "DemonForm", "LimitBreak"}

# Draw cards
DRAW_CARDS = {"BattleTrance", "ShrugItOff", "BurningPact", "Warcry", "ThinkingAhead"}


class HeuristicPolicy:
    """
    Heuristic-based combat policy for Slay the Spire 2.
    """
    
    # Tolerance for incoming damage (will prioritize attack over defense)
    DAMAGE_TOLERANCE = 4
    
    # Low HP threshold - avoid HP-cost cards below this
    LOW_HP_THRESHOLD = 20
    
    def __init__(self, verbose: bool = False):
        self.verbose = verbose
        self.turn_count = 0
    
    def decide(self, player: PlayerState, enemies: List[EnemyState], 
               turn: int) -> Decision:
        """
        Make a decision for the current combat state.
        
        Args:
            player: Current player state
            enemies: List of alive enemies
            turn: Current turn number
            
        Returns:
            Decision with action, card (if any), target (if any), and reason
        """
        self.turn_count = turn
        
        # Get playable cards
        playable = self._get_playable_cards(player)
        
        if not playable:
            return Decision(ActionType.END_TURN, reason="No playable cards")
        
        alive_enemies = [e for e in enemies if e.is_alive]
        if not alive_enemies:
            return Decision(ActionType.END_TURN, reason="No alive enemies")
        
        # Calculate incoming damage this turn
        incoming_damage = self._calculate_incoming_damage(alive_enemies)
        need_block = max(0, incoming_damage - player.block)
        
        if self.verbose:
            print(f"[Turn {turn}] HP={player.hp}/{player.max_hp}, "
                  f"Energy={player.energy}, Block={player.block}, "
                  f"Incoming={incoming_damage}, Need={need_block}")
        
        # === Priority 0: One-shot kill ===
        kill_decision = self._try_one_shot_kill(playable, alive_enemies, player)
        if kill_decision:
            return kill_decision
        
        # === Priority 1: Defense (only if significant damage incoming) ===
        if incoming_damage > self.DAMAGE_TOLERANCE and need_block > 0:
            defend_decision = self._try_defend(playable, need_block, player)
            if defend_decision:
                return defend_decision
        
        # === Priority 2: Attack vulnerable enemies ===
        vuln_decision = self._try_attack_vulnerable(playable, alive_enemies, player)
        if vuln_decision:
            return vuln_decision
        
        # === Priority 3: Apply Vulnerable to high-HP enemies ===
        bash_decision = self._try_apply_vulnerable(playable, alive_enemies, player)
        if bash_decision:
            return bash_decision
        
        # === Priority 4: Play Power cards ===
        power_decision = self._try_play_power(playable, alive_enemies, player)
        if power_decision:
            return power_decision
        
        # === Priority 5: Draw cards ===
        draw_decision = self._try_draw_cards(playable, player)
        if draw_decision:
            return draw_decision
        
        # === Priority 6: Play 0-cost cards ===
        free_decision = self._try_play_zero_cost(playable, alive_enemies, player)
        if free_decision:
            return free_decision
        
        # === Priority 7: Regular attacks (enemy not attacking) ===
        if incoming_damage <= self.DAMAGE_TOLERANCE:
            attack_decision = self._try_attack(playable, alive_enemies, player)
            if attack_decision:
                return attack_decision
        
        # === Priority 8: Any remaining useful card ===
        any_decision = self._try_any_useful_card(playable, alive_enemies, player)
        if any_decision:
            return any_decision
        
        return Decision(ActionType.END_TURN, reason="No beneficial action")
    
    def _get_playable_cards(self, player: PlayerState) -> List[CardInstance]:
        """Get cards that can be played given current energy."""
        return [c for c in player.hand if c.can_play(player.energy)]
    
    def _calculate_incoming_damage(self, enemies: List[EnemyState]) -> int:
        """Calculate total incoming damage from enemies this turn."""
        total = 0
        for e in enemies:
            if e.current_move and e.current_move.intent in (
                IntentType.ATTACK, IntentType.ATTACK_DEBUFF, IntentType.ATTACK_BUFF
            ):
                dmg = e.current_move.damage * e.current_move.hits
                total += dmg
        return total
    
    def _estimate_damage(self, card: CardInstance, player: PlayerState) -> int:
        """Estimate damage a card will deal."""
        d = card.definition
        if d.card_type != CardType.ATTACK:
            return 0
        
        base = d.damage
        if d.hits:
            base *= d.hits
        
        # Add strength
        strength = 0
        for p in player.powers:
            if p.id == "Strength":
                strength = p.amount
                break
        
        return base + strength * (d.hits or 1)
    
    def _estimate_block(self, card: CardInstance, player: PlayerState) -> int:
        """Estimate block a card will provide."""
        d = card.definition
        if d.card_type == CardType.SKILL or d.card_type == CardType.ATTACK:
            return d.block or 0
        return 0
    
    def _is_low_hp(self, player: PlayerState) -> bool:
        """Check if player is at low HP (avoid HP-cost cards)."""
        return player.hp <= self.LOW_HP_THRESHOLD
    
    def _costs_hp(self, card: CardInstance) -> bool:
        """Check if card costs HP to play."""
        base_id = card.definition.id.rstrip("+")
        return base_id in HP_COST_CARDS
    
    def _try_one_shot_kill(self, playable: List[CardInstance], 
                           enemies: List[EnemyState],
                           player: PlayerState) -> Optional[Decision]:
        """Try to find a card that can one-shot an enemy."""
        attacks = [c for c in playable if c.definition.card_type == CardType.ATTACK]
        
        for card in attacks:
            dmg = self._estimate_damage(card, player)
            for enemy in enemies:
                if dmg >= enemy.hp and enemy.is_alive:
                    return Decision(
                        ActionType.PLAY_CARD,
                        card=card,
                        target=enemy,
                        reason=f"One-shot kill {enemy.name} ({dmg} dmg vs {enemy.hp} HP)"
                    )
        return None
    
    def _try_defend(self, playable: List[CardInstance], 
                    need_block: int,
                    player: PlayerState) -> Optional[Decision]:
        """Try to play a defense card if significant damage incoming."""
        # Filter out HP-cost cards if low HP
        if self._is_low_hp(player):
            playable = [c for c in playable if not self._costs_hp(c)]
        
        defend_cards = []
        for c in playable:
            block = self._estimate_block(c, player)
            if block > 0:
                defend_cards.append((c, block))
        
        if not defend_cards:
            return None
        
        # Sort by block value (best first)
        defend_cards.sort(key=lambda x: x[1], reverse=True)
        
        best_card, best_block = defend_cards[0]
        
        # Only defend if it provides meaningful block
        if best_block >= need_block * 0.5:
            return Decision(
                ActionType.PLAY_CARD,
                card=best_card,
                target=None,
                reason=f"Defend: +{best_block} block (need {need_block})"
            )
        
        return None
    
    def _try_attack_vulnerable(self, playable: List[CardInstance],
                                enemies: List[EnemyState],
                                player: PlayerState) -> Optional[Decision]:
        """Attack enemies that have Vulnerable."""
        # Filter out HP-cost cards if low HP
        if self._is_low_hp(player):
            playable = [c for c in playable if not self._costs_hp(c)]
        
        attacks = [c for c in playable if c.definition.card_type == CardType.ATTACK]
        if not attacks:
            return None
        
        # Find vulnerable enemies
        vuln_enemies = []
        for e in enemies:
            for p in e.powers:
                if p.id == "Vulnerable" and p.amount > 0:
                    vuln_enemies.append(e)
                    break
        
        if not vuln_enemies:
            return None
        
        # Pick best attack (highest damage)
        best_attack = max(attacks, key=lambda c: self._estimate_damage(c, player))
        dmg = self._estimate_damage(best_attack, player)
        
        # Target weakest vulnerable enemy
        target = min(vuln_enemies, key=lambda e: e.hp)
        
        return Decision(
            ActionType.PLAY_CARD,
            card=best_attack,
            target=target,
            reason=f"Attack vulnerable {target.name} ({dmg} dmg x1.5)"
        )
    
    def _try_apply_vulnerable(self, playable: List[CardInstance],
                               enemies: List[EnemyState],
                               player: PlayerState) -> Optional[Decision]:
        """Apply Vulnerable to high-HP enemies using Bash/Clothesline."""
        # Filter out HP-cost cards if low HP
        if self._is_low_hp(player):
            playable = [c for c in playable if not self._costs_hp(c)]
        
        vuln_cards = []
        for c in playable:
            base_id = c.definition.id.rstrip("+")
            if base_id in VULNERABLE_CARDS:
                vuln_cards.append(c)
        
        if not vuln_cards:
            return None
        
        # Find enemies without vulnerable
        non_vuln_enemies = []
        for e in enemies:
            has_vuln = any(p.id == "Vulnerable" for p in e.powers)
            if not has_vuln:
                non_vuln_enemies.append(e)
        
        if not non_vuln_enemies:
            return None
        
        # Target highest HP enemy (most value from vulnerable)
        target = max(non_vuln_enemies, key=lambda e: e.hp)
        card = vuln_cards[0]
        
        return Decision(
            ActionType.PLAY_CARD,
            card=card,
            target=target,
            reason=f"Apply Vulnerable to {target.name} (HP: {target.hp})"
        )
    
    def _try_play_power(self, playable: List[CardInstance],
                         enemies: List[EnemyState],
                         player: PlayerState) -> Optional[Decision]:
        """Play power cards when appropriate."""
        # Filter out HP-cost cards if low HP
        if self._is_low_hp(player):
            playable = [c for c in playable if not self._costs_hp(c)]
        
        power_cards = [c for c in playable if c.definition.card_type == CardType.POWER]
        
        if not power_cards:
            return None
        
        # Calculate incoming damage
        incoming = self._calculate_incoming_damage(enemies)
        
        # Only play powers if:
        # 1. Enemy is not attacking (safe turn), OR
        # 2. Have spare energy after playing (>= 1)
        for card in power_cards:
            energy_after = player.energy - card.definition.cost
            
            # Check if already have this power
            base_id = card.definition.id.rstrip("+")
            already_have = any(p.id == base_id for p in player.powers)
            
            if already_have and base_id != "LimitBreak":
                continue
            
            if incoming <= self.DAMAGE_TOLERANCE or energy_after >= 1:
                return Decision(
                    ActionType.PLAY_CARD,
                    card=card,
                    target=None,
                    reason=f"Play Power: {card.definition.id}"
                )
        
        return None
    
    def _try_draw_cards(self, playable: List[CardInstance],
                        player: PlayerState) -> Optional[Decision]:
        """Play draw cards when have spare energy."""
        # Filter out HP-cost cards if low HP
        if self._is_low_hp(player):
            playable = [c for c in playable if not self._costs_hp(c)]
        
        draw_cards = []
        for c in playable:
            base_id = c.definition.id.rstrip("+")
            if base_id in DRAW_CARDS or c.definition.draw > 0:
                draw_cards.append(c)
        
        if not draw_cards:
            return None
        
        # Only draw if have spare energy after playing
        for card in draw_cards:
            energy_after = player.energy - card.definition.cost
            if energy_after >= 1:
                return Decision(
                    ActionType.PLAY_CARD,
                    card=card,
                    target=None,
                    reason=f"Draw cards: {card.definition.id}"
                )
        
        return None
    
    def _try_play_zero_cost(self, playable: List[CardInstance],
                            enemies: List[EnemyState],
                            player: PlayerState) -> Optional[Decision]:
        """Play 0-cost cards for free value."""
        # Filter out HP-cost cards if low HP
        if self._is_low_hp(player):
            playable = [c for c in playable if not self._costs_hp(c)]
        
        zero_cost = [c for c in playable if c.definition.cost == 0]
        
        if not zero_cost:
            return None
        
        # Prioritize attacks if enemies alive
        for card in zero_cost:
            if card.definition.card_type == CardType.ATTACK:
                target = min(enemies, key=lambda e: e.hp) if enemies else None
                return Decision(
                    ActionType.PLAY_CARD,
                    card=card,
                    target=target,
                    reason=f"Free attack: {card.definition.id}"
                )
        
        # Then skills/powers
        card = zero_cost[0]
        return Decision(
            ActionType.PLAY_CARD,
            card=card,
            target=None,
            reason=f"Free card: {card.definition.id}"
        )
    
    def _try_attack(self, playable: List[CardInstance],
                    enemies: List[EnemyState],
                    player: PlayerState) -> Optional[Decision]:
        """Regular attack when enemy not attacking."""
        # Filter out HP-cost cards if low HP
        if self._is_low_hp(player):
            playable = [c for c in playable if not self._costs_hp(c)]
        
        attacks = [c for c in playable if c.definition.card_type == CardType.ATTACK]
        
        if not attacks:
            return None
        
        # Best attack
        best = max(attacks, key=lambda c: self._estimate_damage(c, player))
        dmg = self._estimate_damage(best, player)
        
        # Target weakest enemy
        target = min(enemies, key=lambda e: e.hp)
        
        return Decision(
            ActionType.PLAY_CARD,
            card=best,
            target=target,
            reason=f"Attack {target.name}: {dmg} damage"
        )
    
    def _try_any_useful_card(self, playable: List[CardInstance],
                             enemies: List[EnemyState],
                             player: PlayerState) -> Optional[Decision]:
        """Play any remaining useful card."""
        # Filter out HP-cost cards if low HP
        if self._is_low_hp(player):
            playable = [c for c in playable if not self._costs_hp(c)]
        
        if not playable:
            return None
        
        # Prioritize by card type
        for ctype in [CardType.ATTACK, CardType.SKILL, CardType.POWER]:
            for card in playable:
                if card.definition.card_type == ctype:
                    target = min(enemies, key=lambda e: e.hp) if ctype == CardType.ATTACK else None
                    return Decision(
                        ActionType.PLAY_CARD,
                        card=card,
                        target=target,
                        reason=f"Play remaining: {card.definition.id}"
                    )
        
        return None


def run_heuristic_policy(combat_state, verbose: bool = True) -> Tuple[bool, int]:
    """
    Run a combat using the heuristic policy.
    
    Args:
        combat_state: CombatState instance
        verbose: Print decisions
        
    Returns:
        Tuple of (won, turns)
    """
    policy = HeuristicPolicy(verbose=verbose)
    
    while not combat_state.is_over:
        combat_state.start_player_turn()
        
        while True:
            decision = policy.decide(
                combat_state.player,
                combat_state.alive_enemies,
                combat_state.turn
            )
            
            if decision.action == ActionType.END_TURN:
                if verbose:
                    print(f"[Turn {combat_state.turn}] END TURN: {decision.reason}")
                break
            
            if verbose:
                target_name = decision.target.name if decision.target else "none"
                print(f"[Turn {combat_state.turn}] PLAY {decision.card.definition.id} -> {target_name}")
                print(f"    Reason: {decision.reason}")
            
            success = combat_state.play_card(decision.card, decision.target)
            if not success:
                if verbose:
                    print(f"    [FAILED] Could not play card")
                break
        
        combat_state.end_player_turn()
        
        if combat_state.is_over:
            break
    
    return combat_state.result.won, combat_state.result.turns


if __name__ == "__main__":
    import random
    from simulator.core.player import PlayerState
    from simulator.core.enemy import make_enemy, random_encounter
    from simulator.core.combat import CombatState
    
    print("=" * 60)
    print("HEURISTIC POLICY TEST")
    print("=" * 60)
    
    rng = random.Random(42)
    
    # Test against different encounter types
    results = {"normal": [], "elite": [], "boss": []}
    
    for enc_type in ["normal", "elite"]:
        print(f"\n--- Testing {enc_type.upper()} encounters ---")
        
        for i in range(3):
            # Create fresh player using ironclad factory method
            player = PlayerState.ironclad(hp=80)
            
            # Create enemies
            enemies = random_encounter(enc_type, rng)
            
            # Create combat
            combat = CombatState(player, enemies, rng)
            
            print(f"\n[{enc_type.upper()} #{i+1}] Enemies: {', '.join(e.name for e in enemies)}")
            
            won, turns = run_heuristic_policy(combat, verbose=True)
            
            results[enc_type].append((won, turns))
            
            print(f"\n    Result: {'VICTORY' if won else 'DEFEAT'} in {turns} turns")
            print(f"    HP remaining: {combat.player.hp}/{combat.player.max_hp}")
    
    # Summary
    print("\n" + "=" * 60)
    print("SUMMARY")
    print("=" * 60)
    
    for enc_type, res in results.items():
        if not res:
            continue
        wins = sum(1 for w, _ in res if w)
        total = len(res)
        avg_turns = sum(t for _, t in res) / total if total > 0 else 0
        print(f"{enc_type.upper()}: {wins}/{total} wins, avg {avg_turns:.1f} turns")