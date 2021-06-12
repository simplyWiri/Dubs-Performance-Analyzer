using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;

namespace Analyzer.Profiling
{
    [Entry("entry.update.infocard", Category.Update)]
    internal class H_InfoCard
    {
        public static IEnumerable<PatchWrapper> GetPatchMethods()
        {
            yield return AccessTools.Method(typeof(Dialog_InfoCard), nameof(Dialog_InfoCard.DoWindowContents));
            yield return AccessTools.Method(typeof(Dialog_InfoCard), nameof(Dialog_InfoCard.FillCard));
            yield return AccessTools.Method(typeof(Dialog_InfoCard), nameof(Dialog_InfoCard.DefsToHyperlinks), new[] { typeof(IEnumerable<ThingDef>) });
            yield return AccessTools.Method(typeof(Dialog_InfoCard), nameof(Dialog_InfoCard.DefsToHyperlinks), new[] { typeof(IEnumerable<DefHyperlink>) });
            yield return AccessTools.Method(typeof(Dialog_InfoCard), nameof(Dialog_InfoCard.TitleDefsToHyperlinks));
            yield return AccessTools.Method(typeof(ThingDef), nameof(ThingDef.SpecialDisplayStats));

            yield return AccessTools.Method(typeof(StatsReportUtility), nameof(StatsReportUtility.StatsToDraw), new[] { typeof(Def), typeof(ThingDef) });
            yield return AccessTools.Method(typeof(StatsReportUtility), nameof(StatsReportUtility.StatsToDraw), new[] { typeof(RoyalTitleDef), typeof(Faction) });
            yield return AccessTools.Method(typeof(StatsReportUtility), nameof(StatsReportUtility.StatsToDraw), new[] { typeof(Faction) });
            yield return AccessTools.Method(typeof(StatsReportUtility), nameof(StatsReportUtility.StatsToDraw), new[] { typeof(AbilityDef) });
            yield return AccessTools.Method(typeof(StatsReportUtility), nameof(StatsReportUtility.StatsToDraw), new[] { typeof(Thing) });
            yield return AccessTools.Method(typeof(StatsReportUtility), nameof(StatsReportUtility.StatsToDraw), new[] { typeof(WorldObject) });

            yield return AccessTools.Method(typeof(StatsReportUtility), nameof(StatsReportUtility.DrawStatsWorker));
            yield return AccessTools.Method(typeof(StatsReportUtility), nameof(StatsReportUtility.FinalizeCachedDrawEntries));
        }
    }
}