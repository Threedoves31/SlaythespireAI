"""
Gymnasium environment for STS2 combat simulation.
Observation space: flat vector encoding of combat state.
Action space: Discrete — play card N targeting enemy M, or end turn.
"""
from __future__ import annotations
import random
import numpy as np
import gymnasium as gym
from gymnasium import spaces
from typing import Optional, Tuple, Dict, Any, List

import sys, os
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from core import (
    PlayerState, EnemyState, CombatState, CombatResult,
    make_card, random_encounter, CardType, TargetType, CARD_DB
)

# ─── Constants ────────────────────────────────────────────────────────────────

MAX_HAND_SIZE   = 10
MAX_ENEMIES     = 5
MAX_POWERS      = 10   # per creature
CARD_VOCAB_SIZE = len(CARD_DB) + 1   # +1 for "empty slot"
POWER_VOCAB_SIZE = 20  # number of distinct power types we track

# Build card ID → index mapping
CARD_ID_TO_IDX = {cid: i+1 for i, cid in enumerate(sorted(CARD_DB.keys()))}
CARD_ID_TO_IDX["<empty>"] = 0

POWER_IDS = [
    "Strength", "Dexterity", "Barricade", "Metallicize", "FeelNoPain",
    "DemonForm", "Inflame", "Vulnerable", "Weak", "Frail", "Poison",
    "StrengthDown", "Artifact", "CurlUp", "Plating", "Regen",
    "Intangible", "Ritual", "Enrage", "Combust",
]
POWER_ID_TO_IDX = {pid: i for i, pid in enumerate(POWER_IDS)}

# Observation dimensions
OBS_PLAYER_DIM  = 4 + POWER_VOCAB_SIZE   # hp_norm, max_hp_norm, block_norm, energy_norm, powers
OBS_HAND_DIM    = MAX_HAND_SIZE * (1 + 1 + 3)  # card_idx_norm, cost_norm, type_onehot(3)
OBS_PILES_DIM   = 3   # draw_norm, discard_norm, exhaust_norm
OBS_ENEMY_DIM   = MAX_ENEMIES * (4 + POWER_VOCAB_SIZE + 4)  # hp, max_hp, block, is_alive, powers, intent_onehot(4)
OBS_DIM = OBS_PLAYER_DIM + OBS_HAND_DIM + OBS_PILES_DIM + OBS_ENEMY_DIM

# Action space: (hand_slot × enemy_slot) + end_turn
# hand_slot: 0..MAX_HAND_SIZE-1
# enemy_slot: 0..MAX_ENEMIES-1 (or 0 for self/AoE)
# end_turn: MAX_HAND_SIZE * MAX_ENEMIES
N_ACTIONS = MAX_HAND_SIZE * MAX_ENEMIES + 1
END_TURN_ACTION = N_ACTIONS - 1

INTENT_TYPES = ["Attack", "Defend", "Buff", "Other"]


