# STS2 AI - 头脑风暴与改进方案

## 当前状态

✅ Mod 底层操作正常工作（能够自动出牌）
✅ 基础启发式 AI 已实现

## 改进思路

---

### 1. AI 决策系统改进

#### 1.1 多策略分层决策

| 策略 | 说明 | 适用场景 |
|--------|------|----------|
| **SimpleHeuristic** | 当前：攻击 > 技能 > 能力 | 通用情况 |
| **ThreatBased** | 评估敌人威胁，优先防御或消除高威胁 | 敌人意图攻击时 |
| **ResourceOptimization** | 最大化能量利用效率 | 手牌多、能量足 |
| **ComboOriented** | 寻找卡牌协同效应 | 有特定卡牌组合 |
| **RiskManagement** | 血量低时优先防御 | HP < 50% |
| **Adaptive** | 从历史决策学习 | 长期运行积累数据后 |

**实现建议：**
- 切换策略可通过游戏内快捷键（F2）实时切换
- 每个策略都有不同的权重和优先级
- 自动根据当前状态选择最佳策略

#### 1.2 卡牌价值评估函数

需要为每张牌建立价值评分：

```csharp
float EvaluateCard(CardModel card, CombatState state) {
    float score = 0f;

    // 基础价值
    if (card.Type == CardType.Attack) {
        score += 10f * (card.Damage / card.EnergyCost);
    }

    // 场景加成
    if (state.PlayerHp < state.PlayerMaxHp * 0.5f) {
        if (card.IsDefensive) score += 8f; // 低血时防御更值钱
    }

    // 协同效应
    if (state.HasStrength && card.ScalesWithStrength) {
        score += 5f * state.Strength;
    }

    return score;
}
```

#### 1.3 序列决策（Monte Carlo Tree Search）

当前只考虑单步决策，可以扩展到多步：

```csharp
// 简化 MCTS
List<ActionSequence> SimulateTurn(CombatState state, int simulations) {
    var bestSequence = new ActionSequence();
    float bestScore = -999f;

    for (int i = 0; i < simulations; i++) {
        var sequence = RandomPlayTurn(state);
        var finalState = sequence.EndState;
        float score = EvaluateState(finalState);

        if (score > bestScore) {
            bestScore = score;
            bestSequence = sequence;
        }
    }

    return bestSequence;
}
```

---

### 2. 调试与监控系统

#### 2.1 游戏内调试控制台

| 快捷键 | 功能 | 说明 |
|--------|------|------|
| F1 | 切换调试覆盖层 | 显示 AI 决策过程 |
| F2 | 切换 AI 策略 | 循环切换 6 种策略 |
| F3 | 暂停/继续 AI | 暂停手动接管，继续自动 |
| F4 | 切换手动模式 | 完全手动控制，仅记录 |
| 1-5 | 评分上一步 | 1-5 星级评分 |
| 0 | 标记为差 | 用于学习避免 |
| 9 | 查看决策历史 | 最近 10 个决策 |

#### 2.2 外部监控界面

