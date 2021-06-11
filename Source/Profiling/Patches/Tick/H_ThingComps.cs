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
    [Entry("entry.tick.thingcomp", Category.Tick)]
    internal static class H_ThingComps
    {
        public static IEnumerable<MethodPatchWrapper> GetPatchMethods() => typeof(ThingComp).AllSubnBaseImplsOf((t) => AccessTools.Method(t, "CompTick")).Select(m => (MethodPatchWrapper)m);
    }
}
