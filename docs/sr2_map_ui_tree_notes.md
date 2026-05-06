# SR2 Map UI tree notes

## Big map UI root

```text
MapUI(Clone)                                      [MonomiPark.SlimeRancher.UI.Map.MapUI]
└─ Map
   ├─ Content                                    [MapUI._mapContainer]
   │  ├─ Background                              [Image; MapUI._mapBackground]
   │  │                                          observed: sprite=-, material=Default UI Material, rect=0x0
   │  ├─ BackgroundOverlay                       [Image; MapUI._mapBackgroundOverlay]
   │  │                                          observed main-world: sprite=tilingBG, material=OscillatingWaterUI, rect=0x0
   │  │                                          MapDefinition.MapBackgroundOverlay is applied here by MapUI.SetImageProperties(...)
   │  ├─ Clouds                                  [Image] material=Map Vignette Clouds
   │  │  ├─ ????
   │  ├─ MapHolder
   │  │  ├─ RainbowIslandMap(Clone)              [MonomiPark.SlimeRancher.UI.Map.Map]
   │  │  │  observed area: x=-2000, y=-2600, w=4000, h=3800
   │  │  │  note: this is the prefab branch used for MapDefinition `MainWorldMap`
   │  │  └─ LabyrinthMap(Clone)                  [MonomiPark.SlimeRancher.UI.Map.Map]
   │  │     observed area: x=-3000, y=-500, w=3200, h=2500
   │  │     note: appears together with RainbowIslandMap during tab transitions (`maps=2`)
   │  └─ Markers                                 [MapUI._mapMarkerSection]
   │     ├─ GenericMarker(Clone)                 [MapMarker]
   │     │  ├─ <root Image>                      example sprites: iconGadgetMarkerNo_small, iconGadgetRefineryLink_small
   │     │  └─ Elevation                         example sprite: map_arrowDown_down
   │     ├─ DroneStationMarker(Clone)            [DroneStationMapMarker]
   │     │  ├─ Icon                              example sprite: iconExplorerDroneFace
   │     │  ├─ FaceIcon                          example sprite: iconDroneOvercharge
   │     │  ├─ Elevation                         example sprite: map_arrowDown_down
   │     │  └─ FuelLevelIcon                     example sprite: iconBatteryOvercharge
   │     ├─ AncientTeleporterMarker(Clone)       [AncientTeleporterMarkerUI]
   │     ├─ DetectorGadgetMapMarker(Clone)       [DetectorGadgetMapMarker]
   │     ├─ GordoMarker(Clone)                   [GordoMarkerUI]
   │     ├─ PlortDepositorMarker(Clone)          [PlortDepositorMarkerUI]
   │     ├─ PuzzleSlotMarker(Clone)              [PuzzleSlotMarkerUI]
   │     ├─ StabilizingMarker(Clone)             [StabilizingMarkerUI]
   │     ├─ NavigationMarker(Clone)              [NavigationMarkerUI]
   │     └─ PlayerMapMarker(Clone)               [PlayerMapMarker]
   │        observed sprites: player_face, framePlayerMarker, fxPlayerMarkerCone
   └─ Overlay UI
      ├─ Zoomed Out UI
      │  ├─ Dimmer                               [Image]
      │  └─ HeaderAndTabs
      │     ├─ PrevMap
      │     │  ├─ Icon                           observed sprite=iconZoneConservatory
      │     │  └─ InputIconGroup/...
      │     └─ NextMap
      │        ├─ Icon                           observed sprite=iconZoneConservatory
      │        └─ InputIconGroup/...
      └─ Zoomed In UI
         ├─ TitleContainer
         │  ├─ FillToEdge                        [Image]
         │  └─ Title/Image                       observed sprite=iconZoneSea
         ├─ CounterContainer
         │  ├─ FillToEdge                        [Image]
         │  └─ Counter/Icon                      observed sprite=iconTreasurePod
         └─ WeatherContainer
            ├─ FillToEdge                        [Image]
            └─ Weather/Indicator                 observed sprite=iconWeatherRain
```

## Map branch internals

Both map branches are `Map` prefabs. `Map` has serialized container refs:

```text
Map
├─ _staticMarkerContainer                         [Transform]
├─ _belowMarkersContainer                         [Transform]
├─ _zoneMarkersContainer                          [Transform]
└─ _forceActiveOnAwake                            [GameObject[]]
```

child groups under a map branch:

```text
<MapBranch>
├─ zone graphics                                  [Image/Graphic using MapZone or LabyrinthMapZone materials]
│  ├─ main-world map sprites in dump:
│  │  ├─ Map_Fields
│  │  ├─ Map_Strand_CU5
│  │  ├─ Map_Gorge_CU5
│  │  ├─ Map_Bluffs
│  │  ├─ Map_Sanctuary
│  │  └─ Map_Wall
│  └─ labyrinth map sprite in dump:
│     └─ Map_Labyrinth
├─ zone_fog_areas                                 [per-zone reveal fog; dynamic reveal state]
│  ├─ map_fog_fields1_sdf
│  ├─ map_fog_fields2_sdf
│  ├─ map_fog_strand1_sdf
│  ├─ map_fog_strand2_sdf
│  ├─ map_fog_strand3_sdf
│  ├─ map_fog_strand4_sdf
│  ├─ map_fog_gorge1_sdf
│  ├─ map_fog_gorge2_sdf
│  ├─ map_fog_gorge3_sdf
│  ├─ map_fog_gorge4_sdf
│  ├─ map_fog_sanctuary_sdf
│  ├─ map_fog_labyrinth_hub_sdf
│  ├─ map_fog_labyrinth_core_sdf
│  ├─ map_fog_labyrinth_lavaDepths_sdf
│  ├─ map_fog_labyrinth_waterworks_sdf
│  ├─ map_fog_labyrinth_dreamland_1_sdf
│  ├─ map_fog_labyrinth_dreamland_2_sdf
│  ├─ map_fog_labyrinth_terrarium_1_sdf
│  └─ map_fog_labyrinth_terrarium_2_sdf
├─ fog_static                                     [static masks / non-reveal fog helpers]
│  ├─ Temporary Static Fog Covering
│  ├─ OutsideFogNegativeMasked
│  └─ Negative Mask
├─ zone_links                                     [teleporter/zone dotted links]
│  └─ zone_link_*
│     └─ PortalLineSpline                         [Spline]
│        └─ PortalLineTest                        [PortalLineGraphic + Graphic]
├─ StaticMarkerContainer                          [Map.StaticMarkerContainer; exact child name not verified]
├─ BelowMarkersContainer                          [Map.BelowMarkersContainer]
│  └─ Cone                                        [player cone/lower-layer object; observed by code path]
└─ ZoneMarkersContainer                           [Map.ZoneMarkersContainer]
```

## Relevant materials / shaders

```text
UI/Map/Shaders/MapZone
UI/Map/Shaders/LabyrinthMapZone
UI/Map/Shaders/OscillatingWaterUI
UI/Map/Shaders/LabyrinthCloudSeaUI
UI/Map/Shaders/ShorelineWaterMap
UI/Map/Shaders/PortalLineDot
UI/Map/Shaders/PosterizedFogOfWarClouds
UI/Map/Shaders/PosterizedFogOfWarMask
UI/Map/Shaders/PosterizedFogOfWarNegativeMask
```

## Important identity mismatch

```text
MapDefinition: MainWorldMap
Prefab branch: RainbowIslandMap(Clone)

MapDefinition: LabyrinthMap
Prefab branch: LabyrinthMap(Clone)
```
