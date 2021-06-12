using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace Analyzer.Profiling.Patches.Tick
{
    [Entry("entry.tick.jobdriver", Category.Tick)]
    class H_JobDriver
    {
        [Setting("By Pawn")]
        public static bool ByPawn = false;

        public static IEnumerable<PatchWrapper> GetPatchMethods() => typeof(JobDriver).AllSubnBaseImplsOf((t) => AccessTools.Method(t, "DriverTick")).Select(method => (PatchWrapper) method);

        public static string GetKeyName(JobDriver __instance)
        {
            var str = $"{__instance.GetType().Name}";
            return ByPawn
                ? $"{__instance.pawn.KindLabel} - {str}"
                : str;
        }
    }
}
