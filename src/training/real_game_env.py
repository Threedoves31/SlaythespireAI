"""
Gymnasium environment for training in the actual STS2 game.
Communicates with C# Mod via named pipes.
"""
from __future__ import annotations
import time
import json
import numpy as np
import gymnasium as gym
from gymnasium import spaces
from typing import Optional, Tuple, Dict, Any, List
import threading

try:
    import win32file
    import win32pipe
    import pywintypes
    WIN32_AVAILABLE = True
except ImportError:
    WIN32_AVAILABLE = False
    print("Warning: pywin32 not available. Install with: pip install pywin32")

# ─── Constants ────────────────────────────────────────────────────────────────

PIPE_NAME = r"\\.\pipe\STS2AIBot_Training"
MAX_HAND_SIZE = 10
MAX_ENEMIES = 5
MAX_POWERS = 10

# Card vocabulary
CARD_TYPES = ["Attack", "Skill", "Power", "Status", "Curse"]
TARGET_TYPES = ["AnyEnemy", "AllEnemies", "Self", "None"]

# Power vocabulary
POWER_IDS = [
    "Strength", "Dexterity", "Barricade", "Metallicize", "FeelNoPain",
    "DemonForm", "Inflame", "Vulnerable", "Weak", "Frail", "Poison",
    "StrengthDown", "Artifact", "CurlUp", "Plating", "Regen",
    "Intangible", "Ritual", "Enrage", "Combust",
]
POWER_ID_TO_IDX = {pid: i for i, pid in enumerate(POWER_IDS)}

# Intent types
INTENT_TYPES = ["Attack", "Defend", "Buff", "Debuff", "Unknown"]
INTENT_TO_IDX = {it: i for i, it in enumerate(INTENT_TYPES)}

# Message commands
class MsgCmd:
    RESET = 0
    STEP = 1
    GET_ACTION_MASK = 2
    GET_STATE = 3
    CLOSE = 4

    STATE = 10
    DONE = 11
    ERROR = 12
    ACK = 13


# ─── Pipe Client ───────────────────────────────────────────────────────────────

class PipeClient:
    """Client for communicating with C# Mod via named pipes."""

    def __init__(self, pipe_name: str = PIPE_NAME, timeout: float = 30.0):
        self.pipe_name = pipe_name
        self.timeout = timeout
        self.handle = None
        self._lock = threading.Lock()

    def connect(self) -> bool:
        """Connect to the named pipe server."""
        if not WIN32_AVAILABLE:
            raise RuntimeError("pywin32 is required for pipe communication")

        try:
            self.handle = win32file.CreateFile(
                self.pipe_name,
                win32file.GENERIC_READ | win32file.GENERIC_WRITE,
                0,
                None,
                win32file.OPEN_EXISTING,
                0,
                None
            )
            return True
        except pywintypes.error as e:
            if e.winerror == 232:  # ERROR_PIPE_NOT_CONNECTED
                time.sleep(1.0)
                return self.connect()
            print(f"Pipe connection error: {e}")
            return False

    def send_message(self, command: int, payload: str = "") -> Optional[str]:
        """Send a message and wait for response."""
        with self._lock:
            if self.handle is None:
                return None

            message = f"{command}|{payload}\n"
            try:
                win32file.WriteFile(self.handle, message.encode('utf-8'))
                response = self._read_response()
                return response
            except Exception as e:
                print(f"Send message error: {e}")
                return None

    def _read_response(self) -> str:
        """Read response from pipe."""
        try:
            result = ''
            while True:
                hr, data = win32file.ReadFile(self.handle, 4096, None)
                if hr != 0:
                    raise Exception(f"Read failed with code {hr}")

                chunk = data.decode('utf-8')
                result += chunk

                if '\n' in chunk:
                    break

            return result.strip()
        except Exception as e:
            print(f"Read response error: {e}")
            return ""

    def close(self):
        """Close the pipe connection."""
        if self.handle is not None:
            win32file.CloseHandle(self.handle)
            self.handle = None


# ─── Real Game Environment ───────────────────────────────────────────────────────

