using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace NuclearTriad
{
    /// <summary>
    /// Nuclear Option removes BepInEx's manager object as it starts unless BepInEx hides it, which
    /// it does only with [Chainloader] HideManagerGameObject = true in BepInEx.cfg, off by default.
    /// Every plugin is a component on that object, so without it they load (Awake runs) but never
    /// update, and F1 never opens. BepInEx makes the object and loads every plugin onto it in one
    /// go, before the game's first scene, so a plugin's Awake still comes first. Hiding the object
    /// there does what the setting does, a moment later, so this start works too. The setting
    /// itself goes on through BepInEx's own config, which writes its file as it does at every
    /// start, so from the next start BepInEx hides the object itself.
    /// </summary>
    internal static class BepInExSetup
    {
        const string Section = "Chainloader", Key = "HideManagerGameObject";

        /// <param name="manager">The object the plugins sit on: Nuclear Triad's own GameObject.</param>
        /// <param name="fix">Setup.FixBepInExManager: off leaves BepInEx as it is.</param>
        public static void Apply(ManualLogSource log, GameObject manager, bool fix)
        {
            ConfigEntry<bool> setting = Setting(out string notFound);
            bool hidden = (manager.hideFlags & HideFlags.HideAndDontSave) == HideFlags.HideAndDontSave;
            bool settingOn = setting != null && setting.Value;
            if (hidden && (settingOn || setting == null)) return;

            string file = $@"BepInEx\config\{System.IO.Path.GetFileName(Paths.BepInExConfigPath)}";
            const string why = "Nuclear Option removes BepInEx's manager object as it starts unless it's hidden, and every plugin with it.";
            if (!fix)
            {
                // Hidden already, this start works; the setting is BepInEx's business.
                if (!hidden)
                    log.LogWarning($"BepInEx's [{Section}] {Key} is off, and Setup.FixBepInExManager is off, so Nuclear Triad leaves it. {why} " +
                                   $"Set {Key} = true in {file}.");
                return;
            }

            if (!hidden) manager.hideFlags = HideFlags.HideAndDontSave;
            string failed = setting == null ? notFound : null;
            if (setting != null && !settingOn)
            {
                try
                {
                    setting.Value = true;
                    if (!setting.ConfigFile.SaveOnConfigSet) setting.ConfigFile.Save();
                }
                catch (Exception e)
                {
                    failed = e.Message;
                }
            }

            string state = settingOn ? $"BepInEx's [{Section}] {Key} is on, but its manager object wasn't hidden." : $"BepInEx's [{Section}] {Key} was off.";
            if (failed != null)
                log.LogWarning($"{state} {why} " +
                               (hidden ? "The object was already hidden, so this start works, but Nuclear Triad "
                                       : "Nuclear Triad hid the object for this start, but ") +
                               $"couldn't turn the setting on ({failed}), so it hides the object at every start instead. To set it yourself, " +
                               $"set {Key} = true in {file}.");
            else if (settingOn)
                log.LogWarning($"{state} {why} Nuclear Triad hid it.");
            else
                log.LogWarning($"{state} {why} " +
                               (hidden ? "The object was already hidden, so this start works; Nuclear Triad turned the setting on in "
                                       : "Nuclear Triad hid the object for this start, and turned the setting on in ") +
                               $"{file} for the next. Setting it back to false there undoes that; Setup.FixBepInExManager = false stops " +
                               "Nuclear Triad doing it.");
        }

        // BepInEx keeps its own config, and so this setting, internal. Its section and key are the
        // names people edit, so they hold across BepInEx 5 builds.
        static ConfigEntry<bool> Setting(out string notFound)
        {
            notFound = null;
            try
            {
                PropertyInfo core = typeof(ConfigFile).GetProperty("CoreConfig", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (!(core?.GetValue(null, null) is ConfigFile config))
                {
                    notFound = "no BepInEx core config";
                    return null;
                }
                if (config.TryGetEntry(Section, Key, out ConfigEntry<bool> entry)) return entry;
                notFound = $"no [{Section}] {Key} in BepInEx's core config";
            }
            catch (Exception e)
            {
                notFound = e.Message;
            }
            return null;
        }
    }
}
