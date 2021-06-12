using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Reflection;

namespace Analyzer.Profiling
{
    [Entry("entry.gui.resourcereadout", Category.GUI)]
    internal class H_ResourceReadoutOnGUI
    {
        public static IEnumerable<PatchWrapper> GetPatchMethods() { yield return AccessTools.Method(typeof(ResourceReadout), nameof(ResourceReadout.ResourceReadoutOnGUI)); }
        public static string GetLabel() => "ResourceReadoutOnGUI";
    }
}
