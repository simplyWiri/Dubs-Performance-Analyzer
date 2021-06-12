using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace Analyzer.Profiling
{
    [Entry("entry.update.sections", Category.Update)]
    internal class H_RegenerateLayers
    {
        public static IEnumerable<PatchWrapper> GetPatchMethods() => typeof(SectionLayer).AllSubnBaseImplsOf((t) => AccessTools.Method(t, "Regenerate")).Select(m => new MethodPatchWrapper(m));
    }
}