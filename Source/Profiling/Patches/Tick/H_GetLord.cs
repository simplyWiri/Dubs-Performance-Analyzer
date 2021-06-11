using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Analyzer.Profiling
{
    [Entry("entry.tick.lord", Category.Tick)]
    internal class H_GetLord
    {
        public static IEnumerable<MethodPatchWrapper> GetPatchMethods() => Utility.GetTypeMethods(typeof(Lord)).Select(m => (MethodPatchWrapper)m);
    }
}