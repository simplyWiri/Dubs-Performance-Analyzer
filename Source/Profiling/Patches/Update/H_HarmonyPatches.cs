using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Analyzer.Profiling
{
    [Entry("entry.update.harmonypatches", Category.Update)]
    internal class H_HarmonyPatches
    {
        public static bool Active = false;

        public static IEnumerable<MethodPatchWrapper> GetPatchMethods()
        {
            foreach (var mode in Harmony.GetAllPatchedMethods().ToList())
            {
                var patchInfo = Harmony.GetPatchInfo(mode);
                foreach (var fix in patchInfo.Prefixes.Concat(patchInfo.Postfixes).Where(f => Utility.IsNotAnalyzerPatch(f.owner)))
                {
                    yield return fix.PatchMethod;
                }  
            }
        }
    }
}