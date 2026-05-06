using System;
using System.Collections.Generic;
using MelonLoader;

namespace rancher_minimap
{
    internal enum MinimapMapShape
    {
        Square = 0,
        Circle = 1
    }

    internal interface IMinimapOption
    {
        string Id { get; }
        string Label { get; }
        string Description { get; }
        bool ShowInMenu { get; }
        int ChoiceCount { get; }
        string ChoiceLabel(int index);
        int CurrentIndex();
        void ApplyIndex(int index);
    }

    internal readonly struct MinimapOptionChoice<TValue>
    {
        public readonly TValue Value;
        public readonly string Label;

        public MinimapOptionChoice(TValue value, string label)
        {
            Value = value;
            Label = label;
        }
    }

    internal sealed class MinimapOption<TStored, TValue> : IMinimapOption
    {
        private readonly MelonPreferences_Entry<TStored> _entry;
        private readonly Func<TStored, TValue> _read;
        private readonly Func<TValue, TStored> _write;
        private readonly Func<TValue, TValue> _normalize;
        private readonly Func<TValue, int> _indexOf;
        private readonly Action _markDirty;
        private readonly MinimapOptionChoice<TValue>[] _choices;

        public string Id { get; }
        public string Label { get; }
        public string Description { get; }
        public bool ShowInMenu { get; }
        public int ChoiceCount => _choices.Length;

        public TValue Value
        {
            get => _normalize(_read(_entry.Value));
            set
            {
                var stored = _write(_normalize(value));
                if (EqualityComparer<TStored>.Default.Equals(_entry.Value, stored))
                    return;

                _entry.Value = stored;
                _markDirty();
            }
        }

        public MinimapOption(
            MelonPreferences_Category category,
            string id,
            string label,
            string description,
            TStored defaultStoredValue,
            MinimapOptionChoice<TValue>[] choices,
            Action markDirty,
            bool showInMenu = true,
            Func<TStored, TValue> read = null,
            Func<TValue, TStored> write = null,
            Func<TValue, TValue> normalize = null,
            Func<TValue, int> indexOf = null)
        {
            if (category == null)
                throw new ArgumentNullException(nameof(category));
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Option id is required.", nameof(id));
            if (choices == null)
                throw new ArgumentNullException(nameof(choices));
            if (markDirty == null)
                throw new ArgumentNullException(nameof(markDirty));

            Id = id;
            Label = label ?? id;
            Description = description ?? string.Empty;
            ShowInMenu = showInMenu;
            _choices = choices;
            _markDirty = markDirty;
            _read = read ?? (v => (TValue)(object)v);
            _write = write ?? (v => (TStored)(object)v);
            _normalize = normalize ?? (v => v);
            _indexOf = indexOf;
            _entry = category.CreateEntry(id, defaultStoredValue, Label);
        }

        public string ChoiceLabel(int index)
        {
            if (_choices.Length == 0)
                return string.Empty;

            return _choices[ClampIndex(index)].Label;
        }

        public int CurrentIndex()
        {
            if (_choices.Length == 0)
                return 0;

            if (_indexOf != null)
                return ClampIndex(_indexOf(Value));

            var value = Value;
            for (var i = 0; i < _choices.Length; i++)
            {
                if (EqualityComparer<TValue>.Default.Equals(_choices[i].Value, value))
                    return i;
            }

            return 0;
        }

        public void ApplyIndex(int index)
        {
            if (_choices.Length == 0)
                return;

            Value = _choices[ClampIndex(index)].Value;
        }

        private int ClampIndex(int index)
        {
            if (index < 0)
                return 0;
            return index >= _choices.Length ? _choices.Length - 1 : index;
        }
    }

    internal static class MinimapOptions
    {
        public static MinimapOption<bool, bool> Toggle(
            MelonPreferences_Category category,
            string id,
            string label,
            string description,
            bool defaultValue,
            Action markDirty)
        {
            return new MinimapOption<bool, bool>(
                category,
                id,
                label,
                description,
                defaultValue,
                new[]
                {
                    new MinimapOptionChoice<bool>(false, "Off"),
                    new MinimapOptionChoice<bool>(true, "On")
                },
                markDirty);
        }

        public static MinimapOption<float, float> FloatChoice(
            MelonPreferences_Category category,
            string id,
            string label,
            string description,
            float defaultValue,
            float[] values,
            Func<float, string> format,
            Action markDirty)
        {
            if (values == null || values.Length == 0)
                throw new ArgumentException("A float option needs at least one choice.", nameof(values));
            if (format == null)
                throw new ArgumentNullException(nameof(format));

            var choices = new MinimapOptionChoice<float>[values.Length];
            for (var i = 0; i < values.Length; i++)
                choices[i] = new MinimapOptionChoice<float>(values[i], format(values[i]));

            return new MinimapOption<float, float>(
                category,
                id,
                label,
                description,
                defaultValue,
                choices,
                markDirty,
                normalize: v => Clamp(v, values[0], values[values.Length - 1]),
                indexOf: v => NearestIndex(values, v));
        }

        public static MinimapOption<string, MinimapMapShape> MapShape(
            MelonPreferences_Category category,
            Action markDirty)
        {
            return new MinimapOption<string, MinimapMapShape>(
                category,
                "map_type",
                "Map shape",
                "Use a square or circular minimap crop.",
                "square",
                new[]
                {
                    new MinimapOptionChoice<MinimapMapShape>(MinimapMapShape.Square, "Square"),
                    new MinimapOptionChoice<MinimapMapShape>(MinimapMapShape.Circle, "Circle")
                },
                markDirty,
                read: ParseMapShape,
                write: FormatMapShape);
        }

        public static MinimapOption<bool, bool> HiddenBool(
            MelonPreferences_Category category,
            string id,
            string label,
            bool defaultValue,
            Action markDirty)
        {
            return new MinimapOption<bool, bool>(
                category,
                id,
                label,
                string.Empty,
                defaultValue,
                Array.Empty<MinimapOptionChoice<bool>>(),
                markDirty,
                showInMenu: false);
        }

        public static MinimapOption<int, int> HiddenInt(
            MelonPreferences_Category category,
            string id,
            string label,
            int defaultValue,
            Action markDirty)
        {
            return new MinimapOption<int, int>(
                category,
                id,
                label,
                string.Empty,
                defaultValue,
                Array.Empty<MinimapOptionChoice<int>>(),
                markDirty,
                showInMenu: false);
        }

        private static MinimapMapShape ParseMapShape(string value)
        {
            return string.Equals(value, "circle", StringComparison.OrdinalIgnoreCase)
                ? MinimapMapShape.Circle
                : MinimapMapShape.Square;
        }

        private static string FormatMapShape(MinimapMapShape value)
        {
            return value == MinimapMapShape.Circle ? "circle" : "square";
        }

        private static float Clamp(float value, float min, float max)
        {
            return Math.Min(max, Math.Max(min, value));
        }

        private static int NearestIndex(float[] values, float current)
        {
            var best = 0;
            var bestDist = float.MaxValue;
            for (var i = 0; i < values.Length; i++)
            {
                var dist = Math.Abs(values[i] - current);
                if (dist < bestDist)
                {
                    best = i;
                    bestDist = dist;
                }
            }
            return best;
        }
    }
}
