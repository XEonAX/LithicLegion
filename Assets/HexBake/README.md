# HexBake — the baked hexasphere cell data

**The hexasphere is only a data source.** Its per-cell layout is baked into a linear cubemap,
and the shield shader samples that cubemap on a *plain Unity sphere* with
`normalize(objectSpacePosition)`. Nothing samples the hexasphere at runtime, and
`HexSphere.fbx` must not end up in a gameplay scene.

Why a cubemap works: both the hexasphere and the sphere the shader runs on are centred on
their own origins, so **a direction from the centre identifies the same spot on both**.

Made with Blender **5.2.2 LTS** and Unity **6000.6.3f1** / URP **17.6.0**.

---

## Contents

| file | what it is |
|---|---|
| `HexSphere.fbx` | bake-source mesh: the poked hexasphere with the `HexData` per-corner attribute and a `HexUV` fallback. Referenced by no scene. |
| `HexDataBake.shader` | `Hidden/HexDataBake` — bake-only unlit shader that writes the mesh's `COLOR` straight out. |
| `HexData_Cube.asset` | the baked cubemap: 1024/face, RGBA32, **linear (sRGB off)**, Bilinear, Clamp, 11 mip levels. ~67 MB. |
| `Editor/HexDataCubeBaker.cs` | reproducible baker + verifier (menu items below). |

Menu items:

* **Tools ▸ Shield ▸ Bake HexData Cubemap** — re-renders and writes the cubemap **in place**.
* **Tools ▸ Shield ▸ Verify HexData Cubemap (re-bake and compare)** — re-bakes to a temporary
  cubemap and diffs it against the asset texel by texel.

---

## The baked data (`HexData`)

A **`FLOAT_COLOR` attribute on the `CORNER` domain** — deliberately the float layer, not the
8-bit byte colour layer, which is quantised and may be sRGB-encoded:

| channel | meaning |
|---|---|
| **R** | `1` at the cell's centre vertex, `0` at every rim vertex. Interpolation therefore gives a radial gradient per cell whose iso-lines run parallel to the cell edges, for any polygon. |
| **G** | one random value per original cell (fixed seed `20261014`), identical on all corners of that cell. |
| **B** | `1` if the original face was **not** a hexagon — i.e. flags the 12 pentagons — else `0`. |
| **A** | `1` |

Plus a second UV map **`HexUV`** = `(R, G)` per corner, as a fallback if vertex colours don't
survive some toolchain.

> **UV naming:** Unity's second UV set is `mesh.uv2`, which Shader Graph exposes as the
> **UV1** channel. So "the UV1 fallback" means `HexUV` here.

### Why poke?

Each original n-gon is split into a triangle fan around a new centre vertex, which gives every
corner an unambiguous "distance from this cell's centre". `R` is then a per-corner constant
(`1` only on the centre vertex), and linear interpolation across the fan reproduces the
gradient exactly.

---

## Blender side — how `HexSphere.fbx` was made

Source: `../Blenders/Hexasphere.blend`, object **`Hexasphere.001`** — a Goldberg polyhedron:
**320 verts, 480 edges, 162 faces = 12 pentagons + 150 hexagons, zero triangles**, closed
manifold, all 162 faces flat-shaded.

Steps, as run:

1. **Duplicate** it to `HexSphere_Baked` (on a copy of the mesh datablock) and apply modifiers
   and transforms — both no-ops here (no modifiers, identity transform).
2. **Record every face's side count before modifying anything.**
3. **`bmesh.ops.poke(bm, faces=list(bm.faces), offset=0.0)`** → 482 verts, 960 triangles.
   12 cells get 5-triangle fans, 150 get 6.
4. Create the colour attribute:
   ```python
   attr = me.color_attributes.new(name="HexData", type='FLOAT_COLOR', domain='CORNER')
   me.color_attributes.active_color_index = me.color_attributes.find("HexData")
   me.color_attributes.render_color_index = me.color_attributes.find("HexData")
   ```
5. **Map each fan's new centre vertex back to its original cell.** Every poked triangle has
   exactly one vertex that was not in the original mesh, so group triangles by that vertex and
   match the *union* of the group's original vertices against the original faces' vertex sets.
   (Keying off a single triangle's two rim vertices is wrong — that was an actual bug on the
   first attempt.)
6. Write per corner: `R` = 1 on the centre vertex else 0; `G` = the cell's
   `random.Random(20261014).random()`; `B` = 0 for a 6-sided face else 1; `A` = 1.
7. Add `HexUV` (`me.uv_layers.new(name="HexUV")`) with `uv = (R, G)` per corner.
8. **Verify before exporting:** `R ∈ {0,1}`, 162 distinct `G`, `sum(B) == 12`,
   `HexUV` equals the colour, all faces still flat, mesh still closed.
9. **Export:**
   ```python
   bpy.ops.export_scene.fbx(
       filepath="Assets/HexBake/HexSphere.fbx",
       use_selection=True, object_types={'MESH'},
       use_mesh_modifiers=True, use_triangles=False,
       apply_scale_options='FBX_SCALE_UNITS', apply_unit_scale=True, global_scale=1.0,
       axis_forward='-Z', axis_up='Y',
       bake_anim=False, add_leaf_bones=False,
       mesh_smooth_type='OFF',
       colors_type='LINEAR', prioritize_active_color=True)
   ```

