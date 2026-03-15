using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Logging;

namespace STS2AIBot;

[ModInitializer(nameof(Init))]
public static class ModEntry
{
    public static void Init()
    {
        var harmony = new Harmony("sts2aibot");
        harmony.PatchAll();
        Log.Info("[STS2AIBot] STS2 AI Bot initialized");
        CombatHook.Register();
    }
}