```python
# visual_ai/monitor.py - 外部监控窗口

import tkinter as tk
from threading import Thread
import time

class AIMonitor:
    def __init__(self):
        self.root = tk.Tk()
        self.root.title("STS2 AI Monitor")

        # 实时状态显示
        self.hp_label = tk.Label(self.root, text="HP: -/-", font=("Arial", 14))
        self.energy_label = tk.Label(self.root, text="Energy: -/3", font=("Arial", 14))
        self.hand_listbox = tk.Listbox(self.root, height=10)

        # AI 控制按钮
        self.pause_btn = tk.Button(self.root, text="暂停", command=self.toggle_pause)
        self.strategy_var = tk.StringVar(value="简单启发")
        self.strategy_menu = tk.OptionMenu(
            self.root,
            self.strategy_var,
            ["简单启发", "威胁评估", "资源优化", "组合导向", "风险管理", "自适应"]
        )

        # 评分面板
        self.rating_frame = tk.Frame(self.root)
        for i in range(1, 6):
            tk.Button(self.rating_frame, text=f"{i}星").pack(side=tk.LEFT)

        # 布局
        tk.Label(self.root, text="玩家状态").pack(pady=5)
        tk.Label(self.root, text="HP: -/-", font=("Arial", 18)).pack()
        tk.Label(self.root, text="能量: -/3", font=("Arial", 18)).pack()
        tk.Label(self.root, text="手牌:").pack(pady=10)
        self.hand_listbox.pack(pady=5, fill=tk.X, expand=True)
        tk.Label(self.root, text="AI 策略").pack(pady=10)
        self.strategy_menu.pack(pady=5)
        tk.Label(self.root, text="决策评分").pack(pady=10)
        self.rating_frame.pack()

        # 底部控制
        control_frame = tk.Frame(self.root)
        tk.Button(control_frame, text="暂停", width=10).pack(side=tk.LEFT, padx=5)
        tk.Button(control_frame, text="查看历史", width=10).pack(side=tk.LEFT, padx=5)

        control_frame.pack(pady=10)

    def update_state(self, state):
        """从游戏读取的状态更新界面"""
        self.hp_label.config(text=f"HP: {state.hp}/{state.max_hp}")
        self.energy_label.config(text=f"能量: {state.energy}/{state.max_energy}")

        self.hand_listbox.delete(0, tk.END)
        for card in state.hand:
            self.hand_listbox.insert(tk.END, f"{card.name} [{card.cost}]")
```

---

### 3. 学习与自改进

#### 3.1 人工评分系统

用户可以实时对 AI 的每个决策进行评分：

```csharp
public class FeedbackSystem {
    // 存储评分历史
    Dictionary<string, float> _cardScores = new();
    Dictionary<string, int> _cardPlays = new();

    public void RecordFeedback(string cardId, int stars) {
        _cardScores[cardId] = stars; // 1-5 星
        _cardPlays[cardId] = _cardPlays.GetValueOrDefault(cardId, 0) + 1;
    }

    public float GetCardScore(string cardId) {
        // 加权平均：用户评分占 70%，使用次数占 30%
        float userRating = _cardScores.GetValueOrDefault(cardId, 3f);
        int usage = _cardPlays.GetValueOrDefault(cardId, 1);

        // 使用频率高的卡降低评分（避免过度依赖）
        float frequencyPenalty = Math.Min(usage * 0.1f, 2f);

        return userRating * 0.7f - frequencyPenalty;
    }
}
```

#### 3.2 自学习/在线学习

```python
# training/online_learning.py

import numpy as np
from collections import deque

class OnlineLearner:
    def __init__(self, state_dim, action_dim):
        self.q_table = np.zeros((state_dim, action_dim))
        self.learning_rate = 0.1
        self.discount_factor = 0.95

    def update(self, state, action, reward, next_state):
        """Q-learning 更新"""
        current_q = self.q_table[state][action]
        max_next_q = np.max(self.q_table[next_state])

        new_q = current_q + self.learning_rate * (
            reward + self.discount_factor * max_next_q - current_q
        )

        self.q_table[state][action] = new_q

    def select_action(self, state):
        """ε-greedy 策略"""
        if np.random.random() < 0.1:  # 10% 探索
            return np.random.randint(0, self.q_table.shape[1])
        return np.argmax(self.q_table[state])
```

#### 3.3 专家系统与强化学习结合

```python
# 训练一个专家网络来模仿人类玩家

class ExpertImitation:
    def __init__(self):
        self.expert_data = []  # 收集人类对局数据

    def collect_human_game(self, replay_file):
        """从游戏回放收集人类决策"""
        # 解析游戏日志，提取（状态，动作）对
        pass

    def train_expert_network(self):
        """训练专家网络"""
        # 使用收集的数据训练神经网络
        pass
```

---

### 4. Meta 决策系统

#### 4.1 选牌决策

