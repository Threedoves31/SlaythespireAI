"""
Test a trained agent in the combat simulator.
Allows watching the AI play without opening the actual game.
"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import argparse
import numpy as np
from stable_baselines3 import PPO
from sb3_contrib import MaskablePPO

from simulator.env.combat_env import CombatEnv, END_TURN_ACTION
from simulator.core import CardType, TargetType


def print_combat_state(env):
    """Print a readable view of the current combat state."""
    state = env._state
    if state is None:
        return

    print("\n" + "="*60)
    print(f"Turn {state.turn} | Floor {env.player_hp} HP")
    print("="*60)

    # Player
    p = state.player
    print(f"\n[PLAYER] HP: {p.hp}/{p.max_hp} | Block: {p.block} | Energy: {p.energy}/{p.max_energy}")
    if p.powers:
        print(f"  Powers: {', '.join(f'{pow.id}({pow.amount})' for pow in p.powers)}")

    # Hand
    print(f"\n[HAND] ({len(p.hand)} cards)")
    for i, card in enumerate(p.hand):
        cost = card.cost if card.cost >= 0 else "X"
        target = " → Enemy" if card.target == TargetType.ANY_ENEMY else " → Self"
        playable = "✓" if card.can_play(p.energy) else "✗"
        print(f"  [{i}] {playable} {card.id}[{cost}] {card.definition.card_type.value}{target}")

    # Piles
    print(f"\n[PILES] Draw: {len(p.draw_pile)} | Discard: {len(p.discard_pile)} | Exhaust: {len(p.exhaust_pile)}")

    # Enemies
    print(f"\n[ENEMIES]")
    for i, e in enumerate(state.alive_enemies):
        intent = ""
        if e.current_move:
            if e.current_move.damage > 0:
                intent = f" → {e.current_move.damage}x{e.current_move.hits} {e.current_move.intent.value}"
            else:
                intent = f" → {e.current_move.intent.value}"
        print(f"  [{i}] {e.name}: HP {e.hp}/{e.max_hp} | Block {e.block}{intent}")
        if e.powers:
            print(f"      Powers: {', '.join(f'{pow.id}({pow.amount})' for pow in e.powers)}")

    print("="*60 + "\n")


def print_action(action, env):
    """Print what action the agent took."""
    if action == END_TURN_ACTION:
        print(f"[ACTION] End Turn")
        return

    hand_idx = action // 5  # MAX_ENEMIES
    enemy_idx = action % 5

    state = env._state
    p = state.player

    if hand_idx >= len(p.hand):
        print(f"[ACTION] Invalid: hand slot {hand_idx} empty")
        return

    card = p.hand[hand_idx]
    alive = state.alive_enemies

    if card.target == TargetType.ANY_ENEMY:
        if enemy_idx < len(alive):
            target = alive[enemy_idx]
            print(f"[ACTION] Play {card.id}[{card.cost}] → {target.name}")
        else:
            print(f"[ACTION] Play {card.id}[{card.cost}] → (targeted dead enemy)")
    else:
        print(f"[ACTION] Play {card.id}[{card.cost}] → Self")


def test_agent(
    model_path: str,
    n_episodes: int = 1,
    encounter_type: str = "normal",
    seed: int = 42,
    render: bool = True,
    verbose: bool = True,
):
    """Test a trained agent for n episodes."""
    print(f"\n{'='*60}")
    print(f"Testing agent from: {model_path}")
    print(f"Episodes: {n_episodes} | Encounter: {encounter_type}")
    print(f"{'='*60}\n")

    # Load model
    try:
        # Try MaskablePPO first (for action masking)
        model = MaskablePPO.load(model_path)
    except:
        try:
            # Fallback to regular PPO
            model = PPO.load(model_path)
        except Exception as e:
            print(f"Error loading model: {e}")
            return

    wins = 0
    total_turns = 0
    total_hp_lost = 0

    for ep in range(n_episodes):
        print(f"\n--- Episode {ep + 1}/{n_episodes} ---\n")

        # Create env
        env = CombatEnv(encounter_type=encounter_type, seed=seed + ep)

        obs, info = env.reset()

        if render:
            print_combat_state(env)

        done = False
        step = 0

        while not done:
            step += 1

            # Get action from model
            action, _ = model.predict(obs, deterministic=True)

            if verbose:
                print_action(action, env)

            # Step
            obs, reward, done, truncated, info = env.step(action)

            if render and done:
                # Final state
                print_combat_state(env)
                break

            # Print combat state every few steps or on new turn
            if render and (step % 5 == 0 or env._state.player.energy == env._state.player.max_energy):
                print_combat_state(env)

        # Episode results
        if info.get("result"):
            result = info["result"]
            if result["won"]:
                wins += 1
                print(f"\n[RESULT] VICTORY! Turns: {result['turns']} | HP remaining: {result['hp_remaining']}")
            else:
                print(f"\n[RESULT] DEFEAT. Turns: {result['turns']} | HP lost: {result['hp_lost']}")

            total_turns += result["turns"]
            total_hp_lost += result["hp_lost"]

        env.close()

    # Summary
    print(f"\n{'='*60}")
    print(f"SUMMARY over {n_episodes} episodes:")
    print(f"  Win Rate: {wins}/{n_episodes} ({100*wins/n_episodes:.1f}%)")
    print(f"  Avg Turns: {total_turns/n_episodes:.1f}")
    print(f"  Avg HP Lost: {total_hp_lost/n_episodes:.1f}")
    print(f"{'='*60}\n")


def interactive_mode(encounter_type: str = "normal", seed: int = 42):
    """Play a combat manually to test the simulator."""
    print(f"\n{'='*60}")
    print(f"INTERACTIVE MODE - Manual play testing")
    print(f"Encounter: {encounter_type} | Seed: {seed}")
    print(f"{'='*60}\n")

    env = CombatEnv(encounter_type=encounter_type, seed=seed)
    obs, info = env.reset()

    print_combat_state(env)

    action_mask = info["action_mask"]
    valid_actions = np.where(action_mask)[0]
    print(f"Valid actions: {list(valid_actions)}")
    print(f"Format: hand_idx*5 + enemy_idx, or {END_TURN_ACTION} to end turn")

    done = False
    while not done:
        try:
            action = input(f"\nEnter action: ").strip()
            if action.lower() in ['q', 'quit', 'exit']:
                break
            action = int(action)

            if action == END_TURN_ACTION:
                print_action(action, env)
            else:
                hand_idx = action // 5
                enemy_idx = action % 5
                print_action(action, env)

            obs, reward, done, truncated, info = env.step(action)
            print_combat_state(env)

            action_mask = info["action_mask"]
            valid_actions = np.where(action_mask)[0]
            print(f"Valid actions: {list(valid_actions)}")

        except KeyboardInterrupt:
            break
        except Exception as e:
            print(f"Error: {e}")
            continue

    if info.get("result"):
        result = info["result"]
        if result["won"]:
            print(f"\n[RESULT] VICTORY! Turns: {result['turns']} | HP remaining: {result['hp_remaining']}")
        else:
            print(f"\n[RESULT] DEFEAT. Turns: {result['turns']}")

    env.close()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Test trained STS2 combat agent")
    parser.add_argument("--model", type=str, help="Path to trained model")
    parser.add_argument("--episodes", type=int, default=1, help="Number of episodes to run")
    parser.add_argument("--encounter", default="normal", choices=["normal", "elite", "boss"])
    parser.add_argument("--seed", type=int, default=42)
    parser.add_argument("--interactive", action="store_true", help="Manual play mode")
    parser.add_argument("--no-render", action="store_true", help="Skip detailed state printing")

    args = parser.parse_args()

    if args.interactive:
        interactive_mode(args.encounter, args.seed)
    elif args.model:
        test_agent(
            model_path=args.model,
            n_episodes=args.episodes,
            encounter_type=args.encounter,
            seed=args.seed,
            render=not args.no_render,
            verbose=True,
        )
    else:
        print("Error: Please specify --model for testing or --interactive for manual play")
        print("Example:")
        print("  python test_agent.py --model training/models/ppo_combat_final.zip --episodes 5")
        print("  python test_agent.py --interactive --encounter elite")
