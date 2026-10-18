using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using static NuclearTriad.Dev.Format;

namespace NuclearTriad.Dev
{
    /// <summary>
    /// dump-volumes: every post-processing Volume in the loaded scenes, highest priority first,
    /// with each override it sets. Reads the shared profile unless the game already instantiated a
    /// copy, so dumping never creates one.
    /// </summary>
    internal static class VolumeDump
    {
        public static string Write(string outputDir, string label)
        {
            var report = new Report();
            Volume[] volumes = Resources.FindObjectsOfTypeAll<Volume>()
                .Where(v => v.gameObject.scene.IsValid())
                .OrderByDescending(v => v.priority)
                .ToArray();
            report.Line($"Volumes in loaded scenes: {volumes.Length}");

            foreach (Volume v in volumes)
            {
                report.Line("");
                report.Line($"Volume '{v.name}' at {HierarchyPath(v.transform)}");
                report.Line(1, $"enabled {v.enabled}, active {v.gameObject.activeInHierarchy}, global {v.isGlobal}, priority {v.priority}, weight {v.weight}, " +
                               $"blend distance {v.blendDistance}, layer {LayerMask.LayerToName(v.gameObject.layer)}");
                bool instantiated = v.HasInstantiatedProfile();
                VolumeProfile profile = instantiated ? v.profile : v.sharedProfile;
                report.Line(1, $"profile {Name(profile)}{(instantiated ? " (instantiated copy)" : "")}");
                if (profile == null) continue;

                foreach (VolumeComponent component in profile.components)
                {
                    report.Line(2, $"{component.GetType().Name}: active {component.active}");
                    foreach (FieldInfo field in component.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (field.GetValue(component) is VolumeParameter parameter && parameter.overrideState)
                            report.Line(3, $"{field.Name} = {Value(parameter)}");
                    }
                }
            }

            return $"{volumes.Length} volumes -> {report.Save(outputDir, label, "volumes")}";
        }

        static string Value(VolumeParameter parameter)
        {
            object value;
            try
            {
                value = parameter.GetType().GetProperty("value")?.GetValue(parameter);
            }
            catch (Exception e)
            {
                return $"<unreadable: {e.GetType().Name}>";
            }
            switch (value)
            {
                case null: return "null";
                case UnityEngine.Object o: return Name(o);
                case float f: return f.ToString("G6");
                default: return value.ToString();
            }
        }
    }
}
