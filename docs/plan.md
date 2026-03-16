# 杀戮尖塔 2 AI 自动通关 Mod — 实现计划

## Context

目标：构建一个基于分层强化学习的 AI，能全自动玩杀戮尖塔 2 并通关。
技术路线：分层 RL（战斗层 MCTS/PPO + Meta 层 PPO）+ Python 模拟器训练 + C# Mod 注入实际游戏。
起始角色：铁甲战士 (Ironclad)。
开发优先级：先做 Mod 读取游戏状态 → 再做模拟器 + RL 训练 → 最后整合。

## 部署

编译后将 `STS2AIBot.dll` 和 `STS2AIBot.pck` 复制到游戏 Mods 目录即可加载：

```
D:\Steam\steamapps\common\Slay the Spire 2\mods\
```

---


### 1.3 逆向分析 sts2.dll
用 Visual Studio Object Browser 或 dnSpy/ILSpy 反编译 `sts2.dll`，重点分析：

- `MegaCrit.Sts2.Core.Models` — 所有模型基类（CardModel, MonsterModel, RelicModel, PowerModel）
- `MegaCrit.Sts2.Core.Commands` — 战斗指令系统（DamageCmd, BlockCmd 等）
- 战斗管理器 — 找到管理回合、手牌、能量的核心类
- 玩家/敌人状态 — HP、Block、Status Effects 的存储位置
- 地图系统 — MapNode、路径选择逻辑
- 卡牌奖励 — 战斗后选牌的流程
- 商店/事件/休息点 — 各决策点的触发逻辑
- `Creature` 类 — 玩家和敌人的公共基类（从文档中 `OnPlay(PlayerChoiceContext, Creature target)` 可知）

关键类/命名空间待确认（需要实际反编译）：
- 战斗管理器（可能叫 `CombatManager` / `BattleManager`）
- 手牌管理（`Hand` / `CardGroup`）
- 敌人意图（`Intent` / `EnemyAction`）
- 地图（`MapGenerator` / `MapNode`）

---

## Phase 2: C# Mod — 游戏状态读取

### 2.1 Mod 入口 (`ModEntry.cs`)
```csharp
[ModInitializer(nameof(Init))]
public static class ModEntry
{
    public static void Init()
    {
        Harmony harmony = new("sts2aibot");
        harmony.PatchAll();
        Logger.Log("STS2 AI Bot initialized");
    }
}
```

### 2.2 游戏状态数据结构 (`CombatState.cs`)
定义一个可序列化的状态快照：
```csharp
public class CombatState
{
    public int PlayerHp, PlayerMaxHp, PlayerBlock, PlayerEnergy;
    public List<CardInfo> Hand;        // 当前手牌
    public List<CardInfo> DrawPile;    // 抽牌堆（数量，不知道顺序）
    public List<CardInfo> DiscardPile; // 弃牌堆
    public List<EnemyInfo> Enemies;    // 敌人列表
    public List<PowerInfo> PlayerPowers; // 玩家 buff/debuff
    public int TurnNumber;
    public int FloorNumber;
}
```

### 2.3 Harmony Patches
用 Postfix/Prefix patch 拦截关键游戏事件：
- 回合开始 → 提取完整战斗状态
- 卡牌奖励界面 → 提取可选卡牌
- 地图界面 → 提取可选路径
- 商店界面 → 提取商品列表
- 事件界面 → 提取选项
- 休息点 → 提取可用操作（休息/升级/回忆等）

### 2.4 AI 决策执行 (`ActionExecutor.cs`)
将 AI 输出的动作 ID 转换为游戏操作：
- 出牌：调用游戏内的打牌方法，指定目标
- 结束回合：调用结束回合方法
- 选牌：在奖励界面选择对应卡牌
- 选路：在地图上点击对应节点
- 商店操作：购买/移除卡牌

---

## Phase 3: Python 模拟器

