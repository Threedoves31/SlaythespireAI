using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Logging;
using STS2AIBot.Communication;

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
        Log.Info("[STS2AIBot] STS2 AI Bot initialized");

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
        CombatHook.Register(GameEnv);
    }
}
