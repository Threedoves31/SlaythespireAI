"""
Combat state reader for STS2.
Reads player HP, energy, hand cards, enemies, and intents from screen.
"""
import numpy as np
import cv2
from dataclasses import dataclass
from typing import List, Optional
import re


@dataclass
class CardInfo:
    """Information about a card in hand."""
    id: str = ""
    name: str = ""
    cost: int = 0
    is_playable: bool = True
    card_type: str = ""  # Attack, Skill, Power
    position: tuple = (0, 0)  # (x, y) center of card


@dataclass
class EnemyInfo:
    """Information about an enemy."""
    id: str = ""
    name: str = ""
    hp: int = 0
    max_hp: int = 0
    block: int = 0
    intent_type: str = ""  # Attack, Defend, Buff, Debuff, Unknown
    intent_value: int = 0
    position: tuple = (0, 0)  # (x, y) center of enemy
    powers: List[str] = None

    def __post_init__(self):
        if self.powers is None:
            self.powers = []


@dataclass
class CombatState:
    """Complete combat state."""
    player_hp: int = 80
    player_max_hp: int = 80
    player_block: int = 0
    player_energy: int = 3
    player_max_energy: int = 3
    hand: List[CardInfo] = None
    enemies: List[EnemyInfo] = None
    potions: List[str] = None
    turn_number: int = 0

    def __post_init__(self):
        if self.hand is None:
            self.hand = []
        if self.enemies is None:
            self.enemies = []
        if self.potions is None:
            self.potions = []


