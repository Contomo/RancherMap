using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Il2CppMonomiPark.SlimeRancher.Map;
using Il2CppMonomiPark.SlimeRancher.UI.Map;
using Object = UnityEngine.Object;

namespace rancher_minimap
{
    /// <summary>
    /// Owns every Unity object created for the minimap HUD.
    ///
    /// The player indicator is rebuilt as our own Image from SR2's captured BeaIcon sprite.
    /// We do not clone PlayerMarker/FacingFrame/FacingArrow because those native UI branches are animated and quite distracting in the minimap.
    /// </summary>
    internal sealed class MinimapHudView : IDisposable
    {
        private readonly MinimapSettings _settings;
        private readonly Dictionary<int, RuntimeMarkerView> _markerViews = new Dictionary<int, RuntimeMarkerView>();
        private readonly List<Graphic> _mapBackgroundGraphics = new List<Graphic>();
        private readonly Dictionary<int, float> _mapBackgroundOriginalAlpha = new Dictionary<int, float>();

        private GameObject _root;
        private RectTransform _widgetRect;
        private RectTransform _clipRect;
        private RectTransform _rotatorRect;
        private RectTransform _mapRect;
        private RectTransform _markerLayer;
        private RectTransform _cloudOverlayLayer;
        private RectTransform _playerLayer;
        private CanvasGroup _canvasGroup;
        private Image _shapeMaskImage;
        private RectMask2D _rectMask;
        private Mask _shapeMask;
        private Sprite _markerSprite;
        private Sprite _shapeMaskSprite;
        private GameObject _instancedMapVisual;
        private PlayerMarkerView _playerMarker;
        private MapGeometry _mapGeometry;
        private string _attachedMapKey = "-";
        private string _attachedMapName = "-";
        private float _nextAttachAttempt;
        private bool _hasBuiltRoot;
        private string _lastAppliedFogStateKey = string.Empty;
        private int _attachedTemplateVersion = -1;
        private bool _staticMapVisualPrepared;
        private Vector2 _preparedNativeMapSize = new Vector2(float.NaN, float.NaN);
        private bool _preparedShowBackground;
        private bool _preparedShowDecorativeClouds;
        private bool _preparedShowMarkers;
        private bool _preparedShowPortalLines;

        public string AssetStatus => _instancedMapVisual != null ? "attached:" + _attachedMapName : ("waiting-for-map-visual | " + MapVisualCapture.Status);
        public bool HasMapVisual => _instancedMapVisual != null;
        public MapGeometry Geometry => _mapGeometry;

        public MinimapHudView(MinimapSettings settings)
        {
            _settings = settings;
        }

        private void ClearRuntimeReferencesAfterUnityDestroyed(string reason)
        {
            _markerViews.Clear();
            _root = null;
            _widgetRect = null;
            _clipRect = null;
            _rotatorRect = null;
            _mapRect = null;
            _markerLayer = null;
            _cloudOverlayLayer = null;
            _playerLayer = null;
            _canvasGroup = null;
            _shapeMaskImage = null;
            _rectMask = null;
            _shapeMask = null;
            _playerMarker = null;
            _instancedMapVisual = null;
            _mapGeometry = MapGeometry.Empty;
            _attachedMapKey = "-";
            _attachedMapName = "-";
            _nextAttachAttempt = 0f;
            _lastAppliedFogStateKey = string.Empty;
            _attachedTemplateVersion = -1;
            ResetStaticMapVisualPreparation();
            _mapBackgroundOriginalAlpha.Clear();

            Log.Warn("hud-lifetime: cleared stale Unity references after root disappeared; reason=" + reason);
        }

        public void EnsureBuilt()
        {
            if (_root != null)
                return;

            if (_hasBuiltRoot)
                ClearRuntimeReferencesAfterUnityDestroyed("root destroyed by scene transition or Unity cleanup");

            _markerSprite = SpriteFactory.DiscSprite(new Color(1f, 0.82f, 0.22f, 0.95f));

            _root = new GameObject("Rancher_MinimapHUD");
            _hasBuiltRoot = true;
            Object.DontDestroyOnLoad(_root);

            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 900;

            _canvasGroup = _root.AddComponent<CanvasGroup>();
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;

            _widgetRect = CreateRectTransform("Widget", _root.transform);
            UiRectTransforms.AnchorAt(_widgetRect, UiRectTransforms.TopRight, UiRectTransforms.TopRight);

            _clipRect = CreateRectTransform("Clip", _widgetRect);
            UiRectTransforms.StretchToParent(_clipRect);

            _shapeMaskSprite ??= SpriteFactory.CircleMaskSprite(1024);
            _shapeMaskImage = _clipRect.gameObject.AddComponent<Image>();
            _shapeMaskImage.sprite = _shapeMaskSprite;
            _shapeMaskImage.type = Image.Type.Simple;
            _shapeMaskImage.preserveAspect = false;
            _shapeMaskImage.raycastTarget = false;
            _shapeMaskImage.maskable = false;
            _shapeMaskImage.color = Color.white;
            _shapeMaskImage.enabled = false;

            _rectMask = _clipRect.gameObject.AddComponent<RectMask2D>();
            _shapeMask = _clipRect.gameObject.AddComponent<Mask>();
            _shapeMask.showMaskGraphic = false;
            _shapeMask.enabled = false;

            _rotatorRect = CreateRectTransform("Rotator", _clipRect);
            UiRectTransforms.StretchToParent(_rotatorRect);
            _rotatorRect.pivot = UiRectTransforms.Center;

            _mapRect = CreateRectTransform("MapContent", _rotatorRect);
            UiRectTransforms.AnchorAt(_mapRect, UiRectTransforms.Center, UiRectTransforms.Center);

            _markerLayer = CreateRectTransform("Markers", _mapRect);
            UiRectTransforms.StretchToParent(_markerLayer);

            _cloudOverlayLayer = CreateRectTransform("CloudOverlay", _mapRect);
            UiRectTransforms.StretchToParent(_cloudOverlayLayer);

            _playerLayer = CreateRectTransform("PlayerMarkerOverlay", _clipRect);
            UiRectTransforms.StretchToParent(_playerLayer);
            _playerLayer.pivot = UiRectTransforms.Center;
            _playerMarker = PlayerMarkerView.Create(_playerLayer);

            ApplyClipShape();
        }

