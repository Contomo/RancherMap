using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using HarmonyInstance = HarmonyLib.Harmony;
using Il2CppMonomiPark.SlimeRancher.Map;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Sr2Map = Il2CppMonomiPark.SlimeRancher.UI.Map.Map;
using Sr2MapUI = Il2CppMonomiPark.SlimeRancher.UI.Map.MapUI;
using Sr2MapUIRoot = Il2CppMonomiPark.SlimeRancher.UI.Map.MapUIRoot;

namespace rancher_minimap
{
    /// <summary>
    /// Captures facts from the vanilla map lifecycle instead of guessing asset keys forever.
    ///
    /// Ghidra shows the reliable path:
    /// MapUI.TryInitializeViewModel -> MapDirector.GetOrCreateViewModel(_mapPrefabMapping)
    /// MapUI.OpenMap(mapDefinition) -> instantiate the mapped map prefab into the large map UI.
    ///
    /// This probe logs the actual map objects/images vanilla creates and keeps a disabled
    /// minimap-owned template cloned from the vanilla Map component root.
    /// </summary>
    internal sealed class MapVisualCapture
    {
        private static MapVisualCapture Current;
        private readonly HarmonyInstance _harmony;
        private readonly MinimapSettings _settings;
        private bool _installed;
        private readonly Dictionary<string, MapRuntimeRecord> _mapsByKey = new Dictionary<string, MapRuntimeRecord>(StringComparer.Ordinal);
        private Sprite _capturedBeaIcon;
        private Color _capturedBeaIconColor = Color.clear;
        private Sprite _capturedFacingFrame;
        private Color _capturedFacingFrameColor = Color.clear;
        private Sprite _capturedFacingArrow;
        private Color _capturedFacingArrowColor = Color.clear;
        private Sprite _capturedPlayerCone;
        private Color _capturedPlayerConeColor = Color.clear;
        private Vector2 _capturedPlayerConeSize = new Vector2(96f, 96f);
        private readonly List<GameObject> _markerVisualTemplates = new List<GameObject>();
        private string _lastMapName = "-";
        private bool _loggedImageInventory;
        private int _openMapSeen;
        private int _fogStateVersion;
        private int _markerStateVersion;
        private int _templateVersion;
        private float _pendingDynamicRefreshAt;
        private float _pendingDelayedVisualCaptureAt;
        private GameObject _pendingDelayedVisualRoot;
        private string _pendingDelayedVisualMapKey = "-";
        private string _pendingDelayedVisualSource;

        public static string Status => Current == null ? "capture-not-installed" : Current.DescribeStatus();
        public static bool HasCapturedTemplate => Current != null && Current._mapsByKey.Values.Any(record => record.Template != null);
        public static int FogStateVersion => Current?._fogStateVersion ?? 0;
        public static int MarkerStateVersion => Current?._markerStateVersion ?? 0;
        public static int TemplateVersion => Current?._templateVersion ?? 0;

        public static int TemplateVersionFor(MapDefinition mapDefinition)
        {
            if (Current == null)
                return 0;

            return Current.TryGetRecordByKeyOrAlias(MapObjectNames.MapKey(mapDefinition), out var record)
                ? record.TemplateVersion
                : 0;
        }

        public MapVisualCapture(HarmonyInstance harmony, MinimapSettings settings)
        {
            _harmony = harmony;
            _settings = settings;
            Current = this;
        }

        private sealed class MapRuntimeRecord
        {
            public readonly string Key;
            public MapDefinition Definition;
            public GameObject Template;
            public MapGeometry Geometry = MapGeometry.Empty;
            public List<MarkerSnapshot> Markers = new List<MarkerSnapshot>();
            public readonly MapVisualState VisualState = new MapVisualState();
            public readonly HashSet<string> Aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public int TemplateVersion;

            public MapRuntimeRecord(string key)
            {
                Key = string.IsNullOrEmpty(key) ? "-" : key;
                Aliases.Add(Key);
            }
        }

        private sealed class MapVisualState
        {
            public Dictionary<string, bool> GraphicStates = new Dictionary<string, bool>();
            public Dictionary<string, bool> TransformStates = new Dictionary<string, bool>();
            public Dictionary<string, bool> FogRevealStates = new Dictionary<string, bool>();
            public Dictionary<string, float> ZoneFogCanvasAlphaStates = new Dictionary<string, float>();
        }

        public void Install()
        {
            if (_installed)
                return;

            TryPatch(typeof(Sr2MapUIRoot), "OpenMap", null, nameof(MapUIRootOpenMapPostfix));
            TryPatch(typeof(Sr2MapUIRoot), "TryOpenMap", null, nameof(MapUIRootTryOpenMapPostfix));
            TryPatch(typeof(Sr2MapUI), "TryInitializeViewModel", null, nameof(MapUITryInitializeViewModelPostfix));
            TryPatch(typeof(Sr2MapUI), "HandleMapPageTabbed", null, nameof(MapUIHandleMapPageTabbedPostfix));
            TryPatch(typeof(Sr2MapUI), "OpenPlayerCurrentMap", null, nameof(MapUIOpenPlayerCurrentMapPostfix));
            TryPatch(typeof(Sr2MapUI), "OpenMap", null, nameof(MapUIOpenMapPostfix));
            TryPatch(typeof(Sr2MapUI), "OnCloseInput", null, nameof(MapUIOnCloseInputPostfix));
            TryPatch(typeof(MapDirector), "NotifyZoneUnlocked", null, nameof(MapDirectorNotifyZoneUnlockedPostfix));
            TryPatch(typeof(MapDirector), "RegisterMapUnlockAnimation", null, nameof(MapDirectorRegisterMapUnlockAnimationPostfix));

            _installed = true;
            Log.Info("install", "map-capture lifecycle patches installed");
        }

        public void Dispose()
        {
            foreach (var record in _mapsByKey.Values)
                DestroyTemplate(record);
            _mapsByKey.Clear();
            _capturedBeaIcon = null;
            _capturedFacingFrame = null;
            _capturedFacingArrow = null;
            _capturedPlayerCone = null;
            _capturedPlayerConeSize = new Vector2(96f, 96f);
            foreach (var template in _markerVisualTemplates)
            {
                if (template != null)
                    Object.Destroy(template);
            }
            _markerVisualTemplates.Clear();
            _fogStateVersion = 0;
            _markerStateVersion = 0;
            _templateVersion = 0;
            _pendingDynamicRefreshAt = 0f;
            _pendingDelayedVisualCaptureAt = 0f;
            _pendingDelayedVisualRoot = null;
            _pendingDelayedVisualMapKey = "-";
            _pendingDelayedVisualSource = null;

            if (ReferenceEquals(Current, this))
                Current = null;
        }

        private static void DestroyTemplate(MapRuntimeRecord record)
        {
            if (record?.Template != null)
                Object.Destroy(record.Template);
            if (record != null)
                record.Template = null;
        }

        public static bool TryCloneInto(Transform parent, MapDefinition mapDefinition, out GameObject clone, out MapGeometry geometry)
        {
            clone = null;
            geometry = MapGeometry.Empty;
            return Current != null && Current.TryCloneIntoInstance(parent, mapDefinition, out clone, out geometry);
        }

        public static bool HasTemplateFor(MapDefinition mapDefinition)
        {
            return Current != null && Current.PickTemplateFor(mapDefinition) != null;
        }


        public static bool TryGetCapturedBeaIcon(out Sprite sprite, out Color color)
        {
            if (Current == null)
            {
                sprite = null;
                color = Color.white;
                return false;
            }

            sprite = Current._capturedBeaIcon;
            color = Current._capturedBeaIconColor;
            return sprite != null;
        }

        public static bool TryGetCapturedPlayerFacing(out Sprite frameSprite, out Color frameColor, out Sprite arrowSprite, out Color arrowColor)
        {
            frameSprite = null;
            frameColor = Color.white;
            arrowSprite = null;
            arrowColor = Color.white;

            if (Current == null)
                return false;

            frameSprite = Current._capturedFacingFrame;
            frameColor = Current._capturedFacingFrameColor;
            arrowSprite = Current._capturedFacingArrow;
            arrowColor = Current._capturedFacingArrowColor;
            return frameSprite != null || arrowSprite != null;
        }

        public static bool TryGetCapturedPlayerCone(out Sprite coneSprite, out Color coneColor, out Vector2 coneSize)
        {
            coneSprite = null;
            coneColor = Color.white;
            coneSize = new Vector2(96f, 96f);

            if (Current == null || Current._capturedPlayerCone == null)
                return false;

            coneSprite = Current._capturedPlayerCone;
            coneColor = Current._capturedPlayerConeColor;
            coneSize = Current._capturedPlayerConeSize;
            return true;
        }

        public static bool TryGetCapturedMarkers(MapDefinition mapDefinition, out IReadOnlyList<MarkerSnapshot> markers)
        {
            markers = Array.Empty<MarkerSnapshot>();
            if (Current == null)
                return false;

            var key = MapObjectNames.MapKey(mapDefinition);
            if (Current.TryGetRecord(key, out var record) && record.Markers != null)
            {
                markers = record.Markers;
                return true;
            }

            return false;
        }

        public static bool TryGetCapturedMarkersForKey(string mapKey, out IReadOnlyList<MarkerSnapshot> markers)
        {
            markers = Array.Empty<MarkerSnapshot>();
            if (Current == null || string.IsNullOrEmpty(mapKey) || mapKey == "-")
                return false;

            if (Current.TryGetRecord(mapKey, out var record) && record.Markers != null)
            {
                markers = record.Markers;
                return true;
            }

            return false;
        }

