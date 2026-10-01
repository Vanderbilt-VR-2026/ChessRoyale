using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ChessRoyale.Editor
{
    // This tool operates on preview objects and new visual assets only.
    public sealed class WizardChessAssetPreview : EditorWindow
    {
        private const string SourceFolder = "Assets/ChessRoyale/CustomAssets/WizardChess";
        private static readonly string[] Labels = { "Unconfirmed", "Pawn", "Rook", "Knight", "Bishop", "Queen", "King", "Fragment" };
        private readonly List<Candidate> candidates = new List<Candidate>();
        private readonly List<Mesh> temporaryMeshes = new List<Mesh>();
        private PreviewRenderUtility preview;
        private GameObject source;
        private GameObject board;
        private Vector2 scroll;
        private Vector2 orbit = new Vector2(30, -20);
        private int selected;
        private string outputFolder;
        private string status = "Analyze the imported GLB to create isolated previews.";
        private bool HasCompleteSet => candidates.Count(c => c.Side == "White") == 16 && candidates.Count(c => c.Side == "Black") == 16;

        private sealed class Candidate
        {
            public GameObject Visual;
            public string Name;
            public string Side;
            public int Label;
            public int Triangles;
        }

        private sealed class Part
        {
            public MeshFilter Filter;
            public MeshRenderer Renderer;
            public Vector3[] Vertices;
            public int[][] Triangles;
            public int Offset;
        }

        private sealed class Fragment
        {
            public Bounds Bounds;
            public Vector3 BaseSum;
            public int BaseCount;
            public int Triangles;
            public int Slot;
        }

        [MenuItem("Tools/Chess Royale/Wizard Chess Asset Preview")]
        public static void Open() => GetWindow<WizardChessAssetPreview>("Wizard Chess Preview");

        internal static string GenerateGroupedVisuals()
        {
            WizardChessAssetPreview tool = CreateInstance<WizardChessAssetPreview>();
            try
            {
                tool.Analyze();
                if (tool.board == null) throw new InvalidOperationException(tool.status);
                if (!tool.HasCompleteSet) Debug.LogWarning("Wizard Chess draft: " + tool.status + " Proceeding with available groups.");
                tool.SaveVisuals(allowIncomplete: true);
                if (string.IsNullOrEmpty(tool.outputFolder)) throw new InvalidOperationException(tool.status);
                return tool.outputFolder;
            }
            finally { DestroyImmediate(tool); }
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Fragments are grouped around the 16 starting positions on each side of the board. Labels describe spatial locations, not piece types. Inspect individual groups and the reconstructed set before confirming types.", MessageType.Info);
            if (GUILayout.Button("Analyze Imported GLB")) Analyze();
            EditorGUILayout.LabelField(status, EditorStyles.wordWrappedLabel);
            if (candidates.Count == 0) return;

            EditorGUILayout.BeginHorizontal();
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Width(270), GUILayout.Height(280));
            if (GUILayout.Toggle(selected == -1, "Chessboard", "Button")) selected = -1;
            if (GUILayout.Toggle(selected == -2, "Full reconstructed chess set", "Button")) selected = -2;
            for (int i = 0; i < candidates.Count; i++)
                if (GUILayout.Toggle(selected == i, candidates[i].Name, "Button")) selected = i;
            EditorGUILayout.EndScrollView();
            Rect rect = GUILayoutUtility.GetRect(200, 280, GUILayout.ExpandWidth(true));
            DrawPreview(rect, selected < 0 ? board : candidates[selected].Visual);
            EditorGUILayout.EndHorizontal();

            if (selected >= 0)
            {
                Candidate candidate = candidates[selected];
                EditorGUILayout.LabelField($"{candidate.Side}: {candidate.Triangles:N0} triangles");
                candidate.Label = EditorGUILayout.Popup("Confirmed type", candidate.Label, Labels);
            }
            EditorGUILayout.LabelField("Drag the preview to orbit. Candidates retain their original coordinates.");
            if (GUILayout.Button("Preview Full Reconstructed Chess Set")) selected = -2;
            using (new EditorGUI.DisabledScope(!HasCompleteSet))
                if (GUILayout.Button("Save Board and 32 Grouped Pieces as Visual Prefabs")) SaveVisuals();
            if (!string.IsNullOrEmpty(outputFolder)) EditorGUILayout.SelectableLabel(outputFolder, GUILayout.Height(20));
        }

        private void Analyze()
        {
            Clear();
            try
            {
                string path = AssetDatabase.FindAssets("", new[] { SourceFolder })
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .FirstOrDefault(p => p.EndsWith(".glb", StringComparison.OrdinalIgnoreCase));
                GameObject asset = path == null ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null) throw new InvalidOperationException("No instantiable GLB found in " + SourceFolder);
                preview = new PreviewRenderUtility();
                source = Instantiate(asset);
                source.hideFlags = HideFlags.HideAndDontSave;
                preview.AddSingleGO(source);
                preview.camera.fieldOfView = 35;
                Transform boardGroup = FindGroup("chessboard_mesh");
                board = CopyHierarchy(boardGroup, "Wizard Chessboard Visual", null);
                preview.AddSingleGO(board);
                ExtractGroup(FindGroup("blackpieces_mesh"), "Black");
                ExtractGroup(FindGroup("whitepieces_mesh"), "White");
                source.SetActive(false);
                selected = -1;
                status = $"Grouped {candidates.Count(c => c.Side == "White")} white and {candidates.Count(c => c.Side == "Black")} black pieces, plus one board. Inspect the full set and individual groups. Saving creates a new folder each time.";
            }
            catch (Exception exception)
            {
                Clear();
                status = exception.Message;
                Debug.LogException(exception);
            }
        }

        private Transform FindGroup(string name)
        {
            Transform group = source.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => string.Equals(t.name, name, StringComparison.OrdinalIgnoreCase));
            if (group == null) throw new InvalidOperationException("Missing group: " + name);
            return group;
        }

        // Weld only for connectivity analysis. The saved meshes keep their original vertex channels.
        private void ExtractGroup(Transform group, string side)
        {
            var parts = new List<Part>();
            int count = 0;
            foreach (MeshFilter filter in group.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
                if (mesh == null || renderer == null) continue;
                if (!mesh.isReadable) throw new InvalidOperationException("Mesh is not readable: " + mesh.name + ". Enable Read/Write in its importer and retry.");
                var triangles = new int[mesh.subMeshCount][];
                for (int s = 0; s < triangles.Length; s++)
                {
                    if (mesh.GetTopology(s) != MeshTopology.Triangles)
                        throw new InvalidOperationException("Only triangle meshes are supported.");
                    triangles[s] = mesh.GetTriangles(s);
                }
                parts.Add(new Part { Filter = filter, Renderer = renderer, Vertices = mesh.vertices, Triangles = triangles, Offset = count });
                count += mesh.vertexCount;
            }
            int[] parent = Enumerable.Range(0, count).ToArray();
            var positions = new Dictionary<Vector3Int, int>();
            foreach (Part part in parts)
            {
                for (int v = 0; v < part.Vertices.Length; v++)
                {
                    Vector3 p = group.InverseTransformPoint(part.Filter.transform.TransformPoint(part.Vertices[v]));
                    Vector3Int key = new Vector3Int(Mathf.RoundToInt(p.x * 100000), Mathf.RoundToInt(p.y * 100000), Mathf.RoundToInt(p.z * 100000));
                    int id = part.Offset + v;
                    if (positions.TryGetValue(key, out int other)) Union(parent, id, other);
                    else positions.Add(key, id);
                }
                foreach (int[] triangles in part.Triangles)
                    for (int t = 0; t < triangles.Length; t += 3)
                    {
                        Union(parent, part.Offset + triangles[t], part.Offset + triangles[t + 1]);
                        Union(parent, part.Offset + triangles[t], part.Offset + triangles[t + 2]);
                    }
            }
            var components = new Dictionary<int, Fragment>();
            foreach (Part part in parts)
                foreach (int[] triangles in part.Triangles)
                    for (int t = 0; t < triangles.Length; t += 3)
                    {
                        int root = Find(parent, part.Offset + triangles[t]);
                        if (!components.TryGetValue(root, out Fragment fragment))
                        {
                            Vector3 point = group.InverseTransformPoint(part.Filter.transform.TransformPoint(part.Vertices[triangles[t]]));
                            fragment = new Fragment { Bounds = new Bounds(point, Vector3.zero) };
                            components.Add(root, fragment);
                        }
                        fragment.Triangles++;
                        for (int v = 0; v < 3; v++)
                            fragment.Bounds.Encapsulate(group.InverseTransformPoint(part.Filter.transform.TransformPoint(part.Vertices[triangles[t + v]])));
                    }

            // Analyze in the pieces group's coordinates; this handles imported root axis corrections.
            Bounds boardBounds = BoundsInSpace(FindGroup("chessboard_mesh"), group);
            float stepX = boardBounds.size.x / 8f;
            float stepZ = boardBounds.size.z / 8f;
            float baseBand = Mathf.Min(stepX, stepZ) * 0.08f;
            // Use the board surface, rather than the lowest decorative outlier, as the base plane.
            float floor = boardBounds.max.y;
            bool negativeZ = components.Values.Sum(f => f.Bounds.center.z * f.Triangles) < boardBounds.center.z * components.Values.Sum(f => (double)f.Triangles);
            var anchors = new Vector3[16];
            for (int slot = 0; slot < 16; slot++)
            {
                int rank = negativeZ ? slot / 8 : 7 - slot / 8;
                anchors[slot] = new Vector3(boardBounds.min.x + (slot % 8 + 0.5f) * stepX,
                    floor, boardBounds.min.z + (rank + 0.5f) * stepZ);
            }
            // The lowest band of a fragment gives a better footprint than its whole bounds:
            // tall weapons and leaning silhouettes may extend into neighboring squares.
            foreach (Part part in parts)
                for (int v = 0; v < part.Vertices.Length; v++)
                {
                    if (!components.TryGetValue(Find(parent, part.Offset + v), out Fragment fragment)) continue;
                    Vector3 point = group.InverseTransformPoint(part.Filter.transform.TransformPoint(part.Vertices[v]));
                    if (point.y <= fragment.Bounds.min.y + baseBand)
                    {
                        fragment.BaseSum += point;
                        fragment.BaseCount++;
                    }
                }
            var coreBounds = new Bounds[16];
            var hasCore = new bool[16];
            foreach (Fragment fragment in components.Values.Where(f => f.Bounds.min.y <= floor + baseBand))
            {
                Vector3 footprint = fragment.BaseCount > 0 ? fragment.BaseSum / fragment.BaseCount : fragment.Bounds.center;
                fragment.Slot = NearestAnchor(footprint, anchors);
                if (hasCore[fragment.Slot]) coreBounds[fragment.Slot].Encapsulate(fragment.Bounds);
                else { coreBounds[fragment.Slot] = fragment.Bounds; hasCore[fragment.Slot] = true; }
            }
            foreach (Fragment fragment in components.Values.Where(f => f.Bounds.min.y > floor + baseBand))
            {
                // Attach floating decorations to the nearest base-anchored body, using 3D
                // bounds distance and an X/Z anchor tie-breaker. Keep each fragment intact.
                int nearest = NearestAnchor(fragment.Bounds.center, anchors);
                float best = float.PositiveInfinity;
                for (int slot = 0; slot < 16; slot++)
                {
                    if (!hasCore[slot]) continue;
                    Bounds core = coreBounds[slot];
                    Vector3 gap = Vector3.Max(Vector3.zero, Vector3.Max(core.min - fragment.Bounds.max, fragment.Bounds.min - core.max));
                    Vector3 delta = fragment.Bounds.center - anchors[slot];
                    float score = gap.sqrMagnitude + 0.02f * (delta.x * delta.x + delta.z * delta.z);
                    if (score < best) { best = score; nearest = slot; }
                }
                fragment.Slot = nearest;
            }
            int[] triangleTotals = new int[16];
            foreach (Fragment fragment in components.Values) triangleTotals[fragment.Slot] += fragment.Triangles;
            for (int slot = 0; slot < 16; slot++)
            {
                if (triangleTotals[slot] == 0) continue;
                var replacements = new Dictionary<MeshFilter, Mesh>();
                foreach (Part part in parts)
                {
                    var subsets = new int[part.Triangles.Length][];
                    bool used = false;
                    for (int s = 0; s < subsets.Length; s++)
                    {
                        var subset = new List<int>();
                        int[] triangles = part.Triangles[s];
                        for (int t = 0; t < triangles.Length; t += 3)
                            if (components[Find(parent, part.Offset + triangles[t])].Slot == slot)
                            {
                                subset.Add(triangles[t]); subset.Add(triangles[t + 1]); subset.Add(triangles[t + 2]);
                            }
                        subsets[s] = subset.ToArray();
                        used |= subset.Count > 0;
                    }
                    if (!used) continue;
                    Mesh mesh = Instantiate(part.Filter.sharedMesh);
                    temporaryMeshes.Add(mesh);
                    mesh.name = $"{side}_Position_{slot + 1}_{replacements.Count}";
                    for (int s = 0; s < subsets.Length; s++) mesh.SetTriangles(subsets[s], s, false);
                    int[] referenced = subsets.SelectMany(s => s).Distinct().ToArray();
                    Bounds candidateBounds = new Bounds(part.Vertices[referenced[0]], Vector3.zero);
                    foreach (int vertex in referenced) candidateBounds.Encapsulate(part.Vertices[vertex]);
                    mesh.bounds = candidateBounds;
                    replacements.Add(part.Filter, mesh);
                }
                int rank = negativeZ ? slot / 8 + 1 : 8 - slot / 8;
                string name = $"{side} Position {(char)('A' + slot % 8)}{rank}";
                GameObject visual = CopyHierarchy(group, name, replacements);
                candidates.Add(new Candidate { Visual = visual, Name = name, Side = side, Triangles = triangleTotals[slot] });
                preview.AddSingleGO(visual);
            }
            Debug.Log($"Wizard Chess Preview: {side}: grouped {components.Count} fragments into {triangleTotals.Count(t => t > 0)} positions; retained {triangleTotals.Sum():N0} triangles.");
        }

        private static int NearestAnchor(Vector3 point, Vector3[] anchors)
        {
            int nearest = 0;
            float best = float.PositiveInfinity;
            for (int i = 0; i < anchors.Length; i++)
            {
                Vector3 delta = point - anchors[i];
                float distance = delta.x * delta.x + delta.z * delta.z;
                if (distance < best) { best = distance; nearest = i; }
            }
            return nearest;
        }

        private static Bounds BoundsInSpace(Transform visual, Transform space)
        {
            bool initialized = false;
            Bounds bounds = default;
            foreach (MeshFilter filter in visual.GetComponentsInChildren<MeshFilter>(true))
            {
                Bounds local = filter.sharedMesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = local.center + Vector3.Scale(local.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    point = space.InverseTransformPoint(filter.transform.TransformPoint(point));
                    if (!initialized) { bounds = new Bounds(point, Vector3.zero); initialized = true; }
                    else bounds.Encapsulate(point);
                }
            }
            if (!initialized) throw new InvalidOperationException("Board has no mesh bounds.");
            return bounds;
        }

        private static int Find(int[] parent, int value)
        {
            while (parent[value] != value) { parent[value] = parent[parent[value]]; value = parent[value]; }
            return value;
        }
        private static void Union(int[] parent, int a, int b) => parent[Find(parent, a)] = Find(parent, b);

        // Copy the transform path from the imported root so coordinate corrections remain intact.
        private GameObject CopyHierarchy(Transform group, string name, Dictionary<MeshFilter, Mesh> replacements)
        {
            GameObject root = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            var path = new Stack<Transform>();
            for (Transform t = group.parent; t != null; t = t.parent) path.Push(t);
            Transform destination = root.transform;
            foreach (Transform t in path)
            {
                GameObject ancestor = new GameObject(t.name);
                ancestor.transform.SetParent(destination, false);
                CopyTransform(t, ancestor.transform);
                destination = ancestor.transform;
            }
            CopyVisual(group, destination, replacements);
            return root;
        }

        private static void CopyVisual(Transform original, Transform parent, Dictionary<MeshFilter, Mesh> replacements)
        {
            GameObject copy = new GameObject(original.name);
            copy.transform.SetParent(parent, false);
            CopyTransform(original, copy.transform);
            MeshFilter filter = original.GetComponent<MeshFilter>();
            MeshRenderer renderer = original.GetComponent<MeshRenderer>();
            if (filter != null && renderer != null && (replacements == null || replacements.ContainsKey(filter)))
            {
                copy.AddComponent<MeshFilter>().sharedMesh = replacements == null ? filter.sharedMesh : replacements[filter];
                MeshRenderer output = copy.AddComponent<MeshRenderer>();
                output.sharedMaterials = renderer.sharedMaterials;
                output.shadowCastingMode = renderer.shadowCastingMode;
                output.receiveShadows = renderer.receiveShadows;
            }
            foreach (Transform child in original) CopyVisual(child, copy.transform, replacements);
        }

        private static void CopyTransform(Transform from, Transform to)
        {
            to.localPosition = from.localPosition;
            to.localRotation = from.localRotation;
            to.localScale = from.localScale;
        }

        private void DrawPreview(Rect rect, GameObject visual)
        {
            if (preview == null || visual == null) return;
            Event e = Event.current;
            if (e.type == EventType.MouseDrag && rect.Contains(e.mousePosition)) { orbit += e.delta; e.Use(); Repaint(); }
            if (e.type != EventType.Repaint) return;
            bool fullSet = selected == -2;
            board.SetActive(fullSet || visual == board);
            foreach (Candidate c in candidates) c.Visual.SetActive(fullSet || c.Visual == visual);
            Renderer[] renderers = fullSet
                ? board.GetComponentsInChildren<Renderer>().Concat(candidates.SelectMany(c => c.Visual.GetComponentsInChildren<Renderer>())).ToArray()
                : visual.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
            Quaternion rotation = Quaternion.Euler(-orbit.y, orbit.x, 0);
            float distance = Mathf.Max(bounds.extents.magnitude, 0.1f) / Mathf.Sin(preview.camera.fieldOfView * Mathf.Deg2Rad / 2) * 1.25f;
            preview.camera.transform.SetPositionAndRotation(bounds.center - rotation * Vector3.forward * distance, rotation);
            preview.camera.nearClipPlane = Mathf.Max(0.001f, distance / 1000);
            preview.camera.farClipPlane = distance * 10;
            preview.lights[0].intensity = 1.2f;
            preview.lights[0].transform.rotation = Quaternion.Euler(40, 40, 0);
            preview.lights[1].intensity = 0.8f;
            preview.ambientColor = new Color(0.4f, 0.4f, 0.4f);
            preview.BeginPreview(rect, GUIStyle.none);
            preview.Render(true);
            GUI.DrawTexture(rect, preview.EndPreview(), ScaleMode.StretchToFill, false);
        }

        private void SaveVisuals(bool allowIncomplete = false)
        {
            if (!allowIncomplete && !HasCompleteSet) { status = "Export requires 16 occupied positions per side. Inspect grouping before saving."; return; }
            try
            {
                string folderName = "Extracted_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string unique = AssetDatabase.GenerateUniqueAssetPath(SourceFolder + "/" + folderName);
                AssetDatabase.CreateFolder(SourceFolder, System.IO.Path.GetFileName(unique));
                outputFolder = unique;
                foreach (Mesh mesh in temporaryMeshes)
                    if (!AssetDatabase.Contains(mesh))
                        AssetDatabase.CreateAsset(mesh, AssetDatabase.GenerateUniqueAssetPath(unique + "/" + mesh.name + ".asset"));
                SavePrefab(board, unique + "/Wizard_Chessboard_Visual.prefab");
                foreach (Candidate c in candidates)
                    SavePrefab(c.Visual, unique + "/" + c.Name.Replace(' ', '_') + "_" + Labels[c.Label] + ".prefab");
                AssetDatabase.SaveAssets();
                status = $"Saved one board and {candidates.Count} spatially grouped visual prefabs. Confirmed labels appear in filenames.";
                EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(unique));
            }
            catch (Exception exception) { status = exception.Message; Debug.LogException(exception); }
        }

        private static void SavePrefab(GameObject visual, string path)
        {
            GameObject copy = Instantiate(visual);
            try
            {
                foreach (Transform t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.None;
                copy.SetActive(true);
                PrefabUtility.SaveAsPrefabAsset(copy, path);
            }
            finally { DestroyImmediate(copy); }
        }

        private void OnDisable() => Clear();
        private void Clear()
        {
            preview?.Cleanup();
            preview = null;
            source = null;
            board = null;
            candidates.Clear();
            foreach (Mesh mesh in temporaryMeshes)
                if (mesh != null && !AssetDatabase.Contains(mesh)) DestroyImmediate(mesh);
            temporaryMeshes.Clear();
            outputFolder = null;
        }
    }
}
