"""
Test a trained agent in the actual STS2 game.
Load a trained model and watch it play in the real game.

Usage:
    python test_real_game.py --model training/models/ppo_realgame_final.zip

IMPORTANT:
1. Start the game with the STS2AIBot mod loaded
2. Enter a combat
3. Run this script
"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import argparse
import time
import numpy as np
from sb3_contrib import MaskablePPO

from training.real_game_env import RealGameEnvWithMask, PIPE_NAME


def print_state(obs, info, action=None):
    """Print the current game state."""
    print("\n" + "="*60)

    # Player
    p = obs["player"]
    hp = int(p[0] * 100)
    max_hp = int(p[1] * 100)
    block = int(p[2] * 100)
    energy = int(p[3] * 5)
    max_energy = int(p[4] * 5)
    turn = int(p[5] * 50)

    print(f"Turn {turn}")
    print(f"[PLAYER] HP: {hp}/{max_hp} | Block: {block} | Energy: {energy}/{max_energy}")

    # Hand
    hand = obs["hand"]
    print("\n[HAND]")
    for i in range(10):
        if hand[i, 0] > 0.5:  # has card
            cost = int(hand[i, 1] * 5)
            ctype_idx = np.argmax(hand[i, 2:5])
            ctypes = ["Attack", "Skill", "Power"]
            ctype = ctypes[ctype_idx] if 0 <= ctype_idx < len(ctypes) else "Unknown"
            playable = "✓" if hand[i, 5] > 0.5 else "✗"
            print(f"  [{i}] {playable} {ctype}[{cost}]")

    # Enemies
    enemies = obs["enemies"]
    print("\n[ENEMIES]")
    for i in range(5):
        if enemies[i, 3] > 0.5:  # is alive
            hp_pct = enemies[i, 0]
            max_hp = int(enemies[i, 1] * 200)
            block = int(enemies[i, 2] * 100)

            # Intent
            intent_offset = 4 + 20  # 4 base + 20 powers
            intents = ["Attack", "Defend", "Buff", "Debuff", "Unknown"]
            intent_idx = np.argmax(enemies[i, intent_offset:intent_offset+len(intents)])
            intent = intents[intent_idx] if 0 <= intent_idx < len(intents) else "Unknown"

            print(f"  [{i}] HP: {int(hp_pct*max_hp)}/{max_hp} | Block: {block} | Intent: {intent}")

    # Last action
    if action is not None:
        if action == 50:
            print(f"\n[ACTION] End Turn")
        else:
            hand_idx = action // 5
            enemy_idx = action % 5
            print(f"\n[ACTION] Play hand[{hand_idx}] -> enemy[{enemy_idx}]")

    # Reward
    if "reward" in info:
        print(f"\n[REWARD] {info['reward']:.2f}")

    print("="*60 + "\n")


def print_action_mask(mask):
    """Print the valid actions."""
    valid = np.where(mask)[0]
    print(f"[VALID ACTIONS] {list(valid)}")
    if 50 in valid:
        print("  50 = End Turn")
    for a in valid:
        if a != 50:
            hand_idx = a // 5
            enemy_idx = a % 5
            print(f"  {a} = Play hand[{hand_idx}] -> enemy[{enemy_idx}]")


def test_agent(
    model_path: str,
    n_episodes: int = 1,
    render: bool = True,
    slow: bool = True,
):
    """Test a trained agent in the real game."""
    print("="*60)
    print("STS2 REAL GAME TEST")
    print("="*60)
    print()
    print("IMPORTANT:")
    print("1. Start Slay the Spire 2")
    print("2. Load the STS2AIBot mod")
    print("3. Enter a combat")
    print()

    input("Press Enter when ready...")

    # Load model
    print(f"\nLoading model from {model_path}...")
    model = MaskablePPO.load(model_path)
    print("Model loaded!\n")

    # Create environment
    env = RealGameEnvWithMask(
        pipe_name=PIPE_NAME,
        render_mode="none",
        verbose=False,
    )

    wins = 0
    total_turns = 0

    for ep in range(n_episodes):
        print(f"\n{'='*60}")
        print(f"EPISODE {ep + 1}/{n_episodes}")
        print(f"{'='*60}\n")

        # Reset
        obs, info = env.reset()

        if not info.get("connected", False):
            print("\n[ERROR] Failed to connect to game!")
            print("Please start the game with the mod and enter a combat.")
            break

        if render:
            print_state(obs, info)
            print_action_mask(info["action_mask"])

        # Episode loop
        done = False
        step_count = 0

        while not done:
            # Get action from model
            action, _ = model.predict(obs, deterministic=True)

            if render:
                print_state(obs, info, action)

            # Step
            obs, reward, done, truncated, info = env.step(action)

            if slow:
                time.sleep(0.1)  # Small delay for visibility

            step_count += 1

            if render and not done:
                print_action_mask(info["action_mask"])

        # Episode done
        won = info.get("won", False)
        turns = info.get("turns", 0)
        hp_remaining = info.get("hp_remaining", 0)

        if won:
            wins += 1
            print(f"\n{'='*60}")
            print(f"VICTORY! Turns: {turns} | HP Remaining: {hp_remaining}")
            print(f"{'='*60}\n")
        else:
            print(f"\n{'='*60}")
            print(f"DEFEAT. Turns: {turns}")
            print(f"{'='*60}\n")

        total_turns += turns

        # Wait for user to start next combat
        if ep < n_episodes - 1:
            input("\nPress Enter when you're in the next combat...")

    # Summary
    print(f"\n{'='*60}")
    print(f"SUMMARY")
    print(f"{'='*60}")
    print(f"Episodes: {n_episodes}")
    print(f"Wins: {wins}/{n_episodes} ({100*wins/n_episodes:.1f}%)")
    print(f"Avg Turns: {total_turns/n_episodes:.1f}")
    print(f"{'='*60}\n")

    env.close()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Test agent in actual STS2 game")
    parser.add_argument("--model", type=str, required=True, help="Path to trained model")
    parser.add_argument("--episodes", type=int, default=1, help="Number of episodes to run")
    parser.add_argument("--no-render", action="store_true", help="Disable state printing")
    parser.add_argument("--fast", action="store_true", help="No delay between steps")

    args = parser.parse_args()

    test_agent(
        model_path=args.model,
        n_episodes=args.episodes,
        render=not args.no_render,
        slow=not args.fast,
    )
