using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Logging;
using STS2AIBot.Communication;
using STS2AIBot.UI;
using System.Reflection;
using Godot;

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
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        Log.Info($"[STS2AIBot] STS2 AI Bot v{version} initialized");

        // Initialize training environment
        GameEnv = new GameEnvironment();
        PipeServer = new PipeServer();

        // Connect callbacks
        PipeServer.OnResetRequested += (payload) => GameEnv.Initialize();
        PipeServer.OnStepRequested += GameEnv.Step;
        PipeServer.OnCloseRequested += () => Log.Info("[PipeServer] Close requested");

        PipeServer.GetStateCallback = GameEnv!.GetState;
        PipeServer.GetActionMaskCallback = GameEnv!.GetActionMask;

        // Start pipe server
        PipeServer.Start();

        Log.Info("[STS2AIBot] Training environment started");
        Log.Info("[STS2AIBot] Waiting for Python trainer connection...");

        // Register combat hooks
        CombatHook.Register();

        // Register AIDebugger node to scene tree for keyboard input
        RegisterAIDebugger();
    }

    private static void RegisterAIDebugger()
    {
        // Create and add AIDebugger to the scene tree
        // This is needed for _UnhandledKeyInput to work
        var debugger = new AIDebugger();
        
        // Use SceneTree autoload or add to root
        var sceneTree = Engine.GetMainLoop() as SceneTree;
        if (sceneTree?.Root != null)
        {
            sceneTree.Root.AddChild(debugger);
            Log.Info("[STS2AIBot] AIDebugger registered to scene tree");
        }
        else
        {
            Log.Info("[STS2AIBot] Warning: Could not register AIDebugger - scene tree not available");
        }
    }
}