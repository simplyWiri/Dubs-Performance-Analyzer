using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace Analyzer.Profiling
{
    [Entry("entry.update.gamecomponent", Category.Update)]
    public static class H_GameComponentUpdate
    {
        public static IEnumerable<MethodPatchWrapper> GetPatchMethods() => typeof(GameComponent).AllSubnBaseImplsOf(t => AccessTools.Method(t, "GameComponentUpdate")).Select(m => new MethodPatchWrapper(m));
        public static string GetLabel(GameComponent __instance) => __instance.GetType().Name;
    }

}