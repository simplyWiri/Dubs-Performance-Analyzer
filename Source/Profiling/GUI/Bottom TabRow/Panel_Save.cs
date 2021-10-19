using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;

namespace Analyzer.Profiling
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public class HotSwappableAttribute : Attribute
    {
    }
    
    [HotSwappable]
    public static class Panel_Save
    {
        private static EntryFile file = null;
        private static uint prevIdx = 0;
        private static EntryFile loadedFile = null;

        private static FileHeader curHeader = new FileHeader()
        {
            MAGIC = FileUtility.ENTRY_FILE_MAGIC, // used to verify the file has not been corrupted on disk somehow.
            scribingVer = 1,
            targetEntries = Profiler.RECORDS_HELD,
            name = " " // default to an empty name
        };

        private static string GetStatus()
        {
            if (file == null) return "Idle";

            var ents = file.header.entries;
            var target = file.header.targetEntries;
            
            return ents == target 
                ? "Completed Collection" 
                : $"Collecting Entries {ents}/{target} ({(ents / (float)target) * 100:F2}%)";
        }

        private static void UpdateFile()
        {
            if (file.header.entries >= file.header.targetEntries) return;
            
            var prof = GUIController.CurrentProfiler;

            // todo: can maybe memcopy to improve the performance. 
            var idx = file.header.entries;

            while (prevIdx != prof.currentIndex)
            {
                if (file.header.entryPerCall)
                {
                    for (int i = 0; i < prof.hits[idx]; i++)
                    {
                        file.times[idx] = (float)prof.times[prevIdx] / (float) prof.hits[prevIdx];
                        idx++;
                        file.header.entries = idx;
                        if (idx >= file.header.targetEntries) return;
                    }
                }
                else
                {
                    if (!file.header.onlyEntriesWithValues || prof.times[prevIdx] > 0)
                    {
                        file.times[idx] = (float)prof.times[prevIdx];
                        file.calls[idx] = prof.hits[prevIdx];

                        idx++;
                        file.header.entries = idx;
                        if (idx >= file.header.targetEntries) return;
                    }
                }
                
                prevIdx++;
                prevIdx %= Profiler.RECORDS_HELD;
            }

            prevIdx = prof.currentIndex;
        }

        public static void Draw(Rect r)
        {
            if (file != null)
            {
                UpdateFile();
            }

            var colWidth = Mathf.Max(300, r.width / 3);
            var columnRect = r.RightPartPixels(colWidth);
            r.width -= colWidth;
            
            DrawColumn(columnRect);
            
            var top = r.TopPartPixels(20f);
            r.AdjustVerticallyBy(20f);
            
            DrawTopRow(top);
        }

        public static void DrawTopRow(Rect r)
        {
            try
            {
                Widgets.Label(r.LeftHalf(), "Status: " + GetStatus());
                Widgets.Label(r.RightHalf(), "Previous Saved Entries: " + FileUtility.PreviousEntriesFor(GUIController.CurrentProfiler.label).Count());
                
                throw new Exception();
            }
            catch (Exception)
            {
                
            }
        }

        public static void CheckValidHeader()
        {
            if (curHeader.MAGIC != FileUtility.ENTRY_FILE_MAGIC)
            {
                ThreadSafeLogger.Error($"headers magic value was {curHeader.MAGIC} not the expected {FileUtility.ENTRY_FILE_MAGIC} - correcting");
                curHeader.MAGIC = FileUtility.ENTRY_FILE_MAGIC;
            }

            if (curHeader.entries > 0)
            {
                ThreadSafeLogger.Error($"headers entries value was not 0, resetting");
                curHeader.entries = 0;
            }

            if (curHeader.name == null)
            {
                ThreadSafeLogger.Error("headers name value was null, should have been ' ', resetting");
                curHeader.name = " ";
            }
        }
        
        
        public static void DrawColumn(Rect r)
        {
            var color = GUI.color;
            GUI.color = color * new Color(1f, 1f, 1f, 0.4f);
            Widgets.DrawLineVertical(r.x, r.y, r.height);
            GUI.color = color;

            r = r.ContractedBy(2);
            
            Listing_Standard s = new Listing_Standard();
            s.Begin(r);
            
            DubGUI.Heading(s, "Options");
            s.GapLine(2f);
            s.CheckboxLabeled("Only Entries with Values", ref curHeader.onlyEntriesWithValues);
            s.CheckboxLabeled("One Entry per Call", ref curHeader.entryPerCall);
            float val = curHeader.targetEntries;
            DubGUI.LabeledSliderFloat(s, $"Target Entries : {curHeader.targetEntries}", ref val, 50, 50_000);
            curHeader.targetEntries = Mathf.RoundToInt(val);

            if (s.ButtonText("Load Settings From"))
            {
                var options = new List<FloatMenuOption>();
                foreach (var entry in FileUtility.PreviousEntriesFor(GUIController.CurrentProfiler.label))
                {
                    var header = FileUtility.ReadHeader(entry);
                    options.Add(new FloatMenuOption(header.name == "" ? entry.Name : header.name, () => curHeader = header));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
            
            s.GapLine(12f);

            if (s.ButtonText("Start Collecting Entries"))
            {
                curHeader.methodName = GUIController.CurrentProfiler.label;
                
                CheckValidHeader();

                file = new EntryFile()
                {
                    header = curHeader,
                    times = new float[curHeader.targetEntries],
                };
                if (!curHeader.entryPerCall)
                    file.calls = new int[curHeader.targetEntries];
            }

            if (s.ButtonText("Save"))
            {
                FileUtility.WriteFile(null);
            }

            if (s.ButtonText("Load"))
            {
                var options = new List<FloatMenuOption>();
                foreach (var entry in FileUtility.PreviousEntriesFor(GUIController.CurrentProfiler.label))
                {
                    var header = FileUtility.ReadHeader(entry);
                    options.Add(new FloatMenuOption(header.name == "" ? entry.Name : header.name, () => loadedFile = FileUtility.ReadFile(entry)));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }

            var dbgRect = s.GetRect(Text.LineHeight * 3);
            
            if (Widgets.ButtonText(dbgRect.LeftHalf(),"Print directory path"))
            {
                ThreadSafeLogger.Message(FileUtility.GetDirectory().FullName);
            }
            if (Widgets.ButtonText(dbgRect.RightHalf(),"Print directory files matching current profiler"))
            {
                foreach(var f in FileUtility.PreviousEntriesFor(GUIController.CurrentProfiler.label))
                    ThreadSafeLogger.Message(f.FullName);
            }
            
            s.End();
        }
    }
}