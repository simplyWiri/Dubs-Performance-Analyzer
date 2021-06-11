using RimWorld.QuestGen;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace Analyzer.Profiling
{
    public static class ProfileController
    {
        private static float deltaTime = 0.0f;
        public static float updateFrequency => 1 / Settings.updatesPerSecond; // how many ms per update (capped at every 0.05ms)

        public static IEnumerable<Profiler> GetProfiles()
        {
            if (GUIController.CurrentEntry == null) yield break;

            foreach (var p in ProfilerRegistry.entryToLogs[GUIController.CurrentEntry.type].Select(index => ProfilerRegistry.profilers[index]).Where(p => p != null))
            {
                yield return p;
            }
        }

        public static void ClearProfiles()
        {
            //ProfilerRegistry.Clear();
        }

        public static Profiler GetProfiler(string key)
        {
            return ProfilerRegistry.profilers[ProfilerRegistry.keyToWrapper[key].uid];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Profiler Start(string key, Func<string> GetLabel = null, Type type = null, MethodBase meth = null)
        {
            //if (!Analyzer.CurrentlyProfiling) return null;

            //if (Profiles.TryGetValue(key, out var prof)) return prof.Start();
            //else
            //{
            //    Profiles[key] = GetLabel != null ? new Profiler(key, GetLabel(), type, meth)
            //                                     : new Profiler(key, key, type, meth);

            //    return Profiles[key].Start();
            //}
            return null;
        }

        public static void Stop(string key)
        {
            //if (Profiles.TryGetValue(key, out Profiler prof))
            //    prof.Stop();
        }

        public static void EndUpdate()
        {
            if (Analyzer.CurrentlyPaused) return;

            Analyzer.UpdateCycle(); // Update all our profilers, record measurements

            deltaTime += Time.deltaTime;
            if (deltaTime >= updateFrequency)
            {
                Analyzer.FinishUpdateCycle(); // Process the information for all our profilers.
                deltaTime -= updateFrequency;
            }
        }
    }
}
