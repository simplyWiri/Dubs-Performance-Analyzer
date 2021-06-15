using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;

namespace Analyzer.Profiling
{
    [Entry("entry.update.harmonytranspilers", Category.Update)]
    public static class H_HarmonyTranspilers
    {
        public static void ProfilePatch()
        {
            var patches = Harmony.GetAllPatchedMethods().ToList();

            var filteredTranspilers = patches
                .Where(m => Harmony.GetPatchInfo(m).Transpilers.Any(p => Utility.IsNotAnalyzerPatch(p.owner)) && m is MethodInfo)
                .Select(m => m as MethodInfo)
                .ToList();

            foreach (var meth in filteredTranspilers)
            {
                try
                {
                    MethodTransplanting.ProfileInsertedMethods(meth);
                }
                catch (Exception e)
                {
#if DEBUG
                        ThreadSafeLogger.ReportException(e, $"[Analyzer] Failed to patch transpiler {Utility.GetSignature(meth, false)}");
#endif
#if NDEBUG
                    if (Settings.verboseLogging)
                        ThreadSafeLogger.ReportException(e, $"[Analyzer] Failed to patch transpiler {Utility.GetSignature(meth, false)}");
#endif
                }
            }
        }
    }
}




