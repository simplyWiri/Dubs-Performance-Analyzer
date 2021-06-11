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

        public MethodPatchWrapper(MethodInfo method, MethodInfo customNamer = null, MethodInfo customLabeller = null)
        {
            this.methodInfo = method;
            this.customNamer = customNamer;
            this.customLabeller = customLabeller;
        }

        public void SetEntry(Type entry) => this.entry = entry;

        public MethodInfo methodInfo;
        public MethodInfo customTyper;
        public MethodInfo customNamer;
        public MethodInfo customLabeller;
        public Type entry;
    }
}