### 3.1 核心战斗模拟
从 sts2.dll 反编译中提取铁甲战士的：
- 起始牌组（Strike x5, Defend x4, Bash x1 等）
- 所有可获得卡牌的效果
- Act 1 敌人列表及其 AI 模式（意图模式、伤害值）
- 基础遗物效果

实现最小可用战斗系统：
- 抽牌/出牌/弃牌循环
- 能量系统
- 伤害/格挡计算
- 基础 buff/debuff（力量、敏捷、易伤、虚弱）
- 敌人意图和行动模式

### 3.2 Gymnasium 环境
```python
class CombatEnv(gymnasium.Env):
    observation_space = Dict({
        'player': Box(...),      # HP, energy, block, buffs
        'hand': MultiBinary(N),  # one-hot 手牌编码
        'enemies': Box(...),     # 敌人 HP, intent, buffs
        'draw_pile': Box(...),   # 抽牌堆卡牌计数
        'discard_pile': Box(...) # 弃牌堆卡牌计数
    })
    action_space = Discrete(MAX_ACTIONS)  # 出牌+目标组合 + 结束回合
    # 配合 action mask 过滤无效动作
```

---

## Phase 4: RL 训练

### 4.1 战斗 Agent（优先）
- 算法：PPO + Action Masking（使用 sb3-contrib 的 MaskablePPO）
- 奖励：
  - +100 战斗胜利
  - -100 死亡
  - +0.1 × 造成伤害
  - -0.2 × 受到伤害
  - +5 × 消灭一个敌人
  - -0.01 每步（鼓励高效）
- 训练：16 个并行环境，~5M steps 达到基本能力

### 4.2 Meta Agent（后续）
- 选牌、选路、商店、事件、休息点决策
- 奖励：通关层数、剩余 HP、牌组质量评估

### 4.3 模型导出
训练完成后导出为 ONNX 格式，供 C# Mod 加载推理。

---

## Phase 5: 整合

将 ONNX 模型嵌入 C# Mod，实现完整的自动游戏循环：
1. Mod 拦截游戏状态 → 序列化为张量
2. ONNX Runtime 推理 → 得到动作
3. ActionExecutor 执行动作 → 游戏继续

---

## 实施顺序

| 步骤 | 内容 | 预期产出 |
|------|------|----------|
| 1 | 环境搭建 + conda | 可用的开发环境 |
| 2 | 逆向 sts2.dll | 关键类和方法的文档 |
| 3 | C# Mod 骨架 + 状态读取 | 能在游戏中打印战斗状态的 mod |
| 4 | Python 战斗模拟器 | 可运行的最小战斗模拟 |
| 5 | Gymnasium 环境封装 | 可训练的 RL 环境 |
| 6 | 战斗 PPO 训练 | 能赢 Act1 普通战斗的 agent |
| 7 | Meta agent + 完整模拟器 | 能跑完整局的 agent |
| 8 | ONNX 导出 + Mod 整合 | 实际游戏中自动操作 |

---

## 验证方式

- Phase 2：启动游戏，进入战斗，检查 mod 日志是否正确输出完整战斗状态
- Phase 3：运行模拟器，手动输入动作，验证战斗逻辑正确
- Phase 4：TensorBoard 监控训练曲线，验证胜率逐步提升
- Phase 5：启动游戏，观察 AI 是否能自动出牌并赢得战斗

## 当前第一步

创建 conda 环境 + 项目目录结构 + 开始逆向 sts2.dll。

---

## 进度更新 (2026-03-16)

### ✅ 已完成

| Phase | 状态 | 说明 |
|-------|------|------|
| **Phase 1: 环境搭建** | ✅ 完成 | conda 环境 `sts` 已创建，项目目录结构已建立 |
| **Phase 1: 逆向分析** | ✅ 完成 | tools/ 目录包含反编译的 C# 文件，已识别关键类 |
| **Phase 2: C# Mod** | ✅ 大部分完成 | 状态提取、补丁、启发式 AI、通信模块已完成 |
| **Phase 3: 模拟器** | ✅ 基本完成 | 战斗系统、卡牌、玩家、敌人、Power 系统已实现 |
| **Phase 4: RL 训练** | ⏳ 部分完成 | 实际游戏训练环境就绪，模拟器环境待完善 |
| **Phase 5: 整合** | ⏳ 待开始 | ONNX 导出 + Mod 整合 |

