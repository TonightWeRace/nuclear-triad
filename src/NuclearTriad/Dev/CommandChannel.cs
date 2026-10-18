using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using UnityEngine;

namespace NuclearTriad.Dev
{
    /// <summary>
    /// Lets the dev loop drive the plugin while the pilot keeps the controls. Once a second it claims
    /// BepInEx/config/nuclear-triad.cmd, runs each line as a command, and appends one result line
    /// per command to nuclear-triad.out beside it, as well as to the BepInEx log.
    /// </summary>
    internal sealed class CommandChannel
    {
        const float PollInterval = 1f;

        readonly ManualLogSource log;
        readonly string commandPath;
        readonly string claimedPath;
        readonly string resultPath;
        readonly Dictionary<string, Func<string[], string>> handlers =
            new Dictionary<string, Func<string[], string>>(StringComparer.OrdinalIgnoreCase);
        float nextPoll;

        public CommandChannel(ManualLogSource log, string configDir)
        {
            this.log = log;
            commandPath = Path.Combine(configDir, "nuclear-triad.cmd");
            claimedPath = commandPath + ".running";
            resultPath = Path.Combine(configDir, "nuclear-triad.out");
        }

        public IEnumerable<string> Commands => handlers.Keys;

        public void Register(string name, Func<string[], string> handler) => handlers[name] = handler;

        public void Poll()
        {
            if (Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + PollInterval;
            if (!File.Exists(commandPath)) return;

            string[] lines;
            try
            {
                // Renaming claims the file, so a command written while this batch runs waits for the next poll.
                if (File.Exists(claimedPath)) File.Delete(claimedPath);
                File.Move(commandPath, claimedPath);
                lines = File.ReadAllLines(claimedPath);
                File.Delete(claimedPath);
            }
            catch (IOException e)
            {
                log.LogWarning($"Command file busy; retrying next poll ({e.Message})");
                return;
            }

            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length > 0 && !line.StartsWith("#")) Run(line);
            }
        }

        void Run(string line)
        {
            string[] parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            string[] args = new string[parts.Length - 1];
            Array.Copy(parts, 1, args, 0, args.Length);

            bool ok;
            string result;
            if (!handlers.TryGetValue(parts[0], out Func<string[], string> handler))
            {
                ok = false;
                result = $"unknown command; known: {string.Join(", ", handlers.Keys)}";
            }
            else
            {
                try
                {
                    result = handler(args) ?? "";
                    ok = true;
                }
                catch (Exception e)
                {
                    ok = false;
                    result = e.ToString();
                }
            }

            log.Log(ok ? LogLevel.Info : LogLevel.Error, $"[cmd] {line} -> {result}");
            try
            {
                string status = ok ? "ok" : "error";
                File.AppendAllText(resultPath,
                    $"{DateTime.UtcNow:o}\t{Time.frameCount}\t{status}\t{line}\t{result.Replace("\r", "").Replace("\n", " | ")}{Environment.NewLine}");
            }
            catch (IOException e)
            {
                log.LogWarning($"Couldn't append to {resultPath} ({e.Message})");
            }
        }
    }
}