Blender gotchas worth keeping:

* **`colors_type='LINEAR'`** stops the exporter converting the values. With the default
  `'SRGB'` the data would be gamma-encoded sitting in the file.
* **`prioritize_active_color=True`** keeps `HexData` as the first colour layer, because some
  importers discard everything except the first.
* The mesh was flat-shaded and stays flat; `mesh_smooth_type='OFF'` keeps per-face normals.
* The user's `.blend` was never saved over — this all ran on an in-memory duplicate.

> ⚠️ **This Blender half is currently only *described* here.** It was executed as live Blender
> MCP calls and never saved as a script, so the FBX cannot yet be regenerated from the project
> by a command. See *Reproducibility*.

---

## Unity side — how `HexData_Cube.asset` was made

`Editor/HexDataCubeBaker.cs` → **Tools ▸ Shield ▸ Bake HexData Cubemap**:

1. A **temporary additive scene** — the open scene is never switched away from, closed or saved.
2. One instance of the FBX at identity, isolated on unnamed **layer 31**.
3. A perspective camera at the mesh's **world bounds centre** (`Renderer.bounds.center` — the
   mesh origin need not be the geometric centre), FOV 90, black clear, culling mask = layer 31
   only, **HDR off, MSAA off, post-processing off**.
4. A cube `RenderTexture`: 1024/face, `ARGB32`, **`RenderTextureReadWrite.Linear`**, no MSAA.
5. **`Camera.RenderToCubemap`** — Unity owns the six face orientations, so no flip guessing.
6. Read back per face into a linear RGBA32 `Cubemap` (Bilinear, Clamp), then `Apply(true)` to
   build the mip chain.
7. Write the asset **in place** so its **GUID survives** — recreating it would mint a new GUID
   and null every material's `_HexCube` reference.

Why `Hidden/HexDataBake` looks the way it does:

* **`Cull Off` + `ZTest Always` are required**: the camera sits *inside* the mesh, so it only
  ever sees back faces.
* Its pass is **deliberately untagged**, so URP draws it through its built-in
  `SRPDefaultUnlit` shader-tag entry and no URP includes are needed.
* No lighting, fog or tonemapping — it is a data dump, not a render.

FBX importer settings: **Mesh Compression Off, Weld Vertices Off, Read/Write On, Normals
Import**. Imported result: **1239 verts / 960 tris** — Unity splits vertices because it stores
vertex colour as `Color32`.

`_HexCube` is bound to this cubemap **as the shader property's default** (the graph stores the
asset GUID), so a freshly created material picks it up automatically.

---

## Verification results

**FBX, file level** (parsed the binary directly):

* colour layer `ByPolygonVertex` / `IndexToDirect`; `R ∈ {0,1}`, α = 1
* **162 distinct `G` at full float precision** (max error vs the bake `2.9e-08`)
* `sum(B) == 12`; `HexUV` equals the colour exactly (0 / 2880 mismatches)
* all 2880 corner normals equal their geometric face normal → **flat shading preserved**

**Unity import:**

* `R` exactly 0 / 1 and `B` exactly 0 / 1 → **no gamma shift**
* 162 cells, **0** cells with varying `G` or `B`, `sum(B) == 12`
* vertex colour yields 116 distinct `G` (Unity stores `Color32`), while **`UV1`/`uv2` carries
  all 162** — use the UV1 fallback if `G` ever needs full precision. `G` is currently unused by
  the mask, so this doesn't affect the look.

**End-to-end orientation** (one-off, using two debug shaders since removed): rendered the
hexasphere with `Hidden/HexDataBake` and a primitive sphere sampling the cubemap by normalized
object-space position, same orthographic camera, comparing the central 60 % of the silhouette:

* **mean |ΔR| = 2.44 / 255 (0.96 %)**
* orientation unambiguous — as-is **1.17** blurred error vs **flip-X 49.05**, **rot-180 49.05**,
  best shift **(0, 0)**

**Determinism** (`Tools ▸ Shield ▸ Verify …`): **0 differing texels out of 6,291,456**,
`max |Δ| = 0`. A re-bake leaves both the GUID *and* the file's md5 unchanged.

**Pentagon placement:** the 12 pentagons sit at **±Z** in Unity space (the Blender icosphere's
poles), not ±Y. The ±Z cubemap face centres sample `B = 1.000`.

---

## Reproducibility status

| stage | status |
|---|---|
| Cubemap | **Fully reproducible and self-verifying** via the two menu items. |
| FBX | **Not scripted** — the Blender section above is the record. |
| One-off evidence | The bake scripts and verification images from the original session are inside `.shieldwork/cleanup_backup_20261014.tar.gz` at the project root. |

* Changing `FaceSize` in `HexDataCubeBaker.cs` forces a delete/recreate (a Cubemap's size
  cannot change), which **mints a new GUID** — the script warns about this, and `_HexCube` must
  be reassigned on any material.
* The cubemap is ~67 MB because it is uncompressed RGBA32 plus mips. Rebake as `RGBAHalf`, or
  smaller, if size matters. Only reach for that if you actually see staircase banding on the
  lines — at 1024 the shipped bake shows none.
* `Hidden/HexDataShow` and `Hidden/HexCubeDebug` were rebuild-only helpers for the end-to-end
  orientation check and were deleted in a cleanup; the determinism check does not need them.
