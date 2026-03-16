# 铁甲战士 (Ironclad) — AI 策略文档

## 核心机制

### 力量 (Strength)
- 每点力量增加每次攻击的伤害
- Heavy Blade 力量加成 ×3，是力量体系的核心输出牌
- 优先打出 Inflame、Flex、Spot Weakness 等增益牌，再输出

### 易伤 (Vulnerable)
- 使目标受到的伤害 ×1.5
- Bash 施加 2 层易伤，应优先打在没有易伤的敌人身上
- 易伤后跟 Heavy Blade / Bludgeon 等高伤牌收益最大

### 自残换收益
- Offering：-6 HP，+2 能量 +3 抽牌（高 HP 时极强）
- Hemokinesis：-2 HP，15 伤害（1 费高效）
- 血量充足时（>60%）可以接受自残换收益

### 燃烧之血 (Burning Blood) 遗物
- 战斗结束回 6 HP
- 可以比其他角色更激进地消耗血量

---

## 出牌优先级

### 模拟评估系统
当前使用 DFS 模拟当前回合所有出牌序列，按以下权重评分：

| 因素 | 权重 | 说明 |
|------|------|------|
| 剩余 HP | ×100 | 最重要，每点 HP 价值 100 分 |
| 保留药水 | +30/个 | 每保留一个药水加 30 分 |
| 消灭敌人 | +50/个 | 击杀奖励 |
| 敌人剩余 HP | -0.5/点 | 造成伤害的次要奖励 |
| 格挡 | +0.5/点 | 微小加成 |

### 药水使用条件（保守策略）
- 治疗药水：HP < 40% 才使用
- 防御药水：受到的净伤害 ≥ 当前 HP（致命才用）
- 攻击药水：HP > 60% 且有存活敌人时使用

---

## 卡牌数据（战士完整列表）

### 起始牌组
| 卡牌 | 费用 | 类型 | 效果 |
|------|------|------|------|
| Strike | 1 | 攻击 | 6 伤害 |
| Defend | 1 | 技能 | 5 格挡 |
| Bash | 2 | 攻击 | 8 伤害 + 2 层易伤 |

### Common 攻击
| 卡牌 | 费用 | 效果 | 备注 |
|------|------|------|------|
| Anger | 0 | 6 伤害 | 0 费，加入弃牌堆 |
| Cleave | 1 | 8 群体伤害 | 清小怪 |
| Clothesline | 2 | 12 伤害 | 高伤 |
| Iron Wave | 1 | 5 伤 + 5 挡 | 攻防兼备 |
| Pommel Strike | 1 | 9 伤 + 抽 1 | 抽牌 |
| Sword Boomerang | 1 | 3×3 随机 | 多段 |
| Thunderclap | 1 | 4 群体 | 群体 |
| Twin Strike | 1 | 5×2 | 多段 |
| Wild Strike | 1 | 12 伤 | 加伤口 |
| Headbutt | 1 | 9 伤 + 回顶 | 循环 |
| Heavy Blade | 2 | 14+力量×3 | 力量核心 |

### Common 技能
| 卡牌 | 费用 | 效果 | 备注 |
|------|------|------|------|
| Armaments | 1 | 5 挡 + 升级 | |
| Flex | 0 | +2 力量（临时） | 0 费增益 |
| Shrug It Off | 1 | 8 挡 + 抽 1 | 高效防御 |
| True Grit | 1 | 7 挡 + 消耗 | |
| Warcry | 0 | 抽 1 + 回顶 | 0 费 |

### Common 能力
| 卡牌 | 费用 | 效果 |
|------|------|------|
| Inflame | 1 | +2 永久力量 |
| Metallicize | 1 | 每回合结束 +3 挡 |
| Combust | 1 | 每打出 1 牌 -1 HP，对所有敌人 5 伤 |
| Feel No Pain | 1 | 消耗牌时 +3 挡 |
| Evolve | 1 | 抽到状态牌时抽 1 |
| Fire Breathing | 1 | 抽到状态牌时对所有敌人 6 伤 |
| Rupture | 1 | 自残时 +1 力量 |
| Spot Weakness | 1 | 敌人攻击意图时 +3 力量 |

