﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Analyzer.Profiling
{
    public static class Extensions
    {
        // ugly hack to get around this function showing when profiling methods which occur in multiple categories
        public static bool TryGetValue(this Dictionary<string, int> dict, string key, out int value)
        {
            return dict.TryGetValue(key, out value);
        }

        public static void InPlaceConcat<T>(this IEnumerable<T> instance, params IEnumerable<T>[] lists)
        {
            foreach (var list in lists)
                foreach (var inst in list)
                    instance.Append(inst);
        }

        public static IEnumerable<T> Concat<T>(this IEnumerable<T> instance, params IEnumerable<T>[] lists)
        {
            foreach (var list in lists)
                foreach (var inst in list)
                    yield return inst;

            foreach (var inst in instance)
                yield return inst;
        }

        public static void AdjustHorizonallyBy(this ref Rect rect, float width)
        {
            rect.x += width;
            rect.width -= width;
        }

        public static Rect RetAdjustHorizonallyBy(this Rect rect, float width)
        {
            var retRect = new Rect(rect);
            retRect.x += width;
            retRect.width -= width;

            return retRect;
        }

        public static void AdjustVerticallyBy(this ref Rect rect, float height)
        {
            rect.y += height;
            rect.height -= height;
        }

        public static void ShiftX(this ref Rect rect, float gap = 0)
        {
            rect.x = rect.xMax + gap;
        }

        public static void ShiftY(this ref Rect rect, float gap = 0)
        {
            rect.y = rect.yMax + gap;
        }

        public static Rect RetAdjustVerticallyBy(this Rect rect, float height)
        {
            var retRect = new Rect(rect);
            retRect.y += height;
            retRect.height -= height;

            return retRect;
        }
    }

}
