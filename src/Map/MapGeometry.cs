using UnityEngine;

namespace rancher_minimap
{
    internal readonly struct MapGeometry
    {
        public readonly bool HasWorldArea;
        public readonly bool HasMapArea;
        public readonly bool HasProjectionMapArea;
        public readonly Rect WorldArea;
        public readonly Rect MapArea;
        public readonly Rect ProjectionMapArea;

        public MapGeometry(Rect worldArea, bool hasWorldArea, Rect mapArea, bool hasMapArea)
            : this(worldArea, hasWorldArea, mapArea, hasMapArea, default, false)
        {
        }

        public MapGeometry(Rect worldArea, bool hasWorldArea, Rect mapArea, bool hasMapArea, Rect projectionMapArea, bool hasProjectionMapArea)
        {
            WorldArea = worldArea;
            HasWorldArea = hasWorldArea;
            MapArea = mapArea;
            HasMapArea = hasMapArea;
            ProjectionMapArea = projectionMapArea;
            HasProjectionMapArea = hasProjectionMapArea;
        }

        public bool HasVanillaProjection => HasWorldArea &&
                                            HasProjectionMapArea &&
                                            UnityEngine.Mathf.Abs(WorldArea.width) > 1f &&
                                            UnityEngine.Mathf.Abs(WorldArea.height) > 1f &&
                                            UnityEngine.Mathf.Abs(ProjectionMapArea.width) > 1f &&
                                            UnityEngine.Mathf.Abs(ProjectionMapArea.height) > 1f;

        public static MapGeometry Empty => new MapGeometry(default, false, default, false, default, false);
    }
}
