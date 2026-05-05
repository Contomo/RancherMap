# Marker / Fog / Player Pipeline Notes

## Player icon

The vanilla player marker branch found in logs is:

```text
PlayerMarker(Clone)
  FacingFrame
    FacingArrow
  BeaIcon
```

`BeaIcon` is the actual Beatrix/player icon. `FacingFrame` is the direction facing frame arrow (`/\`)

## Fog/clouds

Current captured hierarchy:

```text
MapUI(Clone)
  Map
    Content
      Background
      BackgroundOverlay
      MapHolder
        RainbowIslandMap(Clone)
          zone_fog_areas
            map_fog_*
          fog_static
            Temporary Static Fog Covering
              covering up static fog...
            OutsideFogNegativeMasked
            <other static fog, if present>
      Clouds
```

`OutsideFogNegativeMasked` seems to not like rotation at all and neither does it like being copied willy nilly.
ie, depends highly on state of large map. im not even sure if its the "outside fog", it looks to me more like its a negative mask for the regular decorative clouds?

## Teleporter dotted lines

The dotted teleporter connector is not text. It is:

```text
MonomiPark.SlimeRancher.Map.PortalLineGraphic : UnityEngine.UI.Graphic
```

Ghidra shows `PortalLineGraphic.OnPopulateMesh(VertexHelper vh)` builds a custom mesh of dot quads along a spline:

1. clear `VertexHelper`
2. calculate spline length / distribute points
3. for each point, evaluate gradient/color
4. convert spline world point to local point
5. emit four vertices and two triangles for the dot quad

## forced traversal trigger from options menu

The native crash address resolved to an object-reference traversal path in GameAssembly. It can be force triggered via:

- `GC.Collect()`
- `GC.WaitForPendingFinalizers()`
- `GC.Collect()`
- `Resources.UnloadUnusedAssets()`
- `GC.Collect()`

> This is a trigger, not a cause.
