using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace Analyzer.Profiling
{
    [Entry("entry.gui.gamecomponent", Category.GUI)]
    public static class H_GameComponentUpdateGUI
    {
        public static IEnumerable<PatchWrapper> GetPatchMethods() => typeof(GameComponent).AllSubnBaseImplsOf((t) => AccessTools.Method(t, "GameComponentOnGUI")).Select(m => (MethodPatchWrapper)m);
        public static string GetLabel(GameComponent __instance) => __instance.GetType().Name;
    }
}