class CombatReader:
    """Reads combat state from screenshot at 2560x1440 resolution."""

    def __init__(self, debug: bool = False, use_ocr: bool = False):
        self.debug = debug
        self.use_ocr = use_ocr

        # Try to import OCR libraries
        self.ocr_available = False
        try:
            import pytesseract
            self.pytesseract = pytesseract
            self.ocr_available = True
            if self.debug:
                print("CombatReader: OCR available (pytesseract)")
        except ImportError:
            if self.debug:
                print("CombatReader: OCR not available, using color/position heuristics")

        # Card database for color-based identification
        self._init_card_colors()

        # Reference positions for 2560x1440
        self.positions = {
            # Player area (bottom left)
            "player_hp": (50, 1180, 300, 1280),
            "player_block": (50, 1280, 300, 1320),
            "player_energy": (50, 1320, 300, 1380),

            # Hand area (bottom center)
            "hand": (100, 1100, 2460, 1400),

            # Enemy area (top)
            "enemies": (100, 100, 2460, 600),

            # Intents area (above enemies)
            "intents": (100, 50, 2460, 200),

            # Potions (right side)
            "potions": (2350, 400, 2500, 1200),

            # End turn button
            "end_turn": (2150, 1250, 2500, 1400),
        }

    def _init_card_colors(self):
        """Initialize color signatures for card identification."""
        # These are approximate colors for Ironclad cards
        # In production, load from config or train a classifier
        self.card_colors = {
            # Attack cards (red border)
            "Attack": {
                "border_hsv": (0, 200, 150),  # Red
                "border_range": ((0, 150, 100), (10, 255, 255)),
            },
            # Skill cards (green border) - Ironclad uses red
            "Skill": {
                "border_hsv": (60, 200, 150),  # Green (used by Silent)
                "border_range": ((50, 150, 100), (70, 255, 255)),
            },
            # Power cards (purple border)
            "Power": {
                "border_hsv": (140, 200, 150),  # Purple
                "border_range": ((130, 150, 100), (150, 255, 255)),
            },
        }

    def read(self, img: np.ndarray) -> Optional[CombatState]:
        """Read complete combat state from screenshot."""
        if img is None:
            return None

        state = CombatState()

        try:
            # Read player stats
            state.player_hp, state.player_max_hp = self._read_hp(img, "player_hp")
            state.player_block = self._read_block(img, "player_block")
            state.player_energy = self._read_energy(img)

            # Read hand cards
            state.hand = self._read_hand(img)

            # Read enemies
            state.enemies = self._read_enemies(img)

            # Read potions
            state.potions = self._read_potions(img)

            if self.debug:
                self._debug_print(state)

            return state

        except Exception as e:
            if self.debug:
                print(f"CombatReader: Error reading state: {e}")
            return None

    def _read_hp(self, img: np.ndarray, region_key: str) -> tuple:
        """Read HP values from screen region."""
        region = self.positions[region_key]
        hp_area = img[region[1]:region[3], region[0]:region[2]]

        # HP is typically displayed as "XX/YY" or "YY XX" format
        # Use OCR if available, otherwise use color-based digit recognition
        if self.ocr_available:
            return self._ocr_hp(hp_area)
        else:
            return self._color_digit_hp(hp_area)

    def _ocr_hp(self, img_area: np.ndarray) -> tuple:
        """Read HP using OCR."""
        # Preprocess for OCR
        gray = cv2.cvtColor(img_area, cv2.COLOR_BGR2GRAY)
        _, thresh = cv2.threshold(gray, 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU)

        # Use Tesseract to read text
        text = self.pytesseract.image_to_string(thresh, config='--psm 7 --oem 3 -c tessedit_char_whitelist=0123456789/')

        # Parse "XX/YY" format
        match = re.search(r'(\d+)/(\d+)', text)
        if match:
            return int(match.group(1)), int(match.group(2))

        # Fallback: look for two numbers
        numbers = re.findall(r'\d+', text)
        if len(numbers) >= 2:
            return int(numbers[0]), int(numbers[1])

        return 80, 80  # Default

    def _color_digit_hp(self, img_area: np.ndarray) -> tuple:
        """Read HP using color-based digit recognition (fallback)."""
        # This is a simplified version - in production, use trained digit recognizer
        # For now, estimate from average brightness (rough correlation with HP)
        gray = cv2.cvtColor(img_area, cv2.COLOR_BGR2GRAY)
        brightness = np.mean(gray)

        # Very rough approximation - replace with proper digit recognition
        hp = max(10, min(150, int(brightness * 0.5)))
        return hp, 100  # Default max HP

    def _read_block(self, img: np.ndarray, region_key: str) -> int:
        """Read block value from screen region."""
        region = self.positions[region_key]
        block_area = img[region[1]:region[3], region[0]:region[2]]

        # Block is shown as a number with a blue/orange bar background
        if self.ocr_available:
            gray = cv2.cvtColor(block_area, cv2.COLOR_BGR2GRAY)
            _, thresh = cv2.threshold(gray, 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU)
            text = self.pytesseract.image_to_string(thresh, config='--psm 7')
            numbers = re.findall(r'\d+', text)
            return int(numbers[0]) if numbers else 0
        else:
            # Simple: look for blue/orange color presence
            hsv = cv2.cvtColor(block_area, cv2.COLOR_BGR2HSV)
            block_mask = cv2.inRange(hsv, (90, 150, 100), (130, 255, 255))  # Blue
            return 10 if cv2.countNonZero(block_mask) > 1000 else 0

    def _read_energy(self, img: np.ndarray) -> int:
        """Read current energy from screen."""
        region = self.positions["player_energy"]
        energy_area = img[region[1]:region[3], region[0]:region[2]]

        # Energy shown as orbs or number
        if self.ocr_available:
            gray = cv2.cvtColor(energy_area, cv2.COLOR_BGR2GRAY)
            _, thresh = cv2.threshold(gray, 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU)
            text = self.pytesseract.image_to_string(thresh, config='--psm 7')
            numbers = re.findall(r'\d+', text)
            return int(numbers[0]) if numbers else 3
        else:
            # Count red energy orbs
            hsv = cv2.cvtColor(energy_area, cv2.COLOR_BGR2HSV)
            energy_mask = cv2.inRange(hsv, (0, 200, 150), (10, 255, 255))  # Red
            energy_count = cv2.countNonZero(energy_mask) // 5000  # Rough estimate
            return min(max(energy_count, 0), 10)

    def _read_hand(self, img: np.ndarray) -> List[CardInfo]:
        """Read cards in player's hand."""
        region = self.positions["hand"]
        hand_area = img[region[1]:region[3], region[0]:region[2]]

        # Find card contours
        gray = cv2.cvtColor(hand_area, cv2.COLOR_BGR2GRAY)
        edges = cv2.Canny(gray, 50, 150)
        contours, _ = cv2.findContours(edges, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)

        cards = []
        for contour in contours:
            area = cv2.contourArea(contour)
            if 50000 < area < 300000:  # Card size range
                x, y, w, h = cv2.boundingRect(contour)

                # Extract card region
                card_img = hand_area[y:y+h, x:x+w]

                # Determine card type from border color
                card_type = self._classify_card_type(card_img)

                # Estimate cost from card area
                cost = self._read_card_cost(card_img)

                # Check if playable (compare cost with player energy - would need state)
                is_playable = True  # Placeholder

                # Calculate center position
                center_x = region[0] + x + w // 2
                center_y = region[1] + y + h // 2

                cards.append(CardInfo(
                    name=f"Card_{len(cards)}",
                    cost=cost,
                    is_playable=is_playable,
                    card_type=card_type,
                    position=(center_x, center_y)
                ))

        if self.debug:
            print(f"CombatReader: Found {len(cards)} cards in hand")

        return cards

    def _classify_card_type(self, card_img: np.ndarray) -> str:
        """Classify card type by border color."""
        # Sample border color
        border_region = card_img[5:15, 5:15]  # Top-left corner border
        hsv = cv2.cvtColor(border_region, cv2.COLOR_BGR2HSV)
        avg_color = cv2.mean(hsv)[0:3]

        # Compare to known card type colors
        best_type = "Skill"  # Default
        best_dist = float('inf')

        for card_type, color_info in self.card_colors.items():
            ref_color = color_info["border_hsv"]
            dist = np.linalg.norm(np.array(avg_color) - np.array(ref_color))
            if dist < best_dist:
                best_dist = dist
                best_type = card_type

        return best_type

    def _read_card_cost(self, card_img: np.ndarray) -> int:
        """Read card cost energy number."""
        # Cost is shown in the top-left or top-right of the card
        # Look for blue/red circle with number inside
        if self.ocr_available:
            cost_region = card_img[10:50, 10:50]  # Top-left area where cost usually is
            gray = cv2.cvtColor(cost_region, cv2.COLOR_BGR2GRAY)
            _, thresh = cv2.threshold(gray, 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU)
            text = self.pytesseract.image_to_string(thresh, config='--psm 7 -c tessedit_char_whitelist=0123X-')
            # Parse cost
            if 'X' in text:
                return -1  # X-cost card
            numbers = re.findall(r'\d+', text)
            return int(numbers[0]) if numbers else 0
        else:
            # Simple: count red/blue energy-like shapes
            # For Ironclad, costs are usually 0-3
            return 1  # Placeholder

    def _read_enemies(self, img: np.ndarray) -> List[EnemyInfo]:
        """Read enemy information from screen."""
        region = self.positions["enemies"]
        enemies_area = img[region[1]:region[3], region[0]:region[2]]

        # Find enemy contours (larger than cards, in upper area)
        gray = cv2.cvtColor(enemies_area, cv2.COLOR_BGR2GRAY)
        _, thresh = cv2.threshold(gray, 200, 255, cv2.THRESH_BINARY_INV)
        contours, _ = cv2.findContours(thresh, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)

        enemies = []
        for contour in contours:
            area = cv2.contourArea(contour)
            if 200000 < area < 2000000:  # Enemy sprite size range
                x, y, w, h = cv2.boundingRect(contour)

                # Calculate center position
                center_x = region[0] + x + w // 2
                center_y = region[1] + y + h // 2

                # Read HP for this enemy
                hp_region = (center_x - 100, center_y - 150, center_x + 100, center_y - 100)
                hp, max_hp = self._read_hp(img, hp_region)

                # Read intent (look for icon above enemy)
                intent_region = (center_x - 50, center_y - 250, center_x + 50, center_y - 150)
                intent_type, intent_value = self._read_intent(img, intent_region)

                enemies.append(EnemyInfo(
                    id=f"Enemy_{len(enemies)}",
                    name=f"Enemy_{len(enemies)}",
                    hp=hp,
                    max_hp=max_hp,
                    block=0,  # Enemy block typically shown separately
                    intent_type=intent_type,
                    intent_value=intent_value,
                    position=(center_x, center_y),
                    powers=[]
                ))

        if self.debug:
            print(f"CombatReader: Found {len(enemies)} enemies")

        return enemies

    def _read_intent(self, img: np.ndarray, region: tuple) -> tuple:
        """Read enemy intent from icon region."""
        x1, y1, x2, y2 = region
        intent_area = img[y1:y2, x1:x2]

        # Intent icons have distinct colors
        hsv = cv2.cvtColor(intent_area, cv2.COLOR_BGR2HSV)

        # Attack (red sword)
        attack_mask = cv2.inRange(hsv, (0, 150, 100), (10, 255, 255))
        # Defend (blue shield)
        defend_mask = cv2.inRange(hsv, (100, 150, 100), (130, 255, 255))
        # Buff (green up arrow)
        buff_mask = cv2.inRange(hsv, (50, 150, 100), (70, 255, 255))
        # Debuff (purple down arrow)
        debuff_mask = cv2.inRange(hsv, (130, 150, 100), (150, 255, 255))

        attack_area = cv2.countNonZero(attack_mask)
        defend_area = cv2.countNonZero(defend_mask)
        buff_area = cv2.countNonZero(buff_mask)
        debuff_area = cv2.countNonZero(debuff_mask)

        max_area = max(attack_area, defend_area, buff_area, debuff_area)

        if max_area < 500:
            return "Unknown", 0
        elif max_area == attack_area:
            # Try to read damage number
            if self.ocr_available:
                # Read number next to sword icon
                pass  # Would need more precise region
            return "Attack", 0
        elif max_area == defend_area:
            return "Defend", 0
        elif max_area == buff_area:
            return "Buff", 0
        else:
            return "Debuff", 0

    def _read_potions(self, img: np.ndarray) -> List[str]:
        """Read available potions from screen."""
        region = self.positions["potions"]
        potions_area = img[region[1]:region[3], region[0]:region[2]]

        # Find potion slots
        gray = cv2.cvtColor(potions_area, cv2.COLOR_BGR2GRAY)
        _, thresh = cv2.threshold(gray, 200, 255, cv2.THRESH_BINARY_INV)
        contours, _ = cv2.findContours(thresh, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)

        potions = []
        for contour in contours:
            area = cv2.contourArea(contour)
            if 50000 < area < 300000:  # Potion slot size
                potions.append(f"Potion_{len(potions)}")

        if self.debug:
            print(f"CombatReader: Found {len(potions)} potions")

        return potions

    def _debug_print(self, state: CombatState):
        """Print combat state for debugging."""
        print(f"\n{'='*60}")
        print(f"Player: HP {state.player_hp}/{state.player_max_hp} | Block {state.player_block} | Energy {state.player_energy}/{state.player_max_energy}")
        print(f"Hand ({len(state.hand)} cards):")
        for i, card in enumerate(state.hand):
            print(f"  [{i}] {card.card_type}[{card.cost}] at {card.position}")
        print(f"Enemies ({len(state.enemies)}):")
        for i, enemy in enumerate(state.enemies):
            print(f"  [{i}] {enemy.name} HP {enemy.hp}/{enemy.max_hp} | Intent {enemy.intent_type}({enemy.intent_value})")
        print(f"Potions: {len(state.potions)}")
        print(f"{'='*60}\n")


def create_combat_reader(debug: bool = False, use_ocr: bool = False) -> CombatReader:
    """Factory function to create combat reader."""
    return CombatReader(debug, use_ocr)


if __name__ == "__main__":
    # Test combat reader
    from .screen_capture import create_capture

    capture = create_capture()
    reader = create_combat_reader(debug=True)

    print("\nTesting combat reader...")
    print("Make sure STS2 is in combat")
    print("Press Ctrl+C to stop\n")

    try:
        while True:
            img = capture.capture()
            if img is not None:
                state = reader.read(img)
                if state:
                    break
    except KeyboardInterrupt:
        print("\nStopped")
