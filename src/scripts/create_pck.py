#!/usr/bin/env python3
"""
Create a minimal Godot 4.x PCK file for STS2 mod.
Based on analysis of working mods like PartyObserver:
- PCK Version 3
- File count 0 (empty PCK, just a marker)
- mod_manifest.json is a separate file in the mod directory
"""

import struct
import os

def create_pck(output_path: str):
    """Create a minimal empty Godot 4.x PCK file (version 3)."""
    
    # Godot 4.x PCK header - match working mods
    PCK_MAGIC = b'GDPC'
    PCK_VERSION = 3  # Working mods use version 3
    GODOT_VERSION = (4, 5, 1, 2)  # Match game's Godot version
    FLAGS = 112  # Match working mods
    FILE_COUNT = 0  # Empty PCK - manifest is separate file
    
    with open(output_path, 'wb') as f:
        # Write header
        f.write(PCK_MAGIC)                                    # Magic (4 bytes)
        f.write(struct.pack('<I', PCK_VERSION))               # Format version (4 bytes)
        f.write(struct.pack('<I', GODOT_VERSION[0]))          # Godot major (4 bytes)
        f.write(struct.pack('<I', GODOT_VERSION[1]))          # Godot minor (4 bytes)
        f.write(struct.pack('<I', GODOT_VERSION[2]))          # Godot patch (4 bytes)
        f.write(struct.pack('<I', GODOT_VERSION[3]))          # Godot extra (4 bytes)
        f.write(struct.pack('<I', FLAGS))                     # Flags (4 bytes)
        
        # Reserved space (16 * 4 = 64 bytes)
        for _ in range(16):
            f.write(struct.pack('<I', 0))
        
        # Number of files (0 for empty PCK)
        f.write(struct.pack('<I', FILE_COUNT))
    
    print(f"Created PCK: {output_path}")
    print(f"  PCK Version: {PCK_VERSION}")
    print(f"  Godot version: {GODOT_VERSION[0]}.{GODOT_VERSION[1]}.{GODOT_VERSION[2]}.{GODOT_VERSION[3]}")
    print(f"  Flags: {FLAGS}")
    print(f"  File count: {FILE_COUNT}")
    print(f"  Total size: {os.path.getsize(output_path)} bytes")
    return True

def main():
    # 脚本在 src/scripts/ 目录，需要回到项目根目录
    script_dir = os.path.dirname(os.path.abspath(__file__))
    project_root = os.path.dirname(script_dir)  # 向上一级到 src/
    project_root = os.path.dirname(project_root)  # 向上一级到项目根目录
    output_path = os.path.join(project_root, "src", "mod", "STS2AIBot.pck")
    
    create_pck(output_path)
    
    # Also copy to game mods directory if it exists
    game_mod_dir = r"D:\Steam\steamapps\common\Slay the Spire 2\mods\STS2AIBot"
    if os.path.exists(game_mod_dir):
        import shutil
        dest = os.path.join(game_mod_dir, "STS2AIBot.pck")
        shutil.copy2(output_path, dest)
        print(f"  Copied to: {dest}")

if __name__ == "__main__":
    main()