### Uncommon 攻击
| 卡牌 | 费用 | 效果 | 备注 |
|------|------|------|------|
| Carnage | 2 | 20 伤（虚空） | 高伤但虚空 |
| Dropkick | 1 | 5 伤（易伤时+能量+抽牌） | 易伤体系 |
| Hemokinesis | 1 | -2 HP + 15 伤 | 自残换伤 |
| Pummel | 1 | 2×4（消耗） | 多段 |
| Rampage | 1 | 8 伤（每次打出+5） | 叠加 |
| Reckless Charge | 0 | 7 伤（加混乱） | 0 费 |
| Whirlwind | X | 5×X 群体 | 清场 |

### Uncommon 技能
| 卡牌 | 费用 | 效果 | 备注 |
|------|------|------|------|
| Battle Trance | 0 | 抽 3（本回合不能抽牌） | 0 费爆发 |
| Blood for Blood | 4 | 18 伤（受伤后-1费） | 受伤后强 |
| Burning Pact | 1 | 消耗 1 牌 + 抽 2 | 牌库优化 |
| Disarm | 1 | 敌人 -2 力量（消耗） | 减伤 |
| Entrench | 2 | 格挡翻倍 | 高格挡时强 |
| Ghostly Armor | 1 | 10 挡（虚空） | |
| Intimidate | 0 | 所有敌人虚弱（消耗） | 0 费控制 |
| Power Through | 1 | 15 挡（加 2 伤口） | 高挡 |
| Rage | 0 | 本回合打出攻击时 +3 挡 | 0 费 |
| Second Wind | 1 | 消耗非攻击牌，每张 +5 挡 | |
| Seeing Red | 1 | +2 能量（消耗） | 能量爆发 |
| Sentinel | 1 | 5 挡（消耗时 +2 能量） | |
| Shockwave | 2 | 所有敌人虚弱+易伤（消耗） | 控制 |
| Spot Weakness | 1 | 敌人攻击时 +3 力量 | |

### Rare 攻击
| 卡牌 | 费用 | 效果 | 备注 |
|------|------|------|------|
| Bludgeon | 3 | 32 伤 | 暴力输出 |
| Feed | 1 | 10 伤（消耗，击杀+永久 HP） | |
| Fiend Fire | 2 | 消耗手牌，每张 7 伤（消耗） | |
| Immolate | 2 | 21 群体伤（加灼烧） | 群体 |
| Reaper | 2 | 4 群体伤，回复等量 HP（消耗） | 回血 |

### Rare 技能/能力
| 卡牌 | 费用 | 效果 | 备注 |
|------|------|------|------|
| Impervious | 2 | 30 挡（消耗） | 紧急防御 |
| Limit Break | 1 | 力量翻倍（消耗） | 力量体系终极 |
| Offering | 0 | -6 HP + 2 能量 + 抽 3（消耗） | 爆发 |
| Barricade | 3 | 格挡不在回合结束消失 | 格挡体系 |
| Berserk | 0 | +1 易伤，每回合 +1 能量 | |
| Brutality | 0 | 每回合开始 -1 HP + 抽 1 | |
| Corruption | 3 | 技能牌费用变 0，打出后消耗 | |
| Demon Form | 3 | 每回合开始 +2 力量 | 力量体系 |
| Exhume | 1 | 从消耗堆取回 1 张牌（消耗） | |
| Juggernaut | 2 | 获得格挡时对随机敌人造成等量伤害 | |

---

## 常见 Combo

1. **Bash → Heavy Blade**：Bash 施加易伤，Heavy Blade 打易伤目标，伤害 ×1.5 且力量加成 ×3
2. **Inflame/Flex → 多段攻击**：先叠力量，再打 Twin Strike / Sword Boomerang
3. **Offering → 大招**：-6 HP 换 +2 能量 +3 抽牌，接 Bludgeon 或 Whirlwind
4. **Limit Break → Heavy Blade**：力量翻倍后 Heavy Blade 伤害爆炸
5. **Shockwave → 全场输出**：易伤+虚弱后接群体攻击

---

## 已知局限

- 模拟不包含抽牌效果（Pommel Strike、Shrug It Off 的抽牌未模拟）
- 不模拟 Rampage 的叠加效果
- Whirlwind 的 X 费在模拟中消耗全部剩余能量
- 不考虑弃牌堆/抽牌堆的牌序
