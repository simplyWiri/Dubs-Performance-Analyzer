﻿using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Verse;

namespace Analyzer.Profiling
{
    public static class Utility
    {
        public static List<string> patchedAssemblies = new List<string>();
        public static List<string> patchedTypes = new List<string>();
        public static List<string> patchedMethods = new List<string>();

        public static bool displayMessages => Settings.verboseLogging;


        public static void ClearPatchCaches()
        {
            patchedAssemblies.Clear();
            patchedTypes.Clear();
            patchedMethods.Clear();

            InternalMethodUtility.ClearCaches();
            MethodTransplanting.ClearCaches();
            TranspilerMethodUtility.ClearCaches();

            MethodInfoCache.ClearCache();

#if DEBUG
            ThreadSafeLogger.Message("[Analyzer] Cleared all caches");
#endif
        }

        /*
         * Utility
         */
        public static IEnumerable<string> GetSplitString(string name)
        {
            List<string> listStrLineElements = new List<string>();


            if (name.Contains(','))
            {
                string[] range = name.Split(',');
                range.Do(str => str.Trim());
                foreach (string str in range)
                {
                    foreach (string ret in GetSplitString(str))
                        yield return ret;
                }

                yield break;
            }

            if (name.Contains(';'))
            {
                string[] range = name.Split(';');
                range.Do(str => str.Trim());
                foreach (string str in range)
                {
                    foreach (string ret in GetSplitString(str))
                        yield return ret;
                }

                yield break;
            }

            // check if our name has a ':', indicating a method
            if (name.Contains(':'))
            {
                yield return name.Trim();
                yield break;
            }

            if (name.Contains('.'))
            {
                if (name.CharacterCount('.') == 1)
                {
                    if (AccessTools.TypeByName(name) != null) // namespace.type
                    {
                        yield return name.Trim();
                        yield break;
                    }
                    else // type.method -> type:method
                    {
                        yield return name.Replace(".", ":")
                            .Trim();
                        yield break;
                    }
                }
                else
                {
                    if (AccessTools.TypeByName(name) != null) // namespace.type.type2 or namespace.namespace2.type etc
                    {
                        yield return name.Trim();
                        yield break;
                    }
                    else
                    {
                        // namespace.type.method
                        int ind = name.LastIndexOf('.');
                        yield return name.Remove(ind, 1)
                            .Insert(ind, ":")
                            .Trim();
                        yield break;
                    }
                }
            }
        }
        
        internal static string GetSignature(MethodBase method, bool showParameters = true)
        {
            var firstParam = true;
            var sigBuilder = new StringBuilder(40);

            string mKey;
            if (method.ReflectedType != null) mKey = TypeName(method.ReflectedType, true) + ":" + method.Name;
            else mKey = TypeName(method.DeclaringType, true) + ":" + method.Name;
            sigBuilder.Append(mKey);

            // Add method generics
            if(method.IsGenericMethod)
            {
                sigBuilder.Append("<");
                foreach(var g in method.GetGenericArguments())
                {
                    if (firstParam) firstParam = false;
                    else sigBuilder.Append(", ");

                    sigBuilder.Append(TypeName(g));
                }
                sigBuilder.Append(">");
            }

            sigBuilder.Append("(");

            if (showParameters)
            {
                firstParam = true;
                foreach (var param in method.GetParameters())
                {
                    if (firstParam)
                    {
                        firstParam = false;
                        if (method.IsDefined(typeof(ExtensionAttribute), false))
                        {
                            sigBuilder.Append("this ");
                        }
                    }
                    else
                        sigBuilder.Append(", ");

                    if (param.ParameterType.IsByRef)
                        sigBuilder.Append("ref ");
                    else if (param.IsOut)
                        sigBuilder.Append("out ");

                    sigBuilder.Append(TypeName(param.ParameterType));
                }
            }

            sigBuilder.Append(")");

            return sigBuilder.ToString();
        }

        internal static bool IsGenericType(Type type, string name)
        {
            return (type.GetGenericArguments()?.Any() ?? false) && name.Contains('`');
        }


        internal static string TypeName(Type type, bool fullName = false)
        {
            var nullableType = Nullable.GetUnderlyingType(type);
            if (nullableType != null) return nullableType.Name + "?";

            var tName = type.ReflectedType == null ? type.Name : type.ReflectedType.Name;

            if (IsGenericType(type, tName))
            {
                var sb = new StringBuilder(tName.Substring(0, tName.IndexOf('`')));
                sb.Append('<');
                var first = true;
                foreach (var t in type.GetGenericArguments())
                {
                    if (!first)
                        sb.Append(", ");

                    sb.Append(TypeName(t));
                    first = false;
                }
                sb.Append('>');
                return sb.ToString();
            }
            else
            {
                string ReplaceOccurence(string typeName, string to)
                {
                    return type.Name.Replace(typeName, to);
                }

                // This finds things like "String[]" as well as just String, which is why its not in the switch
                // Todo: Hash table?
                if (type.Name.Contains("String")) return ReplaceOccurence("String", "string");
                if (type.Name.Contains("Int32")) return ReplaceOccurence("Int32", "int");
                if (type.Name.Contains("Object")) return ReplaceOccurence("Object", "object");
                if (type.Name.Contains("Boolean"))  return ReplaceOccurence("Boolean", "bool");
                if (type.Name.Contains("Decimal")) return ReplaceOccurence("Decimal", "decimal");

                if (type.Name == "Void") return "void"; 

                return fullName ? type.FullName : type.Name;
            }
        }
    

