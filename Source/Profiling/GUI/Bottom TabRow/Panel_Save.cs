using System;
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
        public static void Draw(Rect r)
        {
            try
            {

            }
            catch (Exception)
            {
                
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
                Widgets.Label(r.LeftHalf(), "Status: Idle");
                Widgets.Label(r.RightHalf(), "Previous Saved Entries: 0");
                
                throw new Exception();
            }
            catch (Exception)
            {
                
            }
        }
        
        public static void DrawColumn(Rect r)
        {
            try
            {
                if (Widgets.ButtonText(r.TopPartPixels(20), "Print Directory Information"))
                {
                    ThreadSafeLogger.Message(FileUtility.GetDirectory().FullName);
                }
                r.AdjustVerticallyBy(20f);

                throw new Exception();
            }
            catch (Exception)
            {
                
            }
        }
    }
}