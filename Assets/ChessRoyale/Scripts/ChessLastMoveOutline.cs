using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ChessRoyale
{
    // Separate visual objects keep outline meshes out of piece bounds and capture/promotion models.
    public sealed class ChessLastMoveOutline : MonoBehaviour
    {
        private static Material material;
        private GameObject visuals;
        private bool recentMove;
        private bool targeted;
        private MaterialPropertyBlock properties;
        private static readonly int OutlineColor = Shader.PropertyToID("_OutlineColor");

        public void SetRecentMove(bool value)
        {
            recentMove = value;
            enabled = recentMove || targeted;
        }

        public void SetTargeted(bool value)
        {
            targeted = value;
            enabled = recentMove || targeted;
        }
        private readonly List<MeshRenderer> sources = new List<MeshRenderer>();
        private readonly List<MeshRenderer> outlines = new List<MeshRenderer>();

        private void Start()
        {
            Shader shader = Resources.Load<Shader>("ChessLastMoveOutline");
            if (shader == null)
                return;
            if (material == null)
                material = new Material(shader) { name = "Last Move Red Outline" };
            visuals = new GameObject("Last Move Outline Visuals");
            foreach (MeshFilter source in GetComponentsInChildren<MeshFilter>(true))
            {
                MeshRenderer renderer = source.GetComponent<MeshRenderer>();
                if (renderer == null || source.sharedMesh == null)
                    continue;
                GameObject shell = new GameObject("Red Outline");
                shell.transform.SetParent(visuals.transform, false);
                shell.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;
                MeshRenderer outline = shell.AddComponent<MeshRenderer>();
                outline.sharedMaterials = CreateMaterials(source.sharedMesh.subMeshCount);
                outline.shadowCastingMode = ShadowCastingMode.Off;
                outline.receiveShadows = false;
                outline.lightProbeUsage = LightProbeUsage.Off;
                outline.reflectionProbeUsage = ReflectionProbeUsage.Off;
                outline.enabled = false;
                sources.Add(renderer);
                outlines.Add(outline);
            }
        }

        private static Material[] CreateMaterials(int count)
        {
            Material[] materials = new Material[count];
            for (int i = 0; i < count; i++)
                materials[i] = material;
            return materials;
        }

        private void LateUpdate()
        {
            if (properties == null)
                properties = new MaterialPropertyBlock();
            properties.SetColor(OutlineColor, targeted ? new Color(1f, 0.78f, 0.05f, 1f) : new Color(1f, 0.08f, 0.06f, 1f));
            for (int i = 0; i < sources.Count; i++)
            {
                MeshRenderer source = sources[i];
                MeshRenderer outline = outlines[i];
                outline.enabled = source != null && source.enabled && source.gameObject.activeInHierarchy;
                if (!outline.enabled)
                    continue;
                outline.SetPropertyBlock(properties);
                outline.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                outline.transform.localScale = source.transform.lossyScale;
            }
        }

        private void OnDisable()
        {
            foreach (MeshRenderer outline in outlines)
                if (outline != null)
                    outline.enabled = false;
        }

        private void OnDestroy()
        {
            if (visuals != null)
                Destroy(visuals);
        }
    }
}
