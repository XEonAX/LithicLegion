using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace LithicLegion.HexBake
{
    /// <summary>
    /// Reproducible bake of the hexasphere's per-corner cell data into a linear cubemap.
    ///
    /// The hexasphere (Assets/HexBake/HexSphere.fbx) is only a DATA SOURCE: its per-corner
    /// HexData attribute carries, per cell, R = 1 at the cell centre falling to 0 at the rim,
    /// G = a per-cell random value and B = 1 for the 12 pentagons. Because the hexasphere and
    /// the smooth sphere the shader runs on are both centred on their own origins, a direction
    /// from the centre identifies the same spot on both -- so this bakes "what the data is in
    /// direction d" into a cubemap, and the shader samples it with normalize(objectSpacePosition).
    ///
    /// Pipeline:
    ///   1. a temporary ADDITIVE scene (the user's scene is never opened, closed or saved)
    ///   2. one instance of the FBX at identity, isolated on layer 31
    ///   3. a perspective camera at the mesh's world bounds centre (the mesh origin need not be
    ///      the geometric centre), FOV 90, black clear, culling mask = that layer only,
    ///      HDR off, MSAA off, post-processing off
    ///   4. a 1024/face ARGB32 cube RenderTexture with sRGB OFF and no MSAA
    ///   5. Camera.RenderToCubemap, so Unity owns the six face orientations
    ///   6. read back per face into a linear RGBA32 Cubemap (bilinear, clamp) and generate mips
    ///
    /// Hidden/HexDataBake is unlit, Cull Off and ZTest Always: the camera sits INSIDE the mesh,
    /// so it only ever sees back faces. Its pass is deliberately untagged so URP draws it via
    /// its built-in "SRPDefaultUnlit" shader-tag entry. It writes COLOR straight out with no
    /// lighting, fog or tonemapping.
    ///
    /// The asset is updated IN PLACE so its GUID -- and therefore every material's _HexCube
    /// reference to it -- survives a re-bake. Deleting and recreating it would break them.
    /// </summary>
    public static class HexDataCubeBaker
    {
        const string MeshPath   = "Assets/HexBake/HexSphere.fbx";
        const string ShaderName = "Hidden/HexDataBake";
        const string OutputPath = "Assets/HexBake/HexData_Cube.asset";

        /// <summary>Unnamed layer used only to isolate the bake camera from everything else.</summary>
        const int BakeLayer = 31;

        /// <summary>
        /// Keep at 1024 to update the existing asset in place. Changing this makes the current
        /// asset unusable (a Cubemap's size cannot change), so it will be deleted and recreated,
        /// which mints a NEW GUID and breaks existing _HexCube references.
        /// </summary>
        const int FaceSize = 1024;

        [MenuItem("Tools/Shield/Bake HexData Cubemap", priority = 200)]
        static void BakeMenu() => Run(verifyOnly: false);

        [MenuItem("Tools/Shield/Verify HexData Cubemap (re-bake and compare)", priority = 201)]
        static void VerifyMenu() => Run(verifyOnly: true);

        static void Run(bool verifyOnly)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(MeshPath);
            if (model == null)
            {
                Debug.LogError($"[HexDataCubeBaker] model not found: {MeshPath}");
                return;
            }

            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"[HexDataCubeBaker] shader not found: {ShaderName} " +
                               "(is Assets/HexBake/HexDataBake.shader present and compiled?)");
                return;
            }

            Scene userScene = SceneManager.GetActiveScene();
            bool userSceneWasDirty = userScene.isDirty;

            Scene temp = default;
            bool tempCreated = false;
            Material bakeMat = null;
            RenderTexture rt = null;
            Texture2D readback = null;
            Cubemap fresh = null;
            bool freshBecameAsset = false;

            try
            {
                temp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                tempCreated = true;
                SceneManager.SetActiveScene(temp);

                // ---- one instance of the source mesh, alone on its own layer
                var go = (GameObject)PrefabUtility.InstantiatePrefab(model, temp);
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                go.transform.localScale = Vector3.one;
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                    t.gameObject.layer = BakeLayer;

                var rend = go.GetComponentInChildren<MeshRenderer>();
                if (rend == null)
                {
                    Debug.LogError("[HexDataCubeBaker] the model has no MeshRenderer");
                    return;
                }

                bakeMat = new Material(shader);
                rend.sharedMaterial = bakeMat;

                // ---- camera at the mesh's world bounds centre
                Vector3 centre = rend.bounds.center;

                var camGo = new GameObject("HexDataBakeCamera");
                SceneManager.MoveGameObjectToScene(camGo, temp);
                camGo.transform.SetPositionAndRotation(centre, Quaternion.identity);

                var cam = camGo.AddComponent<Camera>();
                cam.orthographic = false;
                cam.fieldOfView = 90f;              // one 90 degree face per cubemap face
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 100f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                cam.cullingMask = 1 << BakeLayer;   // the hexasphere and nothing else
                cam.allowHDR = false;
                cam.allowMSAA = false;
                cam.useOcclusionCulling = false;

                var camData = camGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                camData.renderPostProcessing = false;
                camData.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.None;

                // ---- linear, unmultisampled cube target
                rt = new RenderTexture(FaceSize, FaceSize, 24, RenderTextureFormat.ARGB32,
                                       RenderTextureReadWrite.Linear);
                rt.dimension = TextureDimension.Cube;
                rt.antiAliasing = 1;
                rt.useMipMap = false;
                rt.Create();

                if (!cam.RenderToCubemap(rt))
                {
                    Debug.LogError("[HexDataCubeBaker] Camera.RenderToCubemap failed");
                    return;
                }

                var faces = new[]
                {
                    CubemapFace.PositiveX, CubemapFace.NegativeX,
                    CubemapFace.PositiveY, CubemapFace.NegativeY,
                    CubemapFace.PositiveZ, CubemapFace.NegativeZ
                };

                fresh = new Cubemap(FaceSize, TextureFormat.RGBA32, true, true); // mipChain, linear
                fresh.filterMode = FilterMode.Bilinear;
                fresh.wrapMode = TextureWrapMode.Clamp;
                fresh.anisoLevel = 1;

                readback = new Texture2D(FaceSize, FaceSize, TextureFormat.RGBA32, false, true);
                foreach (var f in faces)
                {
                    Graphics.SetRenderTarget(rt, 0, f);
                    readback.ReadPixels(new Rect(0, 0, FaceSize, FaceSize), 0, 0, false);
                    fresh.SetPixels(readback.GetPixels(), f);
                }
                Graphics.SetRenderTarget(null);
                fresh.Apply(true);                  // generate the mip chain

                var existing = AssetDatabase.LoadAssetAtPath<Cubemap>(OutputPath);
                long pixels = 6L * FaceSize * FaceSize;

                if (verifyOnly)
                {
                    if (existing == null || !existing.isReadable)
                    {
                        Debug.LogError($"[HexDataCubeBaker] cannot verify: {OutputPath} is missing or not readable");
                        return;
                    }

                    double sum = 0.0;
                    float max = 0f;
                    long differing = 0;
                    foreach (var f in faces)
                    {
                        var a = existing.GetPixels(f);
                        var b = fresh.GetPixels(f);
                        for (int i = 0; i < a.Length; i++)
                        {
                            float d = Mathf.Max(Mathf.Max(Mathf.Abs(a[i].r - b[i].r), Mathf.Abs(a[i].g - b[i].g)),
                                                Mathf.Max(Mathf.Abs(a[i].b - b[i].b), Mathf.Abs(a[i].a - b[i].a)));
                            if (d > 0f) differing++;
                            sum += d;
                            if (d > max) max = d;
                        }
                    }

                    Debug.Log($"[HexDataCubeBaker] VERIFY {OutputPath}\n" +
                              $"  size            {FaceSize}x{FaceSize} x6 = {pixels} texels\n" +
                              $"  differing texels {differing}\n" +
                              $"  max |delta|      {max:F6}\n" +
                              $"  mean |delta|     {sum / pixels:F8}\n" +
                              (differing == 0
                                  ? "  RESULT: identical - the bake reproduces the asset exactly"
                                  : "  RESULT: DIFFERS - the asset is not what this baker produces"));
                    return;
                }

                if (existing != null && existing.width == FaceSize && existing.isReadable)
                {
                    // in place: the GUID, and every material's reference to it, survives
                    foreach (var f in faces)
                        existing.SetPixels(fresh.GetPixels(f), f);
                    existing.filterMode = FilterMode.Bilinear;
                    existing.wrapMode = TextureWrapMode.Clamp;
                    existing.anisoLevel = 1;
                    existing.Apply(true);
                    EditorUtility.SetDirty(existing);
                    AssetDatabase.SaveAssets();

                    Debug.Log($"[HexDataCubeBaker] re-baked {OutputPath} IN PLACE " +
                              $"({FaceSize}x{FaceSize} x6, {pixels} texels). GUID unchanged: " +
                              AssetDatabase.AssetPathToGUID(OutputPath));
                }
                else
                {
                    if (existing != null)
                    {
                        Debug.LogWarning($"[HexDataCubeBaker] existing cubemap is {existing.width}x{existing.width}" +
                                         $" (readable={existing.isReadable}) but this bake is {FaceSize}x{FaceSize}: " +
                                         "deleting and recreating, which mints a NEW GUID. Reassign _HexCube " +
                                         "on any material that referenced it.");
                        AssetDatabase.DeleteAsset(OutputPath);
                    }

                    var asset = fresh;
                    freshBecameAsset = true;
                    AssetDatabase.CreateAsset(asset, OutputPath);
                    AssetDatabase.SaveAssets();

                    Debug.Log($"[HexDataCubeBaker] created {OutputPath} ({FaceSize}x{FaceSize} x6, " +
                              $"{pixels} texels). GUID: {AssetDatabase.AssetPathToGUID(OutputPath)}");
                }
            }
            finally
            {
                if (tempCreated)
                {
                    SceneManager.SetActiveScene(userScene);
                    EditorSceneManager.CloseScene(temp, true);
                }

                if (bakeMat != null) UnityEngine.Object.DestroyImmediate(bakeMat);
                if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
                if (fresh != null && !freshBecameAsset) UnityEngine.Object.DestroyImmediate(fresh);
                if (rt != null)
                {
                    rt.Release();
                    UnityEngine.Object.DestroyImmediate(rt);
                }

                if (userSceneWasDirty != userScene.isDirty)
                    Debug.LogWarning("[HexDataCubeBaker] the user scene's dirty flag changed - please check it");
            }
        }
    }
}