        public static string GetMethodKey(MethodBase meth)
        {
            return GetSignature(meth, false);
        }

        private static void Notify(string message)
        {
#if DEBUG
            ThreadSafeLogger.Error($"[Analyzer] Patching notification: {message}");
#endif
#if NDEBUG
            if (!displayMessages) return;
            ThreadSafeLogger.Message($"[Analyzer] Patching notification: {message}");
#endif
        }

        private static void Warn(string message)
        {
#if DEBUG
            ThreadSafeLogger.Error($"[Analyzer] Patching warning: {message}");
#endif
#if NDEBUG
            if (!displayMessages) return;
            ThreadSafeLogger.Warning($"[Analyzer] Patching warning: {message}");
#endif
        }

        private static void Error(string message)
        {
#if DEBUG
            ThreadSafeLogger.Error($"[Analyzer] Patching error: {message}");
#endif
#if NDEBUG
            if (!displayMessages) return;
            ThreadSafeLogger.Error($"[Analyzer] Patching error: {message}");
#endif
        }

        // returns false is the method is invalid
        public static bool ValidMethod(MethodInfo method)
        {
            if (method == null)
            {
                Error("Null MethodInfo");
                return false;
            }

            var mKey = method?.DeclaringType?.FullName + ":" + method.Name;

            if (!method.HasMethodBody())
            {
                Warn($"Does not have a methodbody - {mKey}");
                return false;
            }

            if (method.IsGenericMethod || method.ContainsGenericParameters)
            {
                Warn($"Can not currently patch generic methods - {mKey}");
                return false;
            }

            if (patchedMethods.Contains(mKey))
            {
                Warn($"Method has already been patched - {mKey}");
                return false;
            }

            patchedMethods.Add(mKey);

            return true;
        }

        public static bool IsNotAnalyzerPatch(string patchId)
        {
            return patchId != Modbase.Harmony.Id && patchId != Modbase.StaticHarmony.Id;
        }

        public static IEnumerable<MethodInfo> GetMethods(string str)
        {
            foreach (var s in GetSplitString(str))
                yield return AccessTools.Method(s);
        }

        public static IEnumerable<MethodInfo> GetMethodsPatching(string str)
        {
            foreach (var meth in GetMethods(str))
            {
                var p = Harmony.GetPatchInfo(meth);

                foreach (var patch in p.Prefixes.Concat(p.Postfixes, p.Transpilers, p.Finalizers))
                    yield return patch.PatchMethod;
            }
        }

        public static IEnumerable<MethodInfo> GetMethodsPatchingType(Type type)
        {
            foreach (var meth in GetTypeMethods(type))
            {
                var p = Harmony.GetPatchInfo(meth);

                foreach (var patch in p.Prefixes.Concat(p.Postfixes, p.Transpilers, p.Finalizers))
                    yield return patch.PatchMethod;
            }
        }


        public static IEnumerable<MethodInfo> GetTypeMethods(Type type)
        {
            foreach (var method in AccessTools.GetDeclaredMethods(type).Where(ValidMethod))
                yield return method;
        }

        public static IEnumerable<MethodInfo> SubClassImplementationsOf(Type baseType, Func<MethodInfo, bool> predicate)
        {
            var meths = new List<MethodInfo>();
            foreach (var t in baseType.AllSubclasses())
            {
                meths.AddRange(GetTypeMethods(t).Where(predicate));
            }

            return meths;
        }

        public static IEnumerable<MethodInfo> SubClassNonAbstractImplementationsOf(Type baseType, Func<MethodInfo, bool> predicate)
        {
            var meths = new List<MethodInfo>();
            foreach (var t in baseType.AllSubclassesNonAbstract())
            {
                meths.AddRange(GetTypeMethods(t).Where(predicate));
            }

            return meths;
        }

        public static IEnumerable<MethodInfo> GetAssemblyMethods(Assembly assembly)
        {
            foreach (var type in assembly.GetTypes().Where(t => t.GetCustomAttribute<CompilerGeneratedAttribute>() == null))
                    foreach (var m in GetTypeMethods(type))
                        yield return m;
        }


        // Unpatching

        public static void UnpatchMethod(string name) => UnpatchMethod(AccessTools.Method(name));

        public static void UnpatchMethod(MethodInfo method)
        {
            foreach (MethodBase methodBase in Harmony.GetAllPatchedMethods())
            {
                Patches infos = Harmony.GetPatchInfo(methodBase);

                var allPatches = infos.Prefixes.Concat(infos.Postfixes, infos.Transpilers, infos.Finalizers);

                if (!allPatches.Any(patch => patch.PatchMethod == method)) continue;

                Modbase.Harmony.Unpatch(methodBase, method);
                return;
            }

            Warn("Failed to locate method to unpatch");
        }

