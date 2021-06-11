using HarmonyLib;
using RimWorld.QuestGen;
using System;
using System.Reflection;
using Verse;

namespace Analyzer.Profiling
{
    [Entry("entry.tick.things", Category.Tick)]
    internal class H_TickListTick
    {
        public static bool Active = false;
        public static bool isPatched = false;

        [Setting("Tick Def", "Show entries by Def")]
        public static bool byDef = false;

        [Setting("Selection", "Show only things which are selected")]
        public static bool bySelection = false;

        public static void ProfilePatch()
        {
            if (isPatched) return;

            Modbase.Harmony.Patch(AccessTools.Method(typeof(TickList), nameof(TickList.Tick)), prefix: new HarmonyMethod(typeof(H_TickListTick), "Prefix"));
            isPatched = true;
        }

        public static void LogMe(Thing sam, Action ac, string fix)
        {
            bool logme = false;
            if (bySelection)
            {
                if (Find.Selector.selected.Any(x => x == sam))
                {
                    logme = true;
                }
            }
            else
            {
                logme = true;
            }

            if (logme)
            {
                string key = sam.GetType().Name;

                if (byDef)
                {
                    key = sam.def.defName;
                }

                if (bySelection)
                {
                    key = sam.ThingID;
                }

                string Namer()
                {
                    if (byDef)
                    {
                        return $"{sam.def.defName} - {sam?.def?.modContentPack?.Name} - {fix} ";
                    }
                    if (bySelection)
                    {
                        return $"{sam.def.defName} - {sam.GetHashCode()} - {sam?.def?.modContentPack?.Name} - {fix}";
                    }

                    return $"{sam.GetType()} {fix}";
                }

                Profiler prof = ProfileController.Start(key, Namer, sam.GetType(), ac.GetMethodInfo());
                ac();
                prof.Stop();
            }
            else
            {
                ac();
            }
        }

        private static bool Prefix(TickList __instance)
        {
            if (!Active)
            {
                return true;
            }

            for (int i = 0; i < __instance.thingsToRegister.Count; i++)
            {
                __instance.BucketOf(__instance.thingsToRegister[i]).Add(__instance.thingsToRegister[i]);
            }

            __instance.thingsToRegister.Clear();
            for (int j = 0; j < __instance.thingsToDeregister.Count; j++)
            {
                __instance.BucketOf(__instance.thingsToDeregister[j]).Remove(__instance.thingsToDeregister[j]);
            }

            __instance.thingsToDeregister.Clear();
            if (DebugSettings.fastEcology)
            {
                Find.World.tileTemperatures.ClearCaches();
                for (int k = 0; k < __instance.thingLists.Count; k++)
                {
                    System.Collections.Generic.List<Thing> list = __instance.thingLists[k];
                    for (int l = 0; l < list.Count; l++)
                    {
                        if (list[l].def.category == ThingCategory.Plant)
                        {
                            list[l].TickLong();
                        }
                    }
                }
            }

            System.Collections.Generic.List<Thing> list2 = __instance.thingLists[Find.TickManager.TicksGame % __instance.TickInterval];
            for (int m = 0; m < list2.Count; m++)
            {
                Thing sam = list2[m];
                if (!sam.Destroyed)
                {
                    try
                    {
                        TickerType tickerType = __instance.tickType;
                        if (tickerType != TickerType.Normal)
                        {
                            if (tickerType != TickerType.Rare)
                            {
                                if (tickerType == TickerType.Long)
                                {
                                    LogMe(sam, sam.TickLong, "TickLong");
                                }
                            }
                            else
                            {
                                LogMe(sam, sam.TickRare, "TickRare");
                            }
                        }
                        else
                        {
                            LogMe(sam, sam.Tick, "Tick");
                        }
                    }
                    catch (Exception ex)
                    {
                        string text = !list2[m].Spawned ? string.Empty : " (at " + list2[m].Position + ")";
                        if (Prefs.DevMode)
                        {
                            Log.Error(string.Concat("Exception ticking ", list2[m].ToStringSafe(), text, ": ", ex));
                        }
                        else
                        {
                            Log.ErrorOnce(
                                string.Concat("Exception ticking ", list2[m].ToStringSafe(), text,
                                    ". Suppressing further errors. Exception: ", ex),
                                list2[m].thingIDNumber ^ 576876901);
                        }
                    }
                }
            }

            return false;
        }
    }



}