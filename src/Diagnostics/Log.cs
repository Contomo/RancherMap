using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace rancher_minimap
{
    /// <summary>
    /// Logging facade. Warnings/errors are production-visible. Info/every-frame probes are diagnostics-only.
    /// </summary>
    internal static partial class Log
    {
#if RMM_DIAGNOSTICS
        private static readonly Dictionary<string, float> LastLog = new Dictionary<string, float>();
        private static bool _diagnosticsEnabled;

        public static void ConfigureDiagnostics(bool enabled) => _diagnosticsEnabled = enabled;

        public static void Info(string message)
        {
            if (_diagnosticsEnabled)
                MelonLogger.Msg(message);
        }

        public static void Info(string id, string message)
        {
            if (_diagnosticsEnabled)
                MelonLogger.Msg($"[{id}] {message}");
        }

        public static void Debug(string id, string message) => Info(id, message);

        public static void Every(string key, float seconds, string message)
        {
            if (!_diagnosticsEnabled)
                return;

            var now = Time.realtimeSinceStartup;
            if (LastLog.TryGetValue(key, out var last) && now - last < seconds)
                return;

            LastLog[key] = now;
            MelonLogger.Msg(message);
        }
#else
        public static void ConfigureDiagnostics(bool enabled) { }
        public static void Info(string message) { }
        public static void Info(string id, string message) { }
        public static void Debug(string id, string message) { }
        public static void Every(string key, float seconds, string message) { }
#endif

        public static void Warn(string message) => MelonLogger.Warning(message);
        public static void Warn(string id, string message) => MelonLogger.Warning($"[{id}] {message}");
        public static void Error(string message) => MelonLogger.Error(message);
        public static void Error(string id, string message) => MelonLogger.Error($"[{id}] {message}");

        public static void Exception(string context, Exception ex)
        {
            MelonLogger.Error($"{context}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
    }
}