```python
class CardSelector:
    def select_best_card(self, options, current_deck):
        """从卡牌奖励中选择最佳卡牌"""
        scores = []
        for card in options:
            # 评分维度：
            # 1. 卡牌强度（模拟出牌胜率）
            # 2. 与现有卡组协同度
            # 3. 稀有度（稀有卡牌优先）
            # 4. 能量成本效率

            strength = self.simulate_card_value(card, current_deck)
            synergy = self.calculate_synergy(card, current_deck)
            rarity_bonus = card.rarity * 2
            cost_efficiency = (strength / max(card.cost, 1))

            score = strength + synergy + rarity_bonus + cost_efficiency
            scores.append((card, score))

        return max(scores, key=lambda x: x[1])[0]
```

#### 4.2 地图路径规划

```python
class MapNavigator:
    def plan_route(self, current_node, available_nodes, game_state):
        """规划地图路径"""
        # 考虑因素：
        # 1. 当前牌组强度
        # 2. 预期敌人类型
        # 3. 休息/商店/事件价值
        # 4. 风险承担

        path_scores = []
        for node in available_nodes:
            node_type = self.detect_node_type(node)
            node_value = self.evaluate_node_value(node_type, game_state)
            risk = self.assess_risk(node_type, node_type, game_state)

            score = node_value * 0.7 - risk * 0.3
            path_scores.append((node, score))

        return sorted(path_scores, key=lambda x: x[1], reverse=True)[0]
```

---

### 5. 数据持久化与分析

#### 5.1 决策历史存储

```csharp
public class DecisionHistory {
    // 存储所有决策用于分析
    List<DecisionRecord> _records = new();

    public void Save(string filePath) {
        var json = JsonSerializer.Serialize(_records);
        File.WriteAllText(filePath, json);
    }

    public void Load(string filePath) {
        var json = File.ReadAllText(filePath);
        _records = JsonSerializer.Deserialize<List<DecisionRecord>>(json);
    }

    public DecisionStats GetStats() {
        // 分析统计数据
        return new DecisionStats {
            WinRate = CalculateWinRate(),
            MostPlayedCards = GetMostPlayedCards(),
            AverageTurnsPerCombat = CalculateAvgTurns(),
            StrategyPerformance = GetStrategyPerformance()
        };
    }
}
```

#### 5.2 可视化分析

```python
# training/analyze_decisions.py

import matplotlib.pyplot as plt

def visualize_decision_patterns(history_file):
    """可视化决策模式"""
    import pandas as pd

    data = pd.read_json(history_file)

    # 1. 卡牌使用频率图
    fig, axes = plt.subplots(2, 2)
    data['card_id'].value_counts().head(10).plot(kind='bar', ax=axes[0,0])
    axes[0,0].set_title('最常用卡牌')

    # 2. 胜率与策略关系
    strategy_winrate = data.groupby('strategy')['success'].mean()
    strategy_winrate.plot(kind='bar', ax=axes[0,1])
    axes[0,1].set_title('各策略胜率')

    plt.tight_layout()
    plt.savefig('decision_analysis.png')
```

---

### 6. 高级功能建议

#### 6.1 实时对抗训练

```python
# 训练 AI 与不同敌人策略对抗

class SelfPlayTrainer:
    def __init__(self):
        self.ai_agent = AIAgent()
        self.enemy_agent = EnemyAgent()  # 模拟玩家

    def train_episode(self, max_turns=100):
        state = initialize_game()
        for turn in range(max_turns):
            # AI 行动
            ai_action = self.ai_agent.select_action(state)
            state = apply_action(state, ai_action)

            # 敌人行动
            enemy_action = self.enemy_agent.select_action(state)
            state = apply_action(state, enemy_action)

            if is_terminal(state):
                break

        return state
```

#### 6.2 多智能体协作

```python
class MultiAgentCoordinator:
    """如果未来支持多角色，可以训练不同角色之间的协作"""

    def __init__(self):
        self.ironclad_agent = CharacterAgent('ironclad')
        self.silent_agent = CharacterAgent('silent')
        self.defect_agent = CharacterAgent('defect')

    def coordinate_team_decisions(self, game_state):
        """协调团队决策"""
        # 分析哪个角色应该承担什么角色
        # 确保能量分配合理
        pass
```

