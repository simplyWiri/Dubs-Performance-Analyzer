using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Analyzer.Profiling
{
    public abstract class PatchWrapper
    {
        public static implicit operator PatchWrapper(MethodInfo method)
        {
            return new MethodPatchWrapper(method);
        }

        public PatchWrapper(MethodInfo method, MethodInfo getKeyName = null, MethodInfo getLabel = null)
        {
            this.target = method;
            this.entries = new List<Type>();
        }

        public virtual int GetUIDFor(string key) => -1;

        public MethodInfo target;
        public List<Type> entries;
        public void AddEntry(Type entry) => this.entries.Add(entry);
    }

    public class MethodPatchWrapper : PatchWrapper
    {
        // method - the target method you want to patch
        // (opt) customNamer - a method which dictates the KEY of the profiler
        // (opt) customLabeller - a method which dictates the LABEL of the profiler viewable in the GUI
        public MethodPatchWrapper(MethodInfo method, MethodInfo getKeyName = null, MethodInfo getLabel = null)
            : base(method)
        {
            this.getKeyName = getKeyName;
            this.getLabel = getLabel;
        }

        public void SetUID(int uid) => this.uid = uid;

        public override int GetUIDFor(string key)
        {
            return uid;
        }

        public int uid = -1;

        public MethodInfo getKeyName;
        public MethodInfo getLabel;
    }

    public class MultiMethodPatchWrapper : PatchWrapper
    {
        public MultiMethodPatchWrapper(MethodInfo calledIn, List<MethodInfo> targets, List<MethodInfo> getKeyNames = null, List<MethodInfo> getLabels = null)
            : base(calledIn)
        {
            var size = targets.Count;
            this.targets = targets;
            this.getKeyNames = getKeyNames ?? new List<MethodInfo>(Enumerable.Repeat((MethodInfo)null, size));
            this.getLabels = getLabels ?? new List<MethodInfo>(Enumerable.Repeat((MethodInfo)null, size));
            this.uids = new List<int>(Enumerable.Repeat(-1, size));
        }

        public void SetUID(int target, int uid) => this.uids[target] = uid;

        public override int GetUIDFor(string key)
        {
            for (var i = 0; i < targets.Count; i++)
            {
                if (Utility.GetSignature(targets[i])
                    .Equals(key)) return uids[i];
            }

            return -1;
        }

        public List<int> uids;
        public List<MethodInfo> targets;
        public List<MethodInfo> getKeyNames;
        public List<MethodInfo> getLabels;
    }
}
