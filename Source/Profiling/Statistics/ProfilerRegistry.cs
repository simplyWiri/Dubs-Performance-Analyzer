using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;

namespace Analyzer.Profiling
{
    public static class ProfilerRegistry
    {
        private static readonly int ARRAY_EXPAND_SIZE = 0x80; // 128

        private static object manipulationLock = new object();
        private static int currentIndex = 0;

        internal static bool[] activePatches = new bool[ARRAY_EXPAND_SIZE];
        internal static MethodBase[] methodBases = new MethodBase[ARRAY_EXPAND_SIZE];
        internal static Profiler[] profilers = new Profiler[ARRAY_EXPAND_SIZE];
        internal static ConcurrentDictionary<string, MethodPatchWrapper> keyToWrapper = new ConcurrentDictionary<string, MethodPatchWrapper>();
        internal static ConcurrentDictionary<Type, HashSet<int>> entryToLogs = new ConcurrentDictionary<Type, HashSet<int>>();

        internal static Dictionary<string, int> nameToProfiler = new Dictionary<string, int>();

        private static readonly FieldInfo Array_MethodBase = AccessTools.Field(typeof(ProfilerRegistry), nameof(methodBases));
        private static readonly FieldInfo Array_Bool = AccessTools.Field(typeof(ProfilerRegistry), nameof(activePatches));

        // Thread safe
        public static void RegisterPatch(string key, MethodPatchWrapper wrapper)
        {
            int index = RetrieveNextId();

            wrapper.SetUID(index);

            while (keyToWrapper.TryAdd(key, wrapper) == false)
            {
                ThreadSafeLogger.Message($"Failed to add {key}-{index} to profiler registry");
            }

            SetInformationFor(index, true, null, wrapper);
        }

        public static void RegisterProfiler(string name, string baseMethodName, Profiler p)
        {
            var id = RetrieveNextId();
            var baseWrapper = keyToWrapper[baseMethodName];

            nameToProfiler.Add(name, id);

            SetInformationFor(id, true, p, baseWrapper);
        }

        internal static void SetInformationFor(int id, bool active, Profiler p, MethodPatchWrapper wrapper)
        {
            activePatches[id] = active;
            profilers[id] = p;
            methodBases[id] = wrapper.target;

            foreach(var entry in wrapper.entries)
                entryToLogs[entry].Add(id);
        }

        public static int RetrieveNextId()
        {
            int index;
            lock (manipulationLock)
            {
                currentIndex += 1;
                index = currentIndex;

                if ((index & 0x7f) == 127)
                {
                    UpdateInternalArrays();
                }
            }

            return index;
        }

        public static Profiler GetProfiler(string key)
        {
            return keyToWrapper.TryGetValue(key, out var value) 
                ? profilers[value.uid] 
                : nameToProfiler.TryGetValue(key, out var prof) 
                    ? profilers[prof] 
                    : null;
        }

        public static IEnumerable<CodeInstruction> GetIL(int index, bool activeCheck = false)
        {
            var field = activeCheck ? Array_Bool : Array_MethodBase;
            var opc = activeCheck ? OpCodes.Ldelem : OpCodes.Ldelem_Ref;
            var type = activeCheck ? typeof(bool) : typeof(MethodBase);

            return new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldsfld, field),
                new CodeInstruction(OpCodes.Ldc_I4, index),
                new CodeInstruction(opc, type)
            };
        }

        private static void UpdateInternalArrays()
        {
            if (Settings.verboseLogging) 
                ThreadSafeLogger.Message("Updating internal arrays for patch registry");

            UpdateArray(activePatches);
            UpdateArray(methodBases);
            UpdateArray(profilers);
        }

        // Based on https://stackoverflow.com/a/30769838
        private static void UpdateArray<T>(T[] basis) 
        {
            while (true)
            {
                var objBefore = Volatile.Read(ref basis);
                var clone = new T[basis.Length + ARRAY_EXPAND_SIZE];
                Array.Copy(basis, clone, basis.Length);

                if (Interlocked.CompareExchange(ref basis, clone, objBefore) == objBefore)
                    return;
            }
        }

        public static void Clear()
        {
            Parallel.For(0, profilers.Length, (p) =>
            {
                profilers[p]?.Clear();
            });
        }

        internal static void UpdateLogsForEntry(Type entry, bool value)
        {
            lock (manipulationLock)
            {
                foreach (var i in entryToLogs[entry])
                {
                    activePatches[i] = value;
                }
            }
        }

        public static void DisableProfilers()
        {
            lock (manipulationLock)
            {
                Parallel.For(0, activePatches.Length - 1, (i) =>
                {
                    activePatches[i] = false;
                });
            }
        }
    }
}
