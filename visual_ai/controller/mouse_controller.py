"""
Mouse controller for STS2 AI.
Handles mouse movement, clicking, and dragging with human-like timing.
"""
import time
import random
from typing import Tuple, Optional

try:
    import pyautogui
    PYAUTOGUI_AVAILABLE = True
    # Safety: add fail-safe
    pyautogui.FAILSAFE = True
except ImportError:
    PYAUTOGUI_AVAILABLE = False
    print("Warning: pyautogui not available. Install with: pip install pyautogui")


class MouseController:
    """Controls mouse for STS2 with human-like behavior."""

    def __init__(self, base_resolution: Tuple[int, int] = (2560, 1440)):
        self.base_resolution = base_resolution
        self.actual_resolution = self._get_resolution()
        self.scale_x = self.actual_resolution[0] / base_resolution[0]
        self.scale_y = self.actual_resolution[1] / base_resolution[1]

        if PYAUTOGUI_AVAILABLE:
            print(f"MouseController: Initialized (actual: {self.actual_resolution}, base: {base_resolution})")

    def _get_resolution(self) -> Tuple[int, int]:
        """Get current screen resolution."""
        if PYAUTOGUI_AVAILABLE:
            try:
                size = pyautogui.size()
                return (size.width, size.height)
            except:
                pass
        return (1920, 1080)  # Fallback

    def scale_coords(self, x: int, y: int) -> Tuple[int, int]:
        """Scale coordinates from base resolution to actual screen."""
        scaled_x = int(x * self.scale_x)
        scaled_y = int(y * self.scale_y)
        return (scaled_x, scaled_y)

    def move_to(self, x: int, y: int, duration: float = 0.1):
        """Move mouse to coordinates with human-like speed."""
        if not PYAUTOGUI_AVAILABLE:
            print("MouseController: pyautogui not available")
            return

        scaled_x, scaled_y = self.scale_coords(x, y)

        # Add slight random variation for human-like movement
        # Small jitter around target
        jitter = 3
        target_x = scaled_x + random.randint(-jitter, jitter)
        target_y = scaled_y + random.randint(-jitter, jitter)

        pyautogui.moveTo(target_x, target_y, duration=duration)

    def click(self, x: int, y: int, button: str = 'left', duration: float = 0.1):
        """Click at coordinates with human-like timing."""
        self.move_to(x, y, duration)
        # Human-like pause before click
        time.sleep(random.uniform(0.05, 0.15))
        if PYAUTOGUI_AVAILABLE:
            pyautogui.click(button=button)

    def double_click(self, x: int, y: int, interval: float = 0.1):
        """Double click at coordinates."""
        self.click(x, y)
        time.sleep(interval)
        self.click(x, y)

    def drag_to(self, start_x: int, start_y: int, end_x: int, end_y: int, duration: float = 0.3):
        """Drag from start to end coordinates (for card targeting)."""
        if not PYAUTOGUI_AVAILABLE:
            print("MouseController: pyautogui not available")
            return

        scaled_start = self.scale_coords(start_x, start_y)
        scaled_end = self.scale_coords(end_x, end_y)

        # Move to start
        self.move_to(start_x, start_y, duration=0.1)
        time.sleep(0.05)

        # Press and drag
        if PYAUTOGUI_AVAILABLE:
            pyautogui.mouseDown()
            pyautogui.moveTo(scaled_end[0], scaled_end[1], duration=duration)
            pyautogui.mouseUp()

    def right_click(self, x: int, y: int):
        """Right click at coordinates."""
        self.click(x, y, button='right')

    def wait_after_action(self, min_ms: int = 100, max_ms: int = 300):
        """Wait after action to simulate human reaction time."""
        time.sleep(random.uniform(min_ms / 1000, max_ms / 1000))


def create_mouse_controller(base_resolution: Tuple[int, int] = (2560, 1440)) -> MouseController:
    """Factory function to create mouse controller."""
    return MouseController(base_resolution)


if __name__ == "__main__":
    # Test mouse controller
    controller = create_mouse_controller()

    print("\nTesting mouse controller...")
    print("Mouse will move to corners of screen")
    print("Press Ctrl+C to stop\n")

    try:
        time.sleep(2)
        # Test movement
        controller.click(100, 100)
        controller.wait_after_action()
        time.sleep(1)

        controller.click(1280, 720)  # Center
        controller.wait_after_action()
        time.sleep(1)

        controller.click(2460, 1400)  # Bottom right
        print("Test complete!")

    except KeyboardInterrupt:
        print("\nStopped")
