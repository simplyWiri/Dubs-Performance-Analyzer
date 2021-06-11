using System.Linq;
using HarmonyLib;
using Verse;

namespace Analyzer.Profiling
{
    internal class H_DoSingleTickUpdate
    {
        public static void Postfix()
        {
            if (GUIController.CurrentCategory == Category.Tick) // If we in Tick mode, finish our update (can happen multiple times p frame)
                ProfileController.EndUpdate();
        }
    }
}