        private static RectTransform CreateRectTransform(string name, Transform parent)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            return obj.AddComponent<RectTransform>();
        }

        public void SetVisible(bool visible)
        {
            if (_root != null && _root.activeSelf != visible)
                _root.SetActive(visible);
        }

        public void UpdateLayout(float yawDegrees, Vector3 playerWorld, float runtimeZoom)
        {
            if (_widgetRect == null || _mapRect == null || _canvasGroup == null || _clipRect == null || _rotatorRect == null)
                return;

            var size = Mathf.Max(1f, _settings.SizePixels);
            UiRectTransforms.AnchorAt(_widgetRect, UiRectTransforms.TopRight, UiRectTransforms.TopRight);
            var edgeOffset = GetViewportEdgeOffsetPixels();
            _widgetRect.anchoredPosition = new Vector2(-edgeOffset, -edgeOffset);
            _widgetRect.sizeDelta = new Vector2(size, size);
            UiRectTransforms.StretchToParent(_clipRect);
            ApplyClipShape();
            var yawUi = -yawDegrees;
            var mapRotationZ = _settings.RotateMap ? yawDegrees : 0f;

            // Keep the container neutral and apply rotation directly to the native map rect.
            // This mirrors the old project's tested-good transform path:
            //   map.localRotation = player yaw
            //   map.anchoredPosition = -Rotate(projectedPlayer, yaw) * zoom
            // The earlier parent-rotator version also centered/rotated correctly; do not treat
            // this direct-map version as an offset fix. It is kept because it is easier to reason
            // about with SR2's cloned UI hierarchy and fewer nested transforms.
            _rotatorRect.anchoredPosition = Vector2.zero;
            _rotatorRect.sizeDelta = Vector2.zero;
            _rotatorRect.localRotation = Quaternion.identity;

            var mapArea = GetCurrentMapArea();
            var nativeMapSize = GetNativeMapSize(mapArea);
            var zoom = Mathf.Clamp(runtimeZoom, 0.50f, 6.0f);

            _mapRect.sizeDelta = nativeMapSize;
            _mapRect.localRotation = Quaternion.Euler(0f, 0f, mapRotationZ);
            _mapRect.localScale = Vector3.one * zoom;
            ApplyStaticMapVisualStateIfNeeded(nativeMapSize);
            SyncFogRevealStateIfNeeded();
            ApplyMapBackgroundOpacity();

            var playerLocal = ProjectWorldToMapLocal(playerWorld, mapArea);
            var rotatedPlayerLocal = RotatePoint(playerLocal, mapRotationZ);
            _mapRect.anchoredPosition = -rotatedPlayerLocal * zoom;
            MapPortalLineOverlays.Update(_instancedMapVisual, _settings.ShowPortalLines);

            if (_playerLayer != null && !_playerLayer.gameObject.activeSelf)
                _playerLayer.gameObject.SetActive(true);

            _playerMarker?.RefreshSprites();
            UpdatePlayerMarker(yawUi);

            _canvasGroup.alpha = _settings.Opacity;

        }

        private void SyncFogRevealStateIfNeeded()
        {
            if (_instancedMapVisual == null)
                return;

            var key = "state:" + MapVisualCapture.FogStateVersion + "|map:" + _attachedMapKey;
            if (string.Equals(_lastAppliedFogStateKey, key, StringComparison.Ordinal))
                return;

            _lastAppliedFogStateKey = key;
            MapVisualCapture.ApplyCapturedFogStates(_instancedMapVisual, _attachedMapKey, "hud-fog-state-sync");
        }

        private void NormalizeCapturedMapContentLayout(Vector2 nativeMapSize)
        {
            if (_instancedMapVisual == null)
                return;

            var root = _instancedMapVisual.transform;
            var background = FindDirectChildByCleanName(root, "Background");
            var overlay = FindDirectChildByCleanName(root, "BackgroundOverlay");
            var mapHolder = FindDirectChildByCleanName(root, "MapHolder");

            NormalizeFullMapRect(background as RectTransform, nativeMapSize);
            NormalizeFullMapRect(overlay as RectTransform, nativeMapSize);
            NormalizeFullMapRect(mapHolder as RectTransform, nativeMapSize);
            DisableEmptyDefaultImageQuads(root);

            NormalizeSelectedMapBranchContainer(mapHolder, nativeMapSize);
        }

        private static void NormalizeFullMapRect(RectTransform rect, Vector2 nativeMapSize)
        {
            if (rect == null)
                return;

            UiRectTransforms.CenterOnParent(rect, Vector2.zero);

            var width = Mathf.Abs(nativeMapSize.x) > 1f ? Mathf.Abs(nativeMapSize.x) : 6400f;
            var height = Mathf.Abs(nativeMapSize.y) > 1f ? Mathf.Abs(nativeMapSize.y) : 6400f;
            rect.sizeDelta = new Vector2(width, height);
        }

