using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace Analyzer.Profiling
{
    [Entry("entry.tick.stats", Category.Tick)]
    internal class H_GetStatValue
    {
        [Setting("By Request Def")]
        public static bool ByDef = false;

        public static IEnumerable<PatchWrapper> GetPatchMethods()
        {
            var baseMethod = AccessTools.Method(typeof(StatWorker), nameof(StatWorker.GetValue), new[] {typeof(StatRequest), typeof(bool)});

            var targets = new List<MethodInfo>()
            {
                AccessTools.Method(typeof(StatWorker), nameof(StatWorker.GetValueUnfinalized)),
                AccessTools.Method(typeof(StatWorker), nameof(StatWorker.FinalizeValue))
            };

            var keyNamers = new List<MethodInfo>()
            {
                AccessTools.Method(typeof(H_GetStatValue), nameof(GetKeyName_GVU)),
                AccessTools.Method(typeof(H_GetStatValue), nameof(GetKeyName_FV))
            };

            yield return new MultiMethodPatchWrapper(baseMethod, targets, keyNamers);
            yield return new MethodPatchWrapper(AccessTools.Method(typeof(StatExtension), nameof(StatExtension.GetStatValue)), AccessTools.Method(typeof(H_GetStatValue), nameof(GetKeyName_SW)));

            foreach (var method in typeof(StatPart).AllSubnBaseImplsOf((t) => AccessTools.Method(t, nameof(StatPart.TransformValue), new Type[] {typeof(StatRequest), typeof(float).MakeByRefType()})))
                yield return method;
        }

        public static string GetKeyName_GVU(StatWorker __instance, StatRequest req) => ByDef ? $"GetValueUnfinalized ({__instance.stat.defName}) for {req.Def.defName}" : $"GetValueUnfinalized ({__instance.stat.defName})";
        public static string GetKeyName_FV(StatWorker __instance, StatRequest req) => ByDef ? $"FinalizeValue ({__instance.stat.defName}) for {req.Def.defName}" : $"FinalizeValue ({__instance.stat.defName})";
        public static string GetKeyName_SW(Thing thing, StatDef stat) => ByDef ? $"{stat.defName} for {thing.def.defName}" : stat.defName;

    }
}