﻿using System;
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
    // Conceptually, we have a list of profilers `profilers`, and associated metadata `activePatches` & `methodBases`. These arrays remain 1-1 in correspondence.
    // We then need a few slices into this list of profilers. 
    // 1. Entry -> Patches | We need to know which patches belong to which entry (`entryToLogs`)
    // 2. Name -> Profile | We need to know for dynamically named entries, what profiler belongs to them (`nameToProfiler`)
    // 3. Name -> Wrapper | We need to know what base wrapper a dynamically named entry belongs to, in order to find the `activePatches` index (`keyToWrapper`)

    public static class ProfilerRegistry
    {
        private static readonly int ARRAY_EXPAND_SIZE = 0x80; // 128

        private static object manipulationLock = new object();
        private static int currentIndex = 0;

        internal static bool[] activePatches = new bool[ARRAY_EXPAND_SIZE];
        internal static MethodBase[] methodBases = new MethodBase[ARRAY_EXPAND_SIZE];
        internal static Profiler[] profilers = new Profiler[ARRAY_EXPAND_SIZE];
        // todo replace with flat array
        internal static ConcurrentDictionary<int, PatchWrapper> keyToWrapper = new ConcurrentDictionary<int, PatchWrapper>();
        internal static ConcurrentDictionary<Type, HashSet<int>> entryToLogs = new ConcurrentDictionary<Type, HashSet<int>>();

        internal static Dictionary<string, int> nameToProfiler = new Dictionary<string, int>();

        private static readonly FieldInfo Array_MethodBase = AccessTools.Field(typeof(ProfilerRegistry), nameof(methodBases));
        private static readonly FieldInfo Array_Bool = AccessTools.Field(typeof(ProfilerRegistry), nameof(activePatches));

        public static PatchWrapper WrapperFromKey(int key)
        {
            return keyToWrapper.TryGetValue(key, out var pw) ? pw : null;
        }

        public static Profiler ProfilerFromLog(ProfileLog log)
        {
            return profilers[log.mKey];
        }

        public static bool AnyActiveProfilers()
        {
            return profilers.Any(p => p != null);
        }

        public static IEnumerable<Profiler> ActiveProfilersForEntry(Entry entry)
        {
            if (entry == null) return null;
            
            List<int> idxs;

            var hs = entryToLogs[entry.type];
            lock (hs)
            {
                idxs = hs.ToList();
            }

            return idxs
                .Select(index => profilers[index])
                .Where(p => p != null)
                .ToList();
        }

        public static IEnumerable<Profiler> ActiveProfilers()
        {
            return profilers
                    .Where(p => p != null)
                    .ToList();
        }

        // Thread safe
        public static void RegisterPatch(string key, PatchWrapper wrapper)
        {
            switch (wrapper)
            {
                case MethodPatchWrapper mpw:
                {
                    if (mpw.uid == -1)
                    {
                        int index = RetrieveNextId();

                        mpw.SetUID(index);
                    }
                    SetInformationFor(mpw.uid, true, null, wrapper.target, wrapper);
                    break;
                }
                case MultiMethodPatchWrapper mmpw:
                {
                    for (var i = 0; i < mmpw.targets.Count; i++)
                    {
                        var index = RetrieveNextId();
                        mmpw.SetUID(i, index);

                        keyToWrapper.TryAdd(index, wrapper);
                        SetInformationFor(index, true, null, mmpw.targets[i], wrapper);
                    }

                    break;
                }
                case TranspiledInMethodPatchWrapper timpw:
                {
                    var bIdx = RetrieveNextId();
                    timpw.baseuid = bIdx;
                    SetInformationFor(bIdx, true, null, wrapper.target, wrapper);

                    for (var i = 0; i < timpw.changeSet.Count; i++)
                    {
                        var index = RetrieveNextId();
                        timpw.SetUID(i, index);

                        keyToWrapper.TryAdd(index, wrapper);
                        SetInformationFor(index, true, null, timpw.changeSet[i].value.operand as MethodInfo, wrapper);
                    }

                    break;
                }
            }

            keyToWrapper.TryAdd(wrapper.GetUIDFor(key), wrapper);
        }

        public static void RegisterProfiler(string name, int baseMethodKey, Profiler p)
        {
            if (nameToProfiler.TryGetValue(name, out var cachedIdx) == false)
            {
                cachedIdx = RetrieveNextId();
                nameToProfiler.Add(name, cachedIdx);
            }

            var baseWrapper = keyToWrapper[baseMethodKey];
            SetInformationFor(cachedIdx, true, p, baseWrapper.target, baseWrapper);
        }

        internal static void SetInformationFor(int id, bool active, Profiler p, MethodInfo target, PatchWrapper wrapper)
        {
            activePatches[id] = active;
            if(p != null)
                profilers[id] = p;
            methodBases[id] = target;

            if(p != null) p.mKey = id;

            foreach (var entry in wrapper.entries)
            {
                var hs = entryToLogs[entry];
                lock (hs)
                {
                    hs.Add(id);
                }
            }
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
                foreach (var pair in entryToLogs)
                {
                    pair.Value.Clear();
                }
                nameToProfiler.Clear();
            }
        }

        internal static void UpdateLogsForEntry(Type entry, bool value)
        {
            lock (manipulationLock)
            {
                var hs = entryToLogs[entry];
                lock (hs)
                {
                    foreach (var i in hs)
                    {
                        activePatches[i] = value;
                    }
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