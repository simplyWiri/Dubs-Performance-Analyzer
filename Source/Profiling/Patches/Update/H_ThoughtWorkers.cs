using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Analyzer.Profiling
{
    [Entry("entry.update.thoughtworker", Category.Update)]
    public class H_ThoughtWorkers
    {
        public static IEnumerable<PatchWrapper> GetPatchMethods()
        {
            foreach (var method in typeof(ThoughtWorker).AllSubnBaseImplsOf((t) => AccessTools.Method(t, "CurrentStateInternal")))
                yield return method;

            foreach (var method in typeof(ThoughtWorker).AllSubnBaseImplsOf((t) => AccessTools.Method(t, "CurrentSocialStateInternal")))
                yield return method;
        }

    }
}
