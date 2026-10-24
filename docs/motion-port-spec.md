# Motion port spec (sfera_client reference)

Source of truth: `External/sfera_client_ref/Source/SferaCoreLoader`.
Reference commit: `459ee38` (2026-07-30).

The reference is the behaviour both sides must reach. Where our code disagrees, the reference wins.

## Frame

The reference integrator runs in the game's native frame: **Y is down**, `SpawnY`/`FeetY` is the
feet line, `-Y` is up (`JumpImpulse = -5.0`, `JumpGravity = 9.8`, `NewVelocityY = VelocityY + g*dt`).
X and Z are world ground axes, `TileSize = 100`.

Port the solver verbatim in that frame. Convert to Godot Y-up only at the boundary
(`godotY = -gameY`), never inside the solver.

## Frame chain (proven)

The solver frame is the repo's **stored** `character.X/Y/Z`, which is the reference's
`FGameWorldPosition` frame. Evidence:

- `SphereClient.Gameplay.cs:62` `TerrainWater.IsUnderwater (character.X, -character.Y, -character.Z)`,
  documented "Stored Y and Z are negated client axes". So Godot world = `(X, -Y, -Z)` of stored.
- `CharacterDbEntry.cs:200` `Origin => new Vector3 (X, Y, Z)` is stored, not world.
- Raw `.lnd` tile vertices are in the stored frame: tile-local x/z span 0..100, Y spans about
  -345..+15.
- `SupportHeightAt` (`GameWorldMovement.cpp:54`) seeds `BestY = FeetY + GroundSnapHeight` and its
  `ConsiderSupport` keeps a candidate only while `Height < BestY`, so the support it picks is the
  **lowest Y** in range. Support is the surface under the feet, so lowest Y is the highest surface:
  Y-down. This is a solver-only argument, no rendering involved.
- Gravity agrees: `UpdateVertical` `NewVelocityY = VelocityY + JumpGravity * DeltaSeconds` with
  `JumpGravity = 9.8` positive, and `JumpImpulse = -5.0` to leave the ground. Positive Y is into
  the ground.
- The camera agrees: `GameWorldRender.cpp:437` eye is `SpawnY - CameraEyeHeight` (above the feet
  by subtracting) and `LookAtRhMatrix (eye, target, FVector3{0, -1, 0})` passes up as **-Y**.

So: solver in stored game coords, Y down, `+Y` into the ground. The only conversion at the
boundary is `(x, y, z)_godot = (x, -y, -z)_stored`, plus the same on the wish and the yaw.

The yaw sign is settled by the send path, no guess left. `ClientApplication.cpp:1275` pushes
`liveYaw = actor->rotation.x`, the client render-space yaw (Y-up), and
`ClientApplication.cpp:1308-1313` builds the render base as
`{serverPose.x, -serverPose.y, -serverPose.z}`, commented "Channel 2 is Godot space". So the wire
pose arrives in **game** axes (Y down) and the yaw on the wire is the **client/Godot** yaw
(Y-up), a mixed pair. `PlayerMotion.Apply` keeps that: `body.Rotation.Y = slot.Yaw` un-negated.
The reference's `SpawnAngle` is the game-space angle and it negates it for the camera
(`GameWorldMovement.cpp:30` `CameraYaw = -SpawnAngle`, remote draw `RotationYMatrix(-Angle)` at
`GameWorldPlayer.cpp:368`). So the solver's angle is `angle = -wireYaw`, and
`VelocityX = (sin(angle) * forward + cos(angle) * right) * Speed` uses that, not the wire value.

The wish needs no conversion: `PlayerMotion` maps it to Godot as `new Vector3 (wish.X, vy, -wish.Y)`,
so `wishZ` is already the game-space Z velocity and `wishX` the game-space X velocity.
`ClientApplication.cpp:1265` sends `commanded_velocity.x/z`, client units per second, which the
server comment records as "about 2.1 for a full step". That 2.1 is the old client's own speed and
is not a solver constant; the solver uses `WalkSpeed 5.1` and `RunMultiplier 2.2`, and the client's
wish must be scaled to that, not left at 2.1.

## Files to mirror

