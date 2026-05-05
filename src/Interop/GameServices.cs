using System;
using System.Collections.Generic;
using System.Linq;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppMonomiPark.SlimeRancher.Map;
using Il2CppMonomiPark.SlimeRancher.SceneManagement;
using Il2CppMonomiPark.SlimeRancher.World;
using UnityEngine;
using Object = UnityEngine.Object;

namespace rancher_minimap
{
    /// <summary>
    /// Typed access to SR2 runtime objects.
    ///
    /// tried to find MapDirector/PlayerDisplayOnMap with
    /// Resources.FindObjectsOfTypeAll(Il2CppSystem.Type). That returns UnityEngine.Object
    /// wrappers, so reflection then ran against UnityEngine.Object and spammed bogus misses such as
    /// "UnityEngine.Object has no field zoneDefinition". This class deliberately uses generated
    /// Il2Cpp wrapper types/generic Resources calls for the SR2 types we already know from the dump.
    /// </summary>
    internal sealed class GameServices
    {
        private float _nextProbe;
        private MapDirector _mapDirector;
        private PlayerDisplayOnMap _playerDisplayOnMap;
        private Transform _playerTransform;
        private Component _teleportablePlayer;
        private Type _teleportablePlayerType;
        private ZoneDefinition _lastZoneDefinition;
        private MapDefinition _lastMapDefinition;
        private SceneGroup _lastSceneGroup;
        private string _lastMapKey = "-";
        private readonly Dictionary<string, MapDefinition> _knownMapDefinitions = new Dictionary<string, MapDefinition>(StringComparer.Ordinal);

        public MapDirector MapDirector => _mapDirector;
        public PlayerDisplayOnMap PlayerDisplayOnMap => _playerDisplayOnMap;
        public Transform PlayerTransform => _playerTransform;
        public string CurrentMapKey => _lastMapKey;

        public bool HasLivePlayer => _playerTransform != null;

        public void Tick()
        {
            if (Time.realtimeSinceStartup < _nextProbe)
                return;

            _nextProbe = Time.realtimeSinceStartup + 1.0f;

            if (_mapDirector == null)
                _mapDirector = Resources.FindObjectsOfTypeAll<MapDirector>().FirstOrDefault(x => x != null);

            if (_playerDisplayOnMap == null)
                _playerDisplayOnMap = Resources.FindObjectsOfTypeAll<PlayerDisplayOnMap>().FirstOrDefault(x => x != null);

            _playerTransform = ResolvePlayerTransform() ?? _playerTransform;
            CacheKnownMapDefinitions();

            var position = TryGetPlayerPosition();
            Log.Every("services", 10f,
                $"services: mapDirector={MapObjectNames.DescribeUnityObject(_mapDirector)} playerDisplay={MapObjectNames.DescribeUnityObject(_playerDisplayOnMap)} playerTransform={MapObjectNames.DescribeUnityObject(_playerTransform)} sceneGroup={MapObjectNames.DescribeUnityObject(_lastSceneGroup)} map={_lastMapKey} pos={FormatPosition(position)}");
        }

        public Vector3? TryGetPlayerPosition()
        {
            var transform = ResolvePlayerTransform() ?? _playerTransform;
            return transform != null ? transform.position : (Vector3?)null;
        }

        public float TryGetPlayerYawDegrees()
        {
            if (_playerDisplayOnMap != null)
            {
                try
                {
                    return _playerDisplayOnMap.GetCurrentRotation().eulerAngles.y;
                }
                catch { }
            }

            var transform = ResolvePlayerTransform() ?? _playerTransform;
            return transform != null ? transform.eulerAngles.y : 0f;
        }

        public ZoneDefinition TryGetZoneDefinition()
        {
            if (_playerDisplayOnMap != null)
            {
                try
                {
                    var zone = _playerDisplayOnMap.GetZoneDefinition();
                    if (zone != null)
                    {
                        _lastZoneDefinition = zone;
                        return zone;
                    }
                }
                catch { }
            }

            try
            {
                var zone = Il2CppMonomiPark.SlimeRancher.UI.Map.MapUIUtilities.GetPlayerCurrentZone();
                if (zone != null)
                {
                    _lastZoneDefinition = zone;
                    return zone;
                }
            }
            catch { }

            return _lastZoneDefinition;
        }

        public MapDefinition TryGetMapDefinition()
        {
            var sceneGroup = TryGetPlayerSceneGroup();
            if (sceneGroup != null)
                _lastSceneGroup = sceneGroup;

            if (sceneGroup != null && TryGetKnownMapForSceneGroup(sceneGroup, out var sceneMap) && sceneMap != null)
            {
                SetLastMapDefinition(sceneMap, "scene-group");
                return sceneMap;
            }

            var zone = TryGetZoneDefinition();
            if (_mapDirector != null && zone != null)
            {
                try
                {
                    var map = _mapDirector.GetMapForZone(zone);
                    if (map != null)
                    {
                        CacheKnownMapDefinition(map);
                        SetLastMapDefinition(map, "zone");
                        return map;
                    }
                }
                catch { }
            }

            var playerPosition = TryGetPlayerPosition();
            var inferredMapKey = playerPosition.HasValue &&
                                 MapVisualCapture.TryInferMapKeyFromWorldPosition(playerPosition.Value, out var inferredKey)
                ? inferredKey
                : null;

            if (!string.IsNullOrEmpty(inferredMapKey))
            {
                if (_knownMapDefinitions.TryGetValue(inferredMapKey, out var inferredMap) && inferredMap != null)
                {
                    SetLastMapDefinition(inferredMap, "position-inference");
                    return inferredMap;
                }
            }

            return _lastMapDefinition;
        }

