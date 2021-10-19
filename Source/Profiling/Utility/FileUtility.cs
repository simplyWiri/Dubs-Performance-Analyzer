using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Verse;

namespace Analyzer.Profiling 
{
    [HotSwappable]
    public static class FileUtility
    {
        private static string INVALID_CHARS = $@"[{Regex.Escape(new string(Path.GetInvalidFileNameChars()))}]+";
        public static string GetFileLocation => Path.Combine(GenFilePaths.SaveDataFolderPath, "Analyzer");

        public static string FinalFileNameFor(string file, int idx) => Path.Combine(GetFileLocation, file + idx + ".data");
        
        public static DirectoryInfo GetDirectory()
        {
            var directory = new DirectoryInfo(GetFileLocation);
            if (!directory.Exists)
                directory.Create();

            return directory;
        }

        // from: https://stackoverflow.com/a/12924582, ignoring reserved kws.
        public static string SanitizeFileName(string filename)
        {
            return Regex.Replace(filename, INVALID_CHARS, "_");
        }

        public static string FileNameFor(MethodInfo method)
        {
            var sig = Utility.GetSignature(method);
            // add suffix for number of entries already in the folder
            return SanitizeFileName(sig);
        }
        
        public static IEnumerable<FileInfo> PreviousEntriesFor(MethodInfo method)
        {
            var fn = FileNameFor(method);

            return GetDirectory().GetFiles().Where(f => f.Name.Contains(fn));
        }

    }
}
