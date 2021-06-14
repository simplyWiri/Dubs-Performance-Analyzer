using HarmonyLib;
using RimWorld;
using RimWorld.BaseGen;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.TestTools;
using Verse;

namespace Analyzer.Profiling
{
    public static class MethodTransplanting
    {
        public static ConcurrentDictionary<MethodBase, PatchWrapper> patchedMethods = new ConcurrentDictionary<MethodBase, PatchWrapper>();
        private static readonly CodeInstMethEqual methComparer = new CodeInstMethEqual();


        private static readonly HarmonyMethod generalTranspiler = new HarmonyMethod(typeof(MethodTransplanting), nameof(ProfileMethodTranspiler));
        private static readonly HarmonyMethod occurencesInTranspiler = new HarmonyMethod(typeof(MethodTransplanting), nameof(ProfileOccurencesInMethodTranspiler));
        internal static readonly HarmonyMethod internalMethodsTranspiler = null;
        internal static readonly HarmonyMethod transpiledInMethodsTranspiler = new HarmonyMethod(AccessTools.Method(typeof(MethodTransplanting), nameof(ProfileAddedMethodsTranspiler)), int.MinValue);

        
        // profiler registry
        private static readonly FieldInfo ProfilerRegistry_Profilers = AccessTools.Field(typeof(ProfilerRegistry), nameof(ProfilerRegistry.profilers));
        private static readonly FieldInfo ProfilerRegistry_CustomProfilers = AccessTools.Field(typeof(ProfilerRegistry), nameof(ProfilerRegistry.nameToProfiler));
        private static readonly MethodInfo ProfilerRegistry_RegisterProfiler = AccessTools.Method(typeof(ProfilerRegistry), nameof(ProfilerRegistry.RegisterProfiler));

        // profiler
        private static readonly MethodInfo Profiler_Start = AccessTools.Method(typeof(Profiler), nameof(Profiler.Start));
        private static readonly MethodInfo Profiler_Stop = AccessTools.Method(typeof(Profiler), nameof(Profiler.Stop));
        private static readonly ConstructorInfo ProfilerCtor = AccessTools.Constructor(typeof(Profiler), new Type[] { typeof(string), typeof(string), typeof(Type), typeof(MethodBase) });

        // dictionary
        private static readonly MethodInfo Dict_TryGetValue = AccessTools.Method(typeof(Dictionary<string, int>), "TryGetValue");

        public class CodeInstMethEqual : EqualityComparer<CodeInstruction>
        {
            // Functions primarily to check if two function call CodeInstructions are the same. 
            public override bool Equals(CodeInstruction a, CodeInstruction b)
            {
                if (a.opcode != b.opcode) return false;
                
                // because our previous check, both must be the same opcode.
                if (a.opcode == OpCodes.Callvirt || a.opcode == OpCodes.Call)
                {
                    return (a.operand as MethodBase)?.Name == (b.operand as MethodBase)?.Name;
                }

                return a.operand == b.operand;
            }

            public override int GetHashCode(CodeInstruction obj)
            {
                return obj.GetHashCode();
            }
        }

        public static void ClearCaches()
        {
            patchedMethods.Clear();
        }

        public static void PatchMethods(Type type)
        {
            // get the methods
            var meths = (IEnumerable<PatchWrapper>)type.GetMethod("GetPatchMethods", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);

            if (meths != null)
                UpdateMethods(type, meths);
        }

        public static void UpdateMethods(Type type, IEnumerable<PatchWrapper> meths)
        {
            var getKeyNameMeth = AccessTools.Method(type, "GetKeyName");
            var getLabelMeth = AccessTools.Method(type, "GetLabel");

            foreach (var meth in meths)
            {
                meth.AddEntry(type);

                if (meth is MethodPatchWrapper mpw)
                {
                    mpw.getKeyName = getKeyNameMeth;
                    mpw.getLabel = getLabelMeth;
                }

                ProfileMethod(meth);
            }
        }

