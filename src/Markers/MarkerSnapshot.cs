using UnityEngine;

namespace rancher_minimap
{
    /// <summary>
    /// Pure marker snapshot owned by the minimap.
    ///
    /// Does not store MapDirector.Marker or IMapMarkerSource wrappers. Marker visuals may hold a
    /// minimap-owned clone of vanilla marker UI, stripped of marker behavior, so compound icons can
    /// render like the original map without retaining source marker objects.
    /// </summary>
    internal readonly struct MarkerSnapshot
    {
        public readonly int Id;
        public readonly Vector3 WorldPosition;
        public readonly Vector2 MapPosition;
        public readonly bool HasMapPosition;
        public readonly string Kind;
        public readonly bool Visible;
        public readonly Sprite Sprite;
        public readonly Color Color;
        public readonly Vector2 Size;
        public readonly GameObject VisualTemplate;

        public MarkerSnapshot(int id, Vector3 worldPosition, string kind, bool visible)
        {
            Id = id;
            WorldPosition = worldPosition;
            MapPosition = default;
            HasMapPosition = false;
            Kind = kind;
            Visible = visible;
            Sprite = null;
            Color = UnityEngine.Color.white;
            Size = new Vector2(32f, 32f);
            VisualTemplate = null;
        }

        public MarkerSnapshot(int id, Vector2 mapPosition, string kind, bool visible, Sprite sprite = null, Color? color = null, Vector2? size = null, GameObject visualTemplate = null)
        {
            Id = id;
            WorldPosition = default;
            MapPosition = mapPosition;
            HasMapPosition = true;
            Kind = kind;
            Visible = visible;
            Sprite = sprite;
            Color = color ?? UnityEngine.Color.white;
            Size = size ?? new Vector2(32f, 32f);
            VisualTemplate = visualTemplate;
        }

        public MarkerSnapshot WithVisible(bool visible)
        {
            return HasMapPosition
                ? new MarkerSnapshot(Id, MapPosition, Kind, visible, Sprite, Color, Size, VisualTemplate)
                : new MarkerSnapshot(Id, WorldPosition, Kind, visible);
        }
    }
}
