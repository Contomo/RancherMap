using System;
using UnityEngine;

namespace rancher_minimap
{
    /// <summary>
    /// Runtime state machine. It glues together services, marker snapshots, and view.
    /// No Harmony patch code belongs here.
    /// </summary>
    internal sealed class MinimapController(MinimapSettings settings) : IDisposable
    {
        private readonly MinimapSettings _settings = settings;
        private readonly GameServices _services = new GameServices();
        private readonly MarkerSourceResolver _markers = new MarkerSourceResolver();
        private readonly MinimapHudView _view = new MinimapHudView(settings);

        private Vector3 _lastPlayerWorld;
        private bool _runtimeMotionInitialized;
        private float _smoothedPlanarSpeed;
        private float _smoothedRuntimeZoom;
        private bool _runtimeZoomInitialized;
        private Il2CppMonomiPark.SlimeRancher.Map.MapDefinition _stableMapDefinition;

        public void Tick()
        {
            using (TimeTracker.Measure("controller.tick"))
            {
                if (!_settings.Enabled)
                {
                    _view.SetVisible(false);
                    return;
                }

                using (TimeTracker.Measure("services.tick"))
                    _services.Tick();

                var pos = _services.TryGetPlayerPosition();
                var yaw = _services.TryGetPlayerYawDegrees();

                // player may show up but still all null (not actually loaded yet)
                if (!pos.HasValue || (pos.Value.sqrMagnitude < 0.0001f && Mathf.Abs(Mathf.DeltaAngle(0f, yaw)) < 0.1f))
                {
                    _view.SetVisible(false);
                    return;
                }

                var currentWorld = pos.Value;
                var runtimeZoom = UpdateRuntimeZoom(currentWorld);
                _lastPlayerWorld = currentWorld;
                _view.EnsureBuilt();

                var observedMapDefinition = _services.TryGetMapDefinition();
                if (observedMapDefinition != null)
                    _stableMapDefinition = observedMapDefinition;
                var mapDefinition = _stableMapDefinition;

                bool hasMapVisual;
                using (TimeTracker.Measure("hud.ensure-map-visual"))
                    hasMapVisual = _view.TryEnsureMapVisual(mapDefinition);

                _view.SetVisible(true);
                if (!hasMapVisual)
                    Log.Every("hud-no-map-visual", 4f, "hud: no captured map visual available yet; rendering marker/player layers only");

                var markerEnabled = _settings.ShowMarkers;
                using (TimeTracker.Measure("markers.resolve"))
                {
                    if (markerEnabled)
                        _markers.Tick(_services, mapDefinition, true);
                    else
                        _markers.Clear("show-markers-off");
                }

                using (TimeTracker.Measure("hud.layout"))
                    _view.UpdateLayout(yaw, currentWorld, runtimeZoom);
                using (TimeTracker.Measure("hud.markers"))
                    _view.UpdateMarkers(markerEnabled ? _markers.Markers : Array.Empty<MarkerSnapshot>());
            }
        }

        /// <summary>
        /// Updates both dynamic zoom and option-change smoothing. Dynamic zoom contributes
        /// a speed-based target offset; runtime smoothing still exists independently so
        /// manual option zoom changes ease instead of snapping.
        /// </summary>
        private float UpdateRuntimeZoom(Vector3 currentWorld)
        {
            if (!_runtimeMotionInitialized)
                InitializeRuntimeMotion(currentWorld);
            else
                UpdateSmoothedPlanarSpeed(currentWorld);

            var normalizedSpeed = Mathf.InverseLerp(0f, 18f, _smoothedPlanarSpeed);
            var zoomOut = Mathf.Clamp(_settings.DynamicZoomMaxOut, 0f, 2f) * normalizedSpeed;
            var targetZoom = Mathf.Clamp(_settings.Zoom - zoomOut, 0.50f, 6.0f);

            if (!_runtimeZoomInitialized)
            {
                _runtimeZoomInitialized = true;
                _smoothedRuntimeZoom = targetZoom;
                return _smoothedRuntimeZoom;
            }

            var dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            var blend = 1f - Mathf.Exp(-8f * dt);
            _smoothedRuntimeZoom = Mathf.Lerp(_smoothedRuntimeZoom, targetZoom, blend);
            return _smoothedRuntimeZoom;
        }

        private void InitializeRuntimeMotion(Vector3 currentWorld)
        {
            _runtimeMotionInitialized = true;
            _lastPlayerWorld = currentWorld;
            _smoothedPlanarSpeed = 0f;
        }

        private void UpdateSmoothedPlanarSpeed(Vector3 currentWorld)
        {
            var planarDistance = (currentWorld - _lastPlayerWorld).magnitude;
            var dtSpeed = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            var rawSpeed = planarDistance / dtSpeed;
            var speedBlend = 1f - Mathf.Exp(-6f * dtSpeed);
            _smoothedPlanarSpeed = Mathf.Lerp(_smoothedPlanarSpeed, rawSpeed, speedBlend);
        }

        public void Dispose() => _view.Dispose();
    }
}