        // This will profile the method `method.target`, and will (if not null) apply the custom namer & labeller
        public static void ProfileMethod(PatchWrapper method)
        { 
            if (patchedMethods.TryGetValue(method.target, out _))
            {
#if DEBUG
                ThreadSafeLogger.Warning($"[Analyzer] Already patched method {Utility.GetSignature(method.target, false)}");
#else
                if (Settings.verboseLogging)
                    ThreadSafeLogger.Warning($"[Analyzer] Already patched method {Utility.GetSignature(wrapper.target, false)}");
#endif
                return;
            }

            patchedMethods.TryAdd(method.target, method);

            Task.Factory.StartNew(delegate
            {
                try
                {
                    var transpiler = method is MultiMethodPatchWrapper ? occurencesInTranspiler : generalTranspiler;
                    Modbase.Harmony.Patch(method.target, transpiler: transpiler);
                }
                catch (Exception e)
                {
#if DEBUG
                    ThreadSafeLogger.ReportException(e, $"Failed to patch the method {Utility.GetSignature(method.target, false)}");
#else
                    if (Settings.verboseLogging)
                        ThreadSafeLogger.ReportException(e, $"Failed to patch the method {Utility.GetSignature(wrapper.target, false)}");
#endif
                }
            });
        }

        public static void ProfileInsertedMethods(MethodInfo baseMethod)
        {
            var originalInstructions = PatchProcessor.GetOriginalInstructions(baseMethod);
            var modInstList = PatchProcessor.GetCurrentInstructions(baseMethod);

            var insts = new Myers<CodeInstruction>(originalInstructions.ToArray(), modInstList.ToArray(), methComparer);
            insts.Compute();

            var changes = insts.changeSet.Where(t => t.change == ChangeType.Added)
                .Where(t => t.value.IsCallInstruction() && t.value.operand is MethodInfo m && m.DeclaringType.FullName.Contains("Analyzer") == false)
                .OrderBy(c => c.index)
                .ToList();

            var wrapper = new TranspiledInMethodPatchWrapper(baseMethod, changes);
            wrapper.AddEntry(typeof(H_HarmonyTranspilersInternalMethods));

            patchedMethods.TryAdd(baseMethod, wrapper);

            try
            {
                Modbase.Harmony.Patch(baseMethod, transpiler: transpiledInMethodsTranspiler);
            }
            catch (Exception e)
            {
                ThreadSafeLogger.ReportException(e, $"Failed to profile the methods added to {Utility.GetSignature(baseMethod)}");
            }
        }

        // **************** Transpilers

        internal static IEnumerable<CodeInstruction> ProfileOccurencesInMethodTranspiler(MethodBase __originalMethod, IEnumerable<CodeInstruction> insts, ILGenerator ilGen)
        {
            var wrapper = patchedMethods[__originalMethod] as MultiMethodPatchWrapper;
            var baseKey = Utility.GetSignature(__originalMethod);
            ProfilerRegistry.RegisterPatch(baseKey, wrapper);

            var profLocal = ilGen.DeclareLocal(typeof(Profiler));
            var keyLocal = ilGen.DeclareLocal(typeof(string));

            var instructions = insts.ToList();

            for (var i = 0; i < instructions.Count; i++)
            {
                var inst = instructions[i];
                if (inst.IsCallInstruction() is false || CallsTarget(inst, wrapper.targets, out var index) is false)
                {
                    yield return inst;
                    continue;
                }

                var uid = wrapper.uids[index];
                var key = Utility.GetSignature(wrapper.targets[index]);
                var getKeyName = wrapper.getKeyNames[index];
                var getLabel = wrapper.getLabels[index];

                foreach (var ins in InsertProfilerStartupCode(instructions, i, ilGen, wrapper.target, keyLocal, profLocal, key, uid, getKeyName, getLabel))
                    yield return ins;

                yield return inst;

                foreach (var ins in InsertProfilerEndCode(instructions, ++i, ilGen, profLocal))
                    yield return ins;
            }
        }

