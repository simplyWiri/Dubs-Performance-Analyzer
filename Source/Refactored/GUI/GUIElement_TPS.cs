﻿using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace Analyzer
{
    [HarmonyPatch(typeof(GlobalControlsUtility), nameof(GlobalControlsUtility.DoTimespeedControls))]
    public static class GUIElement_TPS
    {
        private static DateTime prevTime;
        private static int prevTicks;
        private static int tpsActual = 0;
        private static int prevFrames;
        private static int fpsActual = 0;

        [HarmonyPrefix]
        public static void Prefix(float leftX, float width, ref float curBaseY)
        {

            float trm = Find.TickManager.TickRateMultiplier;
            int tpsTarget = (int)Math.Round((trm == 0f) ? 0f : (60f * trm));

            if (prevTicks == -1)
            {
                prevTicks = GenTicks.TicksAbs;
                prevTime = DateTime.Now;
            }
            else
            {
                DateTime CurrTime = DateTime.Now;
                if (CurrTime.Second != prevTime.Second)
                {
                    prevTime = CurrTime;
                    tpsActual = GenTicks.TicksAbs - prevTicks;
                    prevTicks = GenTicks.TicksAbs;
                    fpsActual = prevFrames;
                    prevFrames = 0;
                }
            }
            prevFrames++;

            Rect rect = new Rect(leftX - 20f, curBaseY - 26f, width + 20f - 7f, 26f);
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(rect, "TPS: " + tpsActual.ToString() + "(" + tpsTarget.ToString() + ")");
            rect.y -= 26f;
            Widgets.Label(rect, "FPS: " + fpsActual.ToString());
            curBaseY -= 52f;
            Text.Anchor = TextAnchor.UpperLeft;
        }
    }
}
