using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace Analyzer.Profiling
{
    public static class StackTraceRegex
    {
        public static Dictionary<string, StackTraceInformation> traces = new Dictionary<string, StackTraceInformation>();

        public static void Add(StackTrace trace)
        {
            var key = trace.ToString();

            if(traces.TryGetValue(key, out var value)) value.Count++;
            else traces.Add(key, new StackTraceInformation(trace));
        }

        public static void Reset()
        {
            traces = new Dictionary<string, StackTraceInformation>();
        }
    }

    public class StackTraceInformation
    {
        public int Count { get; set; } = 1;
        private string[] translatedStringArr = null;
        public string[] TranslatedArr() => translatedStringArr;

        public StackTraceInformation(StackTrace input) => ProcessInput(input);

        private void ProcessInput(StackTrace stackTrace)
        {
            // Translate our input into the strings we will want to show the user
            var processedString = ThreadSafeLogger.ExtractTrace(stackTrace);

            translatedStringArr = processedString.Split('\n');
        }
    }


}
