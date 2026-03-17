using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Commands;
using STS2AIBot.Communication;
using STS2AIBot.UI;
using STS2AIBot.AI;
using System.Reflection;

namespace STS2AIBot;

[ModInitializer(nameof(Init))]
public static class ModEntry
{
    public static PipeServer? PipeServer { get; private set; }
    public static GameEnvironment? GameEnv { get; private set; }

    public static void Init()
    {
        var harmony = new Harmony("sts2aibot");
        harmony.PatchAll();
        
        string version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        Log.Info("[STS2AIBot] STS2 AI Bot v" + version + " initialized");

        // Initialize training environment
        GameEnv = new GameEnvironment();
        PipeServer = new PipeServer();

        // Connect callbacks
        PipeServer.OnResetRequested += () => GameEnv.Initialize();
        PipeServer.OnStepRequested += (arg) => {
            if (int.TryParse(arg, out int action))
                GameEnv.Step(action);
            return GameEnv.GetState();
        };
        PipeServer.OnCloseRequested += () => Log.Info("[PipeServer] Close requested");

        PipeServer.GetStateCallback = GameEnv.GetState;
        PipeServer.GetActionMaskCallback = GameEnv.GetActionMask;

        // Start pipe server
        PipeServer.Start();

        Log.Info("[STS2AIBot] Training environment started");
        Log.Info("[STS2AIBot] Waiting for Python trainer connection...");

        // Register combat hooks
        CombatHook.Register();

        // Register AI card selector for smart card choices (True Grit, Burning Pact, etc.)
        CardSelectCmd.UseSelector(new AICardSelector());
        Log.Info("[STS2AIBot] AICardSelector registered");

        // Register AIDebugger using Harmony patch for delayed initialization
        AIDebuggerRegistrar.Register();
    }
}

/// <summary>
/// Registers UI components (AIDebugger + AIControlPanel) to scene tree.
/// Uses Harmony Postfix to inject after SceneTree is ready.
/// </summary>
public static class AIDebuggerRegistrar
{
    private static AIDebugger? _debugger;
    private static AIControlPanel? _controlPanel;
    private static bool _registered = false;

    public static void Register()
    {
        if (_registered) return;
        _registered = true;

        // Try immediate registration
        TryRegisterUI();

        // Also hook into combat start as backup
        MegaCrit.Sts2.Core.Combat.CombatManager.Instance.TurnStarted += OnTurnStarted;
    }

    private static void OnTurnStarted(MegaCrit.Sts2.Core.Combat.CombatState state)
    {
        if (_debugger == null || _controlPanel == null)
        {
            TryRegisterUI();
        }
    }

    private static void TryRegisterUI()
    {
        try
        {
            var sceneTree = Godot.Engine.GetMainLoop() as Godot.SceneTree;
            if (sceneTree?.Root != null)
            {
                // Register debugger for hotkey handling
                if (_debugger == null)
                {
                    _debugger = new AIDebugger();
                    sceneTree.Root.AddChild(_debugger);
                    Log.Info("[STS2AIBot] AIDebugger registered to scene tree");
                }

                // Register control panel for in-game UI
                if (_controlPanel == null)
                {
                    _controlPanel = new AIControlPanel();
                    sceneTree.Root.AddChild(_controlPanel);
                    Log.Info("[STS2AIBot] AIControlPanel registered (Alt+G to toggle)");
                }
            }
            else
            {
                Log.Info("[STS2AIBot] Scene tree not ready yet, will retry on combat start");
            }
        }
        catch (System.Exception ex)
        {
            Log.Info("[STS2AIBot] Failed to register UI: " + ex.Message);
        }
    }

    public static AIDebugger? Debugger => _debugger;
    public static AIControlPanel? ControlPanel => _controlPanel;
}
