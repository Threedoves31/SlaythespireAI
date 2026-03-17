#!/usr/bin/env python3
"""
AI Control Console - Send commands to STS2 AI Bot via named pipe.

Usage:
    python ai_control.py              # Interactive mode
    python ai_control.py status       # Single command
    python ai_control.py pause        # Toggle pause
    python ai_control.py policy Heuristic  # Set policy
"""

import sys

PIPE_NAME = r"\\.\pipe\STS2AIBot"

COMMANDS = {
    "p": "PAUSE",
    "pause": "PAUSE",
    "m": "MANUAL", 
    "manual": "MANUAL",
    "c": "POLICY",
    "policy": "POLICY",
    "v": "VERBOSE",
    "verbose": "VERBOSE",
    "h": "HISTORY",
    "history": "HISTORY",
    "s": "STATUS",
    "status": "STATUS",
    "?": "HELP",
    "help": "HELP",
}


def send_command(cmd: str) -> str:
    """Send command to pipe and return response."""
    try:
        import win32file
        import win32pipe
        import pywintypes
        
        # Open pipe
        handle = win32file.CreateFile(
            PIPE_NAME,
            win32file.GENERIC_READ | win32file.GENERIC_WRITE,
            0, None,
            win32file.OPEN_EXISTING,
            0, None
        )
        
        try:
            # Send command with newline
            win32file.WriteFile(handle, (cmd + "\n").encode("utf-8"))
            
            # Read response (may need multiple reads for long responses)
            response = b""
            while True:
                try:
                    # Set a timeout for reading
                    hr, data = win32file.ReadFile(handle, 4096)
                    response += data
                    # Check if we got a complete response (ends with newline)
                    if data.endswith(b"\n"):
                        break
                except pywintypes.error as e:
                    if e.winerror == 232:  # ERROR_NO_DATA
                        break
                    raise
            
            return response.decode("utf-8").strip()
        finally:
            win32file.CloseHandle(handle)
            
    except pywintypes.error as e:
        if e.winerror == 2:  # FILE_NOT_FOUND
            return "Error: Cannot connect to game. Is STS2 running with mod loaded?"
        return f"Error: {e.strerror}"
    except ImportError:
        return "Error: pywin32 not installed. Run: pip install pywin32"
    except Exception as e:
        return f"Error: {e}"


def interactive_mode():
    """Run interactive command loop."""
    print("=== STS2 AI Control Console ===")
    print("Commands: pause/p, manual/m, policy/c [type], verbose/v, history/h, status/s, help/?, quit/q")
    print()
    
    while True:
        try:
            line = input("ai> ").strip()
            if not line:
                continue
            
            parts = line.split(None, 1)
            cmd = parts[0].lower()
            
            if cmd in ("q", "quit", "exit"):
                break
            
            # Map short commands
            if cmd in COMMANDS:
                cmd = COMMANDS[cmd]
                if len(parts) > 1:
                    cmd += " " + parts[1]
            
            resp = send_command(cmd)
            print(resp)
            print()
            
        except EOFError:
            break
        except KeyboardInterrupt:
            print("\nBye!")
            break


def main():
    if len(sys.argv) > 1:
        # Single command mode
        cmd_parts = [sys.argv[1].lower()] + sys.argv[2:]
        cmd = cmd_parts[0]
        
        if cmd in COMMANDS:
            cmd = COMMANDS[cmd]
            if len(cmd_parts) > 1:
                cmd += " " + " ".join(cmd_parts[1:])
        
        print(send_command(cmd))
    else:
        interactive_mode()


if __name__ == "__main__":
    main()