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
        public static ConcurrentDictionary<MethodBase, MethodPatchWrapper> patchedMethods = new ConcurrentDictionary<MethodBase, MethodPatchWrapper>();

        private static readonly HarmonyMethod generalTranspiler = new HarmonyMethod(typeof(MethodTransplanting), nameof(Transpiler));
        //private static readonly HarmonyMethod occurencesInTranspiler = new HarmonyMethod(typeof(MethodTransplanting), nameof(Transpiler));
        
        // profiler registry
        private static readonly FieldInfo ProfilerRegistry_Profilers = AccessTools.Field(typeof(ProfilerRegistry), nameof(ProfilerRegistry.profilers));
        private static readonly FieldInfo ProfilerRegistry_CustomProfilers = AccessTools.Field(typeof(ProfilerRegistry), nameof(ProfilerRegistry.nameToProfiler));
        private static readonly MethodInfo ProfilerRegistry_RegisterProfiler = AccessTools.Method(typeof(ProfilerRegistry), nameof(ProfilerRegistry.RegisterProfiler));
        

        // profile controller
        private static readonly FieldInfo ProfilerController_Profiles = AccessTools.Field(typeof(ProfileController), "profiles");

        // profiler
        private static readonly MethodInfo Profiler_Start = AccessTools.Method(typeof(Profiler), nameof(Profiler.Start));
        private static readonly MethodInfo Profiler_Stop = AccessTools.Method(typeof(Profiler), nameof(Profiler.Stop));
        private static readonly ConstructorInfo ProfilerCtor = AccessTools.Constructor(typeof(Profiler), new Type[] { typeof(string), typeof(string), typeof(Type), typeof(MethodBase) });

        // dictionary
        private static readonly MethodInfo Dict_TryGetValue = AccessTools.Method(typeof(Dictionary<string, int>), "TryGetValue");
        private static readonly MethodInfo Dict_Add = AccessTools.Method(typeof(Dictionary<string, Profiler>), "Add");


        public static void ClearCaches()
        {
            patchedMethods.Clear();
        }

        public static void PatchMethods(Type type)
        {
            // get the methods
            var meths = (IEnumerable<MethodPatchWrapper>)type.GetMethod("GetPatchMethods", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);

            if (meths != null)
                UpdateMethods(type, meths);
        }

        public static void UpdateMethods(Type type, IEnumerable<MethodPatchWrapper> meths)
        {
            foreach (var meth in meths)
            {
                meth.SetEntry(type);
                ProfileMethod(meth);
            }
        }

        // This will profile the method `method.target`, and will (if not null) apply the custom namer & labeller
        public static void ProfileMethod(MethodPatchWrapper method)
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
                    Modbase.Harmony.Patch(method.target, transpiler: generalTranspiler);
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



        // This transpiler basically replicates ProfileController.Start, but in IL, and inside the method it is patching, to reduce as much overhead as
        // possible, its quite simple, just long and hard to read.
        public static IEnumerable<CodeInstruction> Transpiler(MethodBase __originalMethod, IEnumerable<CodeInstruction> insts, ILGenerator ilGen)
        {
            var instructions = insts.ToList();
            var profLocal = ilGen.DeclareLocal(typeof(Profiler));
            var keyLocal = ilGen.DeclareLocal(typeof(string));
            var beginLabel = ilGen.DefineLabel();
            var noProfFastPathLabel = ilGen.DefineLabel();
            var noProfLabel = ilGen.DefineLabel();

            var wrapper = patchedMethods[__originalMethod];

            var key = Utility.GetSignature(__originalMethod as MethodInfo, true); // This translates our method into a human-legible key, I.e. Namespace.Type<Generic>:Method
            ProfilerRegistry.RegisterPatch(key, wrapper);
            var methodKey = wrapper.uid;

            // Check if analyzer is active, if not branch to the start of the actual method
            foreach (var inst in InsertActiveCheck(wrapper, beginLabel)) 
                yield return inst;
            
            
            // Retrieve our profiler key, we can retrieve this from a method, so its not the simplest process
            foreach (var inst in InsertProfileRetrieval(wrapper, methodKey, keyLocal, profLocal, beginLabel, noProfLabel, noProfFastPathLabel, ilGen))
                yield return inst;


            { // If we found a profiler - Start it, and skip to the start of execution of the method
                yield return new CodeInstruction(OpCodes.Ldloc, profLocal);
                yield return new CodeInstruction(OpCodes.Call, Profiler_Start);
                yield return new CodeInstruction(OpCodes.Pop); // Profiler.Start returns itself so we pop it off the stack
                yield return new CodeInstruction(OpCodes.Br, beginLabel);
            }

            foreach (var inst in InsertProfilerCreation(wrapper, key, methodKey, keyLocal, profLocal, noProfLabel, noProfFastPathLabel))
                yield return inst;

            yield return new CodeInstruction(OpCodes.Call, Profiler_Start);
            yield return new CodeInstruction(OpCodes.Pop); // profiler.Start returns itself

            instructions.ElementAt(0).WithLabels(beginLabel);

            // For each instruction which exits this function, append our finishing touches (I.e.)
            // if(profiler != null)
            // {
            //      profiler.Stop();
            // }
            // return; // any labels here are moved to the start of the `if`
            foreach (var inst in instructions)
            {
                if (inst.opcode == OpCodes.Ret)
                {
                    var endLabel = ilGen.DefineLabel();

                    // localProf?.Stop();
                    yield return new CodeInstruction(OpCodes.Ldloc, profLocal).MoveLabelsFrom(inst);
                    yield return new CodeInstruction(OpCodes.Brfalse_S, endLabel);

                    yield return new CodeInstruction(OpCodes.Ldloc, profLocal);
                    yield return new CodeInstruction(OpCodes.Call, Profiler_Stop);

                    yield return inst.WithLabels(endLabel);
                }
                else
                {
                    yield return inst;
                }
            }
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
                    yield return new CodeInstruction(OpCodes.Ldarg_S, (originalMethod.IsStatic ? 0 : 1) + origParams.FirstIndexOf(p => p.Name == param.Name && p.ParameterType == param.ParameterType));
                }
            }
        }


        // checks entry.Active && Analyzer.CurrentlyProfiling && !Analyzer.CurrentlyPaused
        // if false, branches to `skipTo`
        // else, continues as normal
        public static IEnumerable<CodeInstruction> InsertActiveCheck(MethodPatchWrapper wrapper, Label skipTo)
        {
            foreach (var inst in ProfilerRegistry.GetIL(wrapper.uid, true))
                yield return inst;

            yield return new CodeInstruction(OpCodes.Brfalse_S, skipTo);
        }

        // Leaves the stack empty, has 3 main effects:
        // 1. profLocal is null, it will be branching to either `noProfileLabel` or `noProfileFastPath` depending on whether 
        // the wrapper.customNamer is set or not
        // 2. customNamer returns null, it will be branching to `beginLabel`
        // 3. profLocal is not null, and it will continue execution as normal
        public static IEnumerable<CodeInstruction> InsertProfileRetrieval(MethodPatchWrapper wrapper, int methodKey, LocalBuilder keyLocal, LocalBuilder profLocal, Label beginlabel, Label noProfileLabel, Label noProfileFastPath, ILGenerator ilGen)
        {
            if (wrapper.customKeyNamer != null)
            {
                var indexVariable = ilGen.DeclareLocal(typeof(int));

                foreach (var codeInst in GetLoadArgsForMethodParams(wrapper.target, wrapper.customKeyNamer.GetParameters()))
                    yield return codeInst;

                yield return new CodeInstruction(OpCodes.Call, wrapper.customKeyNamer);
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
                    yield return new CodeInstruction(OpCodes.Stloc, profLocal);
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
        public static IEnumerable<CodeInstruction> InsertProfilerCreation(MethodPatchWrapper wrapper, string key, int methodKey, LocalBuilder keyLocal, LocalBuilder profLocal, Label noProfLabel, Label noProfFastPathLabel)
        {
            { // if not, we need to make one
                yield return new CodeInstruction(OpCodes.Ldstr, key).WithLabels(noProfFastPathLabel);
                yield return new CodeInstruction(OpCodes.Stloc, keyLocal);

                yield return new CodeInstruction(OpCodes.Ldloc, keyLocal).WithLabels(noProfLabel);


                { // Custom Labelling
                    if (wrapper.customLabeller != null)
                    {
                        foreach (var codeInst in GetLoadArgsForMethodParams(wrapper.target, wrapper.customLabeller.GetParameters()))
                            yield return codeInst;

                        yield return new CodeInstruction(OpCodes.Call, wrapper.customLabeller);
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

            if(wrapper.customKeyNamer != null) // Add to the Profilers dictionary, so we cache creation.
            { 
                yield return new CodeInstruction(OpCodes.Ldloc, keyLocal);
                yield return new CodeInstruction(OpCodes.Ldstr, key);
                yield return new CodeInstruction(OpCodes.Ldloc, profLocal);
                yield return new CodeInstruction(OpCodes.Callvirt, ProfilerRegistry_RegisterProfiler);
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
