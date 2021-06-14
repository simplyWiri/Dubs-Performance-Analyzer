using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using Verse.AI;

namespace Analyzer.Profiling
{
    [Entry("entry.tick.thinknodes", Category.Tick)]
    internal static class H_ThinkNodes
    {
        public static IEnumerable<PatchWrapper> GetPatchMethods() => typeof(ThinkNode).AllSubnBaseImplsOf((t) => AccessTools.Method(t, "TryIssueJobPackage")).Select(m => (PatchWrapper)m);

        public static string GetKeyName(ThinkNode __instance) => __instance.GetType().FullName;
    }
}