using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;


namespace Analyzer.Profiling
{
    [Entry("entry.tick.needs", Category.Tick)]
    internal static class H_NeedsTrackerTick
    {
        [Setting("By pawn")]
        public static bool ByPawn = false;

        public static IEnumerable<PatchWrapper> GetPatchMethods() => typeof(Need).AllSubnBaseImplsOf((t) => AccessTools.Method(t, "NeedInterval")).Select(m => (PatchWrapper)m);

        public static string GetKeyName(Need __instance)
        {
            if (ByPawn)
            {
                return __instance.GetType().Name + " " + __instance.pawn.Name;
            }
            else
            {
                return __instance.GetType().Name;
            }
        }
    }
}