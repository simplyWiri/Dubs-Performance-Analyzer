using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Analyzer.Profiling
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public class HotSwappableAttribute : Attribute
    {
    }

    class Row
    {
        public string name;
        public Func<LogStats, double> getDouble = null;
        public Func<LogStats, int> getInt = null;
        public bool shouldColour;
        
        public Row(string n, Func<LogStats, int> gi, bool sC = true)
        {
            name = n;
            getInt = gi;
            shouldColour = sC;
        }

        public Row(string n, Func<LogStats, double> gd, bool sC = true)
        {
            name = n;
            getDouble = gd;
            shouldColour = sC;
        }

        public bool IsInt() => getInt is not null;

        public int Int(LogStats stats) => getInt(stats);
        public double Double(LogStats stats) => getDouble(stats);

    }
    
    
    
    [HotSwappable]
    public class Panel_Save : IBottomTabRow
    {
        private EntryFile file = null;
        private uint prevIdx = 0;

        private EntryFile lhsEntry = null;
        private EntryFile rhsEntry = null;

        private LogStats lhsStats = null;
        private LogStats rhsStats = null;

        private static List<Row> rows = new List<Row>()
        {
            new Row("Entries", (stats) => stats.Entries, false),
            new Row("Total Calls", (stats) => stats.TotalCalls),
            new Row("Total Time", (stats) => stats.TotalTime),
            new Row("Avg Time/Call", (stats) => stats.MeanTimePerCall),
            new Row("Avg Calls/Update", (stats) => stats.MeanCallsPerUpdateCycle),
            new Row("Avg Time/Update", (stats) => stats.MeanTimePerUpdateCycle),
            new Row("Median Calls", (stats) => stats.MedianCalls),
            new Row("Median Time", (stats) => stats.MedianTime),
            new Row("Max Time", (stats) => stats.HighestTime),
            new Row("Max Calls/Update", (stats) => stats.HighestCalls),
        };

        private FileHeader curHeader = new FileHeader()
        {
            MAGIC = FileUtility.ENTRY_FILE_MAGIC, // used to verify the file has not been corrupted on disk somehow.
            scribingVer = FileUtility.SCRIBE_FILE_VER,
            targetEntries = Profiler.RECORDS_HELD,
            name = " " // default to an empty name
        };
        
        public void ResetState(GeneralInformation? _)
        {
            file = null;
            prevIdx = 0;
            curHeader = new FileHeader()
            {
                MAGIC = FileUtility.ENTRY_FILE_MAGIC, // used to verify the file has not been corrupted on disk somehow.
                scribingVer = FileUtility.SCRIBE_FILE_VER,
                targetEntries = Profiler.RECORDS_HELD,
                name = " " // default to an empty name
            };
            
            lhsEntry = null;
            rhsEntry = null;
            lhsStats = null;
            rhsStats = null;
        }
        
        private string GetStatus()
        {
            if (file == null) return "Idle";

            var ents = file.header.entries;
            var target = file.header.targetEntries;
            
            return ents == target 
                ? "Completed Collection" 
                : $"Collecting Entries {ents}/{target} ({(ents / (float)target) * 100:F2}%)";
        }

        private void UpdateFile()
        {
            if (file.header.entries >= file.header.targetEntries) return;
            
            var prof = GUIController.CurrentProfiler;
            var idx = file.header.entries;
            
            while (prevIdx != prof.currentIndex)
            {
                if (file.header.entries >= file.header.targetEntries) return;
                
                if (file.header.entryPerCall)
                {
                    var len = prof.hits[prevIdx];
                    len = Mathf.Min(file.header.targetEntries - file.header.entries, len);

                    if (len == 0) continue;
                    Array.Fill(file.times, prof.times[prevIdx] / (double)prof.hits[prevIdx], idx, len);

                    idx += len;
                    file.header.entries += len;
                } else if (file.header.onlyEntriesWithValues && prof.hits[prevIdx] > 0)
                {
                    file.times[idx] = prof.times[prevIdx];
                    file.calls[idx] = prof.hits[prevIdx];

                    idx++;
                }
                else
                {
                    var len = (int)((prof.currentIndex < prevIdx)
                        ? prof.currentIndex - prevIdx
                        : Profiler.RECORDS_HELD - prevIdx);
                    
                    len = Math.Min(file.header.targetEntries - file.header.entries, len);
                    if (len == 0) continue;

                    Array.Copy(prof.times, (int)prevIdx, file.times, idx, len);
                    Array.Copy(prof.hits, (int)prevIdx, file.calls, idx, len);

                    idx += len;
                    prevIdx += (uint)len;
                }

                prevIdx++;
                prevIdx %= Profiler.RECORDS_HELD;
                file.header.entries = idx;
            }
        }

        public void Draw(Rect r, GeneralInformation? info)
        {
            if (info == null) return;

            if (file != null)
                UpdateFile();

            var colWidth = Mathf.Max(300, r.width / 3);
            var columnRect = r.RightPartPixels(colWidth);
            r.width -= colWidth;
            
            DrawColumn(columnRect);
            
            var top = r.TopPartPixels(20f);
            r.AdjustVerticallyBy(20f);
            
            DrawTopRow(top);

            if (FileUtility.PreviousEntriesFor(GUIController.CurrentProfiler.label).Any())
            {
                DrawComparison(r);
            }
        }

        private void DrawComparison(Rect r)
        {
            //              [ Left File ]       [ Right File ]     [ Delta ]
            // Calls Mean       25000               23000         -2000 ( -8% )
            // Time Mean       0.037ms             0.031ms        -0.006ms ( - 16% )
            var topPart = r.TopPartPixels(50f);
            r.AdjustVerticallyBy(50f);

            var buttonsRect = topPart.LeftPartPixels(topPart.width - ( "Compare".GetWidthCached() * 2)); 
            var first = buttonsRect.LeftHalf();
            var second = buttonsRect.RightHalf();
            var third = topPart.RightPartPixels("Compare".GetWidthCached() * 1.6f);

            void SelectEntry(Rect rect, bool left)
            {
                var loc = left ? "Left" : "Right";
                var name = $"{loc} File: {(left ? lhsEntry : rhsEntry)?.header.Name ?? "not selected"}";
                if (Widgets.ButtonText(rect.ContractedBy(4f), name))
                {
                    var options = new List<FloatMenuOption>();
                    foreach (var entry in FileUtility.PreviousEntriesFor(GUIController.CurrentProfiler.label))
                    {
                        var header = FileUtility.ReadHeader(entry);

                        var act = left 
                            ? (Action) ( () => lhsEntry = FileUtility.ReadFile(entry) ) 
                            : (Action) ( () => rhsEntry = FileUtility.ReadFile(entry) );
                        
                        options.Add(new FloatMenuOption(header.Name, act)); 
                    }

                    if (file != null && file.header.entries == file.header.targetEntries)
                    {
                        var act = left 
                            ? (Action) ( () => lhsEntry = file ) 
                            : (Action) ( () => rhsEntry = file );
                        options.Add(new FloatMenuOption("Current", act));
                    }
                    Find.WindowStack.Add(new FloatMenu(options));
                }
            }
            
            SelectEntry(first, true);
            SelectEntry(second, false);
            if (Widgets.ButtonText(third.ContractedBy(4f), "Compare"))
            {
                if (lhsEntry is null || rhsEntry is null)
                {
                    ThreadSafeLogger.Error("One of the comparisons was null, waiting for valid inputs");
                    return;
                }
                
                lhsStats = LogStats.GatherStats(lhsEntry.calls, lhsEntry.times, lhsEntry.header.entries);
                rhsStats = LogStats.GatherStats(rhsEntry.calls, rhsEntry.times, rhsEntry.header.entries);
            }

            if (lhsStats is null || rhsStats is null) return;

            var anchor = Text.Anchor;
            var colours = new Color[] { Color.red, GUI.color, Color.green };

            var headerRect = r.TopPartPixels(30f);
            r.AdjustVerticallyBy(30f);
            Text.Anchor = TextAnchor.MiddleCenter;
            
            DubGUI.Heading(headerRect.LeftHalf().LeftHalf(), "Row Value");
            DubGUI.Heading(headerRect.LeftHalf().RightHalf(), "Left Stats");
            DubGUI.Heading(headerRect.RightHalf().LeftHalf(), "Right Stats");
            DubGUI.Heading(headerRect.RightHalf().RightHalf(), "Delta");

            GUI.color *= new Color(1f, 1f, 1f, 0.4f);
            Widgets.DrawLineHorizontal(r.x, r.y, r.width);
            GUI.color = Color.white;

            Text.Anchor = anchor;
                
            for (int i = 0; i < 4; i++)
            {
                if (i > 0)
                {
                    Text.Anchor = TextAnchor.MiddleCenter;
                }
                
                var column = new Rect(r.x + i * (r.width / 4.0f), r.y, r.width / 4.0f, r.height);

                var rect = column.TopPartPixels(Text.LineHeight + 4f);
                foreach (var row in rows)
                {
                    switch (i)
                    {
                        case 0 : Widgets.Label(rect, "  " + row.name); break;
                        case 1 : Widgets.Label(rect, row.IsInt() ? row.Int(lhsStats).ToString() : $"{row.Double(lhsStats):F5}"); break;
                        case 2 : Widgets.Label(rect, row.IsInt() ? row.Int(rhsStats).ToString() : $"{row.Double(rhsStats):F5}"); break;
                        case 3 :
                            
                            var sb = new StringBuilder();
                            double dP = 0;
                            if (row.IsInt())
                            {
                                var delta = row.Int(rhsStats) - row.Int(lhsStats);
                                var sign = (delta > 0) ? "+" : "";
                                dP = (delta / (double)row.Int(lhsStats)) * 100;

                                sb.Append($"{sign}{delta} ( {sign}{dP:F2}% )");
                            }
                            else
                            {                                
                                var delta = row.Double(rhsStats) - row.Double(lhsStats);
                                var sign = (delta > 0) ? "+" : "";
                                dP = (delta / row.Double(lhsStats)) * 100;

                                sb.Append($"{sign}{delta:F5} ( {sign}{dP:F2}% )");
                            }
                            
                            var color = dP switch
                            {
                                < -2.5 => colours[2],
                                > 2.5 => colours[0],
                                _ => colours[1],
                            };
                            
                            GUI.color = color;
                            Widgets.Label(rect, sb.ToString());
                            GUI.color = colours[1];

                            break;
                    }
                    
                    column.AdjustVerticallyBy(Text.LineHeight + 4f);
                    rect = column.TopPartPixels(Text.LineHeight + 4f);
                }
            }
            Text.Anchor = anchor;
        }

        public void DrawTopRow(Rect r)
        {
            var statusString = "Status: " + GetStatus();
            Widgets.Label(r.LeftPartPixels(statusString.GetWidthCached()), statusString);
            var previousEntriesString = "Previous Saved Entries: " + FileUtility.PreviousEntriesFor(GUIController.CurrentProfiler.label).Count();
            Widgets.Label(r.RightPartPixels(previousEntriesString.GetWidthCached()), previousEntriesString);
        }

        public void CheckValidHeader()
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
        
        
        public void DrawColumn(Rect r)
        {
            var color = GUI.color;
            GUI.color = color * new Color(1f, 1f, 1f, 0.4f);
            Widgets.DrawLineVertical(r.x, r.y, r.height);
            GUI.color = color;

            r = r.ContractedBy(2);
            
            var s = new Listing_Standard();
            s.Begin(r);
            
            DubGUI.Heading(s, "Options");
            s.GapLine(2f);
            s.CheckboxLabeled("Only Entries with Values", ref curHeader.onlyEntriesWithValues);
            s.CheckboxLabeled("One Entry per Call", ref curHeader.entryPerCall);
            float val = curHeader.targetEntries;
            DubGUI.LabeledSliderFloat(s, $"Target Entries", ref val, 50, 50_000);
            curHeader.targetEntries = Mathf.RoundToInt(val);
            DubGUI.InputField(s.GetRect(30), "Custom Name", ref curHeader.name, ShowName: true);

            if (s.ButtonText("Copy Settings From"))
            {
                var options = new List<FloatMenuOption>();
                foreach (var entry in FileUtility.PreviousEntriesFor(GUIController.CurrentProfiler.label))
                {
                    var header = FileUtility.ReadHeader(entry);
                    ThreadSafeLogger.Message($"{header.methodName} `{header.name}`");
                    options.Add(new FloatMenuOption(header.Name, () => curHeader = header));
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
                    times = new double[curHeader.targetEntries],
                };
                if (!curHeader.entryPerCall)
                    file.calls = new int[curHeader.targetEntries];
            }

            if (s.ButtonText("Save"))
            {
                FileUtility.WriteFile(file);
                file = null;
                curHeader.entries = 0;
                curHeader.methodName = null;
                curHeader.name = null;
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