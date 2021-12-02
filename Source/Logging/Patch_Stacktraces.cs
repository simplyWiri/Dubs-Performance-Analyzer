using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Analyzer.Profiling;
using HarmonyLib;
using Verse;
using StackTraceUtility = UnityEngine.StackTraceUtility;

namespace Analyzer.Logging
{ 
    internal static class Patch_Stacktraces
    {
        private static Dictionary<long, string> cachedTraces = new Dictionary<long, string>();

        public static void Patch(Harmony h)
        {
            foreach (var method in LogMethods())
                h.Patch(method, 
                    transpiler: new HarmonyMethod(typeof(Patch_Stacktraces), nameof(Patch_Stacktraces.Transpiler)));

            h.Patch(AccessTools.Method(typeof(Exception), "GetStackTrace"),
                transpiler: new HarmonyMethod(typeof(Patch_Stacktraces), nameof(Patch_Stacktraces.ImSoSorry)));
        }
        
        private static IEnumerable<MethodInfo> LogMethods()
        {
            yield return AccessTools.Method(typeof(Log), nameof(Log.Message), new Type[] { typeof(string) });
            yield return AccessTools.Method(typeof(Log), nameof(Log.Warning), new Type[] { typeof(string) });
            yield return AccessTools.Method(typeof(Log), nameof(Log.Error), new Type[] { typeof(string) });
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return instructions.MethodReplacer(
                AccessTools.Method(typeof(StackTraceUtility), nameof(StackTraceUtility.ExtractStackTrace)),
                AccessTools.Method(typeof(Patch_Stacktraces), nameof(Patch_Stacktraces.ExtractStackTrace)
                ));
        }

        private static IEnumerable<CodeInstruction> ImSoSorry(IEnumerable<CodeInstruction> insts)
        {
            return insts.MethodReplacer(
                AccessTools.Method(typeof(Environment), "GetStackTrace"),
                AccessTools.Method(typeof(Patch_Stacktraces), nameof(GetStackTrace)
                ));
        }

        private static string ExtractStackTrace()
        {
            var trace = new StackTrace(1, false);
            return GetCachedTrace(trace);
        }

        private static string GetStackTrace(Exception e, bool needFileInfo)
        {
            var trace = e == null ? new StackTrace(needFileInfo) : new StackTrace(e, needFileInfo);
            return GetCachedTrace(trace, true);
        }

        private static string GetCachedTrace(StackTrace trace, bool exception = false)
        {
            long seed = 0x9e3779b9;
            
            foreach (var m in trace.GetFrames().Select(s => s.GetMethod()))
            {
                seed ^= m.GetHashCode() + 0x9e3779b9 + (seed << 6) + (seed >> 2);
            }

            if (cachedTraces.TryGetValue(seed, out var str)) return str;
            cachedTraces[seed] = Profiling.StackTraceUtility.GetStackTraceString(trace, exception, out _);
            return cachedTraces[seed];
        }

        public static void ClearCaches()
        {
            cachedTraces.Clear();
        }
    }
}