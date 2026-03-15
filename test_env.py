import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from simulator.env.combat_env import CombatEnv
import numpy as np

env = CombatEnv(encounter_type="normal", seed=42)
obs, info = env.reset()
print(f"Obs shape: {obs.shape}, dtype: {obs.dtype}")
print(f"Action space: {env.action_space}")
print(f"Obs space: {env.observation_space}")
print(f"Action mask sum (valid actions): {info['action_mask'].sum()}")

# Run a few random valid actions
total_reward = 0
for step in range(50):
    mask = info["action_mask"]
    valid_actions = np.where(mask)[0]
    action = np.random.choice(valid_actions)
    obs, reward, terminated, truncated, info = env.step(action)
    total_reward += reward
    if terminated:
        print(f"Episode done at step {step+1}, total reward: {total_reward:.2f}")
        break

print("Gymnasium env test passed!")
