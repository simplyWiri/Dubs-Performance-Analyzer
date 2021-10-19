using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Verse;

namespace Analyzer.Profiling 
{
    public struct FileHeader
    {
        // magic must equal 440985710, otherwise the file has been corrupted.
        public int MAGIC;
        public int scribingVer;
        public string methodName;
        public string name;
        public bool entryPerCall;
        public bool onlyEntriesWithValues;
        public int entries;
        public int targetEntries;
    }

    public class EntryFile
    {
        public FileHeader header;
        public float[] times;
        public int[] calls;
    }
    
    [HotSwappable]
    public static class FileUtility
    {
        public const int ENTRY_FILE_MAGIC = 440985710;
        
        private static string INVALID_CHARS = $@"[{Regex.Escape(new string(Path.GetInvalidFileNameChars()))}]+";
        public static string GetFileLocation => Path.Combine(GenFilePaths.SaveDataFolderPath, "Analyzer");

        public static string FinalFileNameFor(string file, int idx) => Path.Combine(GetFileLocation, file + idx + ".data");

        private static FileInfo[] cachedFiles = null;
        private static long lastFileAccess = 0;

        private static void RefreshFiles()
        {
            var access = DateTime.Now.ToFileTimeUtc();

            if (access - lastFileAccess < 15)
            {
                cachedFiles = GetDirectory().GetFiles();
                lastFileAccess = access;
            }
        }
        
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

        public static string FileNameFor(string str)
        {
            // var sig = new StringBuilder(Utility.GetSignature(method, false));
            // // name mangling
            // sig.Append('(');
            // foreach (var param in method.GetParameters())
            // {
            //     if (param.IsOut) sig.Append('o');
            //     if (param.IsIn) sig.Append('r');
            //     sig.Append($"{param.Name[0]}_");
            // }
            //
            // sig.Append(')');

            return SanitizeFileName(str);
        }
        
        public static IEnumerable<FileInfo> PreviousEntriesFor(string s)
        {
            RefreshFiles();
            
            var fn = FileNameFor(s);

            return cachedFiles?.Where(f => f.Name.Contains(fn)) ?? Enumerable.Empty<FileInfo>();
        }
        
        public static EntryFile ReadFile(FileInfo file)
        {
            return null;
        }

        public static void WriteFile(EntryFile file)
        {
            
        }

        public static FileHeader ReadHeader(FileInfo file)
        {
            return new FileHeader();
        }
        
        
        
    }
}