        private void ApplyStaticMapVisualStateIfNeeded(Vector2 nativeMapSize)
        {
            if (_instancedMapVisual == null)
                return;

            var sizeChanged = !_staticMapVisualPrepared ||
                              !Mathf.Approximately(_preparedNativeMapSize.x, nativeMapSize.x) ||
                              !Mathf.Approximately(_preparedNativeMapSize.y, nativeMapSize.y);
            var settingsChanged = !_staticMapVisualPrepared ||
                                  _preparedShowBackground != _settings.ShowMapBackground ||
                                  _preparedShowDecorativeClouds != _settings.ShowDecorativeClouds ||
                                  _preparedShowMarkers != _settings.ShowMarkers ||
                                  _preparedShowPortalLines != _settings.ShowPortalLines;

            if (!sizeChanged && !settingsChanged)
                return;

            using (TimeTracker.Measure("hud.static-map-visual"))
            {
                EnsureInstancedMapVisualNativeSize(nativeMapSize);
                NormalizeCapturedMapContentLayout(nativeMapSize);
                ApplyNativeMarkerBranchVisibility();
                ApplyMapBackgroundVisibility();
                ApplyDecorativeCloudVisibility();
                DisableBrokenStaticAndZoomBoundVisuals();
                MapPortalLineOverlays.EnsureNativeGraphics(_instancedMapVisual, _settings.ShowPortalLines);
            }

            _staticMapVisualPrepared = true;
            _preparedNativeMapSize = nativeMapSize;
            _preparedShowBackground = _settings.ShowMapBackground;
            _preparedShowDecorativeClouds = _settings.ShowDecorativeClouds;
            _preparedShowMarkers = _settings.ShowMarkers;
            _preparedShowPortalLines = _settings.ShowPortalLines;
        }

        private void ResetStaticMapVisualPreparation()
        {
            _staticMapVisualPrepared = false;
            _preparedNativeMapSize = new Vector2(float.NaN, float.NaN);
            _preparedShowBackground = false;
            _preparedShowDecorativeClouds = false;
            _preparedShowMarkers = false;
            _preparedShowPortalLines = false;
            _mapBackgroundGraphics.Clear();
        }


        private void ApplyDecorativeCloudVisibility()
        {
            if (_instancedMapVisual == null)
                return;

            var changed = ApplyGraphicVisibility(MapGraphicClassifier.IsDecorativeCloudGraphic, _settings.ShowDecorativeClouds);
            if (changed > 0)
                Log.Info("hud: decorative cloud graphics " + (_settings.ShowDecorativeClouds ? "enabled" : "disabled") + " changed=" + changed);
        }

        private static void DisableEmptyDefaultImageQuads(Transform root)
        {
            if (root == null)
                return;

            foreach (var image in root.GetComponentsInChildren<Image>(true))
            {
                if (image == null || image.sprite != null || image.material == null)
                    continue;

                if (!MapGraphicClassifier.IsDefaultUiMaterial(image))
                    continue;

                image.enabled = false;
            }
        }

        private void NormalizeSelectedMapBranchContainer(Transform mapHolder, Vector2 nativeMapSize)
        {
            if (mapHolder == null)
                return;

            for (var i = 0; i < mapHolder.childCount; i++)
            {
                var child = mapHolder.GetChild(i);
                if (child == null)
                    continue;

                var rect = child as RectTransform;
                if (rect == null)
                    continue;

                rect.localRotation = Quaternion.identity;
                rect.localScale = Vector3.one;

                if (Mathf.Abs(rect.rect.width) <= 1f && Mathf.Abs(rect.sizeDelta.x) <= 1f &&
                    Mathf.Abs(nativeMapSize.x) > 1f)
                    rect.sizeDelta = nativeMapSize;
            }
        }

        private void ApplyNativeMarkerBranchVisibility()
        {
            if (_instancedMapVisual == null)
                return;

            var show = _settings.ShowMarkers;
            var changed = 0;

            foreach (var transform in _instancedMapVisual.GetComponentsInChildren<Transform>(true))
            {
                if (transform == null)
                    continue;

                if (!IsNativeMarkerBranch(transform))
                    continue;

                if (transform.gameObject.activeSelf != show)
                {
                    transform.gameObject.SetActive(show);
                    changed++;
                }
            }

            if (changed > 0)
                Log.Info("hud: native marker branches " + (show ? "enabled" : "disabled") + " changed=" + changed);
        }

