using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Verse;
using Verse.AI;

namespace Analyzer.Profiling
{
    [Entry("entry.tick.jobgiver", Category.Tick)]
    public static class H_JobGivers
    {
        [Setting("By Pawn")]
        public static bool ByPawn = false;

        public static IEnumerable<PatchWrapper> GetPatchMethods() => typeof(ThinkNode_JobGiver).AllSubnBaseImplsOf((t) => AccessTools.Method(t, "TryGiveJob")).Select(m => (PatchWrapper)m);

        public static string GetKeyName(ThinkNode_JobGiver __instance, Pawn pawn)
        {
            var tName = __instance.GetType().Name;
            if (ByPawn && pawn != null) return $"{pawn.KindLabel} - {tName}";
            return tName;
        }
    }
}