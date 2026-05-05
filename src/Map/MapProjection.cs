using UnityEngine;
using Sr2Map = Il2CppMonomiPark.SlimeRancher.UI.Map.Map;

namespace rancher_minimap
{
    /// <summary>
    /// World-to-map projection. MapProjectionHelper/GetMapPosition and
    /// MapViewModel.TransformMappedWorldToUIPoint are doing a linear map from an authored world rect
    /// to an authored UI rect. This class keeps that math isolated from Unity object mutation.
    /// </summary>
    internal sealed class MapProjection
    {
        private Rect _world = new Rect(-800f, -800f, 1600f, 1600f);
        private Rect _observed = new Rect(-80f, -80f, 160f, 160f);
        private bool _hasObserved;

        public bool HasAuthoredWorldRect { get; private set; }
        public string Source { get; private set; } = "adaptive-fallback";


        public void UpdateFromMapComponent(Sr2Map map)
        {
            if (map == null)
                return;

            try
            {
                var worldArea = map.GetWorldArea();
                if (worldArea.width > 1f && worldArea.height > 1f)
                {
                    _world = worldArea;
                    HasAuthoredWorldRect = true;
                    Source = "ui-map:GetWorldArea";
                }
            }
            catch { }
        }

        public void UpdateFromGeometry(MapGeometry geometry)
        {
            if (geometry.HasWorldArea && Mathf.Abs(geometry.WorldArea.width) > 1f && Mathf.Abs(geometry.WorldArea.height) > 1f)
            {
                _world = geometry.WorldArea;
                HasAuthoredWorldRect = true;
                Source = "captured-map-geometry";
            }
        }

        public void UpdateAuthoredRect(object mapDefinition)
        {
            if (mapDefinition == null)
                return;

            // Names are deliberately broad because IL2CPP field names often survive while accessor
            // names drift. Ghidra confirms MapDefinition is what owns the authored map/world data.
            var rect = ReflectionTools.GetFieldOrPropertyQuiet(mapDefinition,
                "worldArea", "worldRect", "worldBounds", "_worldArea", "_worldRect", "WorldArea", "WorldRect");

            if (rect is Rect r && r.width > 1f && r.height > 1f)
            {
                _world = r;
                HasAuthoredWorldRect = true;
                Source = "map-definition-rect";
                return;
            }

            var maybeMin = ReflectionTools.GetFieldOrPropertyQuiet(mapDefinition, "worldMin", "_worldMin", "minWorld", "_minWorld");
            var maybeMax = ReflectionTools.GetFieldOrPropertyQuiet(mapDefinition, "worldMax", "_worldMax", "maxWorld", "_maxWorld");
            if (maybeMin is Vector2 min2 && maybeMax is Vector2 max2)
            {
                var w = max2.x - min2.x;
                var h = max2.y - min2.y;
                if (w > 1f && h > 1f)
                {
                    _world = new Rect(min2.x, min2.y, w, h);
                    HasAuthoredWorldRect = true;
                    Source = "map-definition-minmax";
                }
            }
        }

        public void Observe(Vector3 world)
        {
            var p = new Vector2(world.x, world.z);
            if (!_hasObserved)
            {
                _observed = new Rect(p.x - 80f, p.y - 80f, 160f, 160f);
                _hasObserved = true;
                return;
            }

            var minX = Mathf.Min(_observed.xMin, p.x - 40f);
            var maxX = Mathf.Max(_observed.xMax, p.x + 40f);
            var minY = Mathf.Min(_observed.yMin, p.y - 40f);
            var maxY = Mathf.Max(_observed.yMax, p.y + 40f);
            _observed = Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        public Vector2 Project01(Vector3 world)
        {
            var rect = HasAuthoredWorldRect ? _world : _observed;
            if (rect.width <= 0.01f || rect.height <= 0.01f)
                return new Vector2(0.5f, 0.5f);

            var u = Mathf.InverseLerp(rect.xMin, rect.xMax, world.x);
            var v = Mathf.InverseLerp(rect.yMin, rect.yMax, world.z);
            return new Vector2(u, v);
        }

        public Vector2 MapToAnchored(Vector3 world, float mapSize)
        {
            var uv = Project01(world);
            return new Vector2((uv.x - 0.5f) * mapSize, (uv.y - 0.5f) * mapSize);
        }
    }
}
