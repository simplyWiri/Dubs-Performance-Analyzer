using System;
using System.ComponentModel;
using System.Reflection;
using System.Text;
using Verse;

namespace Analyzer.Profiling
{
    public class ProfileLog
    {
        // Identify the profiler that created this log
        public int mKey;

        // Metadata
        public int entries;

        public float percent; // % in entry it belongs to
        public double average; // average ms execution time
        public float max; // max ms execution time
        public float total; // total ms execution time
        public float calls; // total calls

        // GUI Metadata, is this entry pinned?
        public bool pinned;

        public MethodBase Method => ProfilerRegistry.methodBases[mKey];
        public Profiler Profiler => ProfilerRegistry.profilers[mKey];
        public string Label => Profiler.label;

        public ProfileLog(int mKey, int entries, double average, float max, float total, float calls, bool pinned)
        {
            this.mKey = mKey;

            this.entries = entries;
            this.average = average;
            this.max = max;
            this.total = total;
            this.calls = calls;

            this.pinned = pinned;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append($"Key: {mKey}, profiler {Profiler?.ToString() ?? "null"} pinned {pinned}");
            sb.Append($"Entries: {entries}, Percent {percent}, Average {average}, Max {max}, Total {total}, Calls {calls}");
            return sb.ToString();
        }
    }
}