using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Logging;
using STS2AIBot.Communication;
using STS2AIBot.UI;
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
        PipeServer.OnResetRequested += (payload) => GameEnv.Initialize();
        PipeServer.OnStepRequested += GameEnv.Step;
        PipeServer.OnCloseRequested += () => Log.Info("[PipeServer] Close requested");

        PipeServer.GetStateCallback = GameEnv.GetState;
        PipeServer.GetActionMaskCallback = GameEnv.GetActionMask;

        // Start pipe server
        PipeServer.Start();

        Log.Info("[STS2AIBot] Training environment started");
        Log.Info("[STS2AIBot] Waiting for Python trainer connection...");

        // Register combat hooks
        CombatHook.Register();

        // Register AIDebugger using Harmony patch for delayed initialization
        AIDebuggerRegistrar.Register();
    }
}

/// <summary>
/// Registers AIDebugger to scene tree when combat starts.
/// Uses Harmony Postfix to inject after SceneTree is ready.
/// </summary>
public static class AIDebuggerRegistrar
{
    private static AIDebugger? _debugger;
    private static bool _registered = false;

    public static void Register()
    {
        if (_registered) return;
        _registered = true;

        // Try immediate registration
        TryRegisterDebugger();

        // Also hook into combat start as backup
        MegaCrit.Sts2.Core.Combat.CombatManager.Instance.TurnStarted += OnTurnStarted;
    }

    private static void OnTurnStarted(MegaCrit.Sts2.Core.Combat.CombatState state)
    {
        if (_debugger == null)
        {
            TryRegisterDebugger();
        }
    }

    private static void TryRegisterDebugger()
    {
        try
        {
            var sceneTree = Godot.Engine.GetMainLoop() as Godot.SceneTree;
            if (sceneTree?.Root != null)
            {
                _debugger = new AIDebugger();
                sceneTree.Root.AddChild(_debugger);
                Log.Info("[STS2AIBot] AIDebugger registered to scene tree");
            }
            else
            {
                Log.Info("[STS2AIBot] Scene tree not ready yet, will retry on combat start");
            }
        }
        catch (System.Exception ex)
        {
            Log.Info("[STS2AIBot] Failed to register AIDebugger: " + ex.Message);
        }
    }

    public static AIDebugger? Debugger => _debugger;
}