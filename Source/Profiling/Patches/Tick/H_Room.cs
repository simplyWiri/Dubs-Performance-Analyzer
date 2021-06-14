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
        public static IEnumerable<PatchWrapper> GetPatchMethods()
        {
            foreach (var m in Utility.GetTypeMethods(typeof(RegionAndRoomUpdater)))
                yield return m;

            var labeller = AccessTools.Method(typeof(H_Room), nameof(H_Room.GetLabel_RSW));

            foreach (var m in typeof(RoomStatWorker).AllSubnBaseImplsOf((t) => AccessTools.Method(t, "GetScore")))
                yield return new MethodPatchWrapper(m, null, labeller);
        }

        public static string GetLabel_RSW(RoomStatWorker __instance) => $"{__instance.def.defName} - {__instance.def.workerClass.FullName}";
    }
}