# Experimental modern forest overlay

The `fore-1` overlay replaces the old tree faces only when the M1.1 experimental
Stage checksum is present. The original Stage checksum disables the overlay.
Scenery actors are visual only; collision remains defined by the Stage.

The placement generator reads the V24 `anchor_game` coordinates in Stage game
units (X/Y/Z), converts them through `WorldSpace.Mirror` to render positions
(X/Y/-Z), and derives tree height from V24 `height_view` using a common,
validated preview scale. V24 `anchor_view` positions are centred, scaled Blender
coordinates and must never be treated as game coordinates. The generator
rejects insufficient or inconsistent anchors and any placement outside known
`fore-1` Stage extents.

The temporary Blender export merges the prepared LOD1 meshes and applies a
collapse decimator to a bounded vertex budget (default 6000 vertices). Blender
assets in the source `.blend` are not modified. The entire model is still
loaded from one GLB, with existing materials and UVs preserved as far as
Blender's mesh reduction permits. The output must be inspected for visual
regressions and transparent foliage.

The gameplay scene registers all 99 authored placements but creates actors only
within 1400 Stage units of the player, releasing them beyond 1900 units. At most
16 modern tree actors may be resident per field, with at most one GPU creation
per frame. This is distance-based actor streaming, **not GPU instancing**. The
renderer reuses each GLB's buffers, while the number of rendered actors is
bounded to limit draw and uniform-buffer pressure. The resident set can show
fewer than 99 trees at a time. Other visual content keeps its normal behaviour.

The Stage publisher and rollback remain the original M1.1 tools, and the asset
installer remains the original M1.2 `m1_2_99_assets.py`. Always restore the Stage
before installing, uninstalling or replacing GLB assets. The runtime checks the
mask Stage checksum, manifest format and GLB SHA-256 before loading.

The M1.3 upgrade modifies only the existing M1.2 placement/export scripts,
`GameScene.ModernForest.cs`, `ModernForestOverlay.cs`, and this document.
The upgrade installer takes SHA-verified backups and refuses unrelated changes.

This is still an experimental forest: it has not been verified on the user's
Blender Flatpak build or Vulkan client. Compare GPU frame times and inspect
trunks, bases, leaf opacity, wind, shadows, and distances before further use.

**Correção M1.3.1:** os limites do renderer utilizam Z positivo (espelho do Z negativo do ficheiro Stage). O preparador obtém os limites pelo parser do SMD original; o overlay em C# valida o mesmo intervalo com margem de 32 unidades.