        public static void UnpatchMethodsOnMethod(string name) => UnpatchMethodsOnMethod(AccessTools.Method(name));
        public static void UnpatchMethodsOnMethod(MethodInfo method) => Modbase.Harmony.Unpatch(method, HarmonyPatchType.All);

        public static void UnPatchTypePatches(string name) => UnPatchTypePatches(AccessTools.TypeByName(name));

        public static void UnPatchTypePatches(Type type)
        {
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(type))
                UnpatchMethodsOnMethod(method);
        }


        /*
         * Internal Method Patching
         */

        public static void PatchInternalMethod(string name, Category category)
        {
            MethodInfo method = null;
            try
            {
                method = AccessTools.Method(name);
            }
            catch (Exception e)
            {
                Error($"Failed to locate method {name}, errored with the message {e.Message}");
                return;
            }

            PatchInternalMethod(method, category);
        }

        public static void PatchInternalMethod(MethodInfo method, Category category)
        {
            if (InternalMethodUtility.PatchedInternals.Contains(method))
            {
                Warn($"Trying to re-transpile an already profiled internal method - {method.DeclaringType.FullName}:{method.Name}");
                return;
            }

            PatchInternalMethodFull(method, category);
        }

        private static void PatchInternalMethodFull(MethodInfo method, Category category)
        {
            try
            {
                bool Valid()
                {
                    var bytes = method.GetMethodBody()?.GetILAsByteArray();
                    if (bytes == null) return false;
                    if (bytes.Length == 0) return false;
                    if (bytes.Length == 1 && bytes.First() == 0x2A) return false;
                    return true;
                }

                if (Valid() == false)
                {
                    Error("Can not patch this method, this is likely a method which is virtually dispatched, and thus can not be generically examined.");
                    return;
                }

                var guiEntry = method.DeclaringType + ":" + method.Name + "-int";
                GUIController.AddEntry(guiEntry, category);
                GUIController.SwapToEntry(guiEntry);

                InternalMethodUtility.PatchedInternals.Add(method);

                Task.Factory.StartNew(() =>
                {
                    try
                    {
                        Modbase.Harmony.Patch(method, transpiler: InternalMethodUtility.InternalProfiler);
                    }
                    catch (Exception e)
                    {
                        ThreadSafeLogger.Error($"Failed to patch the internal method {method.DeclaringType.FullName}:{method.Name}, failed with the error {e.Message} at \n{e.StackTrace}");
                    }
                });
            }
            catch (Exception e)
            {
                Error("Failed to patch internal methods, failed with the error " + e.Message);
                InternalMethodUtility.PatchedInternals.Remove(method);
            }
        }

        private static bool ValidAssembly(Assembly assembly)
        {
            if (assembly.FullName.Contains("0Harmony")) return false;
            if (assembly.FullName.Contains("Cecil")) return false;
            if (assembly.FullName.Contains("Multiplayer")) return false;
            if (assembly.FullName.Contains("UnityEngine")) return false;

            return true;
        }

        public static void PatchAssembly(string name, Category type)
        {
            var mod = LoadedModManager.RunningMods.FirstOrDefault(m => m.Name == name || m.PackageId == name.ToLower());
            if (mod == null)
            {
                Error($"Failed to locate the mod {name}");
                return;
            }

            Notify($"Assembly count: {mod.assemblies?.loadedAssemblies?.Count ?? 0}");
            var assemblies = mod?.assemblies?.loadedAssemblies?.Where(ValidAssembly).ToList();
            Notify($"Assembly count after removing Harmony/Cecil/Multiplayer/UnityEngine: {assemblies?.Count}");

            if (assemblies != null && assemblies.Count() != 0)
            {
                GUIController.AddEntry(mod.Name + "-prof", type);
                GUIController.SwapToEntry(mod.Name + "-prof");

                Task.Factory.StartNew(() => PatchAssemblyFull(mod.Name + "-prof", assemblies.ToList()));
            }
            else
            {
                Error($"Failed to patch {name} - There are no assemblies");
            }
        }

        private static void PatchAssemblyFull(string key, List<Assembly> assemblies)
        {
            var meths = new HashSet<MethodInfo>();

            foreach (var assembly in assemblies)
            {
                try
                {
                    if (patchedAssemblies.Contains(assembly.FullName))
                    {
                        Warn($"patching {assembly.FullName} failed, already patched");
                        return;
                    }

                    patchedAssemblies.Add(assembly.FullName);

                    foreach (var type in assembly.GetTypes())
                    {
                        foreach (var method in AccessTools.GetDeclaredMethods(type).Where(m => ValidMethod(m) && m.DeclaringType == type))
                        {
                            if(!meths.Contains(method))
                                meths.Add(method);
                        }
                    }

                    Notify($"Patched {assembly.FullName}");
                }
                catch (Exception e)
                {
                    Error($"Patching {assembly.FullName} failed, {e.Message}");
                    return;
                }
            }

            MethodTransplanting.UpdateMethods(GUIController.types[key], meths);
        }
    }
}