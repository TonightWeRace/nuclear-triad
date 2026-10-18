using System;
using System.IO;
using System.Text.RegularExpressions;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.Rendering;

namespace NuclearTriad.Dev
{
    /// <summary>
    /// Logs the game, Unity and Steam build at startup, and warns when they differ from the last
    /// build we verified the hooks against.
    /// </summary>
    internal static class VersionGuard
    {
        // Update together with docs/game-internals.md after verifying a new build.
        const string VerifiedSteamBuild = "24724372";
        const string VerifiedBuildHash = "cb745d6c44f1";
        const string VerifiedUnity = "2022.3.62f2";

        public static string SteamBuild { get; private set; }
        public static string BuildHash { get; private set; }

        public static void LogAndCheck(ManualLogSource log, string gameRoot)
        {
            SteamBuild = ReadSteamBuildId(gameRoot);
            BuildHash = ReadBuildHash(gameRoot);
            log.LogInfo(Describe());

            // Verified on Direct3D 11. Direct3D 12 had a first look on 2026-10-16 and looked right;
            // anything else is untested.
            GraphicsDeviceType api = SystemInfo.graphicsDeviceType;
            if (api == GraphicsDeviceType.Direct3D12)
                log.LogInfo("Direct3D 12 has had only a first look; Direct3D 11 is the verified API.");
            else if (api != GraphicsDeviceType.Direct3D11)
                log.LogWarning($"{api} is untested with Nuclear Triad; Direct3D 11 is the verified API, so choose it at launch.");

            if (SteamBuild != VerifiedSteamBuild || BuildHash != VerifiedBuildHash || Application.unityVersion != VerifiedUnity)
            {
                log.LogWarning(
                    $"Unverified game build; last verified was Steam build {VerifiedSteamBuild}, build hash {VerifiedBuildHash}, Unity {VerifiedUnity}. " +
                    @"If patches misbehave after an update, delete BepInEx\cache\harmony_interop_cache.dat.");
            }
        }

        public static string Describe() =>
            $"Game {Application.version}, Unity {Application.unityVersion}, Steam build {SteamBuild ?? "unknown"}, build hash {BuildHash ?? "unknown"}, " +
            SystemInfo.graphicsDeviceVersion;

        // The game sits in steamapps\common\<installdir>, and its manifest two levels up in steamapps.
        static string ReadSteamBuildId(string gameRoot)
        {
            try
            {
                string steamapps = Path.GetDirectoryName(Path.GetDirectoryName(gameRoot.TrimEnd('\\', '/')));
                string manifest = Path.Combine(steamapps, "appmanifest_2168680.acf");
                if (!File.Exists(manifest)) return null;
                Match match = Regex.Match(File.ReadAllText(manifest), "\"buildid\"\\s+\"(\\d+)\"");
                return match.Success ? match.Groups[1].Value : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        static string ReadBuildHash(string gameRoot)
        {
            try
            {
                string path = Path.Combine(gameRoot, "build-hash.txt");
                return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
