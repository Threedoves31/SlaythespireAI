"""
Keyboard controller for STS2 AI.
Handles key presses with human-like timing.
"""
import time
import random
from typing import Optional

try:
    import pyautogui
    PYAUTOGUI_AVAILABLE = True
except ImportError:
    PYAUTOGUI_AVAILABLE = False
    print("Warning: pyautogui not available. Install with: pip install pyautogui")


class KeyboardController:
    """Controls keyboard for STS2 with human-like behavior."""

    def __init__(self):
        if PYAUTOGUI_AVAILABLE:
            print("KeyboardController: Initialized")

    def press(self, key: str, duration: float = 0.05):
        """Press and release a key."""
        if not PYAUTOGUI_AVAILABLE:
            print("KeyboardController: pyautogui not available")
            return

        pyautogui.press(key)
        time.sleep(random.uniform(0.05, 0.15))

    def press_combination(self, keys: list):
        """Press multiple keys simultaneously (e.g., Ctrl+Z)."""
        if not PYAUTOGUI_AVAILABLE:
            return

        pyautogui.hotkey(*keys)
        time.sleep(random.uniform(0.05, 0.15))

    def hold_and_release(self, key: str, hold_duration: float = 0.1):
        """Hold key for duration then release."""
        if not PYAUTOGUI_AVAILABLE:
            return

        pyautogui.keyDown(key)
        time.sleep(hold_duration)
        pyautogui.keyUp(key)

    def type_text(self, text: str, interval: float = 0.05):
        """Type text with human-like timing."""
        if not PYAUTOGUI_AVAILABLE:
            return

        for char in text:
            pyautogui.press(char)
            time.sleep(random.uniform(interval * 0.8, interval * 1.2))

    def wait_after_action(self, min_ms: int = 50, max_ms: int = 150):
        """Wait after key press to simulate human reaction time."""
        time.sleep(random.uniform(min_ms / 1000, max_ms / 1000))

    # STS2 specific shortcuts
    def end_turn(self):
        """End turn (Space or Enter in STS2)."""
        self.press('space')
        self.wait_after_action(200, 400)

    def select_option(self, number: int):
        """Select an option by number key (1, 2, 3)."""
        if 1 <= number <= 9:
            self.press(str(number))
            self.wait_after_action()

    def use_potion(self, slot: int):
        """Use potion from slot (typically 1-3 keys)."""
        if slot in [1, 2, 3]:
            # Potions might be on function keys or other bindings
            # Check actual STS2 key bindings
            pass

    def inspect_card(self):
        """Inspect card (typically Tab or I)."""
        self.press('tab')
        self.wait_after_action()

    def open_menu(self):
        """Open game menu (Esc or P)."""
        self.press('escape')
        self.wait_after_action()


def create_keyboard_controller() -> KeyboardController:
    """Factory function to create keyboard controller."""
    return KeyboardController()


if __name__ == "__main__":
    # Test keyboard controller
    controller = create_keyboard_controller()

    print("\nTesting keyboard controller...")
    print("Will press Space key in 3 seconds...")
    print("Press Ctrl+C to stop\n")

    try:
        time.sleep(3)
        controller.end_turn()
        print("Test complete!")

    except KeyboardInterrupt:
        print("\nStopped")