        // This transpiler basically replicates ProfileController.Start, but in IL, and inside the method it is patching, to reduce as much overhead as
        // possible, its quite simple, just long and hard to read.
        internal static IEnumerable<CodeInstruction> ProfileMethodTranspiler(MethodBase __originalMethod, IEnumerable<CodeInstruction> insts, ILGenerator ilGen)
        {
            var instructions = insts.ToList();
            var profLocal = ilGen.DeclareLocal(typeof(Profiler));
            var keyLocal = ilGen.DeclareLocal(typeof(string));
            var beginLabel = ilGen.DefineLabel();
            var noProfFastPathLabel = ilGen.DefineLabel();
            var noProfLabel = ilGen.DefineLabel();

            var wrapper = patchedMethods[__originalMethod] as MethodPatchWrapper;

            var key = Utility.GetSignature(__originalMethod as MethodInfo, true); // This translates our method into a human-legible key, I.e. Namespace.Type<Generic>:Method
            ProfilerRegistry.RegisterPatch(key, wrapper);

            foreach (var inst in InsertProfilerStartupCode(instructions, 0, ilGen, wrapper.target, keyLocal, profLocal, key, wrapper.uid, wrapper.getKeyName, wrapper.getLabel))
                yield return inst;

            // For each instruction which exits this function, append our finishing touches (I.e.)
            // if(profiler != null)
            // {
            //      profiler.Stop();
            // }
            // return; // any labels here are moved to the start of the `if`
            for(int i = 0; i < instructions.Count; i++)
            {
                var inst = instructions[i];
                if (inst.opcode == OpCodes.Ret)
                {
                    foreach (var ins in InsertProfilerEndCode(instructions, i, ilGen, profLocal))
                        yield return ins;
                }
                else
                {
                    yield return inst;
                }
            }
        }

        internal static IEnumerable<CodeInstruction> ProfileAddedMethodsTranspiler(MethodBase __originalMethod, IEnumerable<CodeInstruction> instructions, ILGenerator ilGen)
        {
            var origMethod = Utility.GetSignature(__originalMethod, false);
            var wrapper = patchedMethods[__originalMethod] as TranspiledInMethodPatchWrapper;
            var insts = instructions.ToList();

            var profLocal = ilGen.DeclareLocal(typeof(Profiler));
            var keyLocal = ilGen.DeclareLocal(typeof(string));

            var baseProfLocal = ilGen.DeclareLocal(typeof(Profiler));
            var baseKeyLocal = ilGen.DeclareLocal(typeof(string));

            ProfilerRegistry.RegisterPatch(origMethod, wrapper);

            foreach (var inst in InsertProfilerStartupCode(insts, 0, ilGen, wrapper.target, baseKeyLocal, baseProfLocal, origMethod, wrapper.baseuid, null, null))
                yield return inst;

            for (int i = 0, changeSetIdx = 0; i < insts.Count; i++)
            {
                if (insts[i].opcode == OpCodes.Ret)
                {
                    foreach (var ins in InsertProfilerEndCode(insts, i, ilGen, baseProfLocal))
                        yield return ins;
                }
                else if (changeSetIdx >= wrapper.changeSet.Count || wrapper.changeSet[changeSetIdx].index != i)
                {
                    yield return insts[i];
                }
                else
                {
                    var change = wrapper.changeSet[changeSetIdx];
                    var target = change.value.operand as MethodInfo;

                    var key = $"{origMethod} : {Utility.GetSignature(change.value.operand as MethodInfo, false)}";

                    foreach (var inst in InsertProfilerStartupCode(insts, change.index, ilGen, target, keyLocal, profLocal, key, wrapper.GetUIDFor(key), null, null))
                        yield return inst;

                    yield return insts[i];

                    var nextInstruction = insts[i + 1];

                    if (nextInstruction.opcode == OpCodes.Ret)
                    {
                        var endLabel = ilGen.DefineLabel();
                        var fullEndLabel = ilGen.DefineLabel();

                        // localProf?.Stop();
                        yield return new CodeInstruction(OpCodes.Ldloc, profLocal).MoveLabelsFrom(nextInstruction);
                        yield return new CodeInstruction(OpCodes.Brfalse_S, endLabel);

                        yield return new CodeInstruction(OpCodes.Ldloc, profLocal);
                        yield return new CodeInstruction(OpCodes.Call, Profiler_Stop);

                        // baseProf?.Stop();
                        yield return new CodeInstruction(OpCodes.Ldloc, baseProfLocal).WithLabels(endLabel);
                        yield return new CodeInstruction(OpCodes.Brfalse_S, fullEndLabel);

                        yield return new CodeInstruction(OpCodes.Ldloc, baseProfLocal);
                        yield return new CodeInstruction(OpCodes.Call, Profiler_Stop);

                        yield return nextInstruction.WithLabels(fullEndLabel);
                    }
                    else
                    {
                        foreach (var inst in InsertProfilerEndCode(insts, i + 1, ilGen, profLocal))
                            yield return inst;
                    }

                    i++;
                    changeSetIdx++;
                }
            }
        }