        private SceneGroup TryGetPlayerSceneGroup()
        {
            if (_playerDisplayOnMap != null)
            {
                try
                {
                    var sceneGroup = _playerDisplayOnMap.GetSceneGroup();
                    if (sceneGroup != null)
                        return sceneGroup;
                }
                catch { }
            }

            var teleportablePlayer = ResolveTeleportablePlayer();
            if (teleportablePlayer != null)
            {
                var fromTeleporter = ReflectionTools.GetFieldOrPropertyQuiet(teleportablePlayer,
                    "SceneGroup", "sceneGroup", "_sceneGroup");
                if (fromTeleporter is SceneGroup sceneGroup)
                    return sceneGroup;
            }

            return _lastSceneGroup;
        }

        private void CacheKnownMapDefinitions()
        {
            try
            {
                foreach (var map in Resources.FindObjectsOfTypeAll<MapDefinition>())
                    CacheKnownMapDefinition(map);
            }
            catch { }
        }

        private void CacheKnownMapDefinition(MapDefinition map)
        {
            if (map == null)
                return;

            try
            {
                if (!string.IsNullOrEmpty(map.name))
                    _knownMapDefinitions[map.name] = map;
            }
            catch { }
        }

        private bool TryGetKnownMapForSceneGroup(SceneGroup sceneGroup, out MapDefinition mapDefinition)
        {
            mapDefinition = null;
            if (sceneGroup == null)
                return false;

            foreach (var map in _knownMapDefinitions.Values)
            {
                if (map == null || !MapContainsSceneGroup(map, sceneGroup))
                    continue;

                mapDefinition = map;
                return true;
            }

            return false;
        }

        private static bool MapContainsSceneGroup(MapDefinition mapDefinition, SceneGroup sceneGroup)
        {
            if (mapDefinition == null || sceneGroup == null)
                return false;

            object relatedScenes = null;
            try { relatedScenes = mapDefinition.RelatedScenes; }
            catch { }
            relatedScenes ??= ReflectionTools.GetFieldOrPropertyQuiet(mapDefinition, "RelatedScenes", "relatedScenes", "_relatedScenes");
            if (relatedScenes == null)
                return false;

            foreach (var item in ReflectionTools.Enumerate(relatedScenes))
                if (ReferenceEquals(item, sceneGroup) || item is Object obj && obj == sceneGroup)
                    return true;

            var gameplaySceneGroups = ReflectionTools.GetFieldOrPropertyQuiet(relatedScenes, "GameplaySceneGroups", "_gameplaySceneGroups");
            foreach (var item in ReflectionTools.Enumerate(gameplaySceneGroups))
                if (ReferenceEquals(item, sceneGroup) || item is Object obj && obj == sceneGroup)
                    return true;

            return false;
        }

        private void SetLastMapDefinition(MapDefinition mapDefinition, string source)
        {
            if (mapDefinition == null)
                return;

            _lastMapDefinition = mapDefinition;
            _lastMapKey = MapObjectNames.MapKey(mapDefinition);
            Log.Every("services-map-" + _lastMapKey + "-" + source, 2f,
                $"services: current map={_lastMapKey} source={source} sceneGroup={MapObjectNames.DescribeUnityObject(_lastSceneGroup)}");
        }

        private Transform ResolvePlayerTransform()
        {
            if (_playerDisplayOnMap != null && _playerDisplayOnMap.transform != null)
                return _playerDisplayOnMap.transform;

            var teleportablePlayer = ResolveTeleportablePlayer();
            if (teleportablePlayer != null && teleportablePlayer.transform != null)
                return teleportablePlayer.transform;

            return ResolveSceneContextPlayerTransform();
        }

        private Component ResolveTeleportablePlayer()
        {
            if (_teleportablePlayer != null)
                return _teleportablePlayer;

            if (_teleportablePlayerType == null)
                _teleportablePlayerType = ReflectionTools.FindAssemblyType("Assembly-CSharp", "Il2Cpp.TeleportablePlayer");

            if (_teleportablePlayerType != null)
            {
                try
                {
                    _teleportablePlayer = Object.FindObjectOfType(Il2CppType.From(_teleportablePlayerType)) as Component;
                    return _teleportablePlayer;
                }
                catch { }
            }

            return null;
        }

        private static Transform ResolveSceneContextPlayerTransform()
        {
            try
            {
                var sceneContext = SceneContext.Instance;
                object player = sceneContext != null ? sceneContext.Player : null;
                if (player is GameObject gameObject)
                    return gameObject.transform;
                if (player is Component component)
                    return component.transform;
                return ReflectionTools.GetFieldOrPropertyQuiet(player, "transform", "Transform") as Transform;
            }
            catch
            {
                return null;
            }
        }

        private static string FormatPosition(Vector3? position)
        {
            return position.HasValue
                ? $"{position.Value.x:F1},{position.Value.y:F1},{position.Value.z:F1}"
                : "-";
        }

    }
}
