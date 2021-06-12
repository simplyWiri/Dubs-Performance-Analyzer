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
        internal static ConcurrentDictionary<string, PatchWrapper> keyToWrapper = new ConcurrentDictionary<string, PatchWrapper>();
        internal static ConcurrentDictionary<Type, HashSet<int>> entryToLogs = new ConcurrentDictionary<Type, HashSet<int>>();

        internal static Dictionary<string, int> nameToProfiler = new Dictionary<string, int>();

        private static readonly FieldInfo Array_MethodBase = AccessTools.Field(typeof(ProfilerRegistry), nameof(methodBases));
        private static readonly FieldInfo Array_Bool = AccessTools.Field(typeof(ProfilerRegistry), nameof(activePatches));

        // Thread safe
        public static void RegisterPatch(string key, PatchWrapper wrapper)
        {
            if (wrapper is MethodPatchWrapper mpw)
            {
                if (mpw.uid == -1)
                {
                    int index = RetrieveNextId();

                    mpw.SetUID(index);
                }
                SetInformationFor(mpw.uid, true, null, wrapper);

            } else if (wrapper is MultiMethodPatchWrapper mmpw)
            {
                for (int i = 0; i < mmpw.targets.Count; i++)
                {
                    var index = RetrieveNextId();
                    mmpw.SetUID(i, index);

                    SetInformationFor(index, true, null, wrapper);

                    var subKey = Utility.GetSignature(mmpw.targets[i]);

                    while (keyToWrapper.TryAdd(subKey, wrapper) == false)
                    {
                        ThreadSafeLogger.Message($"Failed to add {key} to profiler registry");
                    }
                }
            }

            while (keyToWrapper.TryAdd(key, wrapper) == false)
            {
                ThreadSafeLogger.Message($"Failed to add {key} to profiler registry");
            }
        }

        public static void RegisterProfiler(string name, string baseMethodName, Profiler p)
        {
            if (nameToProfiler.TryGetValue(name, out var cachedIdx) == false)
            {
                var id = RetrieveNextId();
                nameToProfiler.Add(name, id);
            }

            var baseWrapper = keyToWrapper[baseMethodName];
            SetInformationFor(cachedIdx, true, p, baseWrapper);
        }

        internal static void SetInformationFor(int id, bool active, Profiler p, PatchWrapper wrapper)
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
                ? profilers[value.GetUIDFor(key)] 
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

            UpdateArray(ref activePatches);
            UpdateArray(ref methodBases);
            UpdateArray(ref profilers);
        }

        // Based on https://stackoverflow.com/a/30769838
        private static void UpdateArray<T>(ref T[] basis) 
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
                profilers[p] = null;
            });
        }

        public static void FlushInformation()
        {
            lock (manipulationLock)
            {
                currentIndex = 0;

                activePatches = new bool[ARRAY_EXPAND_SIZE];
                methodBases = new MethodBase[ARRAY_EXPAND_SIZE];
                profilers = new Profiler[ARRAY_EXPAND_SIZE];

                keyToWrapper.Clear();
                entryToLogs.Clear();
                nameToProfiler.Clear();
            }
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
