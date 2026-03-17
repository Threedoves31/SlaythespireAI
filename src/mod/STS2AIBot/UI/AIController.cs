using System;
using MegaCrit.Sts2.Core.Logging;
using STS2AIBot.AI;

namespace STS2AIBot.UI;

/// <summary>
/// In-game keyboard controller for manual override and debugging.
/// Uses game input bindings to control AI behavior.
/// Now uses PolicyManager for pluggable AI strategies.
/// </summary>
public class AIController
{
    private DebugWindow? _debugWindow;
    private DecisionEngine? _decisionEngine;
    private bool _initialized = false;

    public AIController(DebugWindow debugWindow, DecisionEngine decisionEngine)
    {
        _debugWindow = debugWindow;
        _decisionEngine = decisionEngine;
    }

    public void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        Log.Info("[AIController] Initialized - Press F1 for commands");
        PrintHelp();
    }

    public void OnKeyPressed(string key)
    {
        if (key == null) return;

        switch (key.ToLower())
        {
            // F1 - Toggle debug overlay
            case "f1":
                // Would show in-game overlay if implemented
                Log.Info("[AIController] Debug overlay toggled");
                break;

            // F2 - Cycle AI policy (NEW: uses PolicyManager)
            case "f2":
                CyclePolicy();
                break;

            // F3 - Toggle pause
            case "f3":
                _debugWindow?.TogglePause();
                break;

            // F4 - Toggle manual mode
            case "f4":
                _debugWindow?.ToggleManualMode();
                break;

            // 1-5 - Manual card rating
            case "1":
                _debugWindow?.RateLastAction(1);
                break;
            case "2":
                _debugWindow?.RateLastAction(2);
                break;
            case "3":
                _debugWindow?.RateLastAction(3);
                break;
            case "4":
                _debugWindow?.RateLastAction(4);
                break;
            case "5":
                _debugWindow?.RateLastAction(5);
                break;

            // 0 - Rate poor
            case "0":
                _debugWindow?.RateLastAction(0);
                break;

            // 9 - View history
            case "9":
                _debugWindow?.ShowHistory();
                break;

            // Backspace - Undo last action (manual)
            case "backspace":
                Log.Info("[AIController] Manual undo - (not implemented)");
                break;
        }
    }

    /// <summary>
    /// Cycle through available AI policies using PolicyManager.
    /// </summary>
    private void CyclePolicy()
    {
        PolicyManager.Instance.CyclePolicy();
        var current = PolicyManager.Instance.CurrentPolicy;
        Log.Info($"[AIController] Switched to: {current.Name} - {current.Description}");
        _debugWindow?.ShowMessage($"Policy: {current.Name}");
    }

    private void PrintHelp()
    {
        Log.Info("╔════════════════════════════════════════════════════╗");
        Log.Info("║          STS2 AI - In-Game Commands                ║");
        Log.Info("╠════════════════════════════════════════════════════╣");
        Log.Info("║  F1  - Toggle debug overlay (planned)              ║");
        Log.Info("║  F2  - Cycle AI policy (Heuristic/Sim/Random)      ║");
        Log.Info("║  F3  - Toggle pause state                          ║");
        Log.Info("║  F4  - Toggle manual override mode                 ║");
        Log.Info("╠════════════════════════════════════════════════════╣");
        Log.Info("║  [1-5] - Rate last AI action (1-5 stars)           ║");
        Log.Info("║  [0]   - Rate last action as poor                  ║");
        Log.Info("║  [9]   - View decision history                     ║");
        Log.Info("╠════════════════════════════════════════════════════╣");
        Log.Info($"║  Current: {PolicyManager.Instance.GetStatusString(),-35}║");
        Log.Info("╚════════════════════════════════════════════════════╝");
    }
}
