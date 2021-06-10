using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Analyzer.Profiling
{
    [Entry("thought workers", Category.Update)]
    public class H_ThoughtWorkers
    {
        public static bool Active = false;

        [Setting("By Pawn")]
        public static bool ByPawn = false;

        public static IEnumerable<MethodInfo> GetPatchMethods()
        {
            foreach (var type in typeof(ThoughtWorker).AllSubclasses())
            {
                var method = AccessTools.Method(type, "CurrentStateInternal");
                if(method.DeclaringType == type) yield return method;

                method = AccessTools.Method(type, "CurrentSocialStateInternal");
                if(method.DeclaringType == type) yield return method;
            }
        }

        public static string GetName(ThoughtWorker __instance, Pawn p)
        {
            return ByPawn
                ? $"{p.Name.ToStringShort} - {__instance.GetType().Name}"
                : __instance.GetType().Name;
        }
    }
}