### 关键发现（逆向分析）

已通过分析 `sts2.dll` 确认以下关键类和方法：

**核心类：**
- `CombatManager` - 战斗管理，`Instance.IsInProgress`, `Instance.IsPlayPhase`
- `RunManager` - 游戏运行状态，`DebugOnlyGetState()`
- `Player` - 玩家实体，`Creature`, `PlayerCombatState`, `MaxEnergy`
- `Creature` - 玩家和敌人的公共基类
- `CardModel` - 卡牌基类，`OnPlay(PlayerChoiceContext, Creature target)`
- `MonsterModel` - 敌人模型，`NextMove.Intents`
- `PileType` - 牌堆类型：`Hand`, `Draw`, `Discard`, `Exhaust`

**命令系统：**
- `CardCmd.AutoPlay()` - 自动出牌
- `PlayerCmd.EndTurn()` - 结束回合
- `DamageCmd.Attack()` - 伤害命令
- `BlockingPlayerChoiceContext` - 阻塞式玩家选择上下文

### 已实现功能

**C# Mod (`mod/STS2AIBot/`)：**
- ✅ `ModEntry.cs` - Mod 入口，Harmony 补丁初始化
- ✅ `GameStateReader.cs` - 完整战斗状态提取（玩家、手牌、敌人、Powers、意图）
- ✅ `CombatHook.cs` - 回合开始拦截，启发式 AI 自动出牌
- ✅ `PipeServer.cs` - 命名管道服务器（与 Python 通信）
- ✅ `GameEnvironment.cs` - 训练环境管理（状态序列化、动作执行、奖励计算）

**Python 模拟器 (`simulator/core/`)：**
- ✅ `combat.py` - 核心战斗系统
- ✅ `card.py` - 卡牌定义（含铁甲战士 ~60+ 张卡牌）
- ✅ `player.py` - 玩家状态管理
- ✅ `enemy.py` - 敌人 AI 和意图系统
- ✅ `power.py` - Buff/Debuff 系统

**RL 训练 (`training/`)：**
- ✅ `real_game_env.py` - 实际游戏 Gymnasium 环境
- ✅ `train_real_game.py` - MaskablePPO 训练脚本
- ✅ 命名管道通信机制
- ✅ Action Masking 支持
- ⏳ `combat_env.py` - 模拟器环境（待完善）

### 待完成任务

**优先级 1 - 完善训练流程：**
1. ✅ 实际游戏训练环境已就绪
2. ⏳ 完善 `simulator/env/combat_env.py`（用于快速训练）
3. ⏳ 实现 ONNX 模型导出 (`training/export_onnx.py`)
4. ⏳ 实现 C# Mod 中的 ONNX 推理 (`mod/STS2AIBot/AI/OnnxInference.cs`)

**优先级 2 - Meta Agent：**
1. ⏳ 卡牌奖励决策
2. ⏳ 地图路径选择
3. ⏳ 商店/事件决策

**优先级 3 - 扩展：**
1. ⏳ 支持其他角色（Silent, Defect 等）
2. ⏳ Act 2/3 敌人
3. ⏳ 更多遗物系统

### 下一步建议

1. **测试实际游戏训练：**
   - 启动 STS2 并加载 Mod
   - 进入战斗后运行 `python training/train_real_game.py --steps 1000`
   - 验证通信和基本训练流程

2. **完善模拟器环境：**
   - 将模拟器连接到 Gymnasium 接口
   - 支持快速并行训练（vs 实际游戏的串行训练）

3. **实现 ONNX 导出和推理：**
   - 训练完成后导出模型
   - 在 Mod 中加载并执行推理
