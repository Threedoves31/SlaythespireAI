# 真实游戏训练使用指南

本文档说明如何在杀戮尖塔2真实游戏环境中训练AI Agent。

---

## 架构概述

```
┌─────────────────────────────────────────────────────────────┐
│  Python 训练脚本                                            │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │ train_real_game.py                                      │ │
│  │ - MaskablePPO 算法                                       │ │
│  │ - 通过命名管道与游戏通信                                    │ │
│  └─────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
                          │
                          │ 命名管道 (Named Pipe)
                          │ \\.\pipe\STS2AIBot_Training
                          │
┌─────────────────────────────────────────────────────────────┐
│  杀戮尖塔2 + STS2AIBot Mod                                  │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │ PipeServer.cs - 管道服务器                               │ │
│  │ GameEnvironment.cs - 游戏环境封装                          │ │
│  │ - 接收 Python 发送的动作                                   │ │
│  │ - 执行动作 (出牌/结束回合)                                   │ │
│  │ - 返回游戏状态和奖励                                         │ │
│  └─────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

---

## 准备工作

### 1. 安装 Python 依赖

```bash
pip install -r training/requirements.txt
```

### 2. 编译 C# Mod

在 Visual Studio 中打开 `mod/STS2AIBot.sln`，编译项目。

或者使用命令行：
```bash
cd mod/STS2AIBot
dotnet publish -c Release
```

### 3. 将 Mod 复制到游戏目录

将以下文件复制到游戏安装目录的 `Mods` 文件夹：
- `mod/STS2AIBot.pck`
- `mod/STS2AIBot/STS2AIBot/bin/Release/net9.0/STS2AIBot.dll`

---

## 使用方法

### 训练 Agent

1. **启动游戏** 并加载 STS2AIBot Mod
2. **进入战斗**（任何敌人遭遇战）
3. **运行训练脚本**：
   ```bash
   python training/train_real_game.py --steps 10000
   ```

4. 按 **Enter** 开始训练

脚本将自动控制游戏进行战斗。每次战斗结束（胜利或失败）后：
- **胜利**：开始下一场战斗继续训练
- **失败**：重新开始或开始下一场战斗

### 测试 Agent

```bash
python training/test_real_game.py --model training/models/ppo_realgame_final.zip
```

---

## 参数说明

### train_real_game.py

| 参数 | 默认值 | 说明 |
|------|--------|------|
| `--steps` | 10000 | 训练总步数 |
| `--save-dir` | training/models | 模型保存目录 |
| `--log-dir` | training/logs | TensorBoard 日志目录 |
| `--render` | False | 是否打印游戏状态 |

### test_real_game.py

| 参数 | 默认值 | 说明 |
|------|--------|------|
| `--model` | (必需) | 模型路径 |
| `--episodes` | 1 | 测试回合数 |
| `--no-render` | False | 禁用状态打印 |
| `--fast` | False | 无延迟 |

---

## 监控训练

### 查看控制台输出

训练脚本会打印：
- 每回合的结果（胜利/失败）
- 最近10回合的胜率
- 平均奖励

```
============================================================
Episode 5
  Result: VICTORY
  Turns: 3
  Reward: 98.0
============================================================

[STATS] Last 10 episodes: 7/10 wins (70.0%)
[STATS] Total: 15/20 wins
[STATS] Avg reward (last 10): 45.2
```

### 查看 TensorBoard

```bash
tensorboard --logdir training/logs
```

在浏览器打开 `http://localhost:6000` 查看：
- `rollout/ep_rew_mean` - 平均回合奖励
- `train/learning_rate` - 学习率
- `train/entropy_loss` - 熵损失

---

## 故障排除

### 连接失败

**问题**：`[ERROR] Failed to connect to game!`

**解决方案**：
1. 确认游戏正在运行
2. 确认 STS2AIBot Mod 已加载
3. 确认你已进入战斗
4. 检查游戏日志，确认 `[PipeServer] Client connected!` 消息

### 找不到 pywin32

**问题**：`ModuleNotFoundError: No module named 'win32file'`

**解决方案**：
```bash
pip install pywin32
```

### 游戏无响应

**问题**：执行动作后游戏没有反应

**解决方案**：
1. 检查游戏日志中的错误信息
2. 确认动作掩码正确
3. 重启游戏和训练脚本

---

## 技术细节

### 通信协议

使用 Windows 命名管道进行通信：

```
Python -> C#: COMMAND|JSON_PAYLOAD
C# -> Python: COMMAND|JSON_PAYLOAD
```

### 消息类型

| 命令 | 方向 | 说明 |
|------|------|------|
| RESET (0) | Python -> C# | 重置环境 |
| STEP (1) | Python -> C# | 执行动作 |
| GET_ACTION_MASK (2) | Python -> C# | 获取有效动作 |
| GET_STATE (3) | Python -> C# | 获取当前状态 |
| STATE (10) | C# -> Python | 返回游戏状态 |
| DONE (11) | C# -> Python | 回合结束 |
| ERROR (12) | C# -> Python | 错误 |

### 动作空间

动作格式：`hand_idx * 5 + enemy_idx`

- `hand_idx`: 手牌索引 (0-9)
- `enemy_idx`: 敌人索引 (0-4)
- `50`: 结束回合

### 奖励函数

```
胜利:  +100
失败:  -100
造成伤害:  +0.1 * 伤害值
消灭敌人:  +5 * 敌人数
每步:    -0.01 (鼓励高效)
```

---

## 下一步

1. 训练基础战斗 Agent
2. 扩展到完整的游戏流程（选牌、选路、商店等）
3. 添加多角色支持
4. 优化奖励函数
5. 实现 Meta 层决策
