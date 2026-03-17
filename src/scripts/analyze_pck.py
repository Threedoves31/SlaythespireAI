#!/usr/bin/env python3
"""Analyze PCK file format"""
import struct
import sys

def analyze_pck(pck_path):
    with open(pck_path, 'rb') as f:
        data = f.read()
    
    print(f"File: {pck_path}")
    print(f"Size: {len(data)} bytes")
    
    # Parse header
    magic = data[0:4]
    version = struct.unpack('<I', data[4:8])[0]
    godot_major = struct.unpack('<I', data[8:12])[0]
    godot_minor = struct.unpack('<I', data[12:16])[0]
    godot_patch = struct.unpack('<I', data[16:20])[0]
    godot_extra = struct.unpack('<I', data[20:24])[0]
    flags = struct.unpack('<I', data[24:28])[0]
    
    print(f"Magic: {magic}")
    print(f"Version: {version}")
    print(f"Godot: {godot_major}.{godot_minor}.{godot_patch}.{godot_extra}")
    print(f"Flags: {flags}")
    
    # Skip reserved bytes (16 * 4 = 64 bytes starting at offset 28)
    file_count_offset = 28 + 64
    file_count = struct.unpack('<I', data[file_count_offset:file_count_offset+4])[0]
    print(f"File count: {file_count}")
    
    # Parse file entries
    offset = file_count_offset + 4
    for i in range(file_count):
        path_len = struct.unpack('<I', data[offset:offset+4])[0]
        offset += 4
        path = data[offset:offset+path_len].decode('utf-8')
        offset += path_len
        file_offset = struct.unpack('<Q', data[offset:offset+8])[0]
        offset += 8
        file_size = struct.unpack('<Q', data[offset:offset+8])[0]
        offset += 8
        md5 = data[offset:offset+16].hex()
        offset += 16
        print(f"  File {i+1}: {path}")
        print(f"    Offset: {file_offset}, Size: {file_size}, MD5: {md5[:16]}...")

if __name__ == "__main__":
    pck_path = sys.argv[1] if len(sys.argv) > 1 else r"references\sts2mod_references\Mods\PartyObserver\PartyObserver.pck"
    analyze_pck(pck_path)