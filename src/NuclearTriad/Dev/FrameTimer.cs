using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BepInEx.Logging;
using UnityEngine;

namespace NuclearTriad.Dev
{
    /// <summary>
    /// timing start|stop: mean frame time and 1% lows over 10-second windows, with rig state and
    /// resolution, logged and appended to a CSV. A state change closes the current window early, so
    /// rig-on and rig-off numbers never mix. "start uncapped" lifts the game's frame cap for the run,
    /// so the numbers show headroom rather than the cap.
    /// </summary>
    internal sealed class FrameTimer
    {
        const float WindowSeconds = 10f;

        readonly ManualLogSource log;
        readonly List<float> samples = new List<float>(2048);
        string csvPath;
        string state;
        float windowStart;
        bool running;
        bool restoreCap;
        int savedFrameRate;
        int savedVSync;

        public FrameTimer(ManualLogSource log)
        {
            this.log = log;
        }

        public bool Running => running;

        public string Start(string outputDir, bool uncapped)
        {
            if (running) Stop();
            string dir = Path.Combine(outputDir, "timing");
            Directory.CreateDirectory(dir);
            csvPath = Path.Combine(dir, $"{DateTime.Now:yyyyMMdd-HHmmss}-timing-b{VersionGuard.SteamBuild ?? "unknown"}.csv");
            File.WriteAllText(csvPath, "time,state,width,height,frames,mean_ms,mean_fps,low1pct_ms,low1pct_fps,worst_ms,frame_cap,vsync" + Environment.NewLine);

            if (uncapped)
            {
                savedFrameRate = Application.targetFrameRate;
                savedVSync = QualitySettings.vSyncCount;
                Application.targetFrameRate = -1;
                QualitySettings.vSyncCount = 0;
                restoreCap = true;
            }
            running = true;
            samples.Clear();
            state = null;
            windowStart = Time.unscaledTime;
            return $"timing on{(uncapped ? ", uncapped" : $", frame cap {Application.targetFrameRate}")} -> {csvPath}";
        }

        public string Stop()
        {
            if (!running) return "timing wasn't running";
            Flush();
            running = false;
            if (restoreCap)
            {
                Application.targetFrameRate = savedFrameRate;
                QualitySettings.vSyncCount = savedVSync;
                restoreCap = false;
            }
            return $"timing off -> {csvPath}";
        }

        /// <summary>Call from Update with the current state, such as "rig-on".</summary>
        public void Tick(string currentState)
        {
            if (!running) return;
            if (state != null && currentState != state) Flush();
            state = currentState;
            samples.Add(Time.unscaledDeltaTime * 1000f);
            if (Time.unscaledTime - windowStart >= WindowSeconds) Flush();
        }

        void Flush()
        {
            if (samples.Count > 0 && state != null)
            {
                samples.Sort();
                double mean = samples.Average();
                int worstCount = Math.Max(1, samples.Count / 100);
                double low = samples.Skip(samples.Count - worstCount).Average();
                double worst = samples[samples.Count - 1];
                string row = string.Join(",", new[]
                {
                    DateTime.Now.ToString("o"), state, Screen.width.ToString(), Screen.height.ToString(), samples.Count.ToString(),
                    F(mean), F(1000 / mean), F(low), F(1000 / low), F(worst), Application.targetFrameRate.ToString(), QualitySettings.vSyncCount.ToString(),
                });
                File.AppendAllText(csvPath, row + Environment.NewLine);
                log.LogInfo($"[timing] {state} {Screen.width}x{Screen.height}: {samples.Count} frames, mean {mean:F2} ms ({1000 / mean:F1} fps), " +
                            $"1% low {low:F2} ms ({1000 / low:F1} fps), worst {worst:F2} ms");
            }
            samples.Clear();
            windowStart = Time.unscaledTime;
        }

        static string F(double v) => v.ToString("F3", CultureInfo.InvariantCulture);
    }
}
