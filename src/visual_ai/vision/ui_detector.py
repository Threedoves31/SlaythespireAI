"""
UI detector for STS2.
Identifies which interface the player is currently in.
"""
import numpy as np
from typing import Literal, Optional
import cv2

# UI type definitions
UIType = Literal["combat", "map", "shop", "event", "reward", "rest", "unknown"]


class UIDetector:
    """Detects current UI state from screenshot."""

    def __init__(self, debug: bool = False):
        self.debug = debug
        self._init_templates()

    def _init_templates(self):
        """Initialize detection templates for different UI states.
        In production, these should be loaded from image files.
        For now, using simple color/position heuristics.
        """
        # Reference positions for 2560x1440 resolution
        self.positions = {
            "combat": {
                # Check for end turn button (bottom right)
                "end_turn_region": (2000, 1200, 2500, 1400),
                # Check for hand cards area (bottom)
                "hand_region": (100, 1100, 2460, 1400),
                # Check for enemy intent area (top)
                "intent_region": (100, 100, 2460, 400),
            },
            "map": {
                # Check for map grid
                "map_region": (800, 200, 1760, 1100),
            },
            "shop": {
                # Check for shop cards grid
                "shop_region": (100, 300, 2460, 1200),
                # Check for gold display
                "gold_region": (2200, 50, 2500, 100),
            },
            "event": {
                # Check for event description box
                "event_region": (500, 300, 2060, 900),
                # Check for option buttons
                "options_region": (500, 900, 2060, 1300),
            },
        }

    def detect(self, img: np.ndarray) -> UIType:
        """Detect which UI is currently displayed."""
        if img is None:
            return "unknown"

        # Combat detection - look for end turn button and hand cards
        if self._detect_combat(img):
            return "combat"

        # Map detection - look for map grid pattern
        if self._detect_map(img):
            return "map"

        # Shop detection - look for shop cards and gold
        if self._detect_shop(img):
            return "shop"

        # Event detection - look for event text and options
        if self._detect_event(img):
            return "event"

        # Reward detection - look for card rewards
        if self._detect_reward(img):
            return "reward"

        # Rest detection - look for rest campfire
        if self._detect_rest(img):
            return "rest"

        return "unknown"

    def _detect_combat(self, img: np.ndarray) -> bool:
        """Check if currently in combat."""
        # Check for hand cards area (characteristic background pattern)
        hand_region = self.positions["combat"]["hand_region"]
        hand_area = img[hand_region[1]:hand_region[3], hand_region[0]:hand_region[2]]

        # Look for card-like shapes (multiple rounded rectangles at bottom)
        gray = cv2.cvtColor(hand_area, cv2.COLOR_BGR2GRAY)
        edges = cv2.Canny(gray, 50, 150)

        # Count contours - should have multiple cards in hand
        contours, _ = cv2.findContours(edges, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)

        # If we have multiple contours in hand area, it's likely combat
        card_count = sum(1 for c in contours if cv2.contourArea(c) > 5000)

        # Also check for enemy intent area (distinct icon pattern)
        intent_region = self.positions["combat"]["intent_region"]
        intent_area = img[intent_region[1]:intent_region[3], intent_region[0]:intent_region[2]]

        # Simple heuristic: combat = cards in hand + intent icons visible
        is_combat = card_count >= 3

        if self.debug:
            print(f"Combat detection: {card_count} cards → {is_combat}")

        return is_combat

    def _detect_map(self, img: np.ndarray) -> bool:
        """Check if on map screen."""
        map_region = self.positions["map"]["map_region"]
        map_area = img[map_region[1]:map_region[3], map_region[0]:map_region[2]]

        # Map has a distinctive grid pattern
        gray = cv2.cvtColor(map_area, cv2.COLOR_BGR2GRAY)
        edges = cv2.Canny(gray, 50, 150)

        # Count horizontal and vertical lines (grid)
        lines = cv2.HoughLinesP(edges, 1, np.pi/180, threshold=100, minLineLength=50, maxLineGap=10)

        has_grid = lines is not None and len(lines) > 10

        if self.debug:
            print(f"Map detection: {len(lines) if lines else 0} lines → {has_grid}")

        return has_grid

    def _detect_shop(self, img: np.ndarray) -> bool:
        """Check if in shop."""
        shop_region = self.positions["shop"]["shop_region"]
        shop_area = img[shop_region[1]:shop_region[3], shop_region[0]:shop_region[2]]

        # Shop has multiple card displays
        gray = cv2.cvtColor(shop_area, cv2.COLOR_BGR2GRAY)
        _, thresh = cv2.threshold(gray, 200, 255, cv2.THRESH_BINARY)

        # Count card-like regions
        contours, _ = cv2.findContours(thresh, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)

        # Shop typically shows 3-5 cards for sale
        card_count = sum(1 for c in contours if 10000 < cv2.contourArea(c) < 100000)

        # Also check for gold indicator (yellow text)
        gold_region = self.positions["shop"]["gold_region"]
        gold_area = img[gold_region[1]:gold_region[3], gold_region[0]:gold_region[2]]

        # Look for yellow/gold color
        hsv = cv2.cvtColor(gold_area, cv2.COLOR_BGR2HSV)
        gold_mask = cv2.inRange(hsv, (20, 100, 100), (40, 255, 255))
        has_gold = cv2.countNonZero(gold_mask) > 100

        is_shop = card_count >= 2 and has_gold

        if self.debug:
            print(f"Shop detection: {card_count} cards, gold={has_gold} → {is_shop}")

        return is_shop

    def _detect_event(self, img: np.ndarray) -> bool:
        """Check if in an event."""
        event_region = self.positions["event"]["event_region"]
        event_area = img[event_region[1]:event_region[3], event_region[0]:event_region[2]]

        # Events have text area with option buttons below
        gray = cv2.cvtColor(event_area, cv2.COLOR_BGR2GRAY)

        # Check for text-like patterns (alternating dark/light)
        text_density = np.mean(gray) < 200

        options_region = self.positions["event"]["options_region"]
        options_area = img[options_region[1]:options_region[3], options_region[0]:options_region[2]]

        # Look for button-like regions
        gray_opt = cv2.cvtColor(options_area, cv2.COLOR_BGR2GRAY)
        _, thresh = cv2.threshold(gray_opt, 200, 255, cv2.THRESH_BINARY)
        contours, _ = cv2.findContours(thresh, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)

        button_count = sum(1 for c in contours if 20000 < cv2.contourArea(c) < 300000)

        is_event = text_density and button_count >= 1

        if self.debug:
            print(f"Event detection: text={text_density}, buttons={button_count} → {is_event}")

        return is_event

    def _detect_reward(self, img: np.ndarray) -> bool:
        """Check if at card reward screen."""
        # Similar to shop but typically 3 cards centered
        gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)

        # Look for 3 card rewards in center
        center_region = (500, 400, 2060, 1000)
        center_area = img[center_region[1]:center_region[3], center_region[0]:center_region[2]]

        _, thresh = cv2.threshold(center_area, 200, 255, cv2.THRESH_BINARY)
        contours, _ = cv2.findContours(thresh, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)

        card_count = sum(1 for c in contours if 10000 < cv2.contourArea(c) < 100000)

        # Check for "Card Reward" or similar text area
        is_reward = card_count >= 2 and card_count <= 4

        if self.debug:
            print(f"Reward detection: {card_count} cards → {is_reward}")

        return is_reward

    def _detect_rest(self, img: np.ndarray) -> bool:
        """Check if at rest site."""
        # Rest screen has campfire icon and 3 options
        gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
        _, thresh = cv2.threshold(gray, 200, 255, cv2.THRESH_BINARY)

        contours, _ = cv2.findContours(thresh, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)

        # Look for ~3 button-like options
        button_count = sum(1 for c in contours if 20000 < cv2.contourArea(c) < 300000)

        # Campfire would have distinctive orange/red color
        hsv = cv2.cvtColor(img, cv2.COLOR_BGR2HSV)
        fire_mask = cv2.inRange(hsv, (0, 100, 100), (20, 255, 255))
        has_fire = cv2.countNonZero(fire_mask) > 500

        is_rest = button_count >= 2 and button_count <= 4 and has_fire

        if self.debug:
            print(f"Rest detection: buttons={button_count}, fire={has_fire} → {is_rest}")

        return is_rest


def create_detector(debug: bool = False) -> UIDetector:
    """Factory function to create UI detector."""
    return UIDetector(debug)


if __name__ == "__main__":
    # Test UI detector
    from .screen_capture import create_capture

    capture = create_capture()
    detector = create_detector(debug=True)

    print("\nTesting UI detection...")
    print("Press Ctrl+C to stop\n")

    try:
        while True:
            img = capture.capture()
            if img is not None:
                ui_type = detector.detect(img)
                print(f"Detected UI: {ui_type}")
                break
    except KeyboardInterrupt:
        print("\nStopped")
