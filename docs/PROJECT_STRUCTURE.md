# SlaythespireAI - 项目结构

```
SlaythespireAI/
├── docs/                      # 项目文档
│   └── plan.md              # 实现计划和进度
│
├── references/                 # 外部参考（不随项目提交）
│   ├── sts2mod_references/ # 其他 Mod 的参考代码
│   ├── decompile_refs/      # 反编译的 STS2 代码
│   └── modding_docs.txt    # 官方 Mod 文档
│
├── src/                       # 源代码
│   ├── mod/                 # 方案 A: C# Mod（游戏内 API）
│   │   ├── STS2AIBot/
│   │   │   ├── StateExtractor/
│   │   │   ├── Communication/
│   │   │   ├── Patches/
│   │   │   └── AI/
│   │   └── mod_manifest.json
│   │
│   ├── visual_ai/            # 方案 B: 视觉 AI（屏幕识别 + 键鼠控制）
│   │   ├── vision/
│   │   │   ├── screen_capture.py    # 屏幕捕获
│   │   │   ├── ui_detector.py      # UI 界面检测
│   │   │   └── combat_reader.py    # 战斗状态读取
│   │   ├── controller/
│   │   │   ├── mouse_controller.py    # 鼠标模拟
│   │   │   └── keyboard_controller.py # 键盘模拟
│   │   ├── decision/
│   │   │   └── combat_ai.py         # 战斗 AI 决策
│   │   ├── main.py                  # 主循环
│   │   ├── requirements.txt
│   │   └── README.md
│   │
│   ├── simulator/            # Python 游戏模拟器
│   │   ├── core/
│   │   │   ├── combat.py
│   │   │   ├── card.py
│   │   │   ├── player.py
│   │   │   ├── enemy.py
│   │   │   └── power.py
│   │   ├── env/
│   │   │   └── combat_env.py
│   │   └── data/
│   │
│   └── training/             # RL 训练
│       ├── real_game_env.py    # 实际游戏环境
│       ├── train_real_game.py  # 训练脚本
│       ├── models/
│       ├── logs/
│       └── configs/
│
├── config/                    # 配置文件（可选）
│   └── settings.yaml
│
├── .gitignore                # Git 忽略文件
└── PROJECT_STRUCTURE.md       # 本文件
```

## 两个方案

| 方案 | 路径 | 说明 |
|------|------|------|
| **A. Mod 方案** | `src/mod/` | 通过游戏内 API 直接操作，精确但禁用成就 |
| **B. 视觉方案** | `src/visual_ai/` | 屏幕识别 + 键鼠模拟，像真实玩家，不禁用成就 |

## 开发指南

### 添加新功能

1. **Mod 方案** - 在 `src/mod/STS2AIBot/` 中添加 C# 代码
2. **视觉方案** - 在 `src/visual_ai/` 中添加 Python 代码
3. **模拟器** - 在 `src/simulator/` 中添加游戏逻辑
4. **训练** - 在 `src/training/` 中添加训练脚本

### 运行项目

```bash
# Mod 方案：编译 C# 后复制到游戏 mods 目录
# 视觉方案：
conda activate sts
python src/visual_ai/main.py --debug

# 训练：
python src/training/train_real_game.py --steps 1000
```

### 配置环境

```bash
# 安装依赖
pip install -r src/visual_ai/requirements.txt

# 或安装训练依赖
pip install -r requirements.txt  # 如果在根目录创建
```

## 注意事项

1. **src/mod/** 和 **src/visual_ai/** 是两个独立方案，可以同时存在
2. **references/** 中的代码是参考，不应该直接修改
3. 不要提交 **.pck** 文件（游戏资源包太大）
4. Python 脚本运行时需要游戏窗口可见（不能最小化）
5. 视觉方案需要正确匹配屏幕分辨率