| Reference | Content |
|---|---|
| `Private/Renderer/GameWorld/GameWorldMovement.cpp` | `SnapToGround`, `SetPlayerWorldPosition`, `SupportHeightAt`, `TryMoveTo`, `Jump`, `ApplySlopeSlide`, `UpdateVertical`, `Update`, `RotateView` |
| `Private/Renderer/GameWorld/GameWorldTerrain.cpp` | `BuildTerrainCpuResource` (LND), `TerrainTriangleAt`, `TerrainSampleAtInstance`, `BuildTerrainSurfaceGrid`, `TerrainHeightAt`, `TerrainSurfaceNearAt`, `TerrainSurfaceAt`, `FlatGrassSurfaceAt` |
| `Private/Renderer/GameWorld/GameWorldObjects.cpp` | `BuildStaticCollisionMesh`, `BuildCollisionBvhNode`, `QueryStaticCollisionInstances`, `QueryStaticCollisionTriangles`, `CapsuleOverlapsStatic`, `RecoverFromPenetration`, `StaticFloorHeightAt`, `PointInTriangleXz`, `SegmentTriangleDistanceSquared`, `BoundsIntersect`, `TransformBounds`, `InvertAffineMatrix` |
| `Public/Renderer/GameWorld/GameWorldSupport.h` | `Dot`, `Cross`, `NormalizeVector`, `Subtract`, `Add`, `Scale`, `TransformPoint`, `PointTriangleDistanceSquared` |
| `Public/WorldScene/WorldTypes.h` | `FVector2/3`, `FBox2/3`, `FWorldMapGrid`, `FWorldMapCell::TerrainStem`, `FWorldContourDatabase` |
| `Public/Model/MdlModel.h`, `Private/Model/MdlModel.cpp` | `.mdl` mesh and collision index reader |

## Constants (`GameWorldConfig.h`)

```
TileSize                       100.0     OriginRow 40   OriginColumn 39
WalkSpeed                      5.1       RunMultiplier 2.2
PlayerCollisionRadius          0.32      PlayerCollisionHeight 1.8
MaxStepHeight                  0.6       GroundSnapHeight 0.45
MovementCollisionStep          0.12      MaxMovementDeltaSeconds 0.10
MaxSimulationStepSeconds       0.0333333 CollisionSkin 0.025
CollisionFloorNormalThreshold  0.65      SlopeSlideNormalY 0.65
SlopeSlideFactor               0.6       JumpImpulse -5.0   JumpGravity 9.8
kTerrainSurfaceGridResolution  32        kStaticCollisionCellSize 8.0
kLndHeaderBytes 0x68c0  kLndVertexBytes 40  kLndTriangleBytes 28
```

Our old server numbers (`PlayerMotion.MaxSpeed = 2.1*1.5*2.2`, `StepUpMeters 0.4`,
`SlopeAcceleration 4`, `SweepMargin 0.004`) are gone. The table above wins.

## Geometry sources (raw assets, approach A)

The server reads the assets from inside the repo, not from the client install:

```
Godot/Terrain/Landscape/original/<Landscape|Landscape_ph|Landscape_hr|Landscape_rd>/<stem>.lnd (+ .dds)
Godot/Models/original/<models|Models_hr|Models_ph|Models_rd>/<model>.mdl
```

902 tiles and 1749 models, source dir names kept so the reference's lookup priority survives.
Both roots carry `.gdignore` so the Godot import scan skips them (same trick as
`GodotAssetSource`). Keep them out of the csproj compile globs.

- Terrain: stem from `FWorldMapCell::TerrainStem()` over the map grid. The reference requires a
  `.dds` beside the `.lnd`, and every tile has one.
  Verified layout on real assets: header 0x68c0, vertex count u16 at 4, 40-byte vertices
  (`x,y,z, nx,ny,nz, u,v, du,dv`), then 28-byte triangles with u16 indices at 0/2/4. One tile is
  1296 verts / 1152 tris over a 100x100 tile-local footprint, tile Y spans about -345 to +15, so
  `+Y` is down and matches `JumpImpulse = -5.0`.
- Objects: `.mdl` with the reference collision filter, a triangle is collision only when
  `(Flags & 0x100) == 0` (`GameWorldObjects.cpp:1277`), flags from the `indices_0x0a` section at
  offset 6 per record (`MdlModel.cpp:173`). Placements come from the existing
  `Godot/Terrain/ObjectDataJson/` scan (`TerrainObjectPlacementSource`).
  Skinned models get a cylinder proxy, not triangles: `Radius = clamp(max(w,d) * 0.18, 0.24,
  PlayerCollisionRadius * 1.15)`.

Streaming radii follow the reference: terrain `VisibleRadius = 3` tiles, statics
`StaticObjectRadius = 250` + `TileSize`, and the forward preload guard in
`PreloadStreamingGuard`.

## Streaming reads the raw assets (no bake)