class RealGameEnv(gym.Env):
    """
    Gymnasium environment for training in the actual STS2 game.
    Communicates with C# Mod via named pipes.
    """

    metadata = {"render_modes": ["human", "none"]}

    def __init__(
        self,
        pipe_name: str = PIPE_NAME,
        render_mode: Optional[str] = None,
        verbose: bool = True,
    ):
        super().__init__()

        self.pipe_name = pipe_name
        self.render_mode = render_mode
        self.verbose = verbose

        self.pipe_client = None
        self.connected = False

        # Observation space
        self.observation_space = spaces.Dict({
            'player': spaces.Box(low=0.0, high=1.0, shape=(6 + len(POWER_IDS),), dtype=np.float32),
            'hand': spaces.Box(low=0.0, high=1.0, shape=(MAX_HAND_SIZE, 6), dtype=np.float32),
            'piles': spaces.Box(low=0.0, high=1.0, shape=(3,), dtype=np.float32),
            'enemies': spaces.Box(low=0.0, high=1.0, shape=(MAX_ENEMIES, 4 + len(POWER_IDS) + len(INTENT_TYPES)), dtype=np.float32),
        })

        # Action space: hand_idx * MAX_ENEMIES + enemy_idx, or 50 for end turn
        self.action_space = spaces.Discrete(MAX_HAND_SIZE * MAX_ENEMIES + 1)
        self.END_TURN_ACTION = MAX_HAND_SIZE * MAX_ENEMIES

        # Current state
        self._current_obs = None
        self._current_mask = None

    def _connect(self) -> bool:
        """Connect to the game mod."""
        if self.pipe_client is None:
            self.pipe_client = PipeClient(self.pipe_name)

        if not self.connected:
            if self.verbose:
                print(f"[RealGameEnv] Connecting to {self.pipe_name}...")
            self.connected = self.pipe_client.connect()
            if self.connected and self.verbose:
                print("[RealGameEnv] Connected to game mod!")
            elif self.verbose:
                print("[RealGameEnv] Failed to connect. Make sure the game is running with the mod loaded.")
                print("[RealGameEnv] Start the game, enter a combat, then try again.")

        return self.connected

    def reset(self, *, seed=None, options=None) -> Tuple[Dict, Dict]:
        """Reset the environment for a new episode."""
        if not self._connect():
            # Return default observation if not connected
            obs = self._get_default_obs()
            return obs, {"action_mask": self._get_default_mask(), "connected": False}

        if self.verbose:
            print("[RealGameEnv] Resetting environment...")

        # Send reset command
        response = self.pipe_client.send_message(MsgCmd.RESET)
        if response is None:
            print("[RealGameEnv] No response to reset")
            return self._get_default_obs(), {"action_mask": self._get_default_mask(), "connected": False}

        # Parse state
        obs, info = self._parse_response(response)

        # Get action mask
        mask_response = self.pipe_client.send_message(MsgCmd.GET_ACTION_MASK)
        if mask_response:
            info["action_mask"] = self._parse_action_mask(mask_response)
        else:
            info["action_mask"] = np.zeros(self.action_space.n, dtype=bool)
            info["action_mask"][self.END_TURN_ACTION] = True

        info["connected"] = True

        if self.render_mode == "human":
            self.render()

        return obs, info

    def step(self, action: int) -> Tuple[Dict, float, bool, bool, Dict]:
        """Execute an action in the game."""
        if not self.connected:
            obs = self._get_default_obs()
            return obs, 0.0, True, False, {"action_mask": self._get_default_mask(), "connected": False}

        if self.verbose:
            print(f"[RealGameEnv] Step: action={action}")

        # Send step command
        response = self.pipe_client.send_message(MsgCmd.STEP, str(action))
        if response is None:
            print("[RealGameEnv] No response to step")
            obs = self._get_default_obs()
            return obs, -1.0, True, False, {"action_mask": self._get_default_mask(), "connected": False}

        # Parse response
        obs, info = self._parse_response(response)

        # Check if done
        terminated = obs.get("done", False)
        truncated = False

        reward = obs.get("reward", 0.0)

        # Get action mask for next step
        if not terminated:
            mask_response = self.pipe_client.send_message(MsgCmd.GET_ACTION_MASK)
            if mask_response:
                info["action_mask"] = self._parse_action_mask(mask_response)
            else:
                info["action_mask"] = np.zeros(self.action_space.n, dtype=bool)
                info["action_mask"][self.END_TURN_ACTION] = True
        else:
            info["action_mask"] = np.zeros(self.action_space.n, dtype=bool)

        info["connected"] = True

        if self.render_mode == "human":
            self.render()

        return obs, reward, terminated, truncated, info

    def _parse_response(self, response: str) -> Tuple[Dict, Dict]:
        """Parse the JSON response from the game."""
        try:
            data = json.loads(response)
            obs = self._state_to_obs(data)
            info = {}

            # Extract reward and done
            if "reward" in data:
                info["reward"] = data["reward"]

            if "is_done" in data:
                info["done"] = data["is_done"]

            if "won" in data and data["won"] is not None:
                info["won"] = data["won"]
            if "turns" in data and data["turns"] is not None:
                info["turns"] = data["turns"]
            if "hp_remaining" in data and data["hp_remaining"] is not None:
                info["hp_remaining"] = data["hp_remaining"]

            return obs, info
        except json.JSONDecodeError as e:
            print(f"JSON decode error: {e}")
            return self._get_default_obs(), {}

    def _state_to_obs(self, state: Dict) -> Dict:
        """Convert game state to observation dict."""
        obs = {}

        # Player state
        player = np.zeros(6 + len(POWER_IDS), dtype=np.float32)
        player[0] = state.get("player_hp", 0) / 100.0
        player[1] = state.get("player_max_hp", 0) / 100.0
        player[2] = min(state.get("player_block", 0), 100) / 100.0
        player[3] = state.get("player_energy", 0) / 5.0
        player[4] = state.get("player_max_energy", 0) / 5.0
        player[5] = state.get("turn_number", 0) / 50.0
        obs["player"] = player

        # Hand
        hand = np.zeros((MAX_HAND_SIZE, 6), dtype=np.float32)
        for i, card in enumerate(state.get("hand", [])[:MAX_HAND_SIZE]):
            hand[i, 0] = 1.0  # is_card
            hand[i, 1] = card.get("cost", 0) / 5.0
            ctype = card.get("type", "Skill")
            hand[i, 2] = 1.0 if ctype == "Attack" else 0.0
            hand[i, 3] = 1.0 if ctype == "Skill" else 0.0
            hand[i, 4] = 1.0 if ctype == "Power" else 0.0
            hand[i, 5] = 1.0 if card.get("is_playable", False) else 0.0
        obs["hand"] = hand

        # Piles
        piles = np.zeros(3, dtype=np.float32)
        piles[0] = state.get("draw_pile_count", 0) / 30.0
        piles[1] = state.get("discard_pile_count", 0) / 30.0
        piles[2] = state.get("exhaust_pile_count", 0) / 30.0
        obs["piles"] = piles

        # Enemies
        enemies = np.zeros((MAX_ENEMIES, 4 + len(POWER_IDS) + len(INTENT_TYPES)), dtype=np.float32)
        for i, enemy in enumerate(state.get("enemies", [])[:MAX_ENEMIES]):
            enemies[i, 0] = enemy.get("hp", 0) / max(enemy.get("max_hp", 1), 1) if enemy.get("is_alive", False) else 0.0
            enemies[i, 1] = enemy.get("max_hp", 0) / 200.0
            enemies[i, 2] = min(enemy.get("block", 0), 100) / 100.0
            enemies[i, 3] = 1.0 if enemy.get("is_alive", False) else 0.0

            # Powers
            power_offset = 4
            for j, power in enumerate(enemy.get("powers", [])[:MAX_POWERS]):
                pid = power.get("id", "")
                if pid in POWER_ID_TO_IDX:
                    enemies[i, power_offset + POWER_ID_TO_IDX[pid]] = min(abs(power.get("amount", 0)), 10) / 10.0

            # Intent
            intent_offset = 4 + len(POWER_IDS)
            intent = enemy.get("intent_type", "Unknown")
            if intent in INTENT_TO_IDX:
                enemies[i, intent_offset + INTENT_TO_IDX[intent]] = 1.0

        obs["enemies"] = enemies

        # Extra info
        obs["done"] = state.get("is_done", False)
        obs["reward"] = state.get("reward", 0.0)

        return obs

    def _parse_action_mask(self, mask_str: str) -> np.ndarray:
        """Parse action mask from comma-separated string."""
        try:
            if not mask_str:
                mask = np.zeros(self.action_space.n, dtype=bool)
                mask[self.END_TURN_ACTION] = True
                return mask

            indices = [int(x.strip()) for x in mask_str.split(",") if x.strip()]
            mask = np.zeros(self.action_space.n, dtype=bool)
            for idx in indices:
                if 0 <= idx < self.action_space.n:
                    mask[idx] = True
            return mask
        except Exception as e:
            print(f"Parse action mask error: {e}")
            mask = np.zeros(self.action_space.n, dtype=bool)
            mask[self.END_TURN_ACTION] = True
            return mask

    def _get_default_obs(self) -> Dict:
        """Get default observation when not connected."""
        return {
            "player": np.zeros(6 + len(POWER_IDS), dtype=np.float32),
            "hand": np.zeros((MAX_HAND_SIZE, 6), dtype=np.float32),
            "piles": np.zeros(3, dtype=np.float32),
            "enemies": np.zeros((MAX_ENEMIES, 4 + len(POWER_IDS) + len(INTENT_TYPES)), dtype=np.float32),
            "done": True,
            "reward": 0.0,
        }

    def _get_default_mask(self) -> np.ndarray:
        """Get default action mask when not connected."""
        mask = np.zeros(self.action_space.n, dtype=bool)
        mask[self.END_TURN_ACTION] = True
        return mask

    def render(self):
        """Render the current state."""
        if self.render_mode != "human" or self._current_obs is None:
            return

        obs = self._current_obs
        print("\n" + "="*60)

        # Player
        p = obs["player"]
        print(f"Player: HP {int(p[0]*100)}/{int(p[1]*100)} | Block {int(p[2]*100)} | Energy {int(p[3]*5)}/{int(p[4]*5)}")

        # Hand
        hand = obs["hand"]
        print("Hand:")
        for i in range(MAX_HAND_SIZE):
            if hand[i, 0] > 0.5:
                cost = int(hand[i, 1] * 5)
                ctype_idx = np.argmax(hand[i, 2:5])
                ctypes = ["Attack", "Skill", "Power"]
                ctype = ctypes[ctype_idx]
                playable = "✓" if hand[i, 5] > 0.5 else "✗"
                print(f"  [{i}] {playable} {ctype}[{cost}]")

        # Enemies
        enemies = obs["enemies"]
        print("Enemies:")
        for i in range(MAX_ENEMIES):
            if enemies[i, 3] > 0.5:
                hp_pct = enemies[i, 0]
                max_hp = int(enemies[i, 1] * 200)
                block = int(enemies[i, 2] * 100)
                intent_offset = 4 + len(POWER_IDS)
                intent_idx = np.argmax(enemies[i, intent_offset:intent_offset+len(INTENT_TYPES)])
                intent = INTENT_TYPES[intent_idx] if 0 <= intent_idx < len(INTENT_TYPES) else "Unknown"
                print(f"  [{i}] HP {int(hp_pct*max_hp)}/{max_hp} | Block {block} | Intent {intent}")

        print("="*60 + "\n")

    def close(self):
        """Close the connection."""
        if self.pipe_client:
            self.pipe_client.close()
            self.connected = False
        if self.verbose:
            print("[RealGameEnv] Closed")


# ─── Helper for sb3-contrib (needs action_masks method) ────────────────────────

class RealGameEnvWithMask(RealGameEnv):
    """Wrapper that adds action_masks() method for sb3-contrib."""

    def action_masks(self) -> np.ndarray:
        """Return current action mask."""
        if self._current_mask is None:
            return np.zeros(self.action_space.n, dtype=bool)

        return self._current_mask

    def reset(self, *, seed=None, options=None) -> Tuple[Dict, Dict]:
        obs, info = super().reset(seed=seed, options=options)
        self._current_obs = obs
        self._current_mask = info.get("action_mask", np.zeros(self.action_space.n, dtype=bool))
        return obs, info

    def step(self, action: int) -> Tuple[Dict, float, bool, bool, Dict]:
        obs, reward, terminated, truncated, info = super().step(action)
        self._current_obs = obs
        self._current_mask = info.get("action_mask", np.zeros(self.action_space.n, dtype=bool))
        return obs, reward, terminated, truncated, info