        public static bool TryInferMapKeyFromWorldPosition(Vector3 worldPosition, out string mapKey)
        {
            mapKey = "-";
            if (Current == null)
                return false;

            return Current.TryInferMapKeyFromWorldPositionInternal(worldPosition, out mapKey);
        }


        public static void ApplyCapturedFogStates(GameObject obj, string mapKey, string reason)
        {
            Current?.ApplyCapturedVisualStatesTo(obj, mapKey, reason);
        }

        private MapRuntimeRecord GetOrCreateRecord(string key)
        {
            if (string.IsNullOrEmpty(key) || key == "-")
                key = "UnknownMap";

            if (!_mapsByKey.TryGetValue(key, out var record))
            {
                record = new MapRuntimeRecord(key);
                _mapsByKey[key] = record;
            }

            return record;
        }

        private bool TryGetRecord(string key, out MapRuntimeRecord record)
        {
            record = null;
            return !string.IsNullOrEmpty(key) && key != "-" && _mapsByKey.TryGetValue(key, out record);
        }

        private bool TryGetRecordByKeyOrAlias(string keyOrAlias, out MapRuntimeRecord record)
        {
            if (TryGetRecord(keyOrAlias, out record))
                return true;

            if (string.IsNullOrEmpty(keyOrAlias) || keyOrAlias == "-")
                return false;

            foreach (var candidate in _mapsByKey.Values)
            {
                if (candidate != null && candidate.Aliases.Contains(keyOrAlias))
                {
                    record = candidate;
                    return true;
                }
            }

            record = null;
            return false;
        }

        private static void RegisterObservedMapAlias(MapRuntimeRecord record, GameObject mapObject)
        {
            if (record == null || mapObject == null)
                return;

            var directName = MapObjectNames.CleanCloneName(mapObject.name);
            if (IsMapIdentityAlias(directName))
                AddAlias(record, directName);
        }

        private static void AddAlias(MapRuntimeRecord record, string alias)
        {
            if (record == null || !IsMapIdentityAlias(alias))
                return;

            record.Aliases.Add(MapObjectNames.CleanCloneName(alias));
        }

