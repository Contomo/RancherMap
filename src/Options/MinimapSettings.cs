using System;
using MelonLoader;

namespace rancher_minimap
{
    internal sealed class MinimapSettings
    {
        private const string CategoryName = "RancherMinimap";

        private readonly MelonPreferences_Entry<bool> _enabled;
        private readonly MelonPreferences_Entry<bool> _rotateMap;
        private readonly MelonPreferences_Entry<float> _size;
        private readonly MelonPreferences_Entry<float> _zoom;
        private readonly MelonPreferences_Entry<float> _opacity;
        private readonly MelonPreferences_Entry<float> _iconScale;
        private readonly MelonPreferences_Entry<float> _dynamicZoomMaxOut;
        private readonly MelonPreferences_Entry<bool> _showMarkers;
        private readonly MelonPreferences_Entry<bool> _showMapBackground;
        private readonly MelonPreferences_Entry<bool> _showDecorativeClouds;
        private readonly MelonPreferences_Entry<float> _edgeOffsetPercent;
        private readonly MelonPreferences_Entry<bool> _showPortalLines;
#if RMM_DIAGNOSTICS
        private readonly MelonPreferences_Entry<bool> _diagnosticsEnabled;
        private readonly MelonPreferences_Entry<bool> _performanceLoggingEnabled;
        private readonly MelonPreferences_Entry<bool> _markerVisualDiagnosticsEnabled;
#endif
        private readonly MelonPreferences_Entry<int> _schemaVersion;

        private float _saveAfter;

        private MinimapSettings(MelonPreferences_Category category)
        {
            _enabled = category.CreateEntry("enabled", true, MinimapOptionText.EnabledLabel);
            _rotateMap = category.CreateEntry("rotate_map", true, MinimapOptionText.RotateMapLabel);
            _size = category.CreateEntry("size", 30.0f, MinimapOptionText.SizeLabel);
            _zoom = category.CreateEntry("zoom", 2.0f, MinimapOptionText.ZoomLabel);
            _opacity = category.CreateEntry("opacity", 1.00f, MinimapOptionText.OpacityLabel);
            _iconScale = category.CreateEntry("icon_scale", 0.75f, MinimapOptionText.IconScaleLabel);
            _dynamicZoomMaxOut = category.CreateEntry("dynamic_zoom_max_out", 0.1f, MinimapOptionText.DynamicZoomAmountLabel);
            _showMarkers = category.CreateEntry("show_markers", true, MinimapOptionText.ShowMarkersLabel);
            _showMapBackground = category.CreateEntry("show_map_background", true, MinimapOptionText.ShowMapBackgroundLabel);
            _showDecorativeClouds = category.CreateEntry("show_decorative_clouds", true, MinimapOptionText.ShowDecorativeCloudsLabel);
            _edgeOffsetPercent = category.CreateEntry("edge_offset_percent", 0.0f, MinimapOptionText.EdgeOffsetLabel);
            _showPortalLines = category.CreateEntry("show_portal_lines", false, "Show Teleporter Lines");
#if RMM_DIAGNOSTICS
            _diagnosticsEnabled = category.CreateEntry("diagnostics_enabled", true, "Enable Diagnostics");
            _performanceLoggingEnabled = category.CreateEntry("performance_logging_enabled", true, "Enable Performance Logging");
            _markerVisualDiagnosticsEnabled = category.CreateEntry("marker_visual_diagnostics_enabled", true, "Enable Marker Visual Diagnostics");
#endif
            _schemaVersion = category.CreateEntry("schema_version", 55, "Settings Schema Version");
            MigrateIfNeeded();
        }

        public static MinimapSettings Load()
        {
            var category = MelonPreferences.CreateCategory(CategoryName, "Rancher Minimap");
            return new MinimapSettings(category);
        }

        private void MigrateIfNeeded()
        {
            if (_schemaVersion.Value >= 55)
                return;

#if RMM_DIAGNOSTICS
            _performanceLoggingEnabled.Value = true;
            _markerVisualDiagnosticsEnabled.Value = true;
#endif
            _schemaVersion.Value = 55;
            MelonPreferences.Save();
        }

        public bool Enabled { get => _enabled.Value; set => Set(_enabled, value); }
        public bool RotateMap { get => _rotateMap.Value; set => Set(_rotateMap, value); }
        public float SizePercent { get => Clamp(_size.Value, 10f, 60f); set => Set(_size, Clamp(value, 10f, 60f)); }
        public float SizePixels
        {
            get
            {
                var viewportBasis = Math.Max(1f, Math.Min(UnityEngine.Screen.width, UnityEngine.Screen.height));
                return viewportBasis * SizePercent * 0.01f;
            }
        }
        public float Zoom { get => Clamp(_zoom.Value, 0.50f, 6.0f); set => Set(_zoom, Clamp(value, 0.50f, 6.0f)); }
        public float Opacity { get => _opacity.Value; set => Set(_opacity, Clamp(value, 0.0f, 1.0f)); }
        public float IconScale { get => _iconScale.Value; set => Set(_iconScale, Clamp(value, 0.10f, 5.0f)); }
        public float DynamicZoomMaxOut { get => _dynamicZoomMaxOut.Value; set => Set(_dynamicZoomMaxOut, Clamp(value, 0.0f, 2.0f)); }
        public bool ShowMarkers { get => _showMarkers.Value; set => Set(_showMarkers, value); }
        public bool ShowMapBackground { get => _showMapBackground.Value; set => Set(_showMapBackground, value); }
        public bool ShowDecorativeClouds { get => _showDecorativeClouds.Value; set => Set(_showDecorativeClouds, value); }
        public string FogCloneKey => $"decorativeClouds:{ShowDecorativeClouds}|background:{ShowMapBackground}";
        public float EdgeOffsetPercent { get => Clamp(_edgeOffsetPercent.Value, 0f, 15f); set => Set(_edgeOffsetPercent, Clamp(value, 0f, 15f)); }
        public bool ShowPortalLines { get => _showPortalLines.Value; set => Set(_showPortalLines, value); }
#if RMM_DIAGNOSTICS
        public bool DiagnosticsEnabled => _diagnosticsEnabled.Value;
        public bool PerformanceLoggingEnabled => _performanceLoggingEnabled.Value;
        public bool MarkerVisualDiagnosticsEnabled => _markerVisualDiagnosticsEnabled.Value;
#else
        public bool DiagnosticsEnabled => false;
        public bool PerformanceLoggingEnabled => false;
        public bool MarkerVisualDiagnosticsEnabled => false;
#endif

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

        private void Set<T>(MelonPreferences_Entry<T> entry, T value)
        {
            if (Equals(entry.Value, value))
                return;

            entry.Value = value;
            MarkDirty();
        }

        private static float Clamp(float value, float min, float max) => Math.Min(max, Math.Max(min, value));
    }
}
