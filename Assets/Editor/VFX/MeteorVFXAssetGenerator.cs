using FaceSlapper.VFX;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FaceSlapper.Editor.VFX
{
    /// <summary>Creates the initial meteor assets without replacing edited assets.</summary>
    [InitializeOnLoad]
    public static class MeteorVFXAssetGenerator
    {
        private const string PrefabPath = "Assets/Prefabs/VFX/MeteorVFX.prefab";
        private const string MeshPath = "Assets/Art/Meshes/MeteorShockArc.asset";
        private const string MaterialFolder = "Assets/Art/Materials/VFX";

        static MeteorVFXAssetGenerator()
        {
            EditorApplication.delayCall += GenerateIfMissing;
        }

        [MenuItem("Tools/FaceSlapper/Create Meteor VFX")]
        public static void GenerateIfMissing()
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null)
            {
                Selection.activeObject = existing;
                return;
            }

            Shader arcShader = Shader.Find("FaceSlapper/Meteor/ShockArc");
            Shader particleShader = Shader.Find("FaceSlapper/Meteor/Particle");
            if (arcShader == null || particleShader == null)
                return;

            EnsureFolder("Assets/Prefabs/VFX");
            EnsureFolder("Assets/Art/Meshes");
            EnsureFolder(MaterialFolder);

            Material arcMaterial = CreateArcMaterial(arcShader);
            Material flameMaterial = CreateParticleMaterial(
                "M_MeteorFlame",
                particleShader,
                new Color(2.5f, 0.65f, 0.08f, 1f),
                0.65f,
                BlendMode.One);
            Material smokeMaterial = CreateParticleMaterial(
                "M_MeteorSmoke",
                particleShader,
                new Color(0.2f, 0.16f, 0.14f, 0.65f),
                1.5f,
                BlendMode.OneMinusSrcAlpha);
            Material trailMaterial = CreateParticleMaterial(
                "M_MeteorTrail",
                particleShader,
                new Color(3f, 1.1f, 0.18f, 1f),
                0.8f,
                BlendMode.One);
            Mesh arcMesh = CreateArcMesh();

            GameObject root = new GameObject("MeteorVFX");
            MeteorAtmosphereVFX controller = root.AddComponent<MeteorAtmosphereVFX>();
            Transform visualRoot = CreateChild("VisualRoot", root.transform).transform;

            GameObject arc = CreateChild("ShockArc", visualRoot);
            arc.AddComponent<MeshFilter>().sharedMesh = arcMesh;
            MeshRenderer arcRenderer = arc.AddComponent<MeshRenderer>();
            arcRenderer.sharedMaterial = arcMaterial;
            arcRenderer.shadowCastingMode = ShadowCastingMode.Off;
            arcRenderer.receiveShadows = false;

            ParticleSystem flames = CreateFlames(visualRoot, flameMaterial);
            ParticleSystem smoke = CreateSmoke(visualRoot, smokeMaterial);
            ParticleSystem sparks = CreateSparks(visualRoot, flameMaterial);
            TrailRenderer trail = CreateTrail(root.transform, trailMaterial);
            Light glow = CreateGlow(root.transform);

            SerializedObject serializedController = new SerializedObject(controller);
            SetReference(serializedController, "_visualRoot", visualRoot);
            SetReference(serializedController, "_shockArc", arcRenderer);
            SetReference(serializedController, "_flameParticles", flames);
            SetReference(serializedController, "_smokeParticles", smoke);
            SetReference(serializedController, "_sparkParticles", sparks);
            SetReference(serializedController, "_hotTrail", trail);
            SetReference(serializedController, "_glowLight", glow);
            serializedController.ApplyModifiedPropertiesWithoutUndo();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Selection.activeObject = prefab;
            Debug.Log("Created MeteorVFX prefab at " + PrefabPath);
        }

        private static Material CreateArcMaterial(Shader shader)
        {
            const string path = MaterialFolder + "/M_MeteorShockArc.mat";
            Material material = LoadOrCreateMaterial(path, shader);
            material.SetColor("_BaseColor", new Color(1f, 0.1f, 0.005f, 0.9f));
            material.SetColor("_HotColor", new Color(4f, 1.5f, 0.3f, 1f));
            material.SetFloat("_Emission", 2f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material CreateParticleMaterial(
            string name,
            Shader shader,
            Color color,
            float softness,
            BlendMode destinationBlend)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            Material material = LoadOrCreateMaterial(path, shader);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Softness", softness);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)destinationBlend);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material LoadOrCreateMaterial(string path, Shader shader)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;

            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static Mesh CreateArcMesh()
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (existing != null)
                return existing;

            const int segments = 48;
            const int rings = 9;
            Vector3[] vertices = new Vector3[(segments + 1) * rings];
            Vector2[] uv = new Vector2[vertices.Length];
            int[] triangles = new int[segments * (rings - 1) * 6];

            for (int ring = 0; ring < rings; ring++)
            {
                float t = ring / (float)(rings - 1);
                float radius = Mathf.Lerp(0.12f, 0.72f, t);
                float z = 0.68f - 0.35f * t * t;

                for (int segment = 0; segment <= segments; segment++)
                {
                    float angle = segment / (float)segments * Mathf.PI * 2f;
                    int index = ring * (segments + 1) + segment;
                    vertices[index] = new Vector3(
                        Mathf.Cos(angle) * radius,
                        Mathf.Sin(angle) * radius,
                        z);
                    uv[index] = new Vector2(segment / (float)segments, t);
                }
            }

            int triangle = 0;
            for (int ring = 0; ring < rings - 1; ring++)
            {
                for (int segment = 0; segment < segments; segment++)
                {
                    int current = ring * (segments + 1) + segment;
                    int next = current + segments + 1;
                    triangles[triangle++] = current;
                    triangles[triangle++] = next;
                    triangles[triangle++] = current + 1;
                    triangles[triangle++] = current + 1;
                    triangles[triangle++] = next;
                    triangles[triangle++] = next + 1;
                }
            }

            Mesh mesh = new Mesh { name = "MeteorShockArc" };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, MeshPath);
            return mesh;
        }

        private static ParticleSystem CreateFlames(Transform parent, Material material)
        {
            ParticleSystem system = CreateParticleSystem("FlameParticles", parent, material);
            ParticleSystem.MainModule main = system.main;
            main.startLifetime = 0.35f;
            main.startSpeed = 3.5f;
            main.startSize = 0.4f;
            main.startColor = new Color(1f, 0.75f, 0.35f, 1f);
            main.maxParticles = 300;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 90f;
            ConfigureBackwardHemisphere(system, 0.36f);
            ConfigureNoise(system, 0.35f, 0.8f, 1.2f);
            ConfigureFade(system, new Color(1f, 0.8f, 0.4f), new Color(1f, 0.08f, 0.005f));

            ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 1.6f;
            renderer.velocityScale = 0.06f;
            return system;
        }

        private static ParticleSystem CreateSmoke(Transform parent, Material material)
        {
            ParticleSystem system = CreateParticleSystem("SmokeParticles", parent, material);
            ParticleSystem.MainModule main = system.main;
            main.startLifetime = 4f;
            main.startSpeed = 1.2f;
            main.startSize = 0.65f;
            main.startColor = new Color(0.45f, 0.35f, 0.3f, 0.55f);
            main.maxParticles = 500;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 45f;
            ConfigureBackwardHemisphere(system, 0.28f);
            ConfigureNoise(system, 0.55f, 0.25f, 0.35f);
            ConfigureFade(system, new Color(0.35f, 0.18f, 0.1f), new Color(0.15f, 0.15f, 0.15f));

            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(
                1f,
                new AnimationCurve(
                    new Keyframe(0f, 0.6f),
                    new Keyframe(0.35f, 1.2f),
                    new Keyframe(1f, 2.2f)));
            return system;
        }

        private static ParticleSystem CreateSparks(Transform parent, Material material)
        {
            ParticleSystem system = CreateParticleSystem("SparkParticles", parent, material);
            ParticleSystem.MainModule main = system.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.065f);
            main.startColor = new Color(2f, 0.7f, 0.12f, 1f);
            main.gravityModifier = 0.35f;
            main.maxParticles = 180;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 35f;
            ConfigureBackwardHemisphere(system, 0.48f);
            ConfigureFade(system, new Color(1f, 0.8f, 0.35f), new Color(1f, 0.08f, 0.005f));

            ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 2f;
            renderer.velocityScale = 0.08f;
            return system;
        }

        private static ParticleSystem CreateParticleSystem(
            string name,
            Transform parent,
            Material material)
        {
            GameObject gameObject = CreateChild(name, parent);
            ParticleSystem system = gameObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return system;
        }

        private static void ConfigureBackwardHemisphere(ParticleSystem system, float radius)
        {
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = radius;
            shape.rotation = new Vector3(0f, 180f, 0f);
        }

        private static void ConfigureNoise(
            ParticleSystem system,
            float strength,
            float frequency,
            float scrollSpeed)
        {
            ParticleSystem.NoiseModule noise = system.noise;
            noise.enabled = true;
            noise.strength = strength;
            noise.frequency = frequency;
            noise.scrollSpeed = scrollSpeed;
            noise.damping = true;
        }

        private static void ConfigureFade(ParticleSystem system, Color start, Color end)
        {
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(start, 0f),
                    new GradientColorKey(end, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.8f, 0.25f),
                    new GradientAlphaKey(0f, 1f)
                });

            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            color.color = gradient;
        }

        private static TrailRenderer CreateTrail(Transform parent, Material material)
        {
            GameObject gameObject = CreateChild("HotTrail", parent);
            TrailRenderer trail = gameObject.AddComponent<TrailRenderer>();
            trail.sharedMaterial = material;
            trail.time = 0.65f;
            trail.minVertexDistance = 0.08f;
            trail.widthMultiplier = 0.65f;
            trail.widthCurve = new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(0.35f, 0.55f),
                new Keyframe(1f, 0f));
            trail.colorGradient = new Gradient
            {
                colorKeys = new[]
                {
                    new GradientColorKey(new Color(1f, 0.95f, 0.65f), 0f),
                    new GradientColorKey(new Color(1f, 0.15f, 0.01f), 1f)
                },
                alphaKeys = new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0f, 1f)
                }
            };
            trail.alignment = LineAlignment.View;
            trail.textureMode = LineTextureMode.Stretch;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.autodestruct = false;
            return trail;
        }

        private static Light CreateGlow(Transform parent)
        {
            GameObject gameObject = CreateChild("GlowLight", parent);
            Light light = gameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.28f, 0.04f);
            light.intensity = 0f;
            light.range = 5f;
            light.shadows = LightShadows.None;
            return light;
        }

        private static GameObject CreateChild(string name, Transform parent)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child;
        }

        private static void SetReference(
            SerializedObject serializedObject,
            string propertyName,
            Object value)
        {
            serializedObject.FindProperty(propertyName).objectReferenceValue = value;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string[] parts = path.Split('/');
            string current = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[index]);
                current = next;
            }
        }
    }
}
