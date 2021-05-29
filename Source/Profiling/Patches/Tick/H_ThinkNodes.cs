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
        public static bool Active = false;
        public static List<MethodInfo> patched = new List<MethodInfo>();

        public static IEnumerable<MethodInfo> GetPatchMethods()
        {
            foreach (var typ in GenTypes.AllTypes.Where(t => !t.IsAbstract))
            {
                MethodInfo method = null;

                if (typeof(ThinkNode_JobGiver).IsAssignableFrom(typ))
                {
                    method = AccessTools.Method(typ, nameof(ThinkNode_JobGiver.TryGiveJob));
                }
                else if (typeof(ThinkNode_Tagger).IsAssignableFrom(typ))
                {
                    method = AccessTools.Method(typ, nameof(ThinkNode_Tagger.TryIssueJobPackage));
                }
                else if (typeof(ThinkNode).IsAssignableFrom(typ))
                {
                    method = AccessTools.Method(typ, nameof(ThinkNode.TryIssueJobPackage));
                }

                if (method != null && method.DeclaringType == typ && !patched.Contains(method))
                {
                    yield return method;
                    patched.Add(method);
                }
            }
        }
    }
}