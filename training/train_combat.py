"""
Train a PPO agent with action masking on the STS2 combat simulator.
Uses sb3-contrib MaskablePPO.
"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import numpy as np
from stable_baselines3.common.vec_env import SubprocVecEnv, VecMonitor
from stable_baselines3.common.callbacks import EvalCallback, CheckpointCallback
from sb3_contrib import MaskablePPO
from sb3_contrib.common.wrappers import ActionMasker

from simulator.env.combat_env import CombatEnv


def make_env(encounter_type="normal", seed=None):
    def _init():
        env = CombatEnv(encounter_type=encounter_type, seed=seed)
        env = ActionMasker(env, lambda e: e.action_masks())
        return env
    return _init


def train(
    total_timesteps: int = 2_000_000,
    n_envs: int = 8,
    encounter_type: str = "normal",
    save_dir: str = "training/models",
    log_dir: str = "training/logs",
):
    os.makedirs(save_dir, exist_ok=True)
    os.makedirs(log_dir, exist_ok=True)

    # Vectorized training envs
    train_env = SubprocVecEnv([make_env(encounter_type, seed=i) for i in range(n_envs)])
    train_env = VecMonitor(train_env)

    # Eval env (single, deterministic)
    eval_env = SubprocVecEnv([make_env(encounter_type, seed=9999)])
    eval_env = VecMonitor(eval_env)

    model = MaskablePPO(
        "MlpPolicy",
        train_env,
        verbose=1,
        tensorboard_log=log_dir,
        learning_rate=3e-4,
        n_steps=2048,
        batch_size=256,
        n_epochs=10,
        gamma=0.99,
        gae_lambda=0.95,
        clip_range=0.2,
        ent_coef=0.01,
        policy_kwargs=dict(net_arch=[256, 256]),
    )

    callbacks = [
        EvalCallback(
            eval_env,
            best_model_save_path=save_dir,
            log_path=log_dir,
            eval_freq=50_000 // n_envs,
            n_eval_episodes=50,
            deterministic=True,
        ),
        CheckpointCallback(
            save_freq=200_000 // n_envs,
            save_path=save_dir,
            name_prefix="ppo_combat",
        ),
    ]

    print(f"Training PPO on {encounter_type} encounters for {total_timesteps:,} steps...")
    model.learn(total_timesteps=total_timesteps, callback=callbacks, progress_bar=True)

    model.save(os.path.join(save_dir, "ppo_combat_final"))
    print(f"Model saved to {save_dir}/ppo_combat_final")

    train_env.close()
    eval_env.close()
    return model


if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("--steps", type=int, default=2_000_000)
    parser.add_argument("--envs", type=int, default=8)
    parser.add_argument("--encounter", default="normal", choices=["normal", "elite", "boss"])
    args = parser.parse_args()
    train(args.steps, args.envs, args.encounter)
