using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace rancher_minimap
{
    /// <summary>
    /// Reflection-only diagnostic probes. Runtime interop helpers should not own dump formatting.
    /// </summary>
    internal static class ReflectionDiagnostics
    {
        public static void DumpObjectShape(string key, object target, int maxMembers = 40)
        {
            if (target == null)
            {
                Log.Info($"shape:{key}: null");
                return;
            }

            var type = target.GetType();
            var parts = new List<string>();
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Take(maxMembers))
            {
                object value = null;
                try { value = field.GetValue(target); }
                catch { }
                parts.Add($"field {field.Name}:{field.FieldType.Name}={(value == null ? "null" : value.GetType().Name)}");
            }

            Log.Info($"shape:{key}: {type.FullName}\n  " + string.Join("\n  ", parts));
        }
    }
}
