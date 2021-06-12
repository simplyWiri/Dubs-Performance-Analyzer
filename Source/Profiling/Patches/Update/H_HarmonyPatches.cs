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
        public static IEnumerable<PatchWrapper> GetPatchMethods()
        {
            foreach (var mode in Harmony.GetAllPatchedMethods().ToList())
            {
                var patchInfo = Harmony.GetPatchInfo(mode);
                foreach (var fix in patchInfo.Prefixes.Concat(patchInfo.Postfixes).Where(f => Utility.IsNotAnalyzerPatch(f.owner)))
                {
                    yield return new MethodPatchWrapper(fix.PatchMethod);
                }  
            }
        }
    }
}