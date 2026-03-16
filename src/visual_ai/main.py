"""
Main loop for STS2 Visual AI.
Coordinates screen capture, UI detection, decision making, and input control.
"""
import time
import sys
import os
from pathlib import Path

# Add parent directory to path for imports
sys.path.insert(0, str(Path(__file__).parent.parent))

from visual_ai.vision import (
    create_capture,
    create_detector,
    create_combat_reader,
    UIType
)
from visual_ai.controller import (
    create_mouse_controller,
    create_keyboard_controller
)
from visual_ai.decision import (
    create_combat_ai,
    Action
)


class STS2VisualAI:
    """Main AI controller for STS2 using visual input."""

    def __init__(
        self,
        resolution=(2560, 1440),
        debug=False,
        use_ocr=False,
        use_shortcuts=False
    ):
        self.resolution = resolution
        self.debug = debug
        self.use_shortcuts = use_shortcuts  # Use keyboard shortcuts instead of mouse
        self.running = False

        # Initialize components
        print("Initializing STS2 Visual AI...")

        self.capture = create_capture(resolution)
        self.detector = create_detector(debug=debug)
        self.combat_reader = create_combat_reader(debug=debug, use_ocr=use_ocr)
        self.mouse = create_mouse_controller(resolution)
        self.keyboard = create_keyboard_controller()
        self.ai = create_combat_ai(debug=debug)

        print(f"STS2 Visual AI initialized! Shortcuts mode: {use_shortcuts}")

    def start(self):
        """Start the AI main loop."""
        self.running = True

        print("\n" + "="*60)
        print("STS2 Visual AI - Main Loop")
        print("="*60)
        print("\nControls:")
        print("  Press Ctrl+C to stop")
        print("  Make sure STS2 is running and visible")
        print("  Window will be detected automatically")
        print("\n" + "="*60 + "\n")

        try:
            self._main_loop()
        except KeyboardInterrupt:
            print("\n\nStopping AI...")
            self.running = False

    def _main_loop(self):
        """Main AI loop."""
        while self.running:
            try:
                # Capture screen
                img = self.capture.capture()

                if img is None:
                    print("Waiting for game window...")
                    time.sleep(1)
                    self.capture.refresh_window()
                    continue

                # Detect current UI
                ui_type = self.detector.detect(img)

                if self.debug:
                    print(f"\nCurrent UI: {ui_type}")

                # Handle based on UI type
                if ui_type == "combat":
                    self._handle_combat(img)
                elif ui_type == "map":
                    self._handle_map(img)
                elif ui_type == "shop":
                    self._handle_shop(img)
                elif ui_type == "event":
                    self._handle_event(img)
                elif ui_type == "reward":
                    self._handle_reward(img)
                elif ui_type == "rest":
                    self._handle_rest(img)
                else:
                    if self.debug:
                        print("Unknown UI, waiting...")
                    time.sleep(0.5)

            except Exception as e:
                print(f"Error in main loop: {e}")
                time.sleep(1)

    def _handle_combat(self, img):
        """Handle combat state."""
        # Read combat state
        state = self.combat_reader.read(img)

        if state is None:
            time.sleep(0.2)
            return

        # Check if combat is over
        if not state.enemies or all(e.hp <= 0 for e in state.enemies):
            print("Combat ended (victory!)")
            time.sleep(2)
            return

        if state.player_hp <= 0:
            print("Combat ended (defeat)")
            time.sleep(2)
            return

        # Get AI decision
        action = self.ai.decide(state)

        # Execute action
        self._execute_combat_action(action, state)

        # Wait for animation to play
        time.sleep(0.8)

    def _execute_combat_action(self, action: Action, state):
        """Execute a combat action using mouse/keyboard."""
        if action.type == "play_card":
            card = state.hand[action.card_index]

            if self.use_shortcuts:
                # Use keyboard shortcuts
                # Select card by number (1-9)
                self.keyboard.select_option(action.card_index + 1)
                time.sleep(0.1)

                # If targeting enemy, select target by number
                if action.target_index >= 0 and action.target_index < len(state.enemies):
                    self.keyboard.select_option(action.target_index + 1)
                    time.sleep(0.1)
            else:
                # Use mouse
                # Move to card and click
                x, y = card.position
                self.mouse.click(x, y)
                self.mouse.wait_after_action()

                # If targeting enemy, drag to target
                if action.target_index >= 0 and action.target_index < len(state.enemies):
                    enemy = state.enemies[action.target_index]
                    ex, ey = enemy.position
                    self.mouse.drag_to(x, y, ex, ey, duration=0.2)
                    self.mouse.wait_after_action()

            if self.debug:
                print(f"  Executed: Play {card.name} ({'shortcuts' if self.use_shortcuts else 'mouse'})")

        elif action.type == "end_turn":
            # Press space to end turn
            self.keyboard.end_turn()
            if self.debug:
                print("  Executed: End turn")

    def _handle_map(self, img):
        """Handle map state (not implemented yet)."""
        print("On map screen - not auto-playing yet")
        time.sleep(1)

    def _handle_shop(self, img):
        """Handle shop state (not implemented yet)."""
        print("In shop - not auto-playing yet")
        time.sleep(1)

    def _handle_event(self, img):
        """Handle event state (not implemented yet)."""
        print("In event - not auto-playing yet")
        time.sleep(1)

    def _handle_reward(self, img):
        """Handle card reward state (not implemented yet)."""
        print("Card reward - not auto-playing yet")
        time.sleep(1)

    def _handle_rest(self, img):
        """Handle rest state (not implemented yet)."""
        print("At rest site - not auto-playing yet")
        time.sleep(1)

    def stop(self):
        """Stop the AI."""
        self.running = False


def main():
    """Entry point for STS2 Visual AI."""
    import argparse

    parser = argparse.ArgumentParser(description="STS2 Visual AI - Play STS2 using computer vision")
    parser.add_argument("--resolution", type=str, default="2560x1440",
                       help="Screen resolution (format: WIDTHxHEIGHT)")
    parser.add_argument("--debug", action="store_true",
                       help="Enable debug output")
    parser.add_argument("--ocr", action="store_true",
                       help="Use OCR for reading numbers")
    parser.add_argument("--shortcuts", action="store_true",
                       help="Use keyboard shortcuts instead of mouse (faster)")
    parser.add_argument("--test", action="store_true",
                       help="Test mode - single capture and exit")

    args = parser.parse_args()

    # Parse resolution
    try:
        width, height = map(int, args.resolution.split('x'))
        resolution = (width, height)
    except:
        print(f"Invalid resolution: {args.resolution}")
        print("Use format: WIDTHxHEIGHT (e.g., 2560x1440)")
        return

    # Create AI
    ai = STS2VisualAI(
        resolution=resolution,
        debug=args.debug,
        use_ocr=args.ocr,
        use_shortcuts=args.shortcuts
    )

    if args.test:
        # Test mode - single capture
        print("\nTest mode - capturing single frame...")
        img = ai.capture.capture()
        if img is not None:
            ui_type = ai.detector.detect(img)
            print(f"Detected UI: {ui_type}")

            if ui_type == "combat":
                state = ai.combat_reader.read(img)
                if state:
                    print("\n" + str(state))
        return

    # Start AI
    ai.start()


if __name__ == "__main__":
    main()
