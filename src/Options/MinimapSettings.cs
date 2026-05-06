using System;
using System.Collections.Generic;
using MelonLoader;

namespace rancher_minimap
{
    internal sealed class MinimapSettings
    {
        public const string CategoryName = "RancherMinimap";
        public const string CategoryLabel = "Rancher Minimap";
        public const string RiderTitle = "Minimap";
        public const int CurrentSchemaVersion = 58;

        private readonly List<IMinimapOption> _menuOptions = new List<IMinimapOption>();

        public readonly MinimapOption<bool, bool> EnabledOption;
        public readonly MinimapOption<string, MinimapMapShape> MapShapeOption;
        public readonly MinimapOption<bool, bool> RotateMapOption;
        public readonly MinimapOption<float, float> SizePercentOption;
        public readonly MinimapOption<float, float> EdgeOffsetPercentOption;
        public readonly MinimapOption<float, float> ZoomOption;
        public readonly MinimapOption<float, float> DynamicZoomMaxOutOption;
        public readonly MinimapOption<float, float> OpacityOption;
        public readonly MinimapOption<float, float> IconScaleOption;
        public readonly MinimapOption<bool, bool> ShowMarkersOption;
        public readonly MinimapOption<bool, bool> ShowMapBackgroundOption;
        public readonly MinimapOption<bool, bool> ShowDecorativeCloudsOption;
        public readonly MinimapOption<bool, bool> ShowPortalLinesOption;

#if RMM_DIAGNOSTICS
        private readonly MinimapOption<bool, bool> _diagnosticsEnabled;
        private readonly MinimapOption<bool, bool> _performanceLoggingEnabled;
        private readonly MinimapOption<bool, bool> _markerVisualDiagnosticsEnabled;
#endif
        private readonly MinimapOption<int, int> _schemaVersion;

        private float _saveAfter;

        private MinimapSettings(MelonPreferences_Category category)
        {
            EnabledOption = Add(MinimapOptions.Toggle(
                category,
                "enabled",
                "Enabled",
                "Show or hide the minimap overlay.",
                true,
                MarkDirty));

            MapShapeOption = Add(MinimapOptions.MapShape(category, MarkDirty));

            RotateMapOption = Add(MinimapOptions.Toggle(
                category,
                "rotate_map",
                "Rotate map",
                "Rotate the map around the player.",
                true,
                MarkDirty));

            SizePercentOption = Add(MinimapOptions.FloatChoice(
                category,
                "size",
                "Size",
                "Minimap size as a percentage of the shorter screen edge.",
                30.0f,
                new[] { 10f, 12.5f, 15f, 17.5f, 20f, 22.5f, 25f, 27.5f, 30f, 32.5f, 35f, 37.5f, 40f, 45f, 50f, 55f, 60f },
                v => $"{v:0.#}%",
                MarkDirty));

            EdgeOffsetPercentOption = Add(MinimapOptions.FloatChoice(
                category,
                "edge_offset_percent",
                "Edge offset",
                "Distance from the screen corner as a percentage of the shorter screen edge.",
                0.0f,
                new[] { 0f, 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f, 10f, 11f, 12f, 13f, 14f, 15f },
                v => $"{v:0}%",
                MarkDirty));

            ZoomOption = Add(MinimapOptions.FloatChoice(
                category,
                "zoom",
                "Zoom",
                "Minimap zoom level.",
                2.0f,
                new[] { 0.50f, 0.75f, 1.0f, 1.25f, 1.5f, 2.0f, 2.5f, 3.0f, 3.5f, 4.0f, 4.5f, 5.0f, 5.5f, 6.0f },
                v => $"{v:0.##}x",
                MarkDirty));

            DynamicZoomMaxOutOption = Add(MinimapOptions.FloatChoice(
                category,
                "dynamic_zoom_max_out",
                "Dynamic zoom amount",
                "Decrease the minimap zoom-in amount while moving.",
                0.1f,
                new[] { 0.0f, 0.05f, 0.10f, 0.15f, 0.20f, 0.25f, 0.35f, 0.50f, 0.75f, 1.0f },
                v => v <= 0f ? "Off" : $"{v:0.##}x",
                MarkDirty));

            OpacityOption = Add(MinimapOptions.FloatChoice(
                category,
                "opacity",
                "Opacity",
                "Minimap opacity.",
                1.00f,
                new[] { 0.0f, 0.05f, 0.10f, 0.15f, 0.25f, 0.4f, 0.55f, 0.7f, 0.82f, 0.9f, 1.0f },
                v => $"{v * 100f:0}%",
                MarkDirty));

            IconScaleOption = Add(MinimapOptions.FloatChoice(
                category,
                "icon_scale",
                "Icon scale",
                "Map marker icon scale.",
                0.75f,
                new[] { 0.10f, 0.20f, 0.33f, 0.50f, 0.75f, 1.0f, 1.25f, 1.5f, 2.0f, 2.5f, 3.5f, 5.0f, 7.5f, 10.0f },
                v => $"{v:0.##}x",
                MarkDirty));

            ShowMarkersOption = Add(MinimapOptions.Toggle(
                category,
                "show_markers",
                "Show gadgets",
                "Show gadget markers on the minimap.",
                true,
                MarkDirty));

            ShowMapBackgroundOption = Add(MinimapOptions.Toggle(
                category,
                "show_map_background",
                "Map background",
                "Show the animated vanilla map background layer in the minimap.",
                true,
                MarkDirty));

            ShowDecorativeCloudsOption = Add(MinimapOptions.Toggle(
                category,
                "show_decorative_clouds",
                "Clouds",
                "Show the same decorative clouds as the large map does.",
                true,
                MarkDirty));

            ShowPortalLinesOption = MinimapOptions.HiddenBool(
                category,
                "show_portal_lines",
                "Show teleporter lines",
                false,
                MarkDirty);

#if RMM_DIAGNOSTICS
            _diagnosticsEnabled = MinimapOptions.HiddenBool(category, "diagnostics_enabled", "Enable Diagnostics", true, MarkDirty);
            _performanceLoggingEnabled = MinimapOptions.HiddenBool(category, "performance_logging_enabled", "Enable Performance Logging", true, MarkDirty);
            _markerVisualDiagnosticsEnabled = MinimapOptions.HiddenBool(category, "marker_visual_diagnostics_enabled", "Enable Marker Visual Diagnostics", true, MarkDirty);
#endif
            _schemaVersion = MinimapOptions.HiddenInt(category, "schema_version", "Settings Schema Version", CurrentSchemaVersion, MarkDirty);
            MigrateIfNeeded();
        }

