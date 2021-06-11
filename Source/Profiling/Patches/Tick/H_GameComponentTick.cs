using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace Analyzer.Profiling
{
    [Entry("entry.tick.gamecomponent", Category.Tick)]
    public static class H_GameComponent
    {
        public static IEnumerable<MethodPatchWrapper> GetPatchMethods() => typeof(GameComponent).AllSubnBaseImplsOf((t) => AccessTools.Method(t, "GameComponentTick")).Select(method => (MethodPatchWrapper) method);

        public static string GetLabel(GameComponent __instance) => __instance.GetType().Name;
    }
}

