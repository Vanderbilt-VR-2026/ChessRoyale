using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ChessRoyale
{
    // A visual-only copy lets the actual piece leave play immediately.
    public sealed class ChessCaptureEffect : MonoBehaviour
    {
        private const float DissolveDuration = 0.2f;
        private const float Duration = 0.5f;
        private static float lastSmokeHue;
        private readonly List<Renderer> ghosts = new List<Renderer>();
        private readonly List<Color> colors = new List<Color>();
        private readonly List<Mesh> bakedMeshes = new List<Mesh>();
        private Material material;
        private Material particleMaterial;
        private Material smokeMaterial;
        private Transform ghostRoot;
        private MaterialPropertyBlock properties;
        private float elapsed;

        public static ChessCaptureEffect Play(Transform piece)
        {
            Shader shader = Resources.Load<Shader>("ChessCaptureGlow");
            if (shader == null)
                return null;

            var root = new GameObject("Chess Capture Effect");
            root.transform.SetParent(piece.parent, false);
            root.transform.position = piece.position;
            var effect = root.AddComponent<ChessCaptureEffect>();
            effect.material = new Material(shader);
            effect.particleMaterial = new Material(shader);
            effect.particleMaterial.SetFloat("_Particle", 1f);
            effect.properties = new MaterialPropertyBlock();
            effect.ghostRoot = new GameObject("Dissolving Piece").transform;
            effect.ghostRoot.SetParent(root.transform, false);
            Bounds bounds = new Bounds(piece.position, Vector3.zero);
            bool hasBounds = false;

            foreach (Renderer source in piece.GetComponentsInChildren<Renderer>())
            {
                if (!source.enabled || !source.gameObject.activeInHierarchy)
                    continue;
                Mesh mesh = null;
                if (source is SkinnedMeshRenderer skinned)
                {
                    mesh = new Mesh();
                    skinned.BakeMesh(mesh);
                    effect.bakedMeshes.Add(mesh);
                }
                else if (source is MeshRenderer && source.TryGetComponent(out MeshFilter filter))
                    mesh = filter.sharedMesh;
                if (mesh == null)
                    continue;

                var visual = new GameObject("Capture Ghost");
                visual.transform.SetParent(source.transform, false);
                visual.transform.SetParent(effect.ghostRoot, true);
                visual.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = visual.AddComponent<MeshRenderer>();
                var materials = new Material[mesh.subMeshCount];
                for (int i = 0; i < materials.Length; i++)
                    materials[i] = effect.material;
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                Color color = source.sharedMaterial != null && source.sharedMaterial.HasProperty("_BaseColor")
                    ? source.sharedMaterial.GetColor("_BaseColor") : Color.white;
                effect.ghosts.Add(renderer);
                effect.colors.Add(Color.Lerp(color, new Color(0.35f, 0.8f, 1f), 0.4f));
                if (!hasBounds) { bounds = source.bounds; hasBounds = true; }
                else bounds.Encapsulate(source.bounds);
            }

            effect.SetOpacity(1f);
            effect.CreateParticles(bounds);
            effect.CreateSmoke(bounds, shader);
            return effect;
        }

        private void CreateParticles(Bounds bounds)
        {
            var host = new GameObject("Magic Sparks");
            host.transform.SetParent(transform, false);
            host.transform.position = bounds.center;
            var particles = host.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            float size = Mathf.Max(bounds.size.magnitude, 0.05f);
            var main = particles.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = Duration;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(size * 0.15f, size * 0.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.015f, size * 0.04f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.2f, 1f, 1f), new Color(0.8f, 0.35f, 1f));
            main.maxParticles = 24;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = particles.emission;
            emission.rateOverTime = 0f;
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = size * 0.15f;
            var color = particles.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = particleMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            particles.Play();
            particles.Emit(12);
        }

        private void CreateSmoke(Bounds bounds, Shader shader)
        {
            Texture2D texture = Resources.Load<Texture2D>("CaptureSmoke/whitePuff00");
            if (texture == null)
                return;

            smokeMaterial = new Material(shader);
            smokeMaterial.SetFloat("_Smoke", 1f);
            smokeMaterial.SetTexture("_BaseMap", texture);
            var host = new GameObject("Kenney Magic Smoke");
            host.transform.SetParent(transform, false);
            host.transform.position = bounds.center;
            var particles = host.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            float size = Mathf.Max(bounds.size.magnitude, 0.05f);
            var main = particles.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = Duration;
            main.maxParticles = 6;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.48f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(size * 0.08f, size * 0.22f);
            // Even at birth, the smoke extends beyond the captured piece's bounds.
            main.startSize = new ParticleSystem.MinMaxCurve(size * 1.25f, size * 1.5f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            // Choose a fresh hue per capture, with a nearby tint within this smoke burst.
            lastSmokeHue = Mathf.Repeat(lastSmokeHue + Random.Range(0.18f, 0.82f), 1f);
            Color smokeColor = Color.HSVToRGB(lastSmokeHue, 0.65f, 1f);
            Color smokeTint = Color.HSVToRGB(Mathf.Repeat(lastSmokeHue + 0.05f, 1f), 0.4f, 1f);
            smokeColor.a = smokeTint.a = 0.65f;
            main.startColor = new ParticleSystem.MinMaxGradient(smokeColor, smokeTint);
            var emission = particles.emission;
            emission.rateOverTime = 0f;
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = size * 0.1f;
            var velocity = particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = 0f;
            velocity.y = size * 0.15f;
            velocity.z = 0f;
            var scale = particles.sizeOverLifetime;
            scale.enabled = true;
            scale.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.9f, 1f, 1.3f));
            var color = particles.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = smokeMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            particles.Play();
            particles.Emit(6);
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / DissolveDuration);
            float eased = 1f - Mathf.Pow(1f - progress, 3f);
            ghostRoot.localScale = Vector3.one * Mathf.Lerp(1f, 0.25f, eased);
            SetOpacity(1f - eased);
            if (elapsed >= Duration) Cancel();
        }

        private void SetOpacity(float alpha)
        {
            for (int i = 0; i < ghosts.Count; i++)
            {
                Color color = colors[i];
                color.a = alpha;
                properties.SetColor("_BaseColor", color);
                ghosts[i].SetPropertyBlock(properties);
            }
        }

        public void Cancel()
        {
            gameObject.SetActive(false);
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (material != null) Destroy(material);
            if (particleMaterial != null) Destroy(particleMaterial);
            if (smokeMaterial != null) Destroy(smokeMaterial);
            foreach (Mesh mesh in bakedMeshes) Destroy(mesh);
        }
    }
}
