using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Analyzer.Profiling
{
    public class MethodPatchWrapper
    {
        public static implicit operator MethodPatchWrapper(MethodInfo method)
        {
            return new MethodPatchWrapper(method);
        }

        // method - the target method you want to patch
        // (opt) customNamer - a method which dictates the KEY of the profiler
        // (opt) customLabeller - a method which dictates the LABEL of the profiler viewable in the GUI
        // (opt) calledIn - a list of methods to replace calls to `target` with a profiler version
        public MethodPatchWrapper(MethodInfo method, MethodInfo customKeyNamer = null, MethodInfo customLabeller = null, List<MethodInfo> calledIn = null)
        {
            this.target = method;
            this.customKeyNamer = customKeyNamer;
            this.customLabeller = customLabeller;
            this.entries = new List<Type>();
        }

        public void AddEntry(Type entry) => this.entries.Add(entry);
        public void SetUID(int uid) => this.uid = uid;

        public MethodInfo target;
        public int uid = -1;
        public List<MethodInfo> calledIn = null;

        public MethodInfo customKeyNamer;
        public MethodInfo customLabeller;
        public List<Type> entries;
    }
}
