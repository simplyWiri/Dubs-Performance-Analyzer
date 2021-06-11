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
        public static Dictionary<string, Profiler> profiles = new Dictionary<string, Profiler>();
        public static Profiler[] fastPathProfilers = new Profiler[128];

#if DEBUG
        private static bool midUpdate = false;
#endif
        private static float deltaTime = 0.0f;
        public static float updateFrequency => 1 / Settings.updatesPerSecond; // how many ms per update (capped at every 0.05ms)

        public static Dictionary<string, Profiler> Profiles => profiles;

        public static IEnumerable<KeyValuePair<string, Profiler>> GetProfiles()
        {
            foreach (var pair in profiles)
                yield return pair;

            foreach (var pair in fastPathProfilers.Where(p => p != null).Select(p => new KeyValuePair<string, Profiler>(p.key, p)))
                yield return pair;
        }

        public static void ClearProfiles()
        {
            profiles.Clear();
            Array.Fill(fastPathProfilers, null);
        }

        public static Profiler GetProfiler(string key)
        {
            if (Profiles.TryGetValue(key, out var prof)) return prof;
            
            var p = fastPathProfilers.First(p => p != null && p.key == key);
            return p;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Profiler Start(string key, Func<string> GetLabel = null, Type type = null, MethodBase meth = null)
        {
            if (!Analyzer.CurrentlyProfiling) return null;

            if (Profiles.TryGetValue(key, out var prof)) return prof.Start();
            else
            {
                Profiles[key] = GetLabel != null ? new Profiler(key, GetLabel(), type, meth)
                                                 : new Profiler(key, key, type, meth);

                return Profiles[key].Start();
            }
        }

        public static void Stop(string key)
        {
            if (Profiles.TryGetValue(key, out Profiler prof))
                prof.Stop();
        }

        public static void RegisterFastPath(int key)
        {
            if (key > fastPathProfilers.Length)
            {
                Array.Resize(ref fastPathProfilers, fastPathProfilers.Length + 128);
            }

            fastPathProfilers[key] = null;
        }

        // Mostly here for book keeping, optimised out of a release build.
        [Conditional("DEBUG")]
        public static void BeginUpdate()
        {
#if DEBUG
            if (Analyzer.CurrentlyPaused) return;

            if (midUpdate) ThreadSafeLogger.Error("[Analyzer] Attempting to begin new update cycle when the previous update has not ended");
            midUpdate = true;
#endif
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
#if DEBUG
            midUpdate = false;
#endif
        }
    }
}
