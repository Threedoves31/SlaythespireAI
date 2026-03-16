"""
Train a PPO agent in the actual STS2 game.
Communicates with C# Mod via named pipes.

IMPORTANT:
1. Start the game with the STS2AIBot mod loaded
2. Enter a combat (any encounter)
3. Run this script to start training
4. The game will be controlled by the Python script

Usage:
    python train_real_game.py --steps 10000

Notes:
- Each episode = one combat encounter
- You need to manually start new combats in the game between episodes
- Or use the "Auto Combat" mode if available
"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import argparse
import time
import numpy as np
from stable_baselines3.common.callbacks import BaseCallback, CheckpointCallback
from sb3_contrib import MaskablePPO
from sb3_contrib.common.wrappers import ActionMasker

from training.real_game_env import RealGameEnvWithMask, PIPE_NAME


class EpisodeLoggerCallback(BaseCallback):
    """Log episode statistics."""

    def __init__(self, verbose=0):
        super().__init__(verbose)
        self.episode_rewards = []
        self.episode_wins = []
        self.episode_turns = []
        self.current_episode_reward = 0
        self.episode_count = 0

    def _on_step(self) -> bool:
        info = self.locals.get("infos", [{}])[0] if self.locals else {}

        # Track reward
        if "reward" in info:
            self.current_episode_reward += info["reward"]

        # Check if episode ended
        if info.get("done", False):
            self.episode_count += 1
            won = info.get("won", False)
            turns = info.get("turns", 0)

            self.episode_rewards.append(self.current_episode_reward)
            self.episode_wins.append(won)
            self.episode_turns.append(turns)
            self.current_episode_reward = 0

            # Print stats every episode
            wins_last = sum(self.episode_wins[-10:])
            total_wins = sum(self.episode_wins)
            avg_reward = np.mean(self.episode_rewards[-10:]) if self.episode_rewards else 0

            print(f"\n{'='*60}")
            print(f"Episode {self.episode_count}")
            print(f"  Result: {'VICTORY' if won else 'DEFEAT'}")
            print(f"  Turns: {turns}")
            print(f"  Reward: {self.current_episode_reward:.1f}")
            print(f"{'='*60}\n")

            if self.episode_count % 10 == 0:
                win_rate = 100 * wins_last / min(10, self.episode_count)
                print(f"\n[STATS] Last 10 episodes: {wins_last}/10 wins ({win_rate:.1f}%)")
                print(f"[STATS] Total: {total_wins}/{self.episode_count} wins")
                print(f"[STATS] Avg reward (last 10): {avg_reward:.1f}\n")

        return True


class ConnectionCheckCallback(BaseCallback):
    """Check if connection to game is still alive."""

    def __init__(self, verbose=0):
        super().__init__(verbose)
        self.last_check = 0
        self.check_interval = 100  # Check every 100 steps

    def _on_step(self) -> bool:
        if self.num_timesteps - self.last_check < self.check_interval:
            return True

        self.last_check = self.num_timesteps

        info = self.locals.get("infos", [{}])[0] if self.locals else {}

        if not info.get("connected", True):
            print("\n[WARNING] Lost connection to game!")
            print("Please check:")
            print("1. Game is still running")
            print("2. STS2AIBot mod is loaded")
            print("3. You are in a combat")
            print("4. Pipe server is initialized (check game logs)")
            return False

        return True


def make_env(render_mode=None):
    """Create the real game environment."""
    def _init():
        env = RealGameEnvWithMask(
            pipe_name=PIPE_NAME,
            render_mode=render_mode,
            verbose=False,  # Reduce log spam
        )
        env = ActionMasker(env, lambda e: e.action_masks())
        return env
    return _init


def train(
    total_timesteps: int = 10_000,
    save_dir: str = "training/models",
    log_dir: str = "training/logs",
    render: bool = False,
):
    """Train PPO agent in the real game."""
    os.makedirs(save_dir, exist_ok=True)
    os.makedirs(log_dir, exist_ok=True)

    print("="*60)
    print("STS2 REAL GAME TRAINING")
    print("="*60)
    print()
    print("IMPORTANT: Before starting training:")
    print("1. Start Slay the Spire 2")
    print("2. Load the STS2AIBot mod")
    print("3. Enter a combat (any enemy encounter)")
    print("4. Wait for 'Client connected!' message")
    print()
    print("Training will control the game automatically.")
    print("Each episode = one combat encounter.")
    print()
    print("After each combat:")
    print("- Victory: Start next combat to continue training")
    print("- Defeat: Restart or start next combat")
    print()
    print("="*60)
    print()

    input("Press Enter when you're in combat and ready to start...")

    # Create environment
    env = make_env(render_mode="human" if render else None)()

    # Check connection
    obs, info = env.reset()
    if not info.get("connected", False):
        print("\n[ERROR] Failed to connect to game!")
        print("Please check:")
        print("1. Game is running with STS2AIBot mod loaded")
        print("2. You are in a combat")
        print("3. Pipe server is initialized (check game logs)")
        return

    print("\n[SUCCESS] Connected to game! Starting training...\n")

    # Create model
    model = MaskablePPO(
        "MlpPolicy",
        env,
        verbose=1,
        tensorboard_log=log_dir,
        learning_rate=3e-4,
        n_steps=512,
        batch_size=64,
        n_epochs=10,
        gamma=0.99,
        gae_lambda=0.95,
        clip_range=0.2,
        ent_coef=0.01,
        policy_kwargs=dict(net_arch=[256, 256]),
    )

    # Callbacks
    callbacks = [
        EpisodeLoggerCallback(),
        ConnectionCheckCallback(),
        CheckpointCallback(
            save_freq=2000,
            save_path=save_dir,
            name_prefix="ppo_realgame",
        ),
    ]

    # Train
    print(f"\nTraining for {total_timesteps} steps...\n")

    try:
        model.learn(
            total_timesteps=total_timesteps,
            callback=callbacks,
            progress_bar=True
        )

        # Save final model
        model_path = os.path.join(save_dir, "ppo_realgame_final")
        model.save(model_path)
        print(f"\nModel saved to {model_path}\n")

    except KeyboardInterrupt:
        print("\n\nTraining interrupted by user. Saving current model...")
        model_path = os.path.join(save_dir, "ppo_realgame_interrupted")
        model.save(model_path)
        print(f"Model saved to {model_path}\n")

    except Exception as e:
        print(f"\n\nTraining error: {e}")
        import traceback
        traceback.print_exc()

    finally:
        env.close()

    print("Training complete!")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Train PPO agent in actual STS2 game")
    parser.add_argument("--steps", type=int, default=10000, help="Total training steps")
    parser.add_argument("--save-dir", type=str, default="training/models", help="Model save directory")
    parser.add_argument("--log-dir", type=str, default="training/logs", help="TensorBoard log directory")
    parser.add_argument("--render", action="store_true", help="Render game state during training")
    args = parser.parse_args()

    train(
        total_timesteps=args.steps,
        save_dir=args.save_dir,
        log_dir=args.log_dir,
        render=args.render,
    )
