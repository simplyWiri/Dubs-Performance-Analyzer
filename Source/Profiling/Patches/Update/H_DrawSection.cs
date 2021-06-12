using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Analyzer.Profiling
{
    [Entry("entry.update.mapdrawer", Category.Update)]
    internal class H_DrawSection
    {
        public static IEnumerable<PatchWrapper> GetPatchMethods() => typeof(SectionLayer).AllSubnBaseImplsOf((t) => AccessTools.Method(t, "DrawLayer")).Select(impl => (PatchWrapper) impl);
    }
}