class CombatEnv(gym.Env):
    """
    Single-combat Gymnasium environment.
    Each episode is one combat encounter (normal, elite, or boss).
    """
    metadata = {"render_modes": ["human"]}

    def __init__(
        self,
        encounter_type: str = "normal",
        player_hp: int = 80,
        seed: Optional[int] = None,
        render_mode: Optional[str] = None,
    ):
        super().__init__()
        self.encounter_type = encounter_type
        self.player_hp = player_hp
        self.render_mode = render_mode

        self.observation_space = spaces.Box(
            low=0.0, high=1.0, shape=(OBS_DIM,), dtype=np.float32
        )
        self.action_space = spaces.Discrete(N_ACTIONS)

        self._rng = random.Random(seed)
        self._state: Optional[CombatState] = None
        self._turn_started = False

    # ── Gym interface ─────────────────────────────────────────────────────────

    def reset(self, *, seed=None, options=None) -> Tuple[np.ndarray, Dict]:
        if seed is not None:
            self._rng = random.Random(seed)

        player = PlayerState.ironclad(self.player_hp)
        enemies = random_encounter(self.encounter_type, self._rng)
        self._state = CombatState(player, enemies, self._rng)
        self._state.start_player_turn()
        self._turn_started = True

        obs = self._get_obs()
        info = {"action_mask": self._get_action_mask()}
        return obs, info

    def step(self, action: int) -> Tuple[np.ndarray, float, bool, bool, Dict]:
        assert self._state is not None, "Call reset() first"
        state = self._state
        prev_hp = state.player.hp
        prev_enemy_hp = sum(e.hp for e in state.alive_enemies)

        reward = 0.0
        truncated = False

        if action == END_TURN_ACTION:
            state.end_player_turn()
            if not state.is_over:
                state.start_player_turn()
            reward += -0.01  # small penalty for ending turn (encourages efficiency)
        else:
            hand_idx = action // MAX_ENEMIES
            enemy_idx = action % MAX_ENEMIES

            hand = state.player.hand
            if hand_idx >= len(hand):
                # Invalid action — penalize
                reward = -0.1
            else:
                card = hand[hand_idx]
                alive = state.alive_enemies
                target = alive[enemy_idx] if enemy_idx < len(alive) else (alive[0] if alive else None)

                if not card.can_play(state.player.energy):
                    reward = -0.1
                else:
                    success = state.play_card(card, target)
                    if not success:
                        reward = -0.1
                    else:
                        # Dense reward shaping
                        hp_gained = state.player.hp - prev_hp
                        enemy_hp_lost = prev_enemy_hp - sum(e.hp for e in state.alive_enemies)
                        reward += enemy_hp_lost * 0.1   # reward for dealing damage
                        reward += hp_gained * 0.05      # reward for healing (rare)

        # Terminal rewards
        terminated = state.is_over
        if terminated and state.result:
            if state.result.won:
                reward += 100.0
                reward += state.player.hp * 0.1  # bonus for HP remaining
            else:
                reward -= 100.0

        obs = self._get_obs()
        info = {"action_mask": self._get_action_mask()}
        if terminated and state.result:
            info["result"] = {
                "won": state.result.won,
                "turns": state.result.turns,
                "hp_remaining": state.result.hp_remaining,
                "hp_lost": state.result.hp_lost,
            }

        return obs, reward, terminated, truncated, info

    def action_masks(self) -> np.ndarray:
        """Required by sb3-contrib MaskablePPO."""
        return self._get_action_mask()

    # ── Observation encoding ──────────────────────────────────────────────────

    def _get_obs(self) -> np.ndarray:
        state = self._state
        obs = np.zeros(OBS_DIM, dtype=np.float32)
        offset = 0

        # Player state
        p = state.player
        obs[offset+0] = p.hp / p.max_hp
        obs[offset+1] = p.max_hp / 100.0
        obs[offset+2] = min(p.block, 100) / 100.0
        obs[offset+3] = p.energy / max(p.max_energy, 1)
        offset += 4
        for power in p.powers:
            idx = POWER_ID_TO_IDX.get(power.id, -1)
            if idx >= 0:
                obs[offset + idx] = min(abs(power.amount), 10) / 10.0
        offset += POWER_VOCAB_SIZE

        # Hand
        for i in range(MAX_HAND_SIZE):
            if i < len(p.hand):
                card = p.hand[i]
                obs[offset+0] = CARD_ID_TO_IDX.get(card.id, 0) / CARD_VOCAB_SIZE
                obs[offset+1] = max(0, card.cost) / 5.0
                ctype = card.card_type
                obs[offset+2] = 1.0 if ctype == CardType.ATTACK else 0.0
                obs[offset+3] = 1.0 if ctype == CardType.SKILL else 0.0
                obs[offset+4] = 1.0 if ctype == CardType.POWER else 0.0
            offset += 5

        # Piles
        obs[offset+0] = len(p.draw_pile) / 30.0
        obs[offset+1] = len(p.discard_pile) / 30.0
        obs[offset+2] = len(p.exhaust_pile) / 30.0
        offset += 3

        # Enemies
        for i in range(MAX_ENEMIES):
            if i < len(state.enemies):
                e = state.enemies[i]
                obs[offset+0] = e.hp / max(e.max_hp, 1) if e.is_alive else 0.0
                obs[offset+1] = e.max_hp / 200.0
                obs[offset+2] = min(e.block, 100) / 100.0
                obs[offset+3] = 1.0 if e.is_alive else 0.0
                offset += 4
                for power in e.powers:
                    idx = POWER_ID_TO_IDX.get(power.id, -1)
                    if idx >= 0:
                        obs[offset + idx] = min(abs(power.amount), 10) / 10.0
                offset += POWER_VOCAB_SIZE
                # Intent encoding
                if e.current_move and e.is_alive:
                    intent = e.current_move.intent.value
                    obs[offset+0] = 1.0 if "Attack" in intent else 0.0
                    obs[offset+1] = 1.0 if intent == "Defend" else 0.0
                    obs[offset+2] = 1.0 if intent == "Buff" else 0.0
                    obs[offset+3] = 1.0 if intent == "Debuff" else 0.0
                    # Normalize incoming damage
                    if e.current_move.damage > 0:
                        total_dmg = e.current_move.damage * e.current_move.hits
                        obs[offset+0] = min(total_dmg, 50) / 50.0
                offset += 4
            else:
                offset += 4 + POWER_VOCAB_SIZE + 4

        return obs

    def _get_action_mask(self) -> np.ndarray:
        """True = action is valid."""
        mask = np.zeros(N_ACTIONS, dtype=bool)
        state = self._state
        if state is None or state.is_over:
            mask[END_TURN_ACTION] = True
            return mask

        p = state.player
        alive = state.alive_enemies

        for hand_idx, card in enumerate(p.hand):
            if not card.can_play(p.energy):
                continue
            if card.target == TargetType.ANY_ENEMY:
                for enemy_idx, e in enumerate(alive):
                    if e.is_alive:
                        action = hand_idx * MAX_ENEMIES + enemy_idx
                        if action < END_TURN_ACTION:
                            mask[action] = True
            else:
                # Self-targeting or AoE: use enemy_idx=0
                action = hand_idx * MAX_ENEMIES + 0
                if action < END_TURN_ACTION:
                    mask[action] = True

        # Always allow end turn
        mask[END_TURN_ACTION] = True
        return mask

    def render(self):
        if self.render_mode == "human" and self._state:
            print(self._state)
            print(f"Hand: {self._state.player.hand}")
