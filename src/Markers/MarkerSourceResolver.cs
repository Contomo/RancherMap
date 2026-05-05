using System;
using System.Collections.Generic;
using Il2CppMonomiPark.SlimeRancher.Map;
using UnityEngine;

namespace rancher_minimap
{
    /// <summary>
    /// Resolves marker snapshots without polling MapDirector marker objects at runtime.
    ///
    /// Marker facts now come from vanilla MapUI's already-built marker UI, captured as primitive
    /// anchored-position snapshots during the map lifecycle patch.
    /// </summary>
    internal sealed class MarkerSourceResolver
    {
        private float _nextRefresh;
        private int _lastMarkerStateVersion = -1;
        private readonly List<MarkerSnapshot> _markers = new List<MarkerSnapshot>();

        public IReadOnlyList<MarkerSnapshot> Markers => _markers;
        public string Status { get; private set; } = "not-started";

        public void Clear(string reason)
        {
            _markers.Clear();
            Status = reason;
        }

        public void Tick(GameServices services, MapDefinition mapDefinition, bool collectSnapshots)
        {
            var markerStateVersion = MapVisualCapture.MarkerStateVersion;
            var forcedRefresh = markerStateVersion != _lastMarkerStateVersion;
            if (!forcedRefresh && Time.realtimeSinceStartup < _nextRefresh)
                return;

            _nextRefresh = Time.realtimeSinceStartup + 1.25f;
            _lastMarkerStateVersion = markerStateVersion;
            _markers.Clear();

            IReadOnlyList<MarkerSnapshot> captured = null;
            var currentMapKey = services.CurrentMapKey;
            if (!string.IsNullOrEmpty(currentMapKey) &&
                currentMapKey != "-" &&
                MapVisualCapture.TryGetCapturedMarkersForKey(currentMapKey, out var currentMapMarkers))
            {
                captured = currentMapMarkers;
            }

            if (captured == null && MapVisualCapture.TryGetCapturedMarkers(mapDefinition, out var directMarkers))
            {
                captured = directMarkers;
            }

            var playerPosition = services.TryGetPlayerPosition();
            if (captured == null &&
                playerPosition.HasValue &&
                MapVisualCapture.TryInferMapKeyFromWorldPosition(playerPosition.Value, out var inferredKey) &&
                MapVisualCapture.TryGetCapturedMarkersForKey(inferredKey, out var inferredMarkers))
            {
                captured = inferredMarkers;
            }

            if (captured == null)
            {
                Status = "mapui-marker-cache-missing; open vanilla map once so SR2 can build marker UI";
                return;
            }

            if (captured.Count == 0)
            {
                Status = $"scope=MapUI.marker-section collect={collectSnapshots} active=0/0 markerStateVersion={markerStateVersion}";
                Log.Every("markers-runtime-" + currentMapKey, 4f, "markers: " + Status + " currentMapKey=" + currentMapKey);
                return;
            }

            var visibleCount = 0;
            foreach (var marker in captured)
            {
                // The source RectTransform was already filtered by vanilla MapUI when captured.
                // Do not re-query cloned marker behaviours here: those wrappers can depend on the
                // currently opened/selected big-map state and incorrectly hide Labyrinth markers.
                var visible = marker.Visible;
                if (visible)
                    visibleCount++;

                if (collectSnapshots)
                    _markers.Add(marker.WithVisible(visible));
            }

            Status = $"scope=MapUI.marker-section collect={collectSnapshots} active={visibleCount}/{captured.Count} markerStateVersion={markerStateVersion}";
            Log.Every("markers-runtime-" + currentMapKey, 4f, "markers: " + Status + " currentMapKey=" + currentMapKey);
        }
    }
}
