using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace ChessRoyale.Editor
{
    /// <summary>
    /// Builds a grand tournament-hall backdrop (marble floor, column ring, warm lights) around the chess board.
    /// Everything lives under one root object and is additive: board, pieces, XR rig and teleport areas are not touched.
    /// Quest budget: static + batched geometry, colliders only on the floor, dais and walls (so the XR rig's gravity
    /// has something to stand on), baked lightmap / light probes / reflection probe,
    /// a single realtime (shadowless) spotlight so the moving pieces are lit, no post-processing.
    /// </summary>
    public static class ChessRoyaleGrandHallBuilder
    {
        private const string RootName = "Grand Hall Environment";
        private const string MaterialDir = "Assets/ChessRoyale/Materials/Hall";
        private const string TextureDir = "Assets/ChessRoyale/Textures/Hall";
        private const string LightingSettingsPath = "Assets/ChessRoyale/Scenes/GrandHallLightingSettings.lighting";

        // Layout shared with ChessRoyaleSceneBuilder: board squares cover x -8..8, z -6..10, top surface at y 0.42.
        private static readonly Vector3 HallCenter = new Vector3(0f, 0f, 2f);
        private const float BoardTop = 0.42f;
        private const float BoardHalfSize = 8f;

        private const float HallHalfSize = 24f;
        private const float HallHeight = 14f;
        private const int ColumnCount = 20;
        private const float ColumnRingRadius = 18f;
        private const float ColumnHeight = 12f;

        // URP uses inverse-square falloff, so these are large numbers. Tune after the first bake.
        private const float ChandelierIntensity = 65f;
        private const float BoardSpotIntensity = 140f;
        private static readonly Color WarmLight = new Color(1f, 0.76f, 0.48f);

        // The scene's realtime directional "Key Light" would shine through the ceiling and flatten the hall.
        private const bool DisableLegacyKeyLight = true;

        private static Material floorMarble;
        private static Material columnMarble;
        private static Material darkMarble;
        private static Material gold;
        private static Material wall;
        private static Material glow;

        [MenuItem("Tools/Chess Royale/Build Grand Hall Environment")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("ChessRoyale: leave Play mode before building the Grand Hall.");
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError("ChessRoyale: open the chess scene (ChessRoyaleVR.unity) first.");
                return;
            }

            if (GameObject.Find("CHESS BOARD — 64 Independent Squares") == null)
                Debug.LogWarning("ChessRoyale: board not found in the active scene; the hall is placed using the default board layout.");

            RemoveExisting();
            CreateMaterials();

            GameObject root = new GameObject(RootName);
            Transform shell = NewChild(root.transform, "Shell");
            Transform dais = NewChild(root.transform, "Dais");
            Transform colonnade = NewChild(root.transform, "Colonnade");
            Transform lighting = NewChild(root.transform, "Lighting");

            BuildShell(shell);
            BuildDais(dais);
            BuildColonnade(colonnade);
            BuildLighting(lighting);
            ConfigureSceneLighting();

            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = root;
            Debug.Log("ChessRoyale: Grand Hall built. Save the scene, then run Tools > Chess Royale > Bake Grand Hall Lighting.");
        }

        [MenuItem("Tools/Chess Royale/Remove Grand Hall Environment")]
        public static void Remove()
        {
            if (RemoveExisting())
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        [MenuItem("Tools/Chess Royale/Bake Grand Hall Lighting")]
        public static void Bake()
        {
            if (GameObject.Find(RootName) == null)
            {
                Debug.LogError("ChessRoyale: build the Grand Hall first.");
                return;
            }

            ConfigureSceneLighting();
            if (!EditorSceneManager.SaveOpenScenes())
                return;

            if (!Lightmapping.BakeAsync())
                Debug.LogError("ChessRoyale: could not start the lighting bake.");
        }

        private static bool RemoveExisting()
        {
            GameObject existing = GameObject.Find(RootName);
            if (existing == null)
                return false;

            Object.DestroyImmediate(existing);
            return true;
        }

        // ---------------------------------------------------------------- geometry

        private static void BuildShell(Transform parent)
        {
            float size = HallHalfSize * 2f;
            Vector3 c = HallCenter;

            // 1 m thick so a falling CharacterController can never tunnel through it.
            Solid(Box(parent, "Marble Floor", new Vector3(c.x, -0.5f, c.z), new Vector3(size, 1f, size), floorMarble));

            GameObject ceiling = Box(parent, "Ceiling", new Vector3(c.x, HallHeight + 0.25f, c.z), new Vector3(size + 1f, 0.5f, size + 1f), wall);
            SetLightmapScale(ceiling, 0.25f);

            float mid = HallHeight * 0.5f;
            float offset = HallHalfSize + 0.25f;
            CreateWall(parent, "Wall East", new Vector3(c.x + offset, mid, c.z), new Vector3(0.5f, HallHeight, size + 1f));
            CreateWall(parent, "Wall West", new Vector3(c.x - offset, mid, c.z), new Vector3(0.5f, HallHeight, size + 1f));
            CreateWall(parent, "Wall North", new Vector3(c.x, mid, c.z + offset), new Vector3(size + 1f, HallHeight, 0.5f));
            CreateWall(parent, "Wall South", new Vector3(c.x, mid, c.z - offset), new Vector3(size + 1f, HallHeight, 0.5f));

            // Baseboards and a gold cornice line frame the walls.
            float inner = HallHalfSize - 0.1f;
            float run = size - 0.2f;
            CreateTrim(parent, "East", new Vector3(c.x + inner, 0f, c.z), new Vector3(0.2f, 1f, run), darkMarble, 0.5f);
            CreateTrim(parent, "West", new Vector3(c.x - inner, 0f, c.z), new Vector3(0.2f, 1f, run), darkMarble, 0.5f);
            CreateTrim(parent, "North", new Vector3(c.x, 0f, c.z + inner), new Vector3(run, 1f, 0.2f), darkMarble, 0.5f);
            CreateTrim(parent, "South", new Vector3(c.x, 0f, c.z - inner), new Vector3(run, 1f, 0.2f), darkMarble, 0.5f);
            CreateTrim(parent, "East Cornice", new Vector3(c.x + inner, HallHeight - 0.5f, c.z), new Vector3(0.25f, 0.3f, run), gold, 0.3f);
            CreateTrim(parent, "West Cornice", new Vector3(c.x - inner, HallHeight - 0.5f, c.z), new Vector3(0.25f, 0.3f, run), gold, 0.3f);
            CreateTrim(parent, "North Cornice", new Vector3(c.x, HallHeight - 0.5f, c.z + inner), new Vector3(run, 0.3f, 0.25f), gold, 0.3f);
            CreateTrim(parent, "South Cornice", new Vector3(c.x, HallHeight - 0.5f, c.z - inner), new Vector3(run, 0.3f, 0.25f), gold, 0.3f);
        }

        private static void CreateWall(Transform parent, string name, Vector3 position, Vector3 scale)
        {
            GameObject wallObject = Box(parent, name, position, scale, wall);
            Solid(wallObject);
            SetLightmapScale(wallObject, 0.5f);
        }

        // Trims are placed by floor-level center; height is the scale's Y.
        private static void CreateTrim(Transform parent, string name, Vector3 floorPosition, Vector3 scale, Material material, float lightmapScale)
        {
            GameObject trim = Box(parent, $"Trim {name}", floorPosition + Vector3.up * (scale.y * 0.5f), scale, material);
            SetLightmapScale(trim, lightmapScale);
        }

        private static void BuildDais(Transform parent)
        {
            // Three steps; the top one stays just under the squares (their bottom is at BoardTop).
            float top = BoardTop - 0.005f;
            float side = BoardHalfSize * 2f;
            Vector3 c = HallCenter;
            // Solid all the way down: this is the sealed underside of the board, and it catches the player if they step off it.
            Solid(Box(parent, "Dais Step 1", new Vector3(c.x, 0.07f, c.z), new Vector3(side + 5f, 0.14f, side + 5f), darkMarble));
            Solid(Box(parent, "Dais Step 2", new Vector3(c.x, 0.14f, c.z), new Vector3(side + 3.4f, 0.28f, side + 3.4f), floorMarble));
            Solid(Box(parent, "Dais Step 3", new Vector3(c.x, top * 0.5f, c.z), new Vector3(side + 1.2f, top, side + 1.2f), darkMarble));
        }

        private static void BuildColonnade(Transform parent)
        {
            const float shaftBottom = 0.5f;
            const float shaftTop = ColumnHeight - 0.6f;
            const float architraveY = ColumnHeight + 0.4f;

            Vector3[] positions = new Vector3[ColumnCount];
            for (int i = 0; i < ColumnCount; i++)
            {
                float angle = i * Mathf.PI * 2f / ColumnCount;
                positions[i] = HallCenter + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * ColumnRingRadius;
            }

            for (int i = 0; i < ColumnCount; i++)
            {
                Transform column = NewChild(parent, $"Column {i + 1}");
                Vector3 p = positions[i];

                Static(Box(column, "Plinth", p + Vector3.up * 0.25f, new Vector3(2.2f, 0.5f, 2.2f), darkMarble));
                Static(Cylinder(column, "Shaft", p + Vector3.up * ((shaftBottom + shaftTop) * 0.5f), 1.5f, shaftTop - shaftBottom, columnMarble));
                Static(Cylinder(column, "Lower Ring", p + Vector3.up * (shaftBottom + 0.1f), 1.75f, 0.2f, gold));
                Static(Cylinder(column, "Upper Ring", p + Vector3.up * (shaftTop - 0.1f), 1.75f, 0.2f, gold));
                Static(Box(column, "Capital", p + Vector3.up * (shaftTop + 0.3f), new Vector3(2.2f, 0.6f, 2.2f), columnMarble));
                Static(Box(column, "Abacus", p + Vector3.up * (ColumnHeight + 0.05f), new Vector3(2.5f, 0.1f, 2.5f), gold));
            }

            for (int i = 0; i < ColumnCount; i++)
            {
                Vector3 a = positions[i];
                Vector3 b = positions[(i + 1) % ColumnCount];
                Vector3 span = b - a;
                GameObject beam = Box(parent, $"Architrave {i + 1}", (a + b) * 0.5f + Vector3.up * architraveY, new Vector3(1.1f, 0.8f, span.magnitude), floorMarble);
                beam.transform.rotation = Quaternion.LookRotation(span.normalized, Vector3.up);
                Static(beam);
            }
        }

        // ---------------------------------------------------------------- lighting

        private static void BuildLighting(Transform parent)
        {
            float fixtureY = HallHeight - 1.6f;
            Vector3 c = HallCenter;

            Vector3[] chandeliers =
            {
                new Vector3(c.x, fixtureY, c.z),
                new Vector3(c.x + 13f, fixtureY, c.z),
                new Vector3(c.x - 13f, fixtureY, c.z),
                new Vector3(c.x, fixtureY, c.z + 13f),
                new Vector3(c.x, fixtureY, c.z - 13f),
            };

            for (int i = 0; i < chandeliers.Length; i++)
            {
                Transform chandelier = NewChild(parent, $"Chandelier {i + 1}");
                Vector3 p = chandeliers[i];

                Static(Sphere(chandelier, "Glow", p, 1f, glow));
                Static(Cylinder(chandelier, "Crown", p + Vector3.up * 0.75f, 2.2f, 0.12f, gold));
                float chainLength = HallHeight - (p.y + 0.8f);
                Static(Cylinder(chandelier, "Chain", p + Vector3.up * (0.8f + chainLength * 0.5f), 0.1f, chainLength, gold));

                // Baked: zero runtime cost, shapes the lightmap and the light probes.
                CreateLight(chandelier, "Baked Light", LightType.Point, p + Vector3.down * 0.4f, Quaternion.identity,
                    WarmLight, ChandelierIntensity, 34f, LightmapBakeType.Baked);
            }

            // The one realtime light: a shadowless spotlight over the board so grabbed/moving pieces are lit.
            // Mixed + Indirect Only means the bake still contributes its bounce to the static hall.
            CreateLight(parent, "Board Spotlight", LightType.Spot, new Vector3(c.x, HallHeight - 1f, c.z), Quaternion.Euler(90f, 0f, 0f),
                new Color(1f, 0.84f, 0.62f), BoardSpotIntensity, 26f, LightmapBakeType.Mixed).spotAngle = 85f;

            // Baked probes so moving pieces pick up the warm bounce light of the hall.
            GameObject probes = new GameObject("Piece Light Probes");
            probes.transform.SetParent(parent, false);
            probes.transform.position = c;
            LightProbeGroup group = probes.AddComponent<LightProbeGroup>();
            var positions = new System.Collections.Generic.List<Vector3>();
            foreach (float y in new[] { 1f, 3.5f, 7f })
                for (int x = -2; x <= 2; x++)
                    for (int z = -2; z <= 2; z++)
                        positions.Add(new Vector3(x * 5f, y, z * 5f));
            group.probePositions = positions.ToArray();

            // One baked reflection probe: the metallic pieces and polished floor reflect the hall instead of black.
            GameObject probeObject = new GameObject("Hall Reflection Probe");
            probeObject.transform.SetParent(parent, false);
            probeObject.transform.position = new Vector3(c.x, HallHeight * 0.4f, c.z);
            ReflectionProbe probe = probeObject.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Baked;
            probe.size = new Vector3(HallHalfSize * 2f, HallHeight, HallHalfSize * 2f);
            probe.resolution = 128;
            probe.hdr = false;
            probe.boxProjection = true;
            probe.importance = 1;
            Static(probeObject);

            if (DisableLegacyKeyLight)
            {
                GameObject keyLight = GameObject.Find("Key Light");
                if (keyLight != null && keyLight.TryGetComponent(out Light legacy))
                    legacy.enabled = false;
            }
        }

        private static Light CreateLight(Transform parent, string name, LightType type, Vector3 position, Quaternion rotation,
            Color color, float intensity, float range, LightmapBakeType bakeType)
        {
            GameObject lightObject = new GameObject(name);
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.SetPositionAndRotation(position, rotation);
            Light light = lightObject.AddComponent<Light>();
            light.type = type;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
            light.lightmapBakeType = bakeType;
            return light;
        }

        private static void ConfigureSceneLighting()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.13f, 0.10f, 0.085f);
            RenderSettings.fog = false;

            LightingSettings settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingSettingsPath);
            if (settings == null)
            {
                settings = new LightingSettings { name = "GrandHallLightingSettings" };
                AssetDatabase.CreateAsset(settings, LightingSettingsPath);
            }

            settings.bakedGI = true;
            settings.realtimeGI = false;
            settings.mixedBakeMode = MixedLightingMode.IndirectOnly;
            settings.lightmapper = LightingSettings.Lightmapper.ProgressiveCPU;
            settings.lightmapResolution = 10f;
            settings.lightmapMaxSize = 1024;
            settings.maxBounces = 3;
            settings.ao = true;
            settings.directionalityMode = LightmapsMode.NonDirectional; // one lightmap sample per pixel on Quest
            settings.lightmapCompression = LightmapCompression.NormalQuality;
            EditorUtility.SetDirty(settings);
            Lightmapping.lightingSettings = settings;
        }

        // ---------------------------------------------------------------- materials & textures

        private static void CreateMaterials()
        {
            Directory.CreateDirectory(MaterialDir);
            Directory.CreateDirectory(TextureDir);

            Texture2D cream = GetMarbleTexture("MarbleCream", new Color(0.93f, 0.89f, 0.82f), new Color(0.86f, 0.80f, 0.71f), new Color(0.45f, 0.38f, 0.30f), 11, true);
            Texture2D shaft = GetMarbleTexture("MarbleColumn", new Color(0.95f, 0.92f, 0.86f), new Color(0.88f, 0.83f, 0.75f), new Color(0.50f, 0.43f, 0.35f), 29, false);
            Texture2D dark = GetMarbleTexture("MarbleDark", new Color(0.07f, 0.07f, 0.08f), new Color(0.12f, 0.11f, 0.12f), new Color(0.62f, 0.56f, 0.42f), 53, true);
            AssetDatabase.Refresh();

            floorMarble = CreateLit("HallFloorMarble", Color.white, cream, new Vector2(HallHalfSize * 2f / 8f, HallHalfSize * 2f / 8f), 0f, 0.7f);
            columnMarble = CreateLit("HallColumnMarble", Color.white, shaft, new Vector2(2f, 3f), 0f, 0.55f);
            darkMarble = CreateLit("HallDarkMarble", Color.white, dark, new Vector2(3f, 3f), 0f, 0.75f);
            gold = CreateLit("HallGold", new Color(0.83f, 0.63f, 0.25f), null, Vector2.one, 1f, 0.7f);
            wall = CreateLit("HallWall", new Color(0.24f, 0.10f, 0.08f), null, Vector2.one, 0f, 0.2f);
            glow = CreateLit("HallGlow", new Color(1f, 0.85f, 0.6f), null, Vector2.one, 0f, 0.3f);
            glow.EnableKeyword("_EMISSION");
            glow.SetColor("_EmissionColor", new Color(1f, 0.72f, 0.4f) * 2.5f);
            glow.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            EditorUtility.SetDirty(glow);
            AssetDatabase.SaveAssets();
        }

        private static Material CreateLit(string name, Color color, Texture2D texture, Vector2 tiling, float metallic, float smoothness)
        {
            string path = $"{MaterialDir}/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_BaseColor", color);
            material.SetTexture("_BaseMap", texture);
            material.SetTextureScale("_BaseMap", tiling);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(material);
            return material;
        }

        // Procedural, seamlessly tiling marble so the project needs no external art.
        private static Texture2D GetMarbleTexture(string name, Color baseA, Color baseB, Color vein, int seed, bool slabs)
        {
            string path = $"{TextureDir}/{name}.png";
            if (!File.Exists(path))
            {
                const int size = 1024;
                Texture2D texture = new Texture2D(size, size, TextureFormat.RGB24, false);
                Color[] pixels = new Color[size * size];
                float offset = seed * 13.7f;
                const float twoPi = Mathf.PI * 2f;

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float u = x / (float)size;
                        float v = y / (float)size;
                        float warp = Tileable(u, v, 2, offset, offset + 5f) * 0.65f + Tileable(u, v, 4, offset + 9f, offset) * 0.35f;
                        float broad = Tileable(u, v, 3, offset + 21f, offset + 3f);
                        float main = Mathf.Pow(1f - Mathf.Abs(Mathf.Sin((u * 2f + v + warp * 1.6f) * twoPi)), 11f);
                        float fine = Mathf.Pow(1f - Mathf.Abs(Mathf.Sin((u * -3f + v * 2f + warp * 2.2f) * twoPi)), 22f) * 0.55f;
                        Color color = Color.Lerp(Color.Lerp(baseA, baseB, broad), vein, Mathf.Clamp01(main + fine) * 0.8f);

                        if (slabs)
                        {
                            int half = size / 2;
                            int lx = x % half;
                            int ly = y % half;
                            int edge = Mathf.Min(Mathf.Min(lx, half - 1 - lx), Mathf.Min(ly, half - 1 - ly));
                            int slab = (x / half) + (y / half) * 2;
                            color *= 0.97f + 0.03f * ((slab * 37 + seed) % 5) / 4f;
                            if (edge < 2)
                                color *= 0.35f;
                        }

                        color.a = 1f;
                        pixels[y * size + x] = color;
                    }
                }

                texture.SetPixels(pixels);
                File.WriteAllBytes(path, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(path);

                TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.mipmapEnabled = true;
                importer.anisoLevel = 4;
                importer.textureCompression = TextureImporterCompression.Compressed;
                TextureImporterPlatformSettings quest = importer.GetPlatformTextureSettings("Android");
                quest.overridden = true;
                quest.maxTextureSize = 1024;
                quest.format = TextureImporterFormat.ASTC_6x6;
                importer.SetPlatformTextureSettings(quest);
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // Perlin noise blended across the borders so the result tiles without seams (u, v in 0..1).
        private static float Tileable(float u, float v, int frequency, float ox, float oy)
        {
            float Sample(float x, float y) => Mathf.PerlinNoise(x * frequency + ox, y * frequency + oy);
            return Sample(u, v) * (1f - u) * (1f - v)
                 + Sample(u - 1f, v) * u * (1f - v)
                 + Sample(u, v - 1f) * (1f - u) * v
                 + Sample(u - 1f, v - 1f) * u * v;
        }

        // ---------------------------------------------------------------- helpers

        private static Transform NewChild(Transform parent, string name)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        private static GameObject Box(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
            => Primitive(PrimitiveType.Cube, parent, name, position, scale, material);

        private static GameObject Sphere(Transform parent, string name, Vector3 position, float diameter, Material material)
            => Primitive(PrimitiveType.Sphere, parent, name, position, Vector3.one * diameter, material);

        // Unity's cylinder is 2 units tall and 1 unit wide, so Y scale is half the height.
        private static GameObject Cylinder(Transform parent, string name, Vector3 position, float diameter, float height, Material material)
            => Primitive(PrimitiveType.Cylinder, parent, name, position, new Vector3(diameter, height * 0.5f, diameter), material);

        private static GameObject Primitive(PrimitiveType type, Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;

            // Scenery has no collider by default; Solid() adds one only where the player needs to stand or be stopped.
            Object.DestroyImmediate(go.GetComponent<Collider>());
            return Static(go);
        }

        private static void Solid(GameObject go) => go.AddComponent<BoxCollider>();

        private static GameObject Static(GameObject go)
        {
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic);
            return go;
        }

        private static void SetLightmapScale(GameObject go, float scale)
        {
            SerializedObject serialized = new SerializedObject(go.GetComponent<Renderer>());
            serialized.FindProperty("m_ScaleInLightmap").floatValue = scale;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