        // **************** Transpiler Utility

        public static IEnumerable<CodeInstruction> InsertProfilerStartupCode(
            List<CodeInstruction> instructions, int index, ILGenerator ilGen, MethodInfo target, LocalBuilder keyLocal, LocalBuilder profLocal,
            string key, int uid, MethodInfo getKeyName, MethodInfo getLabel)
        {
            var beginLabel = ilGen.DefineLabel();
            var noProfFastPathLabel = ilGen.DefineLabel();
            var noProfLabel = ilGen.DefineLabel();

            // Check if analyzer is active, if not branch to the start of the actual method
            var startupCode = InsertActiveCheck(uid, beginLabel).ToList();
            startupCode[0].MoveLabelsFrom(instructions[index]);

            foreach (var inst in startupCode) 
                yield return inst;
            
            // Retrieve our profiler key, we can retrieve this from a method, so its not the simplest process
            foreach (var inst in InsertProfileRetrieval(target, getKeyName, uid, keyLocal, profLocal, beginLabel, noProfLabel, noProfFastPathLabel, ilGen))
                yield return inst;


            { // If we found a profiler - Start it, and skip to the start of execution of the method
                yield return new CodeInstruction(OpCodes.Ldloc, profLocal);
                yield return new CodeInstruction(OpCodes.Call, Profiler_Start);
                yield return new CodeInstruction(OpCodes.Pop); // Profiler.Start returns itself so we pop it off the stack
                yield return new CodeInstruction(OpCodes.Br, beginLabel);
            }

            foreach (var inst in InsertProfilerCreation(target, getKeyName, getLabel, key, uid, keyLocal, profLocal, noProfLabel, noProfFastPathLabel))
                yield return inst;

            yield return new CodeInstruction(OpCodes.Call, Profiler_Start);
            yield return new CodeInstruction(OpCodes.Pop); // profiler.Start returns itself

            instructions.ElementAt(index).WithLabels(beginLabel);
        }

