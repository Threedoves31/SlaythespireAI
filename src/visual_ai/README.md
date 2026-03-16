# STS2 Visual AI

A computer vision-based AI for Slay the Spire 2 that reads the game screen and simulates mouse/keyboard input.

## Features

- **Pure visual approach** - No game modding required, won't disable achievements
- **Automatic combat** - AI plays cards and targets enemies
- **Cross-resolution support** - Currently optimized for 2560x1440, extensible to other resolutions
- **Human-like input** - Simulates realistic mouse movement and timing

## Requirements

- Python 3.10+
- Slay the Spire 2 running
- Game window visible (not minimized)

## Installation

```bash
# Create conda environment (if not already)
conda create -n sts python=3.10 -y
conda activate sts

# Install dependencies
pip install -r visual_ai/requirements.txt

# Optional: Install Tesseract for OCR (better number reading)
# Windows: Download installer from https://github.com/UB-Mannheim/tesseract/wiki
# Add to PATH
```

## Usage

### Basic Usage

```bash
conda activate sts
cd e:\Programs\SlaythespireAI
python visual_ai/main.py --debug
```

### Options

```
--resolution WIDTHxHEIGHT    Screen resolution (default: 2560x1440)
--debug                    Enable debug output
--ocr                      Use OCR for reading numbers (slower but more accurate)
--shortcuts                Use keyboard shortcuts for card selection (faster than mouse)
--test                     Test mode - single capture and exit
```

### Examples

```bash
# Run with debug output
python src/visual_ai/main.py --debug

# Run with keyboard shortcuts (faster)
python src/visual_ai/main.py --shortcuts

# Run with OCR for better number recognition
python src/visual_ai/main.py --ocr

# Test screen capture only
python src/visual_ai/main.py --test
```

## How It Works

```
1. Screen Capture → mss captures game window
2. UI Detection   → Identifies current interface (combat/map/shop/etc.)
3. State Reading  → Extracts HP, energy, cards, enemies
4. AI Decision   → Heuristic or RL model chooses action
5. Input Control  → pyautogui simulates mouse/keyboard
6. Repeat
```

## Current Capabilities

| Feature | Status |
|---------|--------|
| Combat (play cards, target enemies) | ✅ Implemented |
| Combat (end turn) | ✅ Implemented |
| Map navigation | ⏳ Planned |
| Shop interactions | ⏳ Planned |
| Event choices | ⏳ Planned |
| Card rewards | ⏳ Planned |
| Rest site options | ⏳ Planned |

## Resolution Support

Currently optimized for **2560x1440**.

To add support for other resolutions:
1. Adjust position regions in `vision/combat_reader.py`
2. Scale coordinates in `controller/mouse_controller.py`
3. Test and adjust as needed

## Development

### Project Structure

```
visual_ai/
├── vision/              # Screen capture and state reading
│   ├── screen_capture.py
│   ├── ui_detector.py
│   └── combat_reader.py
├── controller/           # Mouse/keyboard simulation
│   ├── mouse_controller.py
│   └── keyboard_controller.py
├── decision/            # AI decision making
│   └── combat_ai.py
├── main.py              # Main loop
├── requirements.txt
└── README.md
```

### Testing Components

```bash
# Test screen capture
python -m visual_ai.vision.screen_capture

# Test UI detection
python -m visual_ai.vision.ui_detector

# Test combat reader
python -m visual_ai.vision.combat_reader

# Test mouse controller
python -m visual_ai.controller.mouse_controller

# Test keyboard controller
python -m visual_ai.controller.keyboard_controller

# Test combat AI
python -m visual_ai.decision.combat_ai
```

## Troubleshooting

### Window not found
- Make sure STS2 is running
- Check that the window title matches "Slay the Spire 2"
- Try running in windowed mode (not fullscreen)

### Mouse/keyboard not working
- Run as administrator
- Check that no other program is blocking input
- Try pressing Ctrl+C to stop and restart

### Inaccurate card/enemy detection
- Ensure game is at the correct resolution
- Adjust lighting/contrast settings in game
- Enable OCR with `--ocr` flag

## Notes

- **Safety**: AI will stop if you press Ctrl+C
- **Speed**: Visual approach is slower than mod-based, but more universal
- **Accuracy**: Color/position heuristics may fail on some cards/enemies
- **Training**: This is heuristic AI - for better performance, train an RL model

## Future Improvements

- [ ] Train RL model for combat decisions
- [ ] Add support for other resolutions
- [ ] Implement map navigation
- [ ] Add shop/event auto-play
- [ ] Improve card/enemy classification (ML-based)
- [ ] Add potion usage logic

## License

For personal use only. Do not use for competitive play.