        private static bool IsMapIdentityAlias(string alias)
        {
            var clean = MapObjectNames.CleanCloneName(alias);
            if (string.IsNullOrEmpty(clean) || clean == "-")
                return false;
            if (string.Equals(clean, "Map", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(clean, "Maps", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(clean, "MapUI", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(clean, "MapHolder", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(clean, "Content", StringComparison.OrdinalIgnoreCase))
                return false;

            return clean.EndsWith("Map", StringComparison.OrdinalIgnoreCase);
        }

        private void TryPatch(Type type, string gameMethodName, string prefixName, string postfixName)
        {
            var original = AccessTools.Method(type, gameMethodName);
            if (original == null)
            {
                Log.Warn($"map-capture: method missing {type.FullName}.{gameMethodName}");
                return;
            }

            var self = typeof(MapVisualCapture);
            var prefix = prefixName == null ? null : new HarmonyMethod(AccessTools.Method(self, prefixName));
            var postfix = postfixName == null ? null : new HarmonyMethod(AccessTools.Method(self, postfixName));
            _harmony.Patch(original, prefix: prefix, postfix: postfix);
        }

        private bool TryCloneIntoInstance(Transform parent, MapDefinition mapDefinition, out GameObject clone, out MapGeometry geometry)
        {
            clone = null;
            geometry = MapGeometry.Empty;

            if (parent == null)
                return false;

            var template = PickTemplateFor(mapDefinition);
            return template != null &&
                   TryClonePrefabLikeObject(template, parent, "VanillaMapVisualClone_FromCapturedTemplate", "captured map template", MapObjectNames.MapKey(mapDefinition), PickGeometryFor(mapDefinition), out clone, out geometry);
        }

        private static bool TryClonePrefabLikeObject(GameObject source, Transform parent, string cloneName, string sourceLabel, string mapKey, MapGeometry fallbackGeometry, out GameObject clone, out MapGeometry geometry)
        {
            clone = null;
            geometry = MapGeometry.Empty;

            if (source == null || parent == null)
                return false;

            try
            {
                clone = Object.Instantiate(source, parent);
                clone.name = cloneName;
                clone.hideFlags = HideFlags.HideAndDontSave;
                clone.SetActive(true);
                MapRectTransforms.NormalizeCloneRoot(clone);
                geometry = MergeGeometry(fallbackGeometry, CaptureGeometry(clone));
                DisableRaycastersKeepCanvases(clone);
                StripNonVisualBehaviours(clone);
                StripMapUiMarkerBranches(clone);
                StripPlayerMarkerBranches(clone);
                IsolateCloneMaterials(clone);
                EnableNonFogVisualComponents(clone);
                Current?.ApplyCapturedVisualStatesTo(clone, mapKey, sourceLabel);
                RefreshPortalLineGraphics(clone, sourceLabel + ".clone");
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("map-capture: failed to clone " + sourceLabel + ": " + ex.GetType().Name);
                return false;
            }
        }

        private GameObject PickTemplateFor(MapDefinition mapDefinition)
        {
            return TryGetRecord(MapObjectNames.MapKey(mapDefinition), out var record) ? record.Template : null;
        }


        private string DescribeStatus()
        {
            var templates = string.Join(",", _mapsByKey.Values
                .Where(record => record.Template != null)
                .Select(record => record.Key + "=" + MapObjectNames.DescribeUnityObject(record.Template)));
            if (string.IsNullOrEmpty(templates))
                templates = "-";

            return $"capture openMapSeen={_openMapSeen} templateVersion={_templateVersion} lastMap={_lastMapName} maps={templates} beaIcon={MapObjectNames.DescribeUnityObject(_capturedBeaIcon)}";
        }

        private static void MapUIRootOpenMapPostfix(Sr2MapUIRoot __instance)
        {
            Current?.ObserveMapUiRoot(__instance);
        }

        private static void MapUIRootTryOpenMapPostfix(Sr2MapUIRoot __instance)
        {
            Current?.ObserveMapUiRoot(__instance);
        }

        private static void MapUITryInitializeViewModelPostfix(Sr2MapUI __instance)
        {
            Current?.ObserveMapUI("MapUI.TryInitializeViewModel.Postfix", __instance, null);
        }

        private static void MapUIHandleMapPageTabbedPostfix(Sr2MapUI __instance, int oldIndex, int newIndex)
        {
            Current?.ObserveMapUI($"MapUI.HandleMapPageTabbed.Postfix[{oldIndex}->{newIndex}]", __instance, null);
        }

        private static void MapUIOpenPlayerCurrentMapPostfix(Sr2MapUI __instance)
        {
            Current?.ObserveMapUI("MapUI.OpenPlayerCurrentMap.Postfix", __instance, null);
        }

        private static void MapUIOpenMapPostfix(Sr2MapUI __instance, MapDefinition mapDefinition)
        {
            Current?.ObserveMapUI("MapUI.OpenMap.Postfix", __instance, mapDefinition);
        }

        private static void MapUIOnCloseInputPostfix(Sr2MapUI __instance)
        {
            Current?.CancelPendingDelayedVisualCapture("MapUI.OnCloseInput.Postfix", __instance);
        }

        private static void MapDirectorNotifyZoneUnlockedPostfix(MapDirector __instance, object elementEvent, bool forceOpenMap, float delaySeconds)
        {
            Current?.ScheduleDynamicRefresh("MapDirector.NotifyZoneUnlocked", delaySeconds);
        }

        private static void MapDirectorRegisterMapUnlockAnimationPostfix(MapDirector __instance, object zoneUnlockEvent)
        {
            Current?.ScheduleDynamicRefresh("MapDirector.RegisterMapUnlockAnimation", 0.10f);
        }

        private void ObserveMapUiRoot(Sr2MapUIRoot root)
        {
            if (root == null)
                return;

            var mapUi = root.GetComponentInChildren<Sr2MapUI>(true);
            if (mapUi != null)
                ObserveMapUI("MapUIRoot.childMapUI", mapUi, null);
        }

        private void ObserveMapUI(string source, Sr2MapUI mapUi, MapDefinition mapDefinition)
        {
            if (mapUi == null)
                return;

            if (source.Contains("OpenMap"))
                _openMapSeen++;

            Sr2Map bestMap = null;
            var maps = mapUi.GetComponentsInChildren<Sr2Map>(true);
            if (maps.Length == 0)
            {
                Log.Every("map-capture-empty-" + source, 2f, $"map-capture: {source} mapUI={MapObjectNames.DescribeUnityObject(mapUi)} mapDefinition={MapObjectNames.DescribeUnityObject(mapDefinition)} maps=0");
            }
            else
            {
                Log.Info($"map-capture: {source} mapUI={MapObjectNames.DescribeUnityObject(mapUi)} mapDefinition={MapObjectNames.DescribeUnityObject(mapDefinition)} maps={maps.Length}");

                var bestArea = 0f;
                for (var i = 0; i < maps.Length; i++)
                {
                    var map = maps[i];
                    if (map == null)
                        continue;

                    var area = 0f;
                    Rect worldArea = default;
                    try
                    {
                        worldArea = map.GetWorldArea();
                        area = Mathf.Abs(worldArea.width * worldArea.height);
                    }
                    catch { }

                    var strictKey = InferObservedMapObjectKey(map.gameObject);
                    Log.Info($"map-capture:   map[{i}] key={strictKey ?? "-"} path={MapObjectNames.PathOf(map.transform)} active={map.gameObject.activeInHierarchy} world={MapObjectNames.FormatRect(worldArea)} area={area:F0}");

                    if (bestMap == null || area > bestArea)
                    {
                        bestMap = map;
                        bestArea = area;
                    }
                }
            }

            var resolvedMapKey = ResolveObservedMapKey(mapUi, mapDefinition, bestMap);
            var selectedMap = SelectObservedMapForKey(maps, resolvedMapKey) ?? (maps.Length == 1 ? bestMap : null);

            if (!string.IsNullOrEmpty(resolvedMapKey) && resolvedMapKey != "-")
            {
                var record = GetOrCreateRecord(resolvedMapKey);
                if (mapDefinition != null)
                    record.Definition = mapDefinition;
                if (selectedMap != null)
                    RegisterObservedMapAlias(record, selectedMap.gameObject);
                _lastMapName = resolvedMapKey;
            }

            if (!_loggedImageInventory)
            {
                _loggedImageInventory = true;
                MapVisualDiagnostics.LogImageInventory(_settings, mapUi, source);
            }

            TryCachePlayerMarkerFacts(mapUi, source);

            if (maps.Length == 0 && (string.IsNullOrEmpty(resolvedMapKey) || resolvedMapKey == "-"))
            {
                Log.Every("map-capture-skip-empty-" + source, 2f,
                    "map-capture: skipped map/marker/template cache for " + source + " because no Map component and no map key were available");
                return;
            }

            TryCacheMapUiMarkerFacts(mapUi, resolvedMapKey, source);

            foreach (var map in maps)
                TryCacheObservedMapBranch(map, source, mapDefinition, resolvedMapKey);

            TryCacheTemplateFromMapUiPrefabMapping(mapUi, mapDefinition, source);
        }

        private void TryCacheObservedMapBranch(Sr2Map map, string source, MapDefinition openedMapDefinition, string openedMapKey)
        {
            if (map == null || map.gameObject == null)
                return;

            var observedKey = ResolveObservedMapBranchKey(map, openedMapDefinition, openedMapKey);
            if (string.IsNullOrEmpty(observedKey) || observedKey == "-")
                return;

            var record = GetOrCreateRecord(observedKey);
            if (openedMapDefinition != null && string.Equals(openedMapKey, observedKey, StringComparison.Ordinal))
                record.Definition = openedMapDefinition;
            RegisterObservedMapAlias(record, map.gameObject);

            var geometry = CaptureGeometry(map.gameObject);
            if (geometry.HasWorldArea || geometry.HasMapArea || geometry.HasProjectionMapArea)
                record.Geometry = MergeGeometry(geometry, record.Geometry);

            var visualRoot = FindMapVisualRoot(map);
            if (visualRoot == null)
                return;

            ScheduleDelayedVisualCapture(visualRoot, observedKey, source + "." + observedKey + ".delayed100ms");
            CacheTemplate(visualRoot, observedKey, source + "." + observedKey + ".live-state", geometry);
        }

        private string ResolveObservedMapBranchKey(Sr2Map map, MapDefinition openedMapDefinition, string openedMapKey)
        {
            if (map == null || map.gameObject == null)
                return "-";

            var objectKey = InferObservedMapObjectKey(map.gameObject);
            if (!string.IsNullOrEmpty(objectKey) && TryResolveKnownMapAlias(objectKey, out var knownKey))
                return knownKey;

            if (!string.IsNullOrEmpty(openedMapKey) && openedMapKey != "-" && openedMapDefinition != null)
            {
                var definitionKey = MapObjectNames.MapKey(openedMapDefinition);
                if (string.Equals(definitionKey, objectKey, StringComparison.Ordinal) ||
                    (string.IsNullOrEmpty(objectKey) && map.gameObject.activeInHierarchy))
                    return openedMapKey;
            }

            return !string.IsNullOrEmpty(objectKey) ? objectKey : "-";
        }

        private static Sr2Map SelectObservedMapForKey(Sr2Map[] maps, string mapKey)
        {
            if (maps == null || maps.Length == 0 || string.IsNullOrEmpty(mapKey) || mapKey == "-")
                return null;

            foreach (var map in maps)
            {
                if (map == null)
                    continue;

                var inferred = InferMapKeyFromGameObject(map.gameObject);
                if (string.Equals(inferred, mapKey, StringComparison.Ordinal))
                    return map;
            }

            return null;
        }

        private void ScheduleDynamicRefresh(string reason, float delaySeconds)
        {
            var runAt = Time.realtimeSinceStartup + Mathf.Clamp(delaySeconds, 0.05f, 1.0f);
            if (_pendingDynamicRefreshAt <= 0f || runAt < _pendingDynamicRefreshAt)
                _pendingDynamicRefreshAt = runAt;

            Log.Every("map-capture-schedule-refresh", 2f, $"map-capture: scheduled dynamic refresh reason={reason} at={_pendingDynamicRefreshAt:F2}");
        }

        private static GameObject FindMapVisualRoot(Sr2Map map)
        {
            if (map == null)
                return null;

            var current = map.transform;
            while (current != null)
            {
                var cleanName = MapObjectNames.CleanCloneName(current.name);
                if (string.Equals(cleanName, "Content", StringComparison.OrdinalIgnoreCase))
                    return current.gameObject;
                current = current.parent;
            }

            return map.gameObject;
        }

        public void Tick()
        {
            if (_pendingDynamicRefreshAt > 0f && Time.realtimeSinceStartup >= _pendingDynamicRefreshAt)
            {
                _pendingDynamicRefreshAt = 0f;
                RefreshDynamicTemplateState();
            }

            if (_pendingDelayedVisualCaptureAt > 0f && Time.realtimeSinceStartup >= _pendingDelayedVisualCaptureAt)
            {
                var visualRoot = _pendingDelayedVisualRoot;
                var mapKey = _pendingDelayedVisualMapKey;
                var source = _pendingDelayedVisualSource;
                _pendingDelayedVisualCaptureAt = 0f;
                _pendingDelayedVisualRoot = null;
                _pendingDelayedVisualMapKey = "-";
                _pendingDelayedVisualSource = null;

                if (visualRoot != null && !string.IsNullOrEmpty(mapKey) && mapKey != "-" && visualRoot)
                    TryCacheMapVisualStates(visualRoot, mapKey, source);
            }

        }

        private void ScheduleDelayedVisualCapture(GameObject visualRoot, string mapKey, string source)
        {
            if (visualRoot == null || string.IsNullOrEmpty(mapKey) || mapKey == "-")
                return;

            _pendingDelayedVisualRoot = visualRoot;
            _pendingDelayedVisualMapKey = mapKey;
            _pendingDelayedVisualSource = source;
            _pendingDelayedVisualCaptureAt = Time.realtimeSinceStartup + 0.10f;
        }

        private void CancelPendingDelayedVisualCapture(string reason, Sr2MapUI mapUi)
        {
            if (_pendingDelayedVisualCaptureAt <= 0f)
                return;

            var visualRoot = _pendingDelayedVisualRoot;
            var mapKey = _pendingDelayedVisualMapKey;
            var source = _pendingDelayedVisualSource;

            if (visualRoot == null || string.IsNullOrEmpty(mapKey) || mapKey == "-")
            {
                _pendingDelayedVisualCaptureAt = 0f;
                _pendingDelayedVisualRoot = null;
                _pendingDelayedVisualMapKey = "-";
                _pendingDelayedVisualSource = null;
                return;
            }

            if (mapUi != null)
            {
                var current = visualRoot.transform;
                var belongsToMapUi = false;
                while (current != null)
                {
                    if (current == mapUi.transform)
                    {
                        belongsToMapUi = true;
                        break;
                    }
                    current = current.parent;
                }

                if (!belongsToMapUi)
                    return;
            }

            _pendingDelayedVisualCaptureAt = 0f;
            _pendingDelayedVisualRoot = null;
            _pendingDelayedVisualMapKey = "-";
            _pendingDelayedVisualSource = null;

            Log.Every("map-capture-delayed-visual-cancel", 2f, $"map-capture: canceled delayed visual capture reason={reason} map={mapKey} source={source}");
        }

        private void RefreshDynamicTemplateState()
        {
            _markerStateVersion++;
            Log.Info($"map-capture: dynamic refresh completed markerStateVersion={_markerStateVersion} fogStateVersion={_fogStateVersion}");
        }

        private void TryCacheTemplateFromMapUiPrefabMapping(Sr2MapUI mapUi, MapDefinition mapDefinition, string source)
        {
            if (mapUi == null || mapDefinition == null)
                return;

            var mapName = mapDefinition.name;
            var record = GetOrCreateRecord(mapName);
            record.Definition = mapDefinition;
            if (record.Template != null)
            {
                return;
            }

            try
            {
                var field = AccessTools.Field(typeof(Sr2MapUI), "_mapPrefabMapping");
                var mapping = field != null ? field.GetValue(mapUi) as MapPrefabMapping : null;
                if (mapping == null)
                {
                    Log.Every("map-capture-mapui-prefab-mapping-missing", 6f, "map-capture: MapUI._mapPrefabMapping unavailable from " + source);
                    return;
                }

                var prefab = mapping.GetPrefabFor(mapDefinition);
                if (prefab == null)
                {
                    Log.Every("map-capture-mapui-prefab-mapping-null-" + mapName, 6f, "map-capture: MapUI._mapPrefabMapping returned no prefab for " + mapName);
                    return;
                }

                Log.Info("map-capture: resolved map prefab from MapUI._mapPrefabMapping source=" + source + " map=" + mapName + " prefab=" + MapObjectNames.DescribeUnityObject(prefab));
                CacheTemplateFromPrefab(prefab, mapDefinition, source + ".MapUI._mapPrefabMapping");
            }
            catch (Exception ex)
            {
                Log.Warn("map-capture: MapUI._mapPrefabMapping probe failed: " + ex.GetType().Name);
            }
        }

        private void CacheTemplateFromPrefab(GameObject prefab, MapDefinition mapDefinition, string source)
        {
            if (prefab == null)
                return;

            var mapName = mapDefinition != null ? mapDefinition.name : prefab.name;
            var record = GetOrCreateRecord(mapName);
            if (mapDefinition != null)
                record.Definition = mapDefinition;
            if (record.Template != null)
            {
                return;
            }

            try
            {
                var template = Object.Instantiate(prefab);
                template.name = "RancherMinimapTemplate_" + MapObjectNames.SanitizeName(mapName) + "_FromPrefabMapping";
                template.hideFlags = HideFlags.HideAndDontSave;
                template.SetActive(false);
                Object.DontDestroyOnLoad(template);
                var geometry = CaptureGeometry(template);
                DisableRaycastersKeepCanvases(template);
                StripNonVisualBehaviours(template);
                StripMapUiMarkerBranches(template);
                StripPlayerMarkerBranches(template);
                IsolateCloneMaterials(template);
                EnableNonFogVisualComponents(template);
                ApplyCapturedVisualStatesTo(template, MapObjectNames.MapKey(mapDefinition), source + ".template");
                RefreshPortalLineGraphics(template, source + ".template");

                record.Template = template;
                record.Geometry = geometry;
                record.TemplateVersion++;
                _templateVersion++;
                Log.Info($"map-capture: cached prefab-mapping template via {source}: mapName={mapName} prefab={MapObjectNames.DescribeUnityObject(prefab)} template={MapObjectNames.DescribeUnityObject(template)} geometry={MapObjectNames.DescribeGeometry(geometry)}");
            }
            catch (Exception ex)
            {
                Log.Warn("map-capture: failed to cache prefab-mapping template: " + ex.GetType().Name);
            }
        }

        private void CacheTemplate(GameObject sourceMapObject, string mapName, string source, MapGeometry preferredGeometry)
        {
            if (sourceMapObject == null)
                return;

            if (string.IsNullOrEmpty(mapName) || mapName == "-")
                mapName = InferMapKeyFromGameObject(sourceMapObject) ?? sourceMapObject.name;

            var record = GetOrCreateRecord(mapName);
            if (record.Template != null)
            {
                record.Geometry = MergeGeometry(preferredGeometry, record.Geometry);
                ApplyCapturedVisualStatesTo(record.Template, mapName, source + ".existing-template");
                return;
            }

            try
            {
                var template = Object.Instantiate(sourceMapObject);
                template.name = "RancherMinimapTemplate_" + MapObjectNames.SanitizeName(mapName);
                template.hideFlags = HideFlags.HideAndDontSave;
                template.SetActive(false);
                Object.DontDestroyOnLoad(template);
                var capturedGeometry = CaptureGeometry(template);
                var geometry = preferredGeometry.HasWorldArea || preferredGeometry.HasMapArea || preferredGeometry.HasProjectionMapArea
                    ? MergeGeometry(preferredGeometry, capturedGeometry)
                    : capturedGeometry;
                PruneOtherMapBranches(template, mapName);
                DisableRaycastersKeepCanvases(template);
                StripNonVisualBehaviours(template);
                StripMapUiMarkerBranches(template);
                StripPlayerMarkerBranches(template);
                IsolateCloneMaterials(template);
                EnableNonFogVisualComponents(template);
                ApplyCapturedVisualStatesTo(template, mapName, source + ".template");
                RefreshPortalLineGraphics(template, source + ".template");

                record.Template = template;
                record.Geometry = geometry;
                record.TemplateVersion++;
                _templateVersion++;
                Log.Info($"map-capture: cached private template via {source}: mapName={mapName} source={MapObjectNames.DescribeUnityObject(sourceMapObject)} template={MapObjectNames.DescribeUnityObject(template)} geometry={MapObjectNames.DescribeGeometry(geometry)}");
            }
            catch (Exception ex)
            {
                Log.Warn("map-capture: failed to cache private template: " + ex.GetType().Name);
            }
        }

        private static void PruneOtherMapBranches(GameObject root, string mapKey)
        {
            if (root == null || string.IsNullOrEmpty(mapKey) || mapKey == "-")
                return;

            Transform mapHolder = null;
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (transform == null)
                    continue;

                if (string.Equals(MapObjectNames.CleanCloneName(transform.name), "MapHolder", StringComparison.OrdinalIgnoreCase))
                {
                    mapHolder = transform;
                    break;
                }
            }

            if (mapHolder == null)
                return;

            var disabled = 0;
            for (var i = 0; i < mapHolder.childCount; i++)
            {
                var child = mapHolder.GetChild(i);
                if (child == null)
                    continue;

                var childMapKey = InferMapKeyFromGameObject(child.gameObject);
                if (string.IsNullOrEmpty(childMapKey) || childMapKey == mapKey)
                    continue;

                if (child.gameObject.activeSelf)
                {
                    child.gameObject.SetActive(false);
                    disabled++;
                }
            }

            if (disabled > 0)
                Log.Info($"map-capture: pruned non-selected map branches map={mapKey} disabled={disabled} root={MapObjectNames.DescribeUnityObject(root)}");
        }

        private void TryCacheMapVisualStates(GameObject liveMapRoot, string mapKey, string source)
        {
            if (liveMapRoot == null || string.IsNullOrEmpty(mapKey))
                return;

            var graphicStates = new Dictionary<string, bool>();
            var transformStates = new Dictionary<string, bool>();
            var fogRevealStates = new Dictionary<string, bool>();
            var zoneFogCanvasAlphaStates = new Dictionary<string, float>();
            var graphicsCaptured = 0;
            var fogCaptured = 0;
            var fogRevealed = 0;
            var transformsCaptured = 0;

            foreach (var transform in liveMapRoot.GetComponentsInChildren<Transform>(true))
            {
                if (transform == null || transform == liveMapRoot.transform)
                    continue;

                var key = MapObjectNames.RelativePath(liveMapRoot.transform, transform);
                if (string.IsNullOrEmpty(key))
                    continue;
                if (IsStaticNegativeFogPath(key))
                    continue;

                StoreVisualState(transformStates, key, transform.gameObject.activeSelf);
                transformsCaptured++;
            }

            foreach (var graphic in liveMapRoot.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic == null)
                    continue;

                var key = MapObjectNames.RelativePath(liveMapRoot.transform, graphic.transform);
                if (string.IsNullOrEmpty(key))
                    continue;
                if (IsStaticNegativeFogPath(key))
                    continue;

                var isEnabled = graphic.enabled && graphic.gameObject.activeInHierarchy && graphic.color.a > 0.01f;
                StoreVisualState(graphicStates, key, isEnabled);
                if (MapGraphicClassifier.IsFogGraphic(graphic))
                    fogCaptured++;
                graphicsCaptured++;
            }

            foreach (var canvasGroup in liveMapRoot.GetComponentsInChildren<CanvasGroup>(true))
            {
                if (canvasGroup == null || canvasGroup.transform == null)
                    continue;

                var path = MapObjectNames.RelativePath(liveMapRoot.transform, canvasGroup.transform);
                if (string.IsNullOrEmpty(path) || !IsZoneFogPath(path))
                    continue;

                StoreVisualState(zoneFogCanvasAlphaStates, path, canvasGroup.alpha);
            }

            foreach (var pair in zoneFogCanvasAlphaStates)
            {
                var revealed = pair.Value <= 0.01f;
                StoreVisualState(fogRevealStates, pair.Key, revealed);
                if (revealed)
                    fogRevealed++;
            }

            if (graphicsCaptured == 0 && transformsCaptured == 0)
                return;

            var record = GetOrCreateRecord(mapKey);
            record.VisualState.GraphicStates = graphicStates;
            record.VisualState.TransformStates = transformStates;
            record.VisualState.FogRevealStates = fogRevealStates;
            record.VisualState.ZoneFogCanvasAlphaStates = zoneFogCanvasAlphaStates;
            _fogStateVersion++;
            if (record.Template != null)
                ApplyCapturedVisualStatesTo(record.Template, mapKey, source + ".existing-template");
        }

        private void ApplyCapturedVisualStatesTo(GameObject obj, string mapKey, string reason)
        {
            if (obj == null || string.IsNullOrEmpty(mapKey))
                return;

            TryGetRecordByKeyOrAlias(mapKey, out var record);
            var visualState = record?.VisualState;
            var graphicStates = visualState?.GraphicStates;
            var transformStates = visualState?.TransformStates;
            var fogRevealStates = visualState?.FogRevealStates;
            var zoneFogCanvasAlphaStates = visualState?.ZoneFogCanvasAlphaStates;

            if ((graphicStates == null || graphicStates.Count == 0) &&
                (transformStates == null || transformStates.Count == 0) &&
                (fogRevealStates == null || fogRevealStates.Count == 0) &&
                (zoneFogCanvasAlphaStates == null || zoneFogCanvasAlphaStates.Count == 0))
                return;

            if (transformStates != null && transformStates.Count > 0)
            {
                foreach (var transform in obj.GetComponentsInChildren<Transform>(true))
                {
                    if (transform == null || transform == obj.transform)
                        continue;

                    var key = MapObjectNames.RelativePath(obj.transform, transform);
                    if (IsStaticNegativeFogPath(key))
                        continue;
                    if (!TryGetVisualState(transformStates, key, out var liveActive))
                        continue;

                    if (transform.gameObject.activeSelf != liveActive)
                        transform.gameObject.SetActive(liveActive);
                }
            }

            if (zoneFogCanvasAlphaStates != null && zoneFogCanvasAlphaStates.Count > 0)
            {
                foreach (var canvasGroup in obj.GetComponentsInChildren<CanvasGroup>(true))
                {
                    if (canvasGroup == null || canvasGroup.transform == null)
                        continue;

                    var key = MapObjectNames.RelativePath(obj.transform, canvasGroup.transform);
                    if (!TryGetVisualState(zoneFogCanvasAlphaStates, key, out var alpha))
                        continue;

                    canvasGroup.alpha = alpha;
                }
            }

            if (zoneFogCanvasAlphaStates != null && zoneFogCanvasAlphaStates.Count > 0)
            {
                foreach (var component in obj.GetComponentsInChildren<Component>(true))
                {
                    if (!IsComponentNamed(component, "FogMapElement"))
                        continue;

                    var key = MapObjectNames.RelativePath(obj.transform, component.transform);
                    if (!TryGetVisualState(zoneFogCanvasAlphaStates, key, out var alpha))
                        continue;

                    var revealed = alpha <= 0.01f;
                    TrySetFogRevealState(component, revealed);
                }
            }

            foreach (var graphic in obj.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic == null)
                    continue;

                var key = MapObjectNames.RelativePath(obj.transform, graphic.transform);
                var liveEnabled = graphicStates == null || !TryGetVisualState(graphicStates, key, out var capturedEnabled)
                    ? graphic.enabled
                    : capturedEnabled;
                if (IsStaticNegativeFogPath(key))
                    liveEnabled = graphic.enabled;

                var decorativeCloud = MapGraphicClassifier.IsDecorativeCloudGraphic(graphic);
                graphic.enabled = decorativeCloud
                    ? liveEnabled && _settings.ShowDecorativeClouds
                    : liveEnabled;

                if (decorativeCloud)
                {
                    var color = graphic.color;
                    color.a = Mathf.Min(color.a, 0.40f);
                    graphic.color = color;
                }

                graphic.raycastTarget = false;
            }

        }

        private static void StoreVisualState<T>(Dictionary<string, T> states, string key, T value)
        {
            if (states == null || string.IsNullOrEmpty(key))
                return;

            states[key] = value;
        }

        private static bool TryGetVisualState<T>(Dictionary<string, T> states, string key, out T value)
        {
            value = default;
            return states != null && !string.IsNullOrEmpty(key) && states.TryGetValue(key, out value);
        }

        private static bool IsZoneFogPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            var normalized = path.Replace('\\', '/').Trim('/');
            return normalized.IndexOf("zone_fog_areas/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsStaticNegativeFogPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            var normalized = path.Replace('\\', '/').Trim('/').ToLowerInvariant();
            return normalized.Contains("fog_static/outsidefognegativemasked") ||
                   normalized.Contains("fog_static/negative mask");
        }


        private MapGeometry PickGeometryFor(MapDefinition mapDefinition)
        {
            return TryGetRecordByKeyOrAlias(MapObjectNames.MapKey(mapDefinition), out var record) ? record.Geometry : MapGeometry.Empty;
        }

        internal static void StripPlayerMarkerBranches(GameObject obj)
        {
            if (obj == null)
                return;

            var remove = new List<GameObject>();
            foreach (var rect in obj.GetComponentsInChildren<RectTransform>(true))
            {
                if (rect == null || string.IsNullOrEmpty(rect.name))
                    continue;

                var cleanName = MapObjectNames.CleanCloneName(rect.name);
                var path = MapObjectNames.PathOf(rect);
                if (IsPlayerMarkerBranch(cleanName, path))
                {
                    remove.Add(rect.gameObject);
                    continue;
                }

                foreach (var graphic in rect.GetComponents<Graphic>())
                {
                    if (IsPlayerMarkerGraphic(graphic, path))
                    {
                        remove.Add(rect.gameObject);
                        break;
                    }
                }
            }

            var destroyed = 0;
            var seen = new HashSet<int>();
            foreach (var go in remove)
            {
                if (go == null || !seen.Add(go.GetInstanceID()))
                    continue;

                QuarantineClonedPlayerBranch(go);
                destroyed++;
            }

            if (destroyed > 0)
                Log.Info("map-capture: stripped cloned PlayerMarker branches=" + destroyed + " from " + MapObjectNames.DescribeUnityObject(obj));
        }


        private static bool IsPlayerMarkerBranch(string cleanName, string path)
        {
            if (NameEquals(cleanName, "PlayerMarker") ||
                NameEquals(cleanName, "Player Map Marker") ||
                NameEquals(cleanName, "FacingFrame") ||
                NameEquals(cleanName, "FacingArrow") ||
                NameEquals(cleanName, "BeaIcon"))
                return true;

            return ContainsIgnoreCase(cleanName, "PlayerMapMarker") ||
                   ContainsIgnoreCase(path, "PlayerMapMarker") ||
                   ContainsIgnoreCase(path, "PlayerMarker") ||
                   ContainsIgnoreCase(path, "BelowMarkersContainer/Cone");
        }

        private static bool IsPlayerMarkerGraphic(Graphic graphic, string path)
        {
            if (graphic == null)
                return false;

            var image = graphic as Image;
            var spriteName = image != null && image.sprite != null ? image.sprite.name : string.Empty;
            return NameEquals(spriteName, "framePlayerMarker") ||
                   NameEquals(spriteName, "player_face") ||
                   NameEquals(spriteName, "fxPlayerMarkerCone") ||
                   ContainsIgnoreCase(path, "FacingFrame") ||
                   ContainsIgnoreCase(path, "FacingArrow") ||
                   ContainsIgnoreCase(path, "BeaIcon") ||
                   ContainsIgnoreCase(path, "BelowMarkersContainer/Cone") ||
                   ContainsIgnoreCase(DescribeMaterial(graphic), "framePlayerMarker");
        }

        private static bool NameEquals(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsIgnoreCase(string value, string needle)
        {
            return !string.IsNullOrEmpty(value) &&
                   !string.IsNullOrEmpty(needle) &&
                   value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static void StripMapUiMarkerBranches(GameObject obj)
        {
            if (obj == null)
                return;

            var remove = new List<GameObject>();
            foreach (var transform in obj.GetComponentsInChildren<Transform>(true))
            {
                if (transform == null || string.IsNullOrEmpty(transform.name))
                    continue;

                var cleanName = MapObjectNames.CleanCloneName(transform.name);
                if (string.Equals(cleanName, "Markers", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(cleanName, "MapMarkerSection", StringComparison.OrdinalIgnoreCase) ||
                    cleanName.IndexOf("MarkerSection", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    remove.Add(transform.gameObject);
                }
            }

            var destroyed = 0;
            var seen = new HashSet<int>();
            foreach (var go in remove)
            {
                if (go == null || !seen.Add(go.GetInstanceID()))
                    continue;

                QuarantineClonedPlayerBranch(go);
                destroyed++;
            }

            if (destroyed > 0)
                Log.Info("map-capture: stripped cloned MapUI marker branches=" + destroyed + " from " + MapObjectNames.DescribeUnityObject(obj));
        }

        private static void QuarantineClonedPlayerBranch(GameObject go)
        {
            if (go == null)
                return;
            go.name = "RancherMinimap_Quarantined_" + MapObjectNames.CleanCloneName(go.name);
            go.SetActive(false);
        }

        private static MapGeometry CaptureGeometry(GameObject obj)
        {
            if (obj == null)
                return MapGeometry.Empty;

            var map = obj.GetComponentInChildren<Sr2Map>(true);
            var worldArea = default(Rect);
            var mapArea = default(Rect);
            var projectionMapArea = default(Rect);
            var hasWorldArea = false;
            var hasMapArea = false;
            var hasProjectionMapArea = false;

            if (map != null)
            {
                try
                {
                    worldArea = map.GetWorldArea();
                    hasWorldArea = Mathf.Abs(worldArea.width) > 1f && Mathf.Abs(worldArea.height) > 1f;
                }
                catch (Exception ex)
                {
                    Log.Every("map-capture-geometry-world-failed", 8f, "map-capture: Map.GetWorldArea failed while snapshotting clone geometry: " + ex.GetType().Name);
                }

                try
                {
                    projectionMapArea = map.GetMapArea();
                    hasProjectionMapArea = Mathf.Abs(projectionMapArea.width) > 1f && Mathf.Abs(projectionMapArea.height) > 1f;
                }
                catch (Exception ex)
                {
                    Log.Every("map-capture-geometry-map-failed", 8f, "map-capture: Map.GetMapArea failed while snapshotting clone geometry: " + ex.GetType().Name);
                }

                try
                {
                    mapArea = map.GetFullMapArea();
                    hasMapArea = Mathf.Abs(mapArea.width) > 1f && Mathf.Abs(mapArea.height) > 1f;
                }
                catch (Exception ex)
                {
                    Log.Every("map-capture-geometry-full-map-failed", 8f, "map-capture: Map.GetFullMapArea failed while snapshotting clone geometry: " + ex.GetType().Name);
                }
            }

            if (!hasMapArea)
            {
                var rect = obj.GetComponent<RectTransform>();
                if (rect != null)
                {
                    var width = Mathf.Abs(rect.rect.width) > 1f ? Mathf.Abs(rect.rect.width) : Mathf.Abs(rect.sizeDelta.x);
                    var height = Mathf.Abs(rect.rect.height) > 1f ? Mathf.Abs(rect.rect.height) : Mathf.Abs(rect.sizeDelta.y);
                    if (width > 1f && height > 1f)
                    {
                        mapArea = new Rect(-width * 0.5f, -height * 0.5f, width, height);
                        hasMapArea = true;
                    }
                }
            }

            return new MapGeometry(worldArea, hasWorldArea, mapArea, hasMapArea, projectionMapArea, hasProjectionMapArea);
        }

        private static MapGeometry MergeGeometry(MapGeometry preferred, MapGeometry fallback)
        {
            return new MapGeometry(
                preferred.HasWorldArea ? preferred.WorldArea : fallback.WorldArea,
                preferred.HasWorldArea || fallback.HasWorldArea,
                preferred.HasMapArea ? preferred.MapArea : fallback.MapArea,
                preferred.HasMapArea || fallback.HasMapArea,
                preferred.HasProjectionMapArea ? preferred.ProjectionMapArea : fallback.ProjectionMapArea,
                preferred.HasProjectionMapArea || fallback.HasProjectionMapArea);
        }

        internal static void StripNonVisualBehaviours(GameObject obj, bool preserveStateBehaviours = true)
        {
            if (obj == null)
                return;

            var stripped = 0;
            var disabled = 0;
            foreach (var behaviour in obj.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null)
                    continue;

                if (IsNativePortalLineBranchBehaviour(behaviour))
                    continue;

                if (IsUnityUiVisualBehaviour(behaviour))
                    continue;

                if (IsLikelyCustomVisualBehaviour(behaviour))
                {
                    continue;
                }

                if (!ShouldStripClonedBehaviour(behaviour, preserveStateBehaviours))
                    continue;

                try
                {
                    Object.DestroyImmediate(behaviour);
                    stripped++;
                }
                catch
                {
                    behaviour.enabled = false;
                    disabled++;
                }
            }

            if (stripped > 0 || disabled > 0)
                Log.Info("map-capture: stripped non-visual cloned MonoBehaviours destroyed=" + stripped + " disabledFallback=" + disabled + " from " + MapObjectNames.DescribeUnityObject(obj));
        }

        /// <summary>
        /// Preserve SR2's native teleporter connection branch:
        ///   MapHolder/&lt;MapPrefab&gt;/zone_links/zone_link_*/PortalLineSpline/PortalLineTest
        ///
        /// The dump for PortalLineGraphic.OnPopulateMesh shows it samples a private
        /// MonomiPark.SlimeRancher.Splines.Spline and emits dotted quads in the
        /// PortalLineSpline transform's local coordinates. Removing either the Spline
        /// behaviour on PortalLineSpline or the PortalLineGraphic on PortalLineTest makes
        /// the line vanish. This predicate is intentionally path-specific instead of a
        /// broad "anything spline-like" keep rule.
        /// </summary>
        private static bool IsNativePortalLineBranchBehaviour(MonoBehaviour behaviour)
        {
            if (behaviour == null || behaviour.transform == null)
                return false;

            var path = MapObjectNames.PathOf(behaviour.transform);
            if (path.IndexOf("/zone_links/", StringComparison.OrdinalIgnoreCase) < 0 ||
                path.IndexOf("/PortalLineSpline", StringComparison.OrdinalIgnoreCase) < 0)
                return false;

            var typeName = behaviour.GetType().FullName ?? behaviour.GetType().Name;
            return typeName.IndexOf("PortalLineGraphic", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("MonomiPark.SlimeRancher.Splines.Spline", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.EndsWith(".Spline", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(behaviour.transform.name, "PortalLineSpline", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldStripClonedBehaviour(MonoBehaviour behaviour, bool preserveStateBehaviours)
        {
            if (behaviour == null || behaviour.gameObject == null)
                return false;

            var typeName = behaviour.GetType().FullName ?? behaviour.GetType().Name;
            if (typeName.IndexOf("UnityEngine.UI", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            if (typeName.IndexOf("PortalLineGraphic", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("Graphic", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("MapZone", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("MapLine", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("MapIcon", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            if (preserveStateBehaviours &&
                (typeName.IndexOf("FogMapElement", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 typeName.IndexOf("DynamicMapElement", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 typeName.IndexOf("MapMarker", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 typeName.IndexOf("MarkerUI", StringComparison.OrdinalIgnoreCase) >= 0))
                return false;

            // Keep the clone as visual UI only. Live map clones can carry Region/scene/gameplay
            // behaviours whose OnDisable/OnDestroy paths mutate game registries during shutdown.
            // this might be true but unsure. no source. seems made up.
            return true;
        }

        private static bool IsLikelyCustomVisualBehaviour(MonoBehaviour behaviour)
        {
            if (behaviour == null || behaviour.gameObject == null)
                return false;

            var typeName = behaviour.GetType().FullName ?? behaviour.GetType().Name;
            if (typeName.IndexOf("Graphic", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("MapZone", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("MapLine", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("MapIcon", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            try
            {
                return behaviour.gameObject.GetComponent<CanvasRenderer>() != null;
            }
            catch
            {
                return false;
            }
        }

        internal static void EnableNonFogVisualComponents(GameObject obj)
        {
            if (obj == null)
                return;

            var graphics = obj.GetComponentsInChildren<Graphic>(true);
            foreach (var graphic in graphics)
            {
                if (graphic == null)
                    continue;

                graphic.raycastTarget = false;
                var maskable = graphic as MaskableGraphic;
                if (maskable != null)
                    maskable.maskable = true;

                // Do not force-enable graphics here. Their enabled/active state is part of the
                // captured map state; forcing non-fog graphics on re-showed stale marker/player UI.
                // Do not force-enable fog/cloud graphics. Their enabled/active state is part of
                // the large map's reveal state. v0.16 broke this by enabling every copied fog image.
            }

            foreach (var mask in obj.GetComponentsInChildren<Mask>(true))
            {
                if (mask == null)
                    continue;

                var path = MapObjectNames.PathOf(mask.transform).ToLowerInvariant();
                // The SR2 static outside fog uses a companion negative-mask path. Keep those
                // authored Mask components alive in the clone; disabling all masks flattened the
                // relationship and made the static fog disappear instead of masking correctly.
                if (path.Contains("/fog_static/outsidefognegativemasked") ||
                    path.Contains("/fog_static/negative mask"))
                {
                    mask.enabled = true;
                    continue;
                }

                mask.enabled = false;
            }

            foreach (var mask in obj.GetComponentsInChildren<RectMask2D>(true))
            {
                if (mask != null)
                    mask.enabled = true;
            }
        }

        internal static void RefreshPortalLineGraphics(GameObject obj, string reason)
        {
            MapPortalLineOverlays.Refresh(obj, reason, Current?._settings.ShowPortalLines ?? false);
        }

        private static void IsolateCloneMaterials(GameObject obj)
        {
            if (obj == null)
                return;

            var isolated = 0;
            foreach (var graphic in obj.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic == null || graphic.material == null)
                    continue;

                var material = graphic.material;
                var materialName = MapGraphicClassifier.MaterialNameOf(graphic);
                if (string.Equals(materialName, "Default UI Material", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var copy = new Material(material);
                    copy.name = materialName + "_RMM";
                    graphic.material = copy;
                    isolated++;
                }
                catch { }
            }

            if (isolated > 0)
                Log.Info("map-capture: isolated clone materials count=" + isolated + " from " + MapObjectNames.DescribeUnityObject(obj));
        }

        /// <summary>
        /// Cloned map visuals must not receive input, but native SR2 map graphics still need
        /// their authored Canvas/CanvasRenderer chain. In particular:
        /// MapHolder/<MapPrefab>/zone_links/zone_link_*/PortalLineSpline/PortalLineTest
        /// uses PortalLineGraphic.OnPopulateMesh for the dotted teleporter connection.
        /// </summary>
        internal static void DisableRaycastersKeepCanvases(GameObject obj)
        {
            if (obj == null)
                return;

            foreach (var canvas in obj.GetComponentsInChildren<Canvas>(true))
            {
                if (canvas == null)
                    continue;

                canvas.enabled = true;
                canvas.overrideSorting = false;
                canvas.sortingOrder = 0;
            }

            foreach (var raycaster in obj.GetComponentsInChildren<GraphicRaycaster>(true))
            {
                if (raycaster != null)
                    raycaster.enabled = false;
            }
        }


        private static string DescribeMaterial(Graphic graphic)
        {
            return graphic != null && graphic.material != null ? MapGraphicClassifier.MaterialNameOf(graphic) : "-";
        }

        private static bool IsComponentNamed(Component component, string componentName)
        {
            if (component == null || string.IsNullOrEmpty(componentName))
                return false;

            var typeName = component.GetType().FullName ?? component.GetType().Name;
            return typeName.IndexOf(componentName, StringComparison.OrdinalIgnoreCase) >= 0;
        }



        private static bool TrySetFogRevealState(Component fogElement, bool revealed)
        {
            if (fogElement == null)
                return false;

            try
            {
                var method = FindMethod(fogElement.GetType(), "SetRevealState", 1);
                if (method != null)
                    method.Invoke(fogElement, new object[] { revealed });
                else
                    ReflectionTools.Call(fogElement, "SetRevealState", revealed);
                return true;
            }
            catch (Exception ex)
            {
                Log.Every("map-capture-fog-reveal-write-failed", 8f, "map-capture: FogMapElement reveal write failed: " + ex.GetType().Name);
                return false;
            }
        }


        private static MethodInfo FindMethod(Type type, string name, int parameterCount)
        {
            if (type == null || string.IsNullOrEmpty(name))
                return null;

            return type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.Ordinal) &&
                                     m.GetParameters().Length == parameterCount);
        }

        private void TryCacheMapUiMarkerFacts(Sr2MapUI mapUi, string mapKey, string source)
        {
            if (mapUi == null)
                return;

            var sectionObject = ReflectionTools.GetFieldOrPropertyQuiet(mapUi,
                "_mapMarkerSection", "mapMarkerSection", "MapMarkerSection") as GameObject;

            if (sectionObject == null)
            {
                foreach (var rect in mapUi.GetComponentsInChildren<RectTransform>(true))
                {
                    if (rect == null)
                        continue;

                    var clean = MapObjectNames.CleanCloneName(rect.name);
                    if (clean.IndexOf("MarkerSection", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        string.Equals(clean, "Markers", StringComparison.OrdinalIgnoreCase))
                    {
                        sectionObject = rect.gameObject;
                        break;
                    }
                }
            }

            if (sectionObject == null)
            {
                Log.Every("map-capture-no-marker-section", 8f, "map-capture: no MapUI marker section found source=" + source);
                return;
            }

            var sectionRect = sectionObject.GetComponent<RectTransform>();
            var markers = new List<MarkerSnapshot>();

            // First pass: vanilla MapUI normally instantiates each marker as a root under
            // _mapMarkerSection. Capture those roots only; the snapshot keeps visuals, not live marker controllers.
            if (sectionRect != null)
            {
                for (var i = 0; i < sectionRect.childCount; i++)
                {
                    var child = sectionRect.GetChild(i) as RectTransform;
                    if (child != null)
                        TryAddCapturedMapUiMarker(child, markers, source);
                }
            }

            // Fallback pass for hierarchy variants: look for roots that carry marker controller types.
            if (markers.Count == 0)
            {
                foreach (var rect in sectionObject.GetComponentsInChildren<RectTransform>(true))
                {
                    if (rect == null || rect == sectionRect)
                        continue;

                    if (!HasComponentName(rect.gameObject, "MapMarker"))
                        continue;

                    TryAddCapturedMapUiMarker(rect, markers, source);
                }
            }

            if (string.IsNullOrEmpty(mapKey))
                mapKey = "-";

            var record = GetOrCreateRecord(mapKey);
            if (ShouldPreserveExistingMarkerCache(record.Markers, markers, source))
            {
                Log.Every("map-capture-marker-cache-preserve-" + mapKey, 4f,
                    "map-capture: preserving existing marker cache map=" + mapKey +
                    " source=" + source +
                    " existing=" + (record.Markers != null ? record.Markers.Count : 0) +
                    " captured=" + markers.Count);
                return;
            }

            record.Markers = markers;
            _markerStateVersion++;
            Log.Every("map-capture-marker-cache-" + mapKey, 2f,
                $"map-capture: cached marker snapshots map={mapKey} count={markers.Count} source={source} section={MapObjectNames.PathOf(sectionObject.transform)}");
        }

        private static bool ShouldPreserveExistingMarkerCache(List<MarkerSnapshot> existing, List<MarkerSnapshot> captured, string source)
        {
            if (existing == null || existing.Count == 0 || captured == null)
                return false;

            if (captured.Count == 0)
                return true;

            if (existing.Count < 16)
                return false;

            var transientSource = (source ?? string.Empty).IndexOf("TryInitializeViewModel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  (source ?? string.Empty).IndexOf("HandleMapPageTabbed", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!transientSource)
                return false;

            return captured.Count < Math.Max(4, existing.Count / 4);
        }

        private void TryAddCapturedMapUiMarker(RectTransform rect, List<MarkerSnapshot> markers, string source)
        {
            if (rect == null || markers == null)
                return;

            var path = MapObjectNames.PathOf(rect.transform);
            var lower = path.ToLowerInvariant();
            var clean = MapObjectNames.CleanCloneName(rect.name);

            if (lower.Contains("playermarker") || lower.Contains("player marker") || lower.Contains("navigationmarker") || lower.Contains("waypoint"))
                return;

            var visible = rect.gameObject.activeInHierarchy;
            var pos = rect.anchoredPosition;
            var id = StableMarkerId(path, pos);
            var icon = FindMarkerIcon(rect);

            MarkerVisualDiagnostics.LogSource(_settings, rect, id, clean, source);

            var visualTemplate = CloneMarkerVisualTemplate(rect, id, clean, source);
            var size = visualTemplate != null
                ? RootMarkerSize(rect, icon.Size)
                : icon.Size;

            markers.Add(new MarkerSnapshot(id, pos, clean, visible, icon.Sprite, icon.Color, size, visualTemplate: visualTemplate));
        }

        private static Vector2 RootMarkerSize(RectTransform root, Vector2 fallback)
        {
            if (root == null)
                return fallback;

            var width = Mathf.Abs(root.rect.width);
            var height = Mathf.Abs(root.rect.height);
            if (width < 1f || height < 1f)
                return fallback;

            return new Vector2(width, height);
        }


        private GameObject CloneMarkerVisualTemplate(RectTransform source, int markerId, string markerKind, string sourceLabel)
        {
            if (source == null)
                return null;

            try
            {
                var clone = Object.Instantiate(source.gameObject);
                clone.name = "RancherMinimap_MarkerVisualTemplate_" + markerId;
                clone.hideFlags = HideFlags.HideAndDontSave;
                MarkerVisualDiagnostics.LogCloneStage(_settings, "clone-instantiated", markerId, markerKind, clone, sourceLabel);
                clone.SetActive(false);
                MarkerVisualDiagnostics.LogCloneStage(_settings, "clone-root-disabled", markerId, markerKind, clone, sourceLabel);
                // Marker roots are already the vanilla-resolved marker visuals. Keep the whole
                // visual subtree, but strip live marker scripts. Those scripts can re-evaluate state
                // after the big map closes and collapse compound icons (for example drone body/face
                // layers) back into a partial visual.
                StripNonVisualBehaviours(clone, preserveStateBehaviours: false);
                MarkerVisualDiagnostics.LogCloneStage(_settings, "clone-after-strip", markerId, markerKind, clone, sourceLabel);
                DisableRaycastersKeepCanvases(clone);
                SanitizeMarkerVisualClone(clone);
                MarkerVisualDiagnostics.LogCloneStage(_settings, "clone-after-sanitize", markerId, markerKind, clone, sourceLabel);

                _markerVisualTemplates.Add(clone);
                return clone;
            }
            catch (Exception ex)
            {
                Log.Every("map-capture-marker-visual-clone-failed", 8f, "map-capture: marker visual clone failed: " + ex.GetType().Name);
                return null;
            }
        }

        private static void SanitizeMarkerVisualClone(GameObject clone)
        {
            if (clone == null)
                return;

            foreach (var raycaster in clone.GetComponentsInChildren<GraphicRaycaster>(true))
                if (raycaster != null)
                    raycaster.enabled = false;

            foreach (var graphic in clone.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic == null)
                    continue;

                graphic.raycastTarget = false;
                var maskable = graphic as MaskableGraphic;
                if (maskable != null)
                    maskable.maskable = true;
            }
        }

        private static (Sprite Sprite, Color Color, Vector2 Size) FindMarkerIcon(RectTransform root)
        {
            if (root == null)
                return (null, Color.white, new Vector2(32f, 32f));

            foreach (var image in root.GetComponentsInChildren<Image>(true))
            {
                if (image == null || image.sprite == null)
                    continue;

                var rect = image.rectTransform != null ? image.rectTransform.rect : default;
                var area = Mathf.Abs(rect.width * rect.height);
                if (area < 4f || image.color.a <= 0.01f)
                    continue;

                var path = MapObjectNames.PathOf(image.transform);
                var spriteName = image.sprite != null ? image.sprite.name : string.Empty;
                if (!path.Contains("icon", StringComparison.InvariantCultureIgnoreCase) &&
                    !spriteName.Contains("icon", StringComparison.InvariantCultureIgnoreCase))
                    continue;

                var size = image.rectTransform != null
                    ? new Vector2(Mathf.Abs(image.rectTransform.rect.width), Mathf.Abs(image.rectTransform.rect.height))
                    : new Vector2(32f, 32f);
                if (size.x < 1f || size.y < 1f)
                    size = new Vector2(32f, 32f);

                return (image.sprite, image.color, size);
            }

            return (null, Color.white, new Vector2(32f, 32f));
        }

        private bool TryInferMapKeyFromWorldPositionInternal(Vector3 worldPosition, out string mapKey)
        {
            mapKey = "-";
            string match = null;

            foreach (var record in _mapsByKey.Values)
            {
                if (record == null || !record.Geometry.HasWorldArea || !ContainsWorldPoint(record.Geometry.WorldArea, worldPosition))
                    continue;

                if (match != null)
                    return false;

                match = record.Key;
            }

            if (string.IsNullOrEmpty(match))
                return false;

            mapKey = match;
            return true;
        }

        private static bool ContainsWorldPoint(Rect worldArea, Vector3 worldPosition)
        {
            if (Mathf.Abs(worldArea.width) <= 0.01f || Mathf.Abs(worldArea.height) <= 0.01f)
                return false;

            return worldArea.Contains(new Vector2(worldPosition.x, worldPosition.z));
        }


        private static bool HasComponentName(GameObject go, string namePart)
        {
            if (go == null || string.IsNullOrEmpty(namePart))
                return false;

            foreach (var component in go.GetComponents<Component>())
            {
                if (component == null)
                    continue;

                var typeName = component.GetType().FullName ?? component.GetType().Name;
                if (typeName.IndexOf(namePart, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private static int StableMarkerId(string path, Vector2 pos)
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + (path != null ? path.GetHashCode() : 0);
                hash = hash * 31 + Mathf.RoundToInt(pos.x * 10f);
                hash = hash * 31 + Mathf.RoundToInt(pos.y * 10f);
                return hash;
            }
        }

        private void TryCachePlayerMarkerFacts(Sr2MapUI mapUi, string source)
        {
            if (mapUi == null)
                return;

            RectTransform playerRoot = null;
            var bestScore = int.MinValue;
            Image beaIcon = null;
            Image facingFrame = null;
            Image facingArrow = null;
            Image viewCone = null;

            foreach (var rect in mapUi.GetComponentsInChildren<RectTransform>(true))
            {
                if (rect == null)
                    continue;

                var path = MapObjectNames.PathOf(rect.transform);
                var lower = path.ToLowerInvariant();
                if (!lower.Contains("player"))
                    continue;

                var graphics = rect.GetComponentsInChildren<Graphic>(true);
                if (graphics == null || graphics.Length == 0)
                    continue;

                var area = Mathf.Abs(rect.rect.width * rect.rect.height);
                var score = 0;
                var cleanName = MapObjectNames.CleanCloneName(rect.name).ToLowerInvariant();
                var parentName = rect.parent != null ? MapObjectNames.CleanCloneName(rect.parent.name).ToLowerInvariant() : string.Empty;

                if ((cleanName == "playermarker" || cleanName.Contains("playermarker")) && parentName == "markers") score += 300;
                if (lower.Contains("playermapmarker")) score += 120;
                if (lower.Contains("player") && lower.Contains("marker")) score += 100;
                if (area > 16f && area < 25000f) score += 10;
                score += Math.Min(graphics.Length, 20);

                if (score > bestScore)
                {
                    bestScore = score;
                    playerRoot = rect;
                }
            }

            if (playerRoot == null)
            {
                Log.Every("map-capture-no-player-marker", 8f, "map-capture: no player marker facts found under " + source);
                return;
            }

            Log.Every("map-capture-player-marker-sprite-source", 8f, "map-capture: observed vanilla player marker only for sprite capture source=" + source);

            foreach (var image in playerRoot.GetComponentsInChildren<Image>(true))
            {
                if (image == null || image.sprite == null)
                    continue;

                var imagePath = MapObjectNames.PathOf(image.transform);
                var spriteName = image.sprite != null ? image.sprite.name : string.Empty;
                if (imagePath.IndexOf("BeaIcon", StringComparison.OrdinalIgnoreCase) >= 0)
                    beaIcon = image;
                else if (imagePath.IndexOf("FacingFrame", StringComparison.OrdinalIgnoreCase) >= 0)
                    facingFrame = image;
                else if (imagePath.IndexOf("FacingArrow", StringComparison.OrdinalIgnoreCase) >= 0)
                    facingArrow = image;
                else if (imagePath.IndexOf("BelowMarkersContainer/Cone", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         string.Equals(spriteName, "fxPlayerMarkerCone", StringComparison.OrdinalIgnoreCase))
                    viewCone = image;
            }

            if (viewCone == null)
            {
                foreach (var image in mapUi.GetComponentsInChildren<Image>(true))
                {
                    if (image == null || image.sprite == null)
                        continue;

                    var imagePath = MapObjectNames.PathOf(image.transform);
                    var spriteName = image.sprite.name;
                    if (imagePath.IndexOf("BelowMarkersContainer/Cone", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        string.Equals(spriteName, "fxPlayerMarkerCone", StringComparison.OrdinalIgnoreCase))
                    {
                        viewCone = image;
                        break;
                    }
                }
            }

            if (beaIcon != null && beaIcon.sprite != null)
            {
                _capturedBeaIcon = beaIcon.sprite;
                _capturedBeaIconColor = beaIcon.color;
                if (facingFrame != null && facingFrame.sprite != null)
                {
                    _capturedFacingFrame = facingFrame.sprite;
                    _capturedFacingFrameColor = facingFrame.color;
                }
                if (facingArrow != null && facingArrow.sprite != null)
                {
                    _capturedFacingArrow = facingArrow.sprite;
                    _capturedFacingArrowColor = facingArrow.color;
                }
                if (viewCone != null && viewCone.sprite != null)
                {
                    _capturedPlayerCone = viewCone.sprite;
                    _capturedPlayerConeColor = viewCone.color;
                    var coneRect = viewCone.rectTransform != null ? viewCone.rectTransform.rect : default;
                    var coneWidth = Mathf.Abs(coneRect.width);
                    var coneHeight = Mathf.Abs(coneRect.height);
                    _capturedPlayerConeSize = coneWidth > 1f && coneHeight > 1f
                        ? new Vector2(coneWidth, coneHeight)
                        : new Vector2(96f, 96f);
                }

                Log.Info("map-capture: captured player sprites bea=" + beaIcon.sprite.name +
                         " frame=" + (_capturedFacingFrame != null ? _capturedFacingFrame.name : "-") +
                         " arrow=" + (_capturedFacingArrow != null ? _capturedFacingArrow.name : "-") +
                         " cone=" + (_capturedPlayerCone != null ? _capturedPlayerCone.name : "-") +
                         " source=" + source);
            }
            else
            {
                Log.Every("map-capture-no-bea-icon", 8f, "map-capture: PlayerMarker found but BeaIcon sprite missing source=" + source);
            }
        }

        private static bool IsUnityUiVisualBehaviour(MonoBehaviour behaviour)
        {
            if (behaviour == null)
                return false;

            var typeName = behaviour.GetType().FullName ?? behaviour.GetType().Name;
            if (typeName.IndexOf("UnityEngine.UI", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            var gameObject = behaviour.gameObject;
            if (gameObject == null)
                return false;

            return gameObject.GetComponent<Graphic>() != null
                || gameObject.GetComponent<Mask>() != null
                || gameObject.GetComponent<RectMask2D>() != null;
        }

        private string ResolveObservedMapKey(Sr2MapUI mapUi, MapDefinition mapDefinition, Sr2Map bestMap)
        {
            var fromDefinition = MapObjectNames.MapKey(mapDefinition);
            if (!string.IsNullOrEmpty(fromDefinition) && fromDefinition != "-")
                return fromDefinition;

            try
            {
                var currentMap = ReflectionTools.GetFieldOrPropertyQuiet(mapUi, "_map", "map") as Sr2Map;
                var inferredCurrent = InferMapKeyFromGameObject(currentMap != null ? currentMap.gameObject : null);
                if (!string.IsNullOrEmpty(inferredCurrent))
                    return inferredCurrent;
            }
            catch { }

            var inferredBest = InferMapKeyFromGameObject(bestMap != null ? bestMap.gameObject : null);
            if (!string.IsNullOrEmpty(inferredBest))
                return inferredBest;
            return "-";
        }

        private static string InferMapKeyFromGameObject(GameObject obj)
        {
            var objectKey = InferObservedMapObjectKey(obj);
            if (!string.IsNullOrEmpty(objectKey) && Current != null && Current.TryResolveKnownMapAlias(objectKey, out var knownKey))
                return knownKey;

            return objectKey;
        }

        private static string InferObservedMapObjectKey(GameObject obj)
        {
            if (obj == null)
                return null;

            var name = MapObjectNames.CleanCloneName(obj.name);
            if (IsMapIdentityAlias(name))
                return name;

            var path = MapObjectNames.PathOf(obj.transform);
            foreach (var part in path.Split('/').Select(MapObjectNames.CleanCloneName))
                if (IsMapIdentityAlias(part))
                    return part;

            return null;
        }

        private bool TryResolveKnownMapAlias(string alias, out string mapKey)
        {
            mapKey = null;
            if (!IsMapIdentityAlias(alias))
                return false;

            var clean = MapObjectNames.CleanCloneName(alias);
            foreach (var record in _mapsByKey.Values)
            {
                if (record == null)
                    continue;

                if (record.Aliases.Contains(clean))
                {
                    mapKey = record.Key;
                    return true;
                }
            }

            return false;
        }


    }
}