        public IReadOnlyList<IMinimapOption> MenuOptions => _menuOptions;

        public bool Enabled { get => EnabledOption.Value; set => EnabledOption.Value = value; }
        public MinimapMapShape MapShape { get => MapShapeOption.Value; set => MapShapeOption.Value = value; }
        public bool RotateMap { get => RotateMapOption.Value; set => RotateMapOption.Value = value; }
        public float SizePercent { get => SizePercentOption.Value; set => SizePercentOption.Value = value; }
        public float EdgeOffsetPercent { get => EdgeOffsetPercentOption.Value; set => EdgeOffsetPercentOption.Value = value; }
        public float Zoom { get => ZoomOption.Value; set => ZoomOption.Value = value; }
        public float DynamicZoomMaxOut { get => DynamicZoomMaxOutOption.Value; set => DynamicZoomMaxOutOption.Value = value; }
        public float Opacity { get => OpacityOption.Value; set => OpacityOption.Value = value; }
        public float IconScale { get => IconScaleOption.Value; set => IconScaleOption.Value = value; }
        public bool ShowMarkers { get => ShowMarkersOption.Value; set => ShowMarkersOption.Value = value; }
        public bool ShowMapBackground { get => ShowMapBackgroundOption.Value; set => ShowMapBackgroundOption.Value = value; }
        public bool ShowDecorativeClouds { get => ShowDecorativeCloudsOption.Value; set => ShowDecorativeCloudsOption.Value = value; }
        public bool ShowPortalLines { get => ShowPortalLinesOption.Value; set => ShowPortalLinesOption.Value = value; }

        public float SizePixels
        {
            get
            {
                var viewportBasis = Math.Max(1f, Math.Min(UnityEngine.Screen.width, UnityEngine.Screen.height));
                return viewportBasis * SizePercent * 0.01f;
            }
        }

#if RMM_DIAGNOSTICS
        public bool DiagnosticsEnabled => _diagnosticsEnabled.Value;
        public bool PerformanceLoggingEnabled => _performanceLoggingEnabled.Value;
        public bool MarkerVisualDiagnosticsEnabled => _markerVisualDiagnosticsEnabled.Value;
#else
        public bool DiagnosticsEnabled => false;
        public bool PerformanceLoggingEnabled => false;
        public bool MarkerVisualDiagnosticsEnabled => false;
#endif

        public static MinimapSettings Load()
        {
            var category = MelonPreferences.CreateCategory(CategoryName, CategoryLabel);
            return new MinimapSettings(category);
        }

        public void TickPendingSave()
        {
            if (_saveAfter <= 0f)
                return;

            _saveAfter -= UnityEngine.Time.unscaledDeltaTime;
            if (_saveAfter <= 0f)
                SaveNow();
        }

        public void SaveNow() => MelonPreferences.Save();

        public void MarkDirty() => _saveAfter = 5.0f;

        private TOption Add<TOption>(TOption option) where TOption : IMinimapOption
        {
            if (option.ShowInMenu)
                _menuOptions.Add(option);
            return option;
        }

        private void MigrateIfNeeded()
        {
            if (_schemaVersion.Value >= CurrentSchemaVersion)
                return;

            ShowPortalLinesOption.Value = false;

#if RMM_DIAGNOSTICS
            _performanceLoggingEnabled.Value = true;
            _markerVisualDiagnosticsEnabled.Value = true;
#endif
            _schemaVersion.Value = CurrentSchemaVersion;
            MelonPreferences.Save();
        }
    }
}