---

## 实施优先级

### Phase 1: 增强调试系统（立即）
1. ✅ 游戏内快捷键控制（F1-F4）
2. ✅ 控制台决策日志
3. ✅ 暂停/手动模式
4. ✅ 评分界面
5. ✅ 策略切换

### Phase 2: AI 决策改进（短期）
1. ⏳ 多策略系统（6 种策略切换）
2. ⏳ 威胁评估逻辑
3. ⏳ 风险管理机制
4. ⏳ 协同效应检测
5. ⏳ 决策历史记录

### Phase 3: 学习系统（中期）
1. ⏳ 人工评分数据收集
2. ⏳ 基于评分的卡牌权重调整
3. ⏳ 在线 Q-learning 基础
4. ⏳ 决策统计与可视化

### Phase 4: 高级功能（长期）
1. ⏳ 序列决策（MCTS）
2. ⏳ 外部监控 GUI
3. ⏳ 专家模仿学习
4. ⏳ Meta 决策（选牌、地图）
5. ⏳ 自我对战训练

---

## 技术栈建议

### AI 框架
- **决策逻辑**: C# (游戏内） + Python（训练）
- **强化学习**: Stable-Baselines3 (PPO, DQN)
- **深度学习**: PyTorch + 神经网络
- **序列决策**: MCTS 或 AlphaZero 风格

### 数据存储
- **决策历史**: JSON 文件
- **游戏回放**: 可选的录像功能
- **统计数据库**: SQLite 或 PostgreSQL

### 可视化
- **实时监控**: Tkinter/PyQt
- **训练曲线**: TensorBoard
- **决策分析**: Matplotlib

---

## 用户交互设计

### 游戏内控制

| 操作 | 快捷键 | 说明 |
|------|--------|------|
| 暂停 AI | F3 | 暂停自动操作 |
| 切换策略 | F2 | 下一个策略 |
| 手动模式 | F4 | 完全手动，仅记录 |
| 调试覆盖 | F1 | 显示 AI 思考过程 |

### 评分系统

| 操作 | 快捷键 | 说明 |
|------|--------|------|
| 很好 | 5 | 标记决策为优秀 |
| 好 | 4 | 标记决策为好 |
| 一般 | 3 | 标记决策为一般 |
| 差 | 2 | 标记决策为差 |
| 很差 | 1 | 标记决策为很差 |

### 外部监控

```bash
# 启动外部监控窗口
python visual_ai/monitor.py

# 功能：
# - 实时显示游戏状态
# - 显示 AI 当前策略
# - 手动评分界面
# - 查看决策历史
# - 导出统计数据
```

---

## 关键指标追踪

### 战斗表现
- 每回合平均伤害
- 每回合能量利用率
- 卡牌使用分布
- 胜率按敌人类型
- 平均战斗回合数

### 学习进度
- 决策质量趋势
- 评分与 AI 选择的一致性
- 不同策略的胜率对比
- 探索/利用平衡

---

## 风险与注意事项

1. **游戏版本兼容性**: STS2 在 Early Access，更新可能破坏 Mod
2. **反作弊检测**: 确保行为不像机器人
3. **性能影响**: 大量日志可能影响游戏性能
4. **误决策影响**: 错误决策可能导致角色死亡

---

## 总结

**当前优势**:
- 底层 Mod 直接控制，精确可靠
- 实时决策，无需网络延迟
- 可以完全自动化战斗

**改进方向**:
- 从简单启发式 → 多策略自适应
- 从固定规则 → 学习型系统
- 从自动运行 → 人机协作（评分、暂停）

**下一步建议**:
1. 先完善 Phase 1 的调试功能
2. 实现并测试 Phase 2 的多策略系统
3. 收集数据用于 Phase 3 的学习系统
4. 持续迭代优化决策逻辑
