using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace Analyzer.Profiling
{
    [Entry("entry.tick.room", Category.Tick)]
    internal class H_Room
    {
        public static IEnumerable<MethodPatchWrapper> GetPatchMethods() => typeof(RoomStatWorker).AllSubnBaseImplsOf((t) => AccessTools.Method(t, "GetScore")).Select(m => (MethodPatchWrapper)m);
        public static string GetLabel(RoomStatWorker __instance) => $"{__instance.def.defName} - {__instance.def.workerClass.FullName}";
    }
}