Collision streams straight from `.lnd` and `.mdl`. The `TerrainBake` collision products are not
used by the solver and retire: `GroundShapes` 53 MB, `ObjectShapes` 14 MB, `ObjectChunks`
(index.json) 42 MB, `GroundChunks` 4 MB. `GroundMeshes`, `GeneratedMultiMeshes` and
`GeneratedNavMeshes` stay: they are visual and monster NavMesh, not player collision.
`pack_terrain_ground_chunks.ps1` and `pack_object_collision_chunks.ps1` leave the collision path.

Measured feasibility (`Tools/_tmp/check_mdl_coverage.py`, `Tools/_tmp/check_mdl_cost.py`):

- All 1106 distinct placement names resolve to a vendored `.mdl`, covering all 223428 placements.
  No gaps, so no name needs a fallback.
- All 902 map tile stems resolve to a `.lnd`, each with the `.dds` the reference reader requires.
- Densest 100 m cell: 102 distinct models, 3.4 MB. Reads run about 150 MB/s, so a cold dense cell
  costs about 25 ms. Cache per asset path (reference `TerrainCpuCache`, per-model
  `StaticCollisionMesh`), so the cost is once per model, not once per placement.

Behaviour change that comes with raw triangles, accepted because the reference wins:
`ObjectCollisionPackHeadless.cs:106` used a box from the `.mdl` header for `collision_kind` 0/1
models and skipped `ClientPlacementMarkers` models, because a baked trimesh blocked ground the
client walks over. The reference has neither rule: non-skinned models get raw triangles, skinned
models get a cylinder proxy. So world trees get the cylinder proxy, not a box.

## Tile addressing

`map.bin` is the same 140800-byte table in all three places (verified by SHA256
`637D68EC…`): our `Godot/Terrain/map.bin`, `Godot/Terrain/map.txt` (a byte copy, same name), and
the client's `landscape/map.bin`. It is `80 x 80` records of `stride 0x16`: 20-byte ASCII tile
name, u16 at +20 (the two variant digits), rest padding. A cell is present when the name is
non-empty. Stem = `name_firstDigitSecondDigit` (lowercase on disk), the reference's
`FWorldMapCell::TerrainStem()`.

The client's own ground truth, `GeneratedScripts.cpp:16393` and `16407`:

```cpp
const std::int32_t column = controlledPosition_.x / 100.0f;
const std::int32_t row = controlledPosition_.z / 100.0f;
ambientLandscapeCell_ = column + 40 + (39 - row) * 80;
// back to world: x = (cell % 80 - 40) * 100, z = (39 - cell / 80) * 100
```

So grid x = `floor(worldX/100)`, grid z = `floor(worldZ/100)`, and the flat cell index
`column + 40 + (39 - row) * 80` matches the reference's `OriginRow 40` / `OriginColumn 39`:
the Z axis is flipped in the grid, not the world. `map.bin` is vendored at
`Godot/Terrain/Landscape/original/map.bin` so both sides read one table.

Lookup, the form an implementer wants:

```
Cells[(39 - floor(worldZ/100)) * 80 + (floor(worldX/100) + 40)]
```

`WorldScene.cpp:529` `LoadMapGrid` fills `Cells[z*80 + x]` with x fast, and
`GameWorldTerrain.cpp:966` places a tile at `OriginX = (Row - 40) * TileSize`,
`OriginZ = (39 - Column) * TileSize`, so in the reference's own frame a tile origin is
`(rawX * 100, rawZ * 100)`, no mirror and no offset.

The solver belongs in that frame. Our repo's GridMap frame is a different one:
`TerrainGroundIndex` keys cells as `gx = 79 - rawX`, `gz = rawZ`, and `terrain_scene` sits under
MainServer's `Ry(-90)` (`TerrainCollisionFrame` header). Prove the GridMap-to-reference transform
from the node tree before wiring the solver to it, and append it here. Do not assume the repo's
`gx`/`gz` are the reference's `rawX`/`rawZ`: X is mirrored and the 40 offset is absent.

## Ownership (decision: physics only)

The wire and ownership stay as they are:

1. Client sends wishes on channel 1 (`CommandDatagram`, 34-byte commands, seq, wishX, wishZ, yaw,
   buttons).
2. Server integrates at `SimulationClock.TickHz` 60 with the ported solver, publishes the pose.
3. Client keeps `LocalPrediction` (64-sample replay, `maxYawStep` 4, `stepSeconds` 1/60), and its
   step function becomes the ported solver.

So both sides run the same integrator on the same geometry. The client's replay must produce the
same numbers as the server's step.

## Placements and frame closure (proven)

