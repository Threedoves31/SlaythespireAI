import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from simulator.core import PlayerState, random_encounter, CombatState
import random

rng = random.Random(42)
player = PlayerState.ironclad(80)
enemies = random_encounter('normal', rng)
state = CombatState(player, enemies, rng)
state.start_player_turn()

print('Combat started:', state)
print('Hand:', player.hand)
print('Enemies:', enemies)
print()

for turn in range(10):
    if state.is_over:
        break
    print(f'--- Turn {state.turn} ---')
    playable = state.get_playable_cards()
    print(f'Playable: {[(i, str(c)) for i, c in playable]}')

    for i, card in list(playable):
        if state.is_over:
            break
        targets = state.get_valid_targets(card)
        target = targets[0] if targets else None
        state.play_card(card, target)

    if not state.is_over:
        state.end_player_turn()
        if not state.is_over:
            state.start_player_turn()

    print(f'After turn: {state}')

print()
print('Result:', state.result)
