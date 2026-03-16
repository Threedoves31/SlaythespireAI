"""
Screen capture module for STS2.
Handles window detection and screen capture at specified resolution.
"""
import numpy as np
from typing import Optional, Tuple
import time

try:
    import cv2
    CV2_AVAILABLE = True
except ImportError:
    CV2_AVAILABLE = False
    print("Warning: opencv-python not available. Install with: pip install opencv-python")

try:
    import mss
    MSS_AVAILABLE = True
except ImportError:
    MSS_AVAILABLE = False
    print("Warning: mss not available. Install with: pip install mss")

try:
    import pygetwindow as gw
    WINDOW_AVAILABLE = True
except ImportError:
    WINDOW_AVAILABLE = False
    print("Warning: pygetwindow not available. Install with: pip install pygetwindow")


class ScreenCapture:
    """Screen capture for STS2 at specific resolution."""

    def __init__(self, target_resolution: Tuple[int, int] = (2560, 1440)):
        self.target_resolution = target_resolution
        self.window_title = "Slay the Spire 2"
        self.window_found = False
        self.monitor = None

        # Scale factors (for different resolutions)
        self.base_resolution = (2560, 1440)  # Reference resolution
        self.scale_x = target_resolution[0] / self.base_resolution[0]
        self.scale_y = target_resolution[1] / self.base_resolution[1]

        if MSS_AVAILABLE:
            self._init_mss()
        else:
            print("ScreenCapture: mss not available, using fallback")

        if WINDOW_AVAILABLE:
            self._find_window()

    def _init_mss(self):
        """Initialize mss screen capture."""
        if MSS_AVAILABLE:
            self.mss = mss.mss()
            print(f"ScreenCapture: Initialized for resolution {self.target_resolution}")

    def _find_window(self) -> Optional[Tuple[int, int, int, int]]:
        """Find STS2 window and get its bounds."""
        if not WINDOW_AVAILABLE:
            print("ScreenCapture: pygetwindow not available, using full screen")
            self.monitor = {
                "left": 0,
                "top": 0,
                "width": self.target_resolution[0],
                "height": self.target_resolution[1]
            }
            self.window_found = True
            return (0, 0, self.target_resolution[0], self.target_resolution[1])

        try:
            windows = gw.getWindowsWithTitle(self.window_title)
            if not windows:
                print(f"ScreenCapture: Window '{self.window_title}' not found")
                self.window_found = False
                return None

            window = windows[0]
            # Window might be minimized, restore it
            if window.left == -32000:
                window.restore()
                time.sleep(0.5)
                window = gw.getWindowsWithTitle(self.window_title)[0]

            left, top = window.left, window.top
            width, height = window.width, window.height

            # Center crop to target resolution if window is larger
            if width > self.target_resolution[0]:
                left += (width - self.target_resolution[0]) // 2
                width = self.target_resolution[0]
            if height > self.target_resolution[1]:
                top += (height - self.target_resolution[1]) // 2
                height = self.target_resolution[1]

            bounds = (left, top, width, height)
            self.monitor = {
                "left": left,
                "top": top,
                "width": width,
                "height": height
            }
            self.window_found = True
            print(f"ScreenCapture: Found window at {bounds}")
            return bounds

        except Exception as e:
            print(f"ScreenCapture: Error finding window: {e}")
            self.window_found = False
            return None

    def capture(self) -> Optional[np.ndarray]:
        """Capture screen and return as numpy array (BGR format)."""
        if not MSS_AVAILABLE:
            print("ScreenCapture: mss not available")
            return None

        if not self.window_found:
            self._find_window()
            if not self.window_found:
                return None

        try:
            screenshot = self.mss.grab(self.monitor)
            img = np.array(screenshot)
            # mss returns BGRA, convert to BGR
            img = cv2.cvtColor(img, cv2.COLOR_BGRA2BGR) if CV2_AVAILABLE else img[:, :, :3]
            return img
        except Exception as e:
            print(f"ScreenCapture: Error capturing: {e}")
            return None

    def refresh_window(self):
        """Refresh window detection (call when game might have moved)."""
        self._find_window()

    def scale_coords(self, x: float, y: float) -> Tuple[int, int]:
        """Scale coordinates from base resolution to actual resolution."""
        scaled_x = int(x * self.scale_x)
        scaled_y = int(y * self.scale_y)
        return (scaled_x, scaled_y)

    def save_debug_image(self, img: np.ndarray, filename: str):
        """Save image for debugging."""
        if CV2_AVAILABLE:
            cv2.imwrite(filename, img)
            print(f"ScreenCapture: Saved debug image to {filename}")


def create_capture(resolution: Tuple[int, int] = (2560, 1440)) -> ScreenCapture:
    """Factory function to create screen capture."""
    return ScreenCapture(resolution)


if __name__ == "__main__":
    # Test screen capture
    capture = create_capture()

    print("\nTesting screen capture...")
    print("Make sure STS2 is running and visible")
    print("Press Ctrl+C to stop\n")

    try:
        while True:
            img = capture.capture()
            if img is not None:
                print(f"Captured: {img.shape}")
                capture.save_debug_image(img, "visual_ai/data/debug_capture.png")
                break
            time.sleep(1)

    except KeyboardInterrupt:
        print("\nStopped")
