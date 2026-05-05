using System;
using System.Collections.Generic;
using System.Reflection;

namespace rancher_minimap
{
    internal enum OptionKind
    {
        Toggle,
        Range
    }

    internal sealed class OptionRow
    {
        public string Id { get; private set; } = string.Empty;
        public string Label { get; private set; } = string.Empty;
        public string Details { get; private set; } = string.Empty;
        public OptionKind Kind { get; private set; }
        public float Min { get; private set; }
        public float Max { get; private set; }
        public float Step { get; private set; }
        public Func<float> GetFloat { get; private set; } = () => 0f;
        public Action<float> SetFloat { get; private set; } = _ => { };
        public Func<bool> GetBool { get; private set; } = () => false;
        public Action<bool> SetBool { get; private set; } = _ => { };

        public static OptionRow Toggle(string label, string details, Func<bool> get, Action<bool> set)
        {
            return new OptionRow
            {
                Id = MakeId(label),
                Label = label,
                Details = details,
                Kind = OptionKind.Toggle,
                Min = 0f,
                Max = 1f,
                Step = 1f,
                GetBool = get,
                SetBool = set,
                GetFloat = () => get() ? 1f : 0f,
                SetFloat = v => set(v >= 0.5f)
            };
        }

        public static OptionRow Range(string label, string details, float min, float max, float step, Func<float> get, Action<float> set)
        {
            return new OptionRow
            {
                Id = MakeId(label),
                Label = label,
                Details = details,
                Kind = OptionKind.Range,
                Min = min,
                Max = max,
                Step = step,
                GetFloat = get,
                SetFloat = set
            };
        }

        public int CurrentIndex()
        {
            var value = GetFloat();
            return (int)Math.Round((value - Min) / Step);
        }

        public void ApplyIndex(int index)
        {
            var value = Math.Min(Max, Math.Max(Min, Min + index * Step));
            SetFloat(value);
        }

        private static string MakeId(string label) => label.ToLowerInvariant().Replace(" ", "_");
    }

    /// <summary>
    /// Binds vanilla scripted option callbacks back to MinimapSettings. It is keyed by
    /// OptionsItemDefinition.ReferenceId so cloned/constructed definitions can survive object
    /// recreation by Unity.
    /// </summary>
    internal static class ScriptedOptionBridge
    {
        private static readonly Dictionary<string, OptionRow> Rows = new Dictionary<string, OptionRow>();

        public static void Register(string referenceId, OptionRow row)
        {
            Rows[referenceId] = row;
        }

        public static bool TryGet(object definition, out OptionRow row)
        {
            row = null;
            if (definition == null)
                return false;

            var id = ReflectionTools.Call<string>(definition, "get_ReferenceId")
                     ?? ReflectionTools.GetFieldOrProperty(definition, "referenceId", "_referenceId", "ReferenceId") as string;
            return id != null && Rows.TryGetValue(id, out row);
        }
    }
}
