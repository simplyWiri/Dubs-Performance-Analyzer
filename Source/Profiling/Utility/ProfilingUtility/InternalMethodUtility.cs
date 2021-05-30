using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading;
using Verse;

namespace Analyzer.Profiling
{
    public static class InternalMethodUtility
    {
        public static HarmonyMethod InternalProfiler = new HarmonyMethod(typeof(InternalMethodUtility), nameof(InternalMethodUtility.Transpiler));

        public static HashSet<MethodInfo> PatchedInternals = new HashSet<MethodInfo>();

        public static void ClearCaches()
        {
            PatchedInternals.Clear();

#if DEBUG
            ThreadSafeLogger.Message("[Analyzer] Cleaned up the internal method caches");
#endif
        }


        // We do *NOT* check for methods that are empty, or pure virtual, as they can still be dispatched and timed
        // however we need do need to prevent them being internal patched.
        public static bool ValidCallInstruction(List<CodeInstruction> instructions, int i, out MethodBase methodBase, out string key)
        {
            methodBase = null;
            key = null;

            var instruction = instructions[i];
            // needs to be Call || CallVirt
            if ( !(instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt)) return false;
            // can not be constrained
            if (i > 0 && instructions[i - 1].opcode == OpCodes.Constrained) return false;

            methodBase = instruction.operand as MethodBase;
            key = Utility.GetMethodKey(methodBase);

            // Make sure it is not an analyzer profiling method
            return !methodBase.DeclaringType.FullName.Contains("Analyzer.Profiling");
        }

        private static IEnumerable<CodeInstruction> Transpiler(MethodBase __originalMethod, IEnumerable<CodeInstruction> codeInstructions)
        {
            try
            {
                var instructions = codeInstructions.ToList();

                for (int i = 0; i < instructions.Count; i++)
                {
                    if (ValidCallInstruction(instructions, i, out var meth, out var key) == false) continue;
                    
                    var index = MethodInfoCache.AddMethod(key, meth);

                    var inst = MethodTransplanting.ReplaceMethodInstruction(
                        instructions[i],
                        key,
                        GUIController.types[__originalMethod.DeclaringType + ":" + __originalMethod.Name + "-int"],
                        index);

                    instructions[i] = inst;
                }

                return instructions;
            }
            catch (Exception e)
            {

#if DEBUG
                ThreadSafeLogger.Error($"Failed to patch the internal method {__originalMethod.DeclaringType.FullName}:{__originalMethod.Name}, failed with the error {e.Message} at \n{e.StackTrace}");
#else
                if(Settings.verboseLogging)
                    ThreadSafeLogger.Warning($"Failed to patch the internal method {__originalMethod.DeclaringType.FullName}:{__originalMethod.Name}, failed with the error " + e.Message);
#endif

                return codeInstructions;
            }
        }
    }
}