        private static bool IsNativeMarkerBranch(Transform transform)
        {
            if (transform == null)
                return false;

            var clean = MapObjectNames.CleanCloneName(transform.name);
            if (string.Equals(clean, "PlayerMarker", StringComparison.OrdinalIgnoreCase) ||
                clean.IndexOf("PlayerMarker", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            if (string.Equals(clean, "MapMarkerSection", StringComparison.OrdinalIgnoreCase) ||
                clean.IndexOf("MarkerSection", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (!string.Equals(clean, "Markers", StringComparison.OrdinalIgnoreCase))
                return false;

            // Only match vanilla marker branches inside the cloned map visual, not the HUD's
            // separate runtime marker layer.
            var path = MapObjectNames.PathOf(transform).Replace('\\', '/');
            return path.IndexOf("VanillaMapVisualClone", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   path.IndexOf("RancherMinimapTemplate", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Transform FindDirectChildByCleanName(Transform root, string cleanName)
        {
            if (root == null || string.IsNullOrEmpty(cleanName))
                return null;

            for (var i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (child != null && string.Equals(MapObjectNames.CleanCloneName(child.name), cleanName, StringComparison.OrdinalIgnoreCase))
                    return child;
            }

            return null;
        }

        private void ApplyMapBackgroundVisibility()
        {
            RefreshMapBackgroundGraphics();
            ApplyMapBackgroundOpacity();
        }

        private void RefreshMapBackgroundGraphics()
        {
            _mapBackgroundGraphics.Clear();

            if (_instancedMapVisual == null)
                return;

            var root = _instancedMapVisual.transform;
            foreach (var graphic in _instancedMapVisual.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic == null || IsBrokenStaticOrZoomBoundVisual(graphic.transform))
                    continue;

                if (!IsMapBackgroundGraphicOrChild(graphic, root))
                    continue;

                _mapBackgroundGraphics.Add(graphic);
                graphic.raycastTarget = false;
            }
        }

        private void ApplyMapBackgroundOpacity()
        {
            if (_instancedMapVisual == null)
                return;

            if (_mapBackgroundGraphics.Count == 0)
                RefreshMapBackgroundGraphics();

            var visible = _settings.ShowMapBackground;
            var root = _instancedMapVisual.transform;
            for (var i = _mapBackgroundGraphics.Count - 1; i >= 0; i--)
            {
                var graphic = _mapBackgroundGraphics[i];
                if (graphic == null)
                {
                    _mapBackgroundGraphics.RemoveAt(i);
                    continue;
                }

                var id = graphic.GetInstanceID();
                if (!_mapBackgroundOriginalAlpha.TryGetValue(id, out var originalAlpha))
                {
                    originalAlpha = graphic.color.a > 0.001f ? graphic.color.a : 1.0f;
                    _mapBackgroundOriginalAlpha[id] = originalAlpha;
                }

                if (visible)
                    EnsureActiveThroughRoot(graphic.transform, root);

                if (!graphic.enabled)
                    graphic.enabled = true;

                var targetAlpha = visible ? originalAlpha : 0.0f;
                var color = graphic.color;
                if (!Mathf.Approximately(color.a, targetAlpha))
                {
                    color.a = targetAlpha;
                    graphic.color = color;
                }

                graphic.canvasRenderer.SetAlpha(targetAlpha);
                graphic.raycastTarget = false;
            }
        }

        private static bool IsMapBackgroundGraphicOrChild(Graphic graphic, Transform root)
        {
            if (graphic == null)
                return false;

            if (MapGraphicClassifier.IsMapBackgroundGraphic(graphic))
                return true;

            var current = graphic.transform;
            var guard = 0;
            while (current != null && guard < 32)
            {
                if (MapGraphicClassifier.IsMapBackgroundTransform(current, root))
                    return true;

                if (current == root)
                    break;

                current = current.parent;
                guard++;
            }

            return false;
        }

        private int ApplyGraphicVisibility(Func<Graphic, bool> predicate, bool visible)
        {
            if (_instancedMapVisual == null || predicate == null)
                return 0;

            var changed = 0;
            var root = _instancedMapVisual.transform;
            foreach (var graphic in _instancedMapVisual.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic == null || IsBrokenStaticOrZoomBoundVisual(graphic.transform))
                    continue;

                if (!predicate(graphic))
                    continue;

                graphic.raycastTarget = false;
                if (visible)
                    changed += EnsureActiveThroughRoot(graphic.transform, root);

                if (graphic.enabled == visible)
                    continue;

                graphic.enabled = visible;
                changed++;
            }

            return changed;
        }

        private static int EnsureActiveThroughRoot(Transform transform, Transform root)
        {
            var changed = 0;
            var current = transform;
            while (current != null)
            {
                if (!current.gameObject.activeSelf)
                {
                    current.gameObject.SetActive(true);
                    changed++;
                }

                if (current == root)
                    break;

                current = current.parent;
            }

            return changed;
        }

        public void UpdateMarkers(IReadOnlyList<MarkerSnapshot> markers)
        {
            if (_markerLayer == null || !_settings.ShowMarkers)
            {
                foreach (var view in _markerViews.Values)
                    SetMarkerRootActive(view, false);
                return;
            }

            markers ??= Array.Empty<MarkerSnapshot>();
            var mapArea = GetCurrentMapArea();
            var markerScreenScale = _settings.IconScale / _settings.Zoom;
            var markerRotation = _settings.RotateMap && _mapRect != null
                ? Quaternion.Euler(0f, 0f, -_mapRect.localEulerAngles.z)
                : Quaternion.identity;
            var alive = new HashSet<int>();
            var activeCount = 0;

            foreach (var marker in markers)
            {
                if (!marker.Visible)
                    continue;

                alive.Add(marker.Id);
                if (_markerViews.TryGetValue(marker.Id, out var view) && !view.MatchesTemplate(marker.VisualTemplate))
                {
                    view.Destroy();
                    _markerViews.Remove(marker.Id);
                    view = null;
                }

                if (view == null && !_markerViews.TryGetValue(marker.Id, out view))
                {
                    view = CreateMarkerView(marker);
                    _markerViews[marker.Id] = view;
                }

                var mapPosition = ProjectMarkerToMapLocal(marker, mapArea);
                view.SetMapPosition(mapPosition);
                view.SetIcon(marker, _markerSprite);
                view.SetVisualTransform(markerRotation, markerScreenScale);
                SetMarkerRootActive(view, true);
                activeCount++;
            }

            foreach (var pair in _markerViews)
                if (!alive.Contains(pair.Key))
                    SetMarkerRootActive(pair.Value, false);

            Log.Every("markers-hud-" + _attachedMapKey, 10f,
                $"markers: hud active={activeCount}/{markers.Count} map={_attachedMapKey} rotate={_settings.RotateMap}");
        }

        private static void SetMarkerRootActive(RuntimeMarkerView view, bool active)
        {
            if (view?.Root != null && view.Root.activeSelf != active)
                view.Root.SetActive(active);
        }

        public bool TryEnsureMapVisual(MapDefinition mapDefinition)
        {
            if (_instancedMapVisual != null && !MapMatches(mapDefinition))
            {
                Log.Info($"hud: map changed from {_attachedMapName} to {MapObjectNames.DescribeUnityObject(mapDefinition)}; replacing visual");
                DestroyMapVisual();
            }

            if (_instancedMapVisual != null && _attachedTemplateVersion != MapVisualCapture.TemplateVersionFor(mapDefinition))
            {
                Log.Info($"hud: captured map template changed version={_attachedTemplateVersion}->{MapVisualCapture.TemplateVersionFor(mapDefinition)}; replacing visual");
                DestroyMapVisual();
            }

            if (_instancedMapVisual != null)
                return true;

            if (Time.realtimeSinceStartup < _nextAttachAttempt)
                return false;

            _nextAttachAttempt = Time.realtimeSinceStartup + (MapVisualCapture.HasTemplateFor(mapDefinition) ? 0.10f : 1.25f);

            TryAttachVanillaMapVisual(mapDefinition);
            return _instancedMapVisual != null;
        }

        public void Dispose()
        {
            if (_root != null)
                Object.Destroy(_root);
            ClearRuntimeReferencesAfterUnityDestroyed("Dispose");
            _hasBuiltRoot = false;
        }

        public void InvalidateAttachedMap(string reason)
        {
            if (_instancedMapVisual == null)
                return;

            Log.Info($"hud: invalidating attached map reason={reason} attached={_attachedMapName}");
            DestroyMapVisual();
        }

        private RuntimeMarkerView CreateMarkerView(MarkerSnapshot marker)
        {
            var obj = new GameObject($"Marker_{marker.Id}");
            obj.transform.SetParent(_markerLayer, false);
            var rect = obj.AddComponent<RectTransform>();
            UiRectTransforms.AnchorAt(rect, UiRectTransforms.Center, UiRectTransforms.Center);

            if (marker.VisualTemplate != null)
            {
                var visual = Object.Instantiate(marker.VisualTemplate, rect);
                visual.name = "Visual";
                visual.hideFlags = HideFlags.HideAndDontSave;
                visual.SetActive(true);
                MarkerVisualDiagnostics.LogRuntimeInstance(_settings, marker, visual, _attachedMapKey);
                var visualRect = visual.GetComponent<RectTransform>();
                if (visualRect != null)
                {
                    var capturedSize = ResolveRectSize(visualRect, marker.Size);
                    UiRectTransforms.CenterOnParent(visualRect, Vector2.zero, capturedSize);
                    visualRect.localScale = Vector3.one * GetUniformScale(capturedSize, marker.Size);
                }

                return RuntimeMarkerView.ClonedVisual(obj, rect, visual, marker.VisualTemplate);
            }

            var image = obj.AddComponent<Image>();
            image.sprite = marker.Sprite != null ? marker.Sprite : _markerSprite;
            image.color = marker.Color;
            image.raycastTarget = false;
            image.maskable = true;
            return RuntimeMarkerView.Fallback(obj, rect, image);
        }

        private static Vector2 ResolveRectSize(RectTransform rect, Vector2 fallback)
        {
            if (TryGetValidSize(rect != null ? rect.rect.size : Vector2.zero, out var rectSize))
                return rectSize;

            if (TryGetValidSize(rect != null ? rect.sizeDelta : Vector2.zero, out var sizeDelta))
                return sizeDelta;

            if (TryGetValidSize(fallback, out var fallbackSize))
                return fallbackSize;

            return new Vector2(32f, 32f);
        }

        private static bool TryGetValidSize(Vector2 size, out Vector2 absoluteSize)
        {
            absoluteSize = new Vector2(Mathf.Abs(size.x), Mathf.Abs(size.y));
            return absoluteSize.x >= 1f && absoluteSize.y >= 1f;
        }

        private static float GetUniformScale(Vector2 sourceSize, Vector2 targetSize)
        {
            var source = new Vector2(Mathf.Max(1f, Mathf.Abs(sourceSize.x)), Mathf.Max(1f, Mathf.Abs(sourceSize.y)));
            var target = new Vector2(Mathf.Max(1f, Mathf.Abs(targetSize.x)), Mathf.Max(1f, Mathf.Abs(targetSize.y)));
            var ratio = Vector2.Scale(target, new Vector2(1f / source.x, 1f / source.y));
            return Mathf.Clamp(Mathf.Min(ratio.x, ratio.y), 0.01f, 100f);
        }

        private void TryAttachVanillaMapVisual(MapDefinition mapDefinition)
        {
            if (_mapRect == null || _instancedMapVisual != null)
                return;

            if (MapVisualCapture.TryCloneInto(_mapRect, mapDefinition, out _instancedMapVisual, out _mapGeometry))
            {
                _attachedMapKey = MapObjectNames.MapKey(mapDefinition);
                _attachedMapName = MapObjectNames.DescribeUnityObject(mapDefinition);
                _attachedTemplateVersion = MapVisualCapture.TemplateVersionFor(mapDefinition);
                Log.Info("hud: attached map visual map=" + _attachedMapKey +
                         " name=" + _attachedMapName +
                         " templateVersion=" + _attachedTemplateVersion +
                         " geometry=" + MapObjectNames.DescribeGeometry(_mapGeometry));
                ResetStaticMapVisualPreparation();
                OrderMapSiblings();
                _playerMarker?.RefreshSprites();
            }
        }

        private void DisableBrokenStaticAndZoomBoundVisuals()
        {
            if (_instancedMapVisual == null)
                return;

            var changed = 0;
            foreach (var transform in _instancedMapVisual.GetComponentsInChildren<Transform>(true))
            {
                if (transform == null || !IsBrokenStaticOrZoomBoundVisual(transform))
                    continue;

                if (transform.gameObject.activeSelf)
                {
                    transform.gameObject.SetActive(false);
                    changed++;
                }
            }

            if (changed > 0)
                Log.Info("hud: disabled static/zoom-bound map visuals changed=" + changed + " map=" + _attachedMapKey);
        }

        private bool IsBrokenStaticOrZoomBoundVisual(Transform transform)
        {
            if (transform == null)
                return false;

            var originalPath = MapObjectNames.PathOf(transform);
            if (IsVanillaPlayerViewportVisual(transform, originalPath))
                return true;

            var path = originalPath.ToLowerInvariant();
            if (!path.Contains("/fog_static/"))
                return false;

            var clean = MapObjectNames.CleanCloneName(transform.name);
            return string.Equals(clean, "OutsideFogNegativeMasked", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(clean, "Negative Mask", StringComparison.OrdinalIgnoreCase) ||
                   clean.IndexOf("NegativeMask", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsVanillaPlayerViewportVisual(Transform transform, string path)
        {
            if (transform == null)
                return false;

            var normalized = (path ?? string.Empty).Replace('\\', '/');
            if (normalized.IndexOf("BelowMarkersContainer/Cone", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            var image = transform.GetComponent<Image>();
            var spriteName = image != null && image.sprite != null ? image.sprite.name : string.Empty;
            return string.Equals(spriteName, "fxPlayerMarkerCone", StringComparison.OrdinalIgnoreCase);
        }

        private void OrderMapSiblings()
        {
            _instancedMapVisual?.transform.SetAsFirstSibling();

            _markerLayer?.SetAsLastSibling();

            _cloudOverlayLayer?.SetAsLastSibling();

            _playerLayer?.SetAsLastSibling();
        }

        private void DestroyMapVisual()
        {
            if (_instancedMapVisual != null)
                Object.Destroy(_instancedMapVisual);
            _instancedMapVisual = null;
            _mapGeometry = MapGeometry.Empty;
            _attachedMapKey = "-";
            _attachedMapName = "-";
            _lastAppliedFogStateKey = string.Empty;
            _attachedTemplateVersion = -1;
            ResetStaticMapVisualPreparation();
            _mapBackgroundOriginalAlpha.Clear();
        }


        private bool MapMatches(MapDefinition mapDefinition)
        {
            if (mapDefinition == null || string.IsNullOrEmpty(_attachedMapKey) || _attachedMapKey == "-")
                return true;

            return string.Equals(MapObjectNames.MapKey(mapDefinition), _attachedMapKey, StringComparison.Ordinal);
        }


        private void UpdatePlayerMarker(float yawUi)
        {
            if (_playerLayer == null || _playerMarker == null)
                return;

            _playerLayer.anchoredPosition = Vector2.zero;
            _playerLayer.sizeDelta = Vector2.zero;
            _playerLayer.localScale = Vector3.one;
            _playerLayer.localRotation = Quaternion.identity;

            var facingRotation = _settings.RotateMap
                ? Quaternion.identity
                : Quaternion.Euler(0f, 0f, yawUi);

            _playerMarker.UpdateLayout(_settings.IconScale, facingRotation);
        }

        private void ApplyClipShape()
        {
            var circle = _settings.MapShape == MinimapMapShape.Circle;

            if (_rectMask != null)
                _rectMask.enabled = !circle;

            if (_shapeMask != null)
            {
                _shapeMask.enabled = circle;
                _shapeMask.showMaskGraphic = false;
            }

            if (_shapeMaskImage != null)
            {
                _shapeMaskImage.enabled = circle;
                _shapeMaskImage.SetVerticesDirty();
            }
        }

        private float GetViewportEdgeOffsetPixels()
        {
            var viewportBasis = Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height));
            return viewportBasis * Mathf.Clamp(_settings.EdgeOffsetPercent, 0f, 15f) * 0.01f;
        }

        private Rect GetCurrentWorldArea()
        {
            if (_mapGeometry.HasWorldArea)
                return _mapGeometry.WorldArea;

            return new Rect(-1f, -1f, 2f, 2f);
        }

        private Rect GetCurrentMapArea()
        {
            if (_mapGeometry.HasMapArea)
                return _mapGeometry.MapArea;

            var cloneRect = _instancedMapVisual != null ? _instancedMapVisual.GetComponent<RectTransform>() : null;
            var width = cloneRect != null && Mathf.Abs(cloneRect.rect.width) > 1f ? Mathf.Abs(cloneRect.rect.width) : 1024f;
            var height = cloneRect != null && Mathf.Abs(cloneRect.rect.height) > 1f ? Mathf.Abs(cloneRect.rect.height) : 1024f;
            return new Rect(-width * 0.5f, -height * 0.5f, width, height);
        }

        private static Vector2 GetNativeMapSize(Rect mapArea)
        {
            var width = Mathf.Abs(mapArea.width);
            var height = Mathf.Abs(mapArea.height);
            if (width < 1f) width = 1024f;
            if (height < 1f) height = 1024f;
            return new Vector2(width, height);
        }

        private Vector2 ProjectMarkerToMapLocal(MarkerSnapshot marker, Rect mapArea)
        {
            return marker.HasMapPosition
                ? marker.MapPosition
                : ProjectWorldToMapLocal(marker.WorldPosition, mapArea);
        }

        private Vector2 ProjectWorldToMapLocal(Vector3 world, Rect mapArea)
        {
            if (_mapGeometry.HasVanillaProjection)
                return ProjectWorldToVanillaMapLocal(world, _mapGeometry.WorldArea, _mapGeometry.ProjectionMapArea, mapArea);

            var worldArea = GetCurrentWorldArea();
            if (Mathf.Abs(worldArea.width) <= 1f || Mathf.Abs(worldArea.height) <= 1f || Mathf.Abs(mapArea.width) <= 1f || Mathf.Abs(mapArea.height) <= 1f)
                return Vector2.zero;

            var normalized = new Vector2(
                Mathf.InverseLerp(worldArea.xMin, worldArea.xMax, world.x),
                Mathf.InverseLerp(worldArea.yMin, worldArea.yMax, world.z));
            return LerpRect(mapArea, normalized);
        }

        private static Vector2 ProjectWorldToVanillaMapLocal(Vector3 world, Rect worldArea, Rect projectionMapArea, Rect clampArea)
        {
            var worldWidth = worldArea.xMax - worldArea.xMin;
            var worldHeight = worldArea.yMax - worldArea.yMin;
            if (Mathf.Abs(worldWidth) <= 1f || Mathf.Abs(worldHeight) <= 1f)
                return Vector2.zero;

            var scaleX = (projectionMapArea.xMax - projectionMapArea.xMin) / worldWidth;
            var scaleY = (projectionMapArea.yMax - projectionMapArea.yMin) / worldHeight;
            var offsetX = projectionMapArea.xMax - worldArea.xMax * scaleX;
            var offsetY = projectionMapArea.yMax - worldArea.yMax * scaleY;

            var x = offsetX + world.x * scaleX;
            var y = offsetY + world.z * scaleY;

            if (Mathf.Abs(clampArea.width) > 1f && Mathf.Abs(clampArea.height) > 1f)
            {
                x = Mathf.Clamp(x, Mathf.Min(clampArea.xMin, clampArea.xMax), Mathf.Max(clampArea.xMin, clampArea.xMax));
                y = Mathf.Clamp(y, Mathf.Min(clampArea.yMin, clampArea.yMax), Mathf.Max(clampArea.yMin, clampArea.yMax));
            }

            return new Vector2(x, y);
        }

        private void EnsureInstancedMapVisualNativeSize(Vector2 nativeMapSize)
        {
            if (_instancedMapVisual == null)
                return;

            var rect = _instancedMapVisual.GetComponent<RectTransform>();
            if (rect == null)
                return;

            UiRectTransforms.CenterOnParent(rect, Vector2.zero);
            rect.sizeDelta = nativeMapSize;
        }

        private static Vector2 LerpRect(Rect rect, Vector2 normalized)
        {
            return new Vector2(
                Mathf.LerpUnclamped(rect.xMin, rect.xMax, normalized.x),
                Mathf.LerpUnclamped(rect.yMin, rect.yMax, normalized.y));
        }

        private static Vector2 RotatePoint(Vector2 point, float degrees)
        {
            // Unity has Vector2.Lerp, but no Vector2.Rotate; direct sin/cos keeps this branch allocation-free.
            if (Mathf.Approximately(degrees, 0f))
                return point;

            var radians = degrees * Mathf.Deg2Rad;
            var sin = Mathf.Sin(radians);
            var cos = Mathf.Cos(radians);
            return new Vector2(point.x * cos - point.y * sin, point.x * sin + point.y * cos);
        }


        private sealed class PlayerMarkerView
        {
            private const float BasePixelSize = 64f;

            private readonly PlayerImage _cone;
            private readonly PlayerImage _frame;
            private readonly PlayerImage _arrow;
            private readonly PlayerImage _bea;

            private Vector2 _capturedConeSize;

            public readonly RectTransform Root;
            private readonly RectTransform _facingRoot;

            private PlayerMarkerView(RectTransform root, RectTransform facingRoot, PlayerImage cone, PlayerImage frame, PlayerImage arrow, PlayerImage bea)
            {
                Root = root;
                _facingRoot = facingRoot;
                _cone = cone;
                _frame = frame;
                _arrow = arrow;
                _bea = bea;
            }

            public static PlayerMarkerView Create(Transform parent)
            {
                var root = CreateRectTransform("PlayerMarker", parent);
                UiRectTransforms.CenterOnParent(root, Vector2.zero, new Vector2(BasePixelSize, BasePixelSize));

                var facingRoot = CreateRectTransform("Facing", root);
                UiRectTransforms.CenterOnParent(facingRoot, Vector2.zero);

                var cone = PlayerImage.Create("ViewCone", facingRoot, new Vector2(0.5f, 0f));
                var frame = PlayerImage.Create("FacingFrame", facingRoot, UiRectTransforms.Center);
                var arrow = PlayerImage.Create("FacingArrow", facingRoot, UiRectTransforms.Center);
                var bea = PlayerImage.Create("BeaIcon", root, UiRectTransforms.Center);

                cone.Rect.SetAsFirstSibling();
                bea.Rect.SetAsLastSibling();
                return new PlayerMarkerView(root, facingRoot, cone, frame, arrow, bea);
            }

            public void RefreshSprites()
            {
                Sprite sprite;
                Color color;
                if (MapVisualCapture.TryGetCapturedBeaIcon(out sprite, out color))
                    _bea.Set(sprite, color);
                else
                    _bea.SetEnabled(false);

                Vector2 coneSize;
                if (MapVisualCapture.TryGetCapturedPlayerCone(out sprite, out color, out coneSize))
                {
                    _capturedConeSize = coneSize;
                    _cone.Set(sprite, color);
                }
                else
                {
                    _capturedConeSize = Vector2.zero;
                    _cone.SetEnabled(false);
                }

                Sprite frameSprite;
                Sprite arrowSprite;
                Color frameColor;
                Color arrowColor;
                if (MapVisualCapture.TryGetCapturedPlayerFacing(out frameSprite, out frameColor, out arrowSprite, out arrowColor))
                {
                    _frame.Set(frameSprite, frameColor);
                    _arrow.Set(arrowSprite, arrowColor);
                }
                else
                {
                    _frame.SetEnabled(false);
                    _arrow.SetEnabled(false);
                }

            }

            public void UpdateLayout(float iconScale, Quaternion facingRotation)
            {
                var markerSize = BasePixelSize * Mathf.Clamp(iconScale, 0.01f, 100f);
                UiRectTransforms.CenterOnParent(Root, Vector2.zero, new Vector2(markerSize, markerSize));
                UiRectTransforms.CenterOnParent(_facingRoot, Vector2.zero);
                _facingRoot.localRotation = facingRotation;
                ApplySpriteLayout();
            }

            private void ApplySpriteLayout()
            {
                var baseSize = Mathf.Max(1f, Mathf.Min(Root.rect.width, Root.rect.height));

                LayoutCone(baseSize);
                LayoutCentered(_frame, baseSize);
                LayoutCentered(_arrow, baseSize);
                LayoutCentered(_bea, baseSize * 0.56f);

                _cone.Rect.SetAsFirstSibling();
                _bea.Rect.SetAsLastSibling();
            }

            private void LayoutCone(float baseSize)
            {
                UiRectTransforms.AnchorAt(_cone.Rect, UiRectTransforms.Center, new Vector2(0.5f, 0f));
                _cone.Rect.anchoredPosition = Vector2.zero;
                _cone.Rect.sizeDelta = GetConeSize(baseSize);
                _cone.Rect.localRotation = Quaternion.identity;
            }

            private static void LayoutCentered(PlayerImage image, float size)
            {
                UiRectTransforms.AnchorAt(image.Rect, UiRectTransforms.Center, UiRectTransforms.Center);
                image.Rect.anchoredPosition = new Vector2(0f, GetSpriteCircleCenterOffset(image.Image.sprite, size));
                image.Rect.sizeDelta = new Vector2(size, size);
                image.Rect.localRotation = Quaternion.identity;
            }

            private Vector2 GetConeSize(float baseSize)
            {
                if (Mathf.Abs(_capturedConeSize.x) <= 1f || Mathf.Abs(_capturedConeSize.y) <= 1f)
                    return new Vector2(baseSize * 1.75f, baseSize * 1.75f);

                var width = Mathf.Clamp(Mathf.Abs(_capturedConeSize.x), baseSize, baseSize * 3f);
                var height = Mathf.Clamp(Mathf.Abs(_capturedConeSize.y), baseSize, baseSize * 3f);
                var scale = baseSize / BasePixelSize;
                return new Vector2(width * scale, height * scale);
            }

            private static float GetSpriteCircleCenterOffset(Sprite sprite, float drawnHeight)
            {
                if (sprite == null || sprite.rect.height <= 0f)
                    return 0f;

                var drawnWidth = drawnHeight * (sprite.rect.width / sprite.rect.height);
                return (drawnHeight - drawnWidth) * 0.5f;
            }

            private sealed class PlayerImage
            {
                public readonly RectTransform Rect;
                public readonly Image Image;

                private PlayerImage(RectTransform rect, Image image)
                {
                    Rect = rect;
                    Image = image;
                }

                public static PlayerImage Create(string name, Transform parent, Vector2 pivot)
                {
                    var rect = CreateRectTransform(name, parent);
                    UiRectTransforms.AnchorAt(rect, UiRectTransforms.Center, pivot);
                    rect.anchoredPosition = Vector2.zero;
                    rect.sizeDelta = new Vector2(BasePixelSize, BasePixelSize);

                    var image = rect.gameObject.AddComponent<Image>();
                    image.raycastTarget = false;
                    image.maskable = true;
                    image.preserveAspect = true;
                    image.enabled = false;
                    return new PlayerImage(rect, image);
                }

                public void Set(Sprite sprite, Color color)
                {
                    if (sprite == null)
                    {
                        SetEnabled(false);
                        return;
                    }

                    Image.sprite = sprite;
                    Image.color = color;
                    Image.enabled = true;
                }

                public void SetEnabled(bool enabled)
                {
                    Image.enabled = enabled;
                }
            }
        }


        private sealed class RuntimeMarkerView
        {
            public readonly GameObject Root;
            public readonly RectTransform Rect;
            public readonly Image Image;
            public readonly GameObject Visual;
            public readonly GameObject SourceTemplate;

            private RuntimeMarkerView(GameObject root, RectTransform rect, Image image, GameObject visual, GameObject sourceTemplate)
            {
                Root = root;
                Rect = rect;
                Image = image;
                Visual = visual;
                SourceTemplate = sourceTemplate;
            }

            public static RuntimeMarkerView Fallback(GameObject root, RectTransform rect, Image image)
            {
                return new RuntimeMarkerView(root, rect, image, null, null);
            }

            public static RuntimeMarkerView ClonedVisual(GameObject root, RectTransform rect, GameObject visual, GameObject sourceTemplate)
            {
                return new RuntimeMarkerView(root, rect, null, visual, sourceTemplate);
            }

            public bool MatchesTemplate(GameObject template)
            {
                return SourceTemplate == template;
            }

            public void Destroy()
            {
                if (Root != null)
                    Object.Destroy(Root);
            }


            public void SetMapPosition(Vector2 mapPosition)
            {
                if (Rect != null)
                    Rect.anchoredPosition = mapPosition;
            }

            public void SetIcon(MarkerSnapshot marker, Sprite fallbackSprite)
            {
                if (Rect == null)
                    return;

                var width = Mathf.Clamp(Mathf.Abs(marker.Size.x) > 1f ? Mathf.Abs(marker.Size.x) : 32f, 12f, 128f);
                var height = Mathf.Clamp(Mathf.Abs(marker.Size.y) > 1f ? Mathf.Abs(marker.Size.y) : 32f, 12f, 128f);
                Rect.sizeDelta = new Vector2(width, height);

                if (Image == null)
                    return;

                Image.sprite = marker.Sprite != null ? marker.Sprite : fallbackSprite;
                Image.color = marker.Color;
                Image.preserveAspect = true;
            }

            public void SetVisualTransform(Quaternion markerRotation, float markerScale)
            {
                if (Rect == null)
                    return;

                Rect.localScale = Vector3.one * markerScale;
                Rect.localRotation = markerRotation;
            }
        }
    }
}