        // profiler?.Stop();
        public static IEnumerable<CodeInstruction> InsertProfilerEndCode(List<CodeInstruction> instructions, int index, ILGenerator ilGen, LocalBuilder profLocal)
        {
            var endLabel = ilGen.DefineLabel();
            var noFinalInst = index >= instructions.Count;
            var nextInst = instructions[index];

            // localProf?.Stop();
            yield return new CodeInstruction(OpCodes.Ldloc, profLocal).MoveLabelsFrom(nextInst);
            yield return noFinalInst ? new CodeInstruction(OpCodes.Ret) : new CodeInstruction(OpCodes.Brfalse_S, endLabel);

            yield return new CodeInstruction(OpCodes.Ldloc, profLocal);
            yield return new CodeInstruction(OpCodes.Call, Profiler_Stop);

            yield return nextInst.WithLabels(endLabel);
        }
        // Emulates Harmonys '__instance' & '___fieldName' & parameter sniping.
        private static IEnumerable<CodeInstruction> GetLoadArgsForMethodParams(MethodBase originalMethod, ParameterInfo[] methodparams)
        {
            var origParams = originalMethod.GetParameters();
            var origType = originalMethod.GetType();

            foreach (var param in methodparams)
            {
                if (param.Name == "__instance") // Trying to get the instance of the object (assumed to be non static)
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0); // Push the instance of the object on the stack (up to the user to get this right)
                }
                else if (param.Name.StartsWith("___")) // Trying to get a field from the object (static or non static)
                {
                    var fieldName = param.Name.Remove(0, 3);

                    var fieldInfo = AccessTools.Field(origType, fieldName);

                    if (fieldInfo.IsStatic)
                    {
                        yield return new CodeInstruction(OpCodes.Ldsfld, fieldInfo);
                    }
                    else
                    {
                        yield return new CodeInstruction(OpCodes.Ldarg_0); // push the instance onto the stack, then grab the field from the instance.
                       yield return new CodeInstruction(OpCodes.Ldfld, fieldInfo);
                    }
                }
                else // Trying to intercept a param from the original method
                {
                    var idx = 0;
                    for (int i = 0; i < origParams.Length; i++)
                    {
                        var p = origParams[i];

                        if (p.ParameterType == param.ParameterType)
                        {
                            if (p.Name == param.Name)
                            {
                                idx = i;
                                break;
                            }

                            idx = i;
                        }
                    }

                    yield return new CodeInstruction(OpCodes.Ldarg_S, (originalMethod.IsStatic ? 0 : 1) + idx);
                }
            }
        }

        private static bool CallsTarget(CodeInstruction instruction, List<MethodInfo> targets, out int index)
        {
            index = -1;
            for (var j = 0; j < targets.Count; j++)
            {
                if (instruction.Calls(targets[j]) == false) continue;

                index = j;
                return true;
            }

            return false;
        }

        public static IEnumerable<CodeInstruction> InsertActiveCheck(int patchUID, Label skipTo)
        {
            foreach (var inst in ProfilerRegistry.GetIL(patchUID, true))
                yield return inst;

            yield return new CodeInstruction(OpCodes.Brfalse_S, skipTo);
        }

        // Leaves the stack empty, has 3 main effects:
        // 1. profLocal is null, it will be branching to either `noProfileLabel` or `noProfileFastPath` depending on whether 
        // the wrapper.customNamer is set or not
        // 2. customNamer returns null, it will be branching to `beginLabel`
        // 3. profLocal is not null, and it will continue execution as normal
        public static IEnumerable<CodeInstruction> InsertProfileRetrieval(MethodInfo target, MethodInfo getKeyName, int methodKey, LocalBuilder keyLocal, LocalBuilder profLocal, Label beginlabel, Label noProfileLabel, Label noProfileFastPath, ILGenerator ilGen)
        {
            if (getKeyName != null)
            {
                var indexVariable = ilGen.DeclareLocal(typeof(int));

                foreach (var codeInst in GetLoadArgsForMethodParams(target, getKeyName.GetParameters()))
                    yield return codeInst;

                yield return new CodeInstruction(OpCodes.Call, getKeyName);
                yield return new CodeInstruction(OpCodes.Stloc, keyLocal);

                // if our key is null, start the method (opt out by name)
                yield return new CodeInstruction(OpCodes.Ldloc, keyLocal);
                yield return new CodeInstruction(OpCodes.Brfalse, beginlabel);

                { // if(ProfilerRegistry.nameToProfiler(key, out var id))
                    yield return new CodeInstruction(OpCodes.Ldsfld, ProfilerRegistry_CustomProfilers);
                    yield return new CodeInstruction(OpCodes.Ldloc, keyLocal);
                    yield return new CodeInstruction(OpCodes.Ldloca_S, indexVariable);
                    yield return new CodeInstruction(OpCodes.Callvirt, Dict_TryGetValue);
                    yield return new CodeInstruction(OpCodes.Brfalse, noProfileLabel);
                    
                    // profLocal = ProfilerRegistry.profilers[id]
                    yield return new CodeInstruction(OpCodes.Ldsfld, ProfilerRegistry_Profilers);
                    yield return new CodeInstruction(OpCodes.Ldloc, indexVariable);
                    yield return new CodeInstruction(OpCodes.Ldelem, typeof(Profiler));
                    yield return new CodeInstruction(OpCodes.Dup);
                    yield return new CodeInstruction(OpCodes.Stloc, profLocal);
                    yield return new CodeInstruction(OpCodes.Brfalse, noProfileLabel);
                }
            }
            else
            {
                // fast path
                yield return new CodeInstruction(OpCodes.Ldsfld, ProfilerRegistry_Profilers);
                yield return new CodeInstruction(OpCodes.Ldc_I4, methodKey);
                yield return new CodeInstruction(OpCodes.Ldelem, typeof(Profiler));
                yield return new CodeInstruction(OpCodes.Stloc, profLocal);
                yield return new CodeInstruction(OpCodes.Ldloc, profLocal);
                yield return new CodeInstruction(OpCodes.Brfalse, noProfileFastPath); // need to push the str to the stack for the label
            }
        }
        
        // Creates a profiler, and stores the value in the Profilers dictionary, or the fast path array.
        // there will be a profiler on the stack after the execution of this code.
        public static IEnumerable<CodeInstruction> InsertProfilerCreation(MethodInfo target, MethodInfo getKeyName, MethodInfo getLabel, string key, int methodKey, LocalBuilder keyLocal, LocalBuilder profLocal, Label noProfLabel, Label noProfFastPathLabel)
        {
            { // if not, we need to make one
                if (getKeyName == null)
                {
                    yield return new CodeInstruction(OpCodes.Ldstr, key).WithLabels(noProfFastPathLabel);
                    yield return new CodeInstruction(OpCodes.Stloc, keyLocal);
                }

                yield return new CodeInstruction(OpCodes.Ldloc, keyLocal).WithLabels(noProfLabel);

                { // Custom Labelling
                    if (getLabel != null)
                    {
                        foreach (var codeInst in GetLoadArgsForMethodParams(target, getLabel.GetParameters()))
                            yield return codeInst;

                        yield return new CodeInstruction(OpCodes.Call, getLabel);
                    }
                    else
                    {
                        yield return new CodeInstruction(OpCodes.Dup); // duplicate the key on the stack so the key is both the key and the label in ProfileController.Start
                    }
                } 
                
                // return null
                yield return new CodeInstruction(OpCodes.Ldnull);

                { // get our methodinfo from the metadata
                    foreach (var inst in ProfilerRegistry.GetIL(methodKey))
                        yield return inst;
                }

                yield return new CodeInstruction(OpCodes.Newobj, ProfilerCtor); // new Profiler();
                yield return new CodeInstruction(OpCodes.Dup);
                yield return new CodeInstruction(OpCodes.Stloc, profLocal);
            }

            if(getKeyName != null) // Add to the Profilers dictionary, so we cache creation.
            { 
                yield return new CodeInstruction(OpCodes.Ldloc, keyLocal);
                yield return new CodeInstruction(OpCodes.Ldstr, key);
                yield return new CodeInstruction(OpCodes.Ldloc, profLocal);
                yield return new CodeInstruction(OpCodes.Call, ProfilerRegistry_RegisterProfiler);
            }
            else
            {
                yield return new CodeInstruction(OpCodes.Ldsfld, ProfilerRegistry_Profilers);
                yield return new CodeInstruction(OpCodes.Ldc_I4, methodKey);
                yield return new CodeInstruction(OpCodes.Ldloc, profLocal);
                yield return new CodeInstruction(OpCodes.Stelem, typeof(Profiler));
            }
        }



        // Utility for internal && transpiler profiling.
        // This method takes a codeinstruction (of type Call or CallVirt), a key, a type, and a fieldinfo of a dictionary
        // and will return a new codeinstruction, which the same opcode as the instruction passed to it, and the method
        // will be a new dynamic method, which duplicates the functionality of the original method, while adding proiling to it.
        // 
        // The key is used for keying into the dictionary field you give, which will be expected to return a MethodInfo
        // this will be then used in the call to ProfileController.Start
        //public static CodeInstruction ReplaceMethodInstruction(CodeInstruction inst, string key, Type type, int index)
        //{
        //    var method = inst.operand as MethodInfo;
        //    if (method == null) return inst;

        //    Type[] parameters = null;


        //    if (method.Attributes.HasFlag(MethodAttributes.Static)) // If we have a static method, we don't need to grab the instance
        //        parameters = method.GetParameters().Select(param => param.ParameterType).ToArray();
        //    else if (method.DeclaringType.IsValueType) // if we have a struct, we need to make the struct a ref, otherwise you resort to black magic
        //        parameters = method.GetParameters().Select(param => param.ParameterType).Prepend(method.DeclaringType.MakeByRefType()).ToArray();
        //    else // otherwise, we have an instance-nonstruct class, lets all our instance, and our parameter types
        //        parameters = method.GetParameters().Select(param => param.ParameterType).Prepend(method.DeclaringType).ToArray();

        //    DynamicMethod meth = new DynamicMethod(
        //        method.Name + "_runtimeReplacement",
        //        MethodAttributes.Public,
        //        method.CallingConvention,
        //        method.ReturnType,
        //        parameters,
        //        method.DeclaringType.IsInterface ? typeof(void) : method.DeclaringType,
        //        true
        //        );

        //    ILGenerator gen = meth.GetILGenerator(512);

        //    // local variable for profiler
        //    LocalBuilder localProfiler = gen.DeclareLocal(typeof(Profiler));

        //    InsertStartIL(type, gen, key, localProfiler, index);

        //    // dynamically add our parameters, as many as they are, onto the stack for our original method
        //    for (int i = 0; i < parameters.Length; i++)
        //        gen.Emit(OpCodes.Ldarg, i);

        //    gen.Emit(inst.opcode, method); // call our original method, (all parameters are on the stack)

        //    InsertRetIL(type, gen, localProfiler); // wrap our function up, return a value if required

        //    return new CodeInstruction(inst)
        //    {
        //        opcode = OpCodes.Call,
        //        operand = meth // our created dynamic method
        //    };
        //}


        // Utility for IL insertion
        //public static void InsertStartIL(Type type, ILGenerator ilGen, string key, LocalBuilder profiler, int index)
        //{
        //    // if(Active && AnalyzerState.CurrentlyRunning)
        //    // { 
        //    Label skipLabel = ilGen.DefineLabel();

        //    ilGen.Emit(OpCodes.Ldsfld, type.GetField("Active", BindingFlags.Public | BindingFlags.Static));
        //    ilGen.Emit(OpCodes.Brfalse_S, skipLabel);

        //    ilGen.Emit(OpCodes.Call, Analyzer_Get_CurrentlyProfiling);
        //    ilGen.Emit(OpCodes.Brfalse_S, skipLabel);

        //    ilGen.Emit(OpCodes.Ldstr, key);
        //    // load our string to stack

        //    ilGen.Emit(OpCodes.Ldnull);
        //    ilGen.Emit(OpCodes.Ldnull);
        //    // load our null variables

        //    ilGen.Emit(OpCodes.Ldsfld, MethodInfoCache.internalArray);
        //    ilGen.Emit(OpCodes.Ldc_I4, index);
        //    ilGen.Emit(OpCodes.Ldc_I4_7);
        //    ilGen.Emit(OpCodes.Shr);
        //    ilGen.Emit(OpCodes.Callvirt, MethodInfoCache.accessList);
        //    ilGen.Emit(OpCodes.Ldc_I4, index);
        //    ilGen.Emit(OpCodes.Ldc_I4, 127);
        //    ilGen.Emit(OpCodes.And);
        //    ilGen.Emit(OpCodes.Ldelem_Ref);

        //    ilGen.Emit(OpCodes.Call, ProfileController_Start);
        //    ilGen.Emit(OpCodes.Stloc, profiler.LocalIndex);
        //    // localProfiler = ProfileController.Start(key, null, null, null, null, KeyMethods[key]);

        //    ilGen.MarkLabel(skipLabel);
        //}

        //public static void InsertRetIL(Type type, ILGenerator ilGen, LocalBuilder profiler)
        //{
        //    Label skipLabel = ilGen.DefineLabel();
        //    // if(profiler != null)
        //    // {
        //    ilGen.Emit(OpCodes.Ldloc, profiler);
        //    ilGen.Emit(OpCodes.Brfalse_S, skipLabel);
        //    // profiler.Stop();
        //    ilGen.Emit(OpCodes.Ldloc, profiler.LocalIndex);
        //    ilGen.Emit(OpCodes.Call, Profiler_Stop);
        //    // }
        //    ilGen.MarkLabel(skipLabel);

        //    ilGen.Emit(OpCodes.Ret);
        //}
    }
}
