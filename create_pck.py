#!/usr/bin/env python3
"""
Create a minimal Godot 4.x PCK file for STS2 mod.
PCK format: https://docs.godotengine.org/en/stable/tutorials/export/exporting_pcks.html

This creates a minimal PCK that just contains mod_manifest.json for mod discovery.
The actual code is loaded from the DLL file.
"""

import struct
import json
import os
import sys

def create_pck(manifest: dict, output_path: str):
    """Create a minimal Godot 4.x PCK file."""
    
    # Godot 4.x PCK header
    PCK_MAGIC = b'GDPC'
    PCK_VERSION = 2  # Godot 4.x uses version 2
    GODOT_VERSION = (4, 5, 1, 0)  # Match game's Godot version
    FLAGS = 0
    
    # Prepare manifest JSON
    manifest_json = json.dumps(manifest, ensure_ascii=False, indent=2)
    manifest_bytes = manifest_json.encode('utf-8')
    
    # File path in PCK (Godot res:// path)
    res_path = f'res://mod_manifest.json'
    res_path_bytes = res_path.encode('utf-8')
    
    # Calculate file base offset (after header + file table)
    # Header: 4 (magic) + 4 (version) + 4*4 (godot ver) + 4 (flags) + 4*16 (reserved) + 4 (file count) = 88 bytes
    # File entry: 4 (path len) + len(path) + 8 (offset) + 8 (size) + 16 (md5)
    header_size = 88
    file_entry_size = 4 + len(res_path_bytes) + 8 + 8 + 16
    file_base_offset = header_size + file_entry_size
    
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
        
        # Number of files
        f.write(struct.pack('<I', 1))  # Just one file
        
        # File entry
        f.write(struct.pack('<I', len(res_path_bytes)))       # Path length (4 bytes)
        f.write(res_path_bytes)                               # Path
        f.write(struct.pack('<Q', 0))                         # Offset (relative to file base, 0 = immediately after header)
        f.write(struct.pack('<Q', len(manifest_bytes)))       # Size
        f.write(b'\x00' * 16)                                 # MD5 hash (zero = not validated)
        
        # File content
        f.write(manifest_bytes)
    
    print(f"Created PCK: {output_path}")
    print(f"  Godot version: {GODOT_VERSION[0]}.{GODOT_VERSION[1]}.{GODOT_VERSION[2]}")
    print(f"  Embedded: {res_path} ({len(manifest_bytes)} bytes)")
    print(f"  Total size: {os.path.getsize(output_path)} bytes")
    return True

def main():
    # Mod configuration
    manifest = {
        "pck_name": "STS2AIBot",
        "name": "STS2 AI Bot",
        "author": "sts2aibot",
        "description": "Reinforcement learning AI that automatically plays Slay the Spire 2.",
        "version": "v0.1"
    }
    
    # Output path
    script_dir = os.path.dirname(os.path.abspath(__file__))
    output_path = os.path.join(script_dir, "src", "mod", "STS2AIBot.pck")
    
    create_pck(manifest, output_path)
    
    # Also copy to game mods directory if specified
    game_mod_dir = r"D:\Steam\steamapps\common\Slay the Spire 2\mods\STS2AIBot"
    if os.path.exists(game_mod_dir):
        import shutil
        dest = os.path.join(game_mod_dir, "STS2AIBot.pck")
        shutil.copy2(output_path, dest)
        print(f"  Copied to: {dest}")

if __name__ == "__main__":
    main()