The reference reads placements straight from `params/sst*.mbd`, the same 252 files we vendored
(`GameWorldObjects.cpp:1404`): u16 record count at 0, u16 0 at 2, then 44-byte records. 20-byte
ASCII name at 0, position floats at 20/24/28, rotation floats read as X at **36**, Y at **32**,
Z at **40**, and the world matrix is
`RotationY(-rot.Y) * RotationX(rot.X) * RotationZ(rot.Z)` with the position in `_41.._43`
(`GameWorldSupport.h:343` `PlacementMatrix`).

Verified against our `ObjectDataJson` (`Tools/_tmp/check_mbd_placements.py`,
`check_mbd_rotation.py`): `sst_00.mbd` is count 1899, 83560 bytes, exactly `4 + 1899*44`;
positions equal the JSON `x,y,z` **un-negated**; and f32 = `yaw`, f36 = `pitch`, f40 = `roll`
bit-for-bit. So the JSON is the same table in the same axes, no conversion needed.

Frame closure (`Tools/_tmp/check_placement_frame.py`): over 217192 named placements, the Y
component spans -2842.3 to 2100.0, median 159.7. Sampled LND tile Y spans -2843.0 to 286.5,
median 159.5. Same band, so LND vertices, `.mbd` placement positions, and the stored
`character.X/Y/Z` are all **one frame**: Y down. The solver needs no asset-space conversion at all.

## Server core, already built (`Server\Simulation\Motion\`)

Pure C#, no Godot physics, float, mirrors the reference function-for-function. Build on these, do
not re-implement them.

| File | Public surface |
|---|---|
| `MotionConfig.cs` | every constant in the table above, plus `SkinnedProxyRadius (w, d)` and `NonCollisionTriangleFlag` |
| `MotionMath.cs` | `Dot Cross Subtract Add Scale NormalizeVector TransformPoint AxisValue BoundsIntersect` |
| `MotionMatrix.cs` | row-major `_11.._44`: `Identity Placement RotationX RotationY RotationZ Multiply TryInvert FromBasis` |
| `MotionGeometry.cs` | `MotionBounds` (`IsValid Contains Intersects Expand CenterAlong ExtentAlong Transform`), `MotionTriangle`, `MotionBvhNode`, `PointInTriangleXz`, `ClosestPointOnTriangle`, `PointTriangleDistanceSquared`, `SegmentIntersectsTriangle`, `SegmentSegmentDistanceSquared`, `SegmentTriangleDistanceSquared` |
| `MotionTerrainResource.cs` | `Build (lndPath)`, `CellTriangles (cellIndex)`, `SurfaceCellIndex (local, tileSize)`, `Positions Indices SurfaceCellStarts SurfaceCellTriangles Bounds` |
| `MotionTerrainField.cs` | `Add (resource, tileX, tileZ)`, `TileForGridCell (gridX, gridZ)`, `HeightAt`, `SurfaceAt`, `SurfaceNearAt`, `LoadedCount`, `NoteMissing/HasMissing` |
| `MotionCollisionMesh.cs` | `Build (vertices, indices)` + `Triangles Order Nodes` |
| `MotionModelResource.cs` | `Build (mdlPath)` + `Bounds IsSkinned ProxyRadius CollisionMesh` |
| `MotionStaticField.cs` | `Add (resource, in MotionMatrix world)`, `CapsuleOverlapsStatic (x, feetY, z, out MotionCollisionHit, ignoreSupportingFloor)`, `StaticFloorHeightAt (x, z, minY, maxY, out y, out normal)`, `Clear`, `LoadedCount` |
| `MotionStaticField.cs` | `MotionCollisionHit { Vector3 Normal; float Penetration; }` |

Frame: all of these take and return the game frame (Y down), tile-local inside a
`MotionTerrainResource`. Tile placement follows `TileForGridCell`. Model world matrices come from
`MotionMatrix.Placement (position, rotation)` with the `.mbd` rotation read as
`(pitch at 36, yaw at 32, roll at 40)` mapped to `Vector3 (pitch, yaw, roll)` per
`PlacementMatrix`'s `RotationY(-rot.Y) * RotationX(rot.X) * RotationZ(rot.Z)` order, so
`rotation.X = pitch`, `rotation.Y = yaw`, `rotation.Z = roll`.

Still to write: the integrator (`MotionPlayer.cs`) and the asset streamer (`MotionWorldStream.cs`),
then the `PlayerMotion.cs` rewire.

## Acceptance

- One shared constant set, no per-side tuning.
- The solver is pure: no Godot node, no `PhysicsServer3D`, no physics-engine shape casts.
- Terrain support, step-up, slope slide, and penetration recovery all come from the reference
  functions, not from the old sweep/ring/descent code.
- Server build clean (`dotnet build`). Client x64 Debug build clean.
