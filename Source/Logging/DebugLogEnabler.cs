using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace Analyzer.Logging
{
    public static class DebugLogenabler
    {
        public static void ErrorPrefix()
        {
            if (!Settings.enableLog) return;
            
        }

        public static void ErrorPostfix()
        {
            
        }

        public static bool DevModePrefix(ref bool __result)
        {
            if (!Settings.enableLog) return true;
            __result = Prefs.data == null || Prefs.data.devMode;
            return false;
        }

        public static void DebugKeysPatch(DebugWindowsOpener __instance)
        {
            if (Prefs.DevMode || !Settings.enableLog) return;

            if (KeyBindingDefOf.Dev_ToggleDebugLog.KeyDownEvent)
            {
                __instance.ToggleLogWindow();
                Event.current.Use();
            }
            if (KeyBindingDefOf.Dev_ToggleDebugActionsMenu.KeyDownEvent)
            {
                __instance.ToggleDebugActionsMenu();
                Event.current.Use();
            }
            if (KeyBindingDefOf.Dev_ToggleDebugLogMenu.KeyDownEvent)
            {
                __instance.ToggleDebugLogMenu();
                Event.current.Use();
            }
            if (KeyBindingDefOf.Dev_ToggleDebugSettingsMenu.KeyDownEvent)
            {
                __instance.ToggleDebugSettingsMenu();
                Event.current.Use();
            }
            if (KeyBindingDefOf.Dev_ToggleDebugInspector.KeyDownEvent)
            {
                __instance.ToggleDebugInspector();
                Event.current.Use();
            }
            if (Current.ProgramState == ProgramState.Playing && KeyBindingDefOf.Dev_ToggleGodMode.KeyDownEvent)
            {
                __instance.ToggleGodMode();
                Event.current.Use();
            }
		}
    }
}