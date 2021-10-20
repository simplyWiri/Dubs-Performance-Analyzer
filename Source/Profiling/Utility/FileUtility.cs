using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
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
        
        public string Name => name == " " ? methodName : name;
    }

    public class EntryFile
    {
        public FileHeader header;
        public double[] times;
        public int[] calls;

    }
    
    [HotSwappable]
    public static class FileUtility
    {
        public const int ENTRY_FILE_MAGIC = 440985710;
        public const int SCRIBE_FILE_VER = 1;
        
        private static string INVALID_CHARS = $@"[{Regex.Escape(new string(Path.GetInvalidFileNameChars()))}]+";
        public static string GetFileLocation => Path.Combine(GenFilePaths.SaveDataFolderPath, "Analyzer");

        private static string FinalFileNameFor(string str) => Path.Combine(GetFileLocation, SanitizeFileName(str) + '-' + PreviousEntriesFor(str).Count() + ".data");

        private static FileInfo[] cachedFiles = null;
        private static long lastFileAccess = 0;
        private static bool changed = false;

        private static void RefreshFiles()
        {
            var access = DateTime.Now.ToFileTimeUtc();

            if (!changed && access - lastFileAccess <= 15) return;
            
            cachedFiles = GetDirectory().GetFiles();
            lastFileAccess = access;
            changed = false;
        }
        
        public static DirectoryInfo GetDirectory()
        {
            var directory = new DirectoryInfo(GetFileLocation);
            if (!directory.Exists)
                directory.Create();

            return directory;
        }

        // taken in part from: https://stackoverflow.com/a/12924582, ignoring reserved kws.
        public static string SanitizeFileName(string filename)
        {
            return Regex.Replace(filename, INVALID_CHARS, "_").Replace(' ', '_');
        }
        
        public static IEnumerable<FileInfo> PreviousEntriesFor(string s)
        {
            RefreshFiles();
            
            var fn = SanitizeFileName(s);

            return cachedFiles?.Where(f => f.Name.Contains(fn)) ?? Enumerable.Empty<FileInfo>();
        }
        
        public static EntryFile ReadFile(FileInfo file)
        {
            var entryFile = new EntryFile();

            try
            {
                using (var reader = new BinaryReader(file.OpenRead()))
                {
                    entryFile.header = ReadHeader(reader);
                    if (entryFile.header.MAGIC == -1) return null;

                    entryFile.times = new double[entryFile.header.entries];
                    if (!entryFile.header.entryPerCall)
                        entryFile.calls = new int[entryFile.header.entries];

                    for (int i = 0; i < entryFile.header.entries; i++)
                    {
                        entryFile.times[i] = reader.ReadDouble();
                        if (!entryFile.header.entryPerCall)
                            entryFile.calls[i] = reader.ReadInt32();
                    }

                    reader.Close();
                    reader.Dispose();
                }
            }
            catch (Exception e)
            {
                ThreadSafeLogger.ReportException(e, "Failed while reading entry file from disk.");
            }
            
            return entryFile;
        }

        public static void WriteFile(EntryFile file)
        {
            var fileName = FinalFileNameFor(file.header.methodName);
            
            try
            {
                using (var writer = new BinaryWriter(File.Open(fileName, FileMode.Create)))
                {
                    writer.Write(file.header.MAGIC);
                    writer.Write(file.header.scribingVer);
                    writer.Write(file.header.methodName);
                    writer.Write(file.header.name);
                    writer.Write(file.header.entryPerCall);
                    writer.Write(file.header.onlyEntriesWithValues);
                    writer.Write(file.header.entries);
                    writer.Write(file.header.targetEntries);

                    // interleaved is faster by profiling, (even if less cache-efficient) 
                    for (var i = 0; i < file.header.entries; i++)
                    {
                        writer.Write(file.times[i]);
                        if (!file.header.entryPerCall)
                            writer.Write(file.calls[i]);
                    }
                    
                    writer.Close();
                    writer.Dispose();
                }
            }
            catch (Exception e)
            {
                ThreadSafeLogger.ReportException(e, $"Caught an exception when writing file to disk, if the file exists on disk, it should be deleted at {fileName}");
            }
 

            changed = true;
        }

        public static FileHeader ReadHeader(FileInfo file)
        {
            return ReadHeader(new BinaryReader(file.OpenRead()));
        }

        public static FileHeader ReadHeader(BinaryReader reader)
        {
            var fileHeader = new FileHeader()
            {
                MAGIC = reader.ReadInt32(),
                scribingVer = reader.ReadInt32(),
                methodName = reader.ReadString(),
                name = reader.ReadString(),
                entryPerCall = reader.ReadBoolean(),
                onlyEntriesWithValues = reader.ReadBoolean(),
                entries = reader.ReadInt32(),
                targetEntries = reader.ReadInt32()
            };

            if (fileHeader.MAGIC == ENTRY_FILE_MAGIC) return fileHeader;
            
            ThreadSafeLogger.Error($"Loaded header has an invalid MAGIC number, this indicates disk corruption");
            return new FileHeader() { MAGIC = -1 }; // magic = -1 is an error value. 
        }
        
        
        
    }
}
