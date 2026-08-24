using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Vent.Core.Damage;
using Vent.Core.Pooling;
using Vent.Core.Utility;
using Vent.Enemies.Runtime;
using Vent.Enemies.Spawning;
using Vent.Player;
using Vent.Player.Camera;
using Vent.Player.Health;
using Vent.Player.Movement;
using Vent.Weapons.Runtime;
using Vent.Weapons.VFX;
using Vent.Weapons.View;

namespace Vent.Editor
{
    /// <summary>
    /// Builds every prefab from primitives and wires component references. Greybox by design:
    /// the point of this project is the code, so the art is whatever a cube can be.
    /// </summary>
    public static class PrefabFactory
    {
        [MenuItem("Vent/3. Generate Prefabs")]
        public static void GenerateMenu()
        {
            GameAssets a = AssetFactory.CreateAll();
            CreateAll(a);
            AssetDatabase.SaveAssets();
            Debug.Log("[Vent] Prefabs generated.");
        }

        public static void CreateAll(GameAssets a)
        {
            a.MuzzleFlashPrefab = CreateMuzzleFlash(a);
            a.TracerPrefab = CreateTracer(a);
            a.ImpactPrefab = CreateImpact("VFX_Impact", a.Spark, count: 14, speedMin: 3f, speedMax: 6f, size: 0.03f, gravity: 1.5f);
            a.BloodImpactPrefab = CreateImpact("VFX_BloodImpact", a.Blood, count: 22, speedMin: 2f, speedMax: 5f, size: 0.045f, gravity: 2.5f);

            a.SmgViewModel = CreateSmgViewModel(a);
            a.PistolViewModel = CreatePistolViewModel(a);
            a.Smg.SetPresentation(a.SmgViewModel, a.MuzzleFlashPrefab, a.TracerPrefab, a.ImpactPrefab, a.BloodImpactPrefab);
            a.Pistol.SetPresentation(a.PistolViewModel, a.MuzzleFlashPrefab, a.TracerPrefab, a.ImpactPrefab, a.BloodImpactPrefab);
            EditorUtility.SetDirty(a.Smg);
            EditorUtility.SetDirty(a.Pistol);

            a.ZombiePrefab = CreateZombie(a);
            a.Zombie.SetPrefab(a.ZombiePrefab);
            EditorUtility.SetDirty(a.Zombie);

            a.VentPrefab = CreateVent(a);
            a.PlayerPrefab = CreatePlayer(a);
        }

        // ------------------------------------------------------------------ VFX

        private static GameObject CreateMuzzleFlash(GameAssets a)
        {
            var root = new GameObject("VFX_MuzzleFlash");
            root.AddComponent<PooledObject>();
            var flash = root.AddComponent<MuzzleFlash>();

            var lightGo = new GameObject("Light");
            lightGo.transform.SetParent(root.transform, false);
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.72f, 0.35f);
            light.range = 5f;
            light.intensity = 6f;
            light.shadows = LightShadows.None;

            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            for (int i = 0; i < 3; i++)
            {
                GameObject blade = Primitive(PrimitiveType.Cube, $"Blade{i}", visual.transform,
                    new Vector3(0f, 0f, 0.08f), new Vector3(0.05f, 0.22f, 0.01f), a.Flash, collider: false);
                blade.transform.localRotation = Quaternion.Euler(0f, 0f, i * 60f);
            }

            SetPrivate(flash, "flashLight", light);
            SetPrivate(flash, "visual", visual.transform);
            Layers.SetRecursively(root, Layers.WeaponViewIndex);
            // Lights are culled per camera by layer. The Player layer carries no renderers and is
            // visible to both the world camera and the weapon overlay camera, so a light placed
            // there illuminates the room and the gun alike.
            lightGo.layer = Layers.PlayerIndex;
            return Save(root);
        }

        private static GameObject CreateTracer(GameAssets a)
        {
            var root = new GameObject("VFX_Tracer");
            var line = root.AddComponent<LineRenderer>();
            line.material = a.Tracer;
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.widthMultiplier = 0.02f;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.alignment = LineAlignment.View;
            root.AddComponent<PooledObject>();
            root.AddComponent<Tracer>();
            return Save(root);
        }

        private static GameObject CreateImpact(string name, Material material, int count, float speedMin, float speedMax, float size, float gravity)
        {
            var root = new GameObject(name);
            root.AddComponent<PooledObject>();
            var auto = root.AddComponent<AutoRelease>();
            auto.Lifetime = 0.8f;

            var ps = root.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = ps.main;
            main.duration = 0.5f;
            main.loop = false;
            main.playOnAwake = true;
            main.startLifetime = 0.4f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
            main.startSize = size;
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 64;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 35f;
            shape.radius = 0.02f;

            var renderer = root.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return Save(root);
        }

        // ------------------------------------------------------------------ weapons

        private static GameObject CreateSmgViewModel(GameAssets a)
        {
            var root = new GameObject("VM_SMG");
            var vm = root.AddComponent<WeaponViewModel>();
            Transform t = root.transform;
            Primitive(PrimitiveType.Cube, "Receiver", t, new Vector3(0f, 0f, 0f), new Vector3(0.05f, 0.07f, 0.36f), a.GunMetal, false);
            Primitive(PrimitiveType.Cube, "Barrel", t, new Vector3(0f, 0.015f, 0.26f), new Vector3(0.025f, 0.025f, 0.18f), a.GunMetal, false);
            Primitive(PrimitiveType.Cube, "Magazine", t, new Vector3(0f, -0.1f, 0.03f), new Vector3(0.035f, 0.14f, 0.05f), a.GunMetal, false);
            GameObject grip = Primitive(PrimitiveType.Cube, "Grip", t, new Vector3(0f, -0.08f, -0.1f), new Vector3(0.035f, 0.1f, 0.04f), a.GunAccent, false);
            grip.transform.localRotation = Quaternion.Euler(15f, 0f, 0f);
            Primitive(PrimitiveType.Cube, "Stock", t, new Vector3(0f, -0.01f, -0.24f), new Vector3(0.04f, 0.05f, 0.16f), a.GunAccent, false);
            Primitive(PrimitiveType.Cube, "Sight", t, new Vector3(0f, 0.05f, 0.05f), new Vector3(0.015f, 0.03f, 0.05f), a.GunMetal, false);
            var muzzle = new GameObject("Muzzle");
            muzzle.transform.SetParent(t, false);
            muzzle.transform.localPosition = new Vector3(0f, 0.015f, 0.36f);
            vm.SetMuzzle(muzzle.transform);
            Layers.SetRecursively(root, Layers.WeaponViewIndex);
            return Save(root);
        }

        private static GameObject CreatePistolViewModel(GameAssets a)
        {
            var root = new GameObject("VM_Pistol");
            var vm = root.AddComponent<WeaponViewModel>();
            Transform t = root.transform;
            Primitive(PrimitiveType.Cube, "Slide", t, new Vector3(0f, 0.02f, 0.04f), new Vector3(0.035f, 0.045f, 0.2f), a.GunMetal, false);
            Primitive(PrimitiveType.Cube, "Frame", t, new Vector3(0f, -0.01f, 0.02f), new Vector3(0.03f, 0.03f, 0.14f), a.GunMetal, false);
            GameObject grip = Primitive(PrimitiveType.Cube, "Grip", t, new Vector3(0f, -0.07f, -0.04f), new Vector3(0.03f, 0.11f, 0.04f), a.GunAccent, false);
            grip.transform.localRotation = Quaternion.Euler(15f, 0f, 0f);
            Primitive(PrimitiveType.Cube, "Sight", t, new Vector3(0f, 0.05f, -0.03f), new Vector3(0.01f, 0.015f, 0.02f), a.GunMetal, false);
            var muzzle = new GameObject("Muzzle");
            muzzle.transform.SetParent(t, false);
            muzzle.transform.localPosition = new Vector3(0f, 0.02f, 0.15f);
            vm.SetMuzzle(muzzle.transform);
            SetPrivate(vm, "hipPosition", new Vector3(0.2f, -0.18f, 0.38f));
            SetPrivate(vm, "aimPosition", new Vector3(0f, -0.11f, 0.28f));
            Layers.SetRecursively(root, Layers.WeaponViewIndex);
            return Save(root);
        }

        // ------------------------------------------------------------------ zombie

        private static GameObject CreateZombie(GameAssets a)
        {
            var root = new GameObject("Zombie");
            var agent = root.AddComponent<NavMeshAgent>();
            agent.radius = 0.35f;
            agent.height = 1.9f;
            agent.speed = 3.4f;
            agent.acceleration = 18f;
            agent.angularSpeed = 360f;
            agent.stoppingDistance = 1f;
            agent.autoBraking = true;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;
            agent.enabled = false; // enabled by Zombie only once it stands on the NavMesh
            root.AddComponent<PooledObject>();
            var zombie = root.AddComponent<Zombie>();

            var rig = new GameObject("Rig");
            rig.transform.SetParent(root.transform, false);
            var animator = rig.AddComponent<ZombieAnimator>();

            // Body pivot carries the collider; the scaled mesh is a child so children are not distorted.
            var body = new GameObject("Body");
            body.transform.SetParent(rig.transform, false);
            var bodyCollider = body.AddComponent<CapsuleCollider>();
            bodyCollider.center = new Vector3(0f, 0.95f, 0f);
            bodyCollider.radius = 0.32f;
            bodyCollider.height = 1.5f;
            body.AddComponent<Hitbox>().Configure(1f, head: false);
            Primitive(PrimitiveType.Capsule, "BodyMesh", body.transform, new Vector3(0f, 0.95f, 0f), new Vector3(0.62f, 0.7f, 0.5f), a.ZombieSkin, false);

            var head = new GameObject("Head");
            head.transform.SetParent(body.transform, false);
            head.transform.localPosition = new Vector3(0f, 1.72f, 0f);
            var headCollider = head.AddComponent<SphereCollider>();
            headCollider.radius = 0.2f;
            head.AddComponent<Hitbox>().Configure(2.5f, head: true);
            Primitive(PrimitiveType.Sphere, "HeadMesh", head.transform, Vector3.zero, Vector3.one * 0.4f, a.ZombieHead, false);

            Transform leftArm = Limb("LeftArm", body.transform, new Vector3(-0.36f, 1.45f, 0f), a.ZombieSkin);
            Transform rightArm = Limb("RightArm", body.transform, new Vector3(0.36f, 1.45f, 0f), a.ZombieSkin);
            Primitive(PrimitiveType.Cube, "LeftLeg", rig.transform, new Vector3(-0.15f, 0.35f, 0f), new Vector3(0.18f, 0.7f, 0.18f), a.ZombieSkin, false);
            Primitive(PrimitiveType.Cube, "RightLeg", rig.transform, new Vector3(0.15f, 0.35f, 0f), new Vector3(0.18f, 0.7f, 0.18f), a.ZombieSkin, false);

            animator.Configure(body.transform, head.transform, leftArm, rightArm, root.GetComponentsInChildren<Renderer>());
            zombie.Configure(a.Zombie, a.Zombies, a.Kill, animator);
            Layers.SetRecursively(root, Layers.ZombieIndex);
            return Save(root);
        }

        private static Transform Limb(string name, Transform parent, Vector3 pivot, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pivot;
            Primitive(PrimitiveType.Cube, "Mesh", go.transform, new Vector3(0f, -0.3f, 0f), new Vector3(0.14f, 0.6f, 0.14f), material, false);
            return go.transform;
        }

        // ------------------------------------------------------------------ vent

        private static GameObject CreateVent(GameAssets a)
        {
            var root = new GameObject("Vent");
            var vent = root.AddComponent<AirVent>();
            Transform t = root.transform;

            GameObject frame = Primitive(PrimitiveType.Cube, "Frame", t, Vector3.zero, new Vector3(0.9f, 0.6f, 0.06f), a.VentMetal, collider: true);
            for (int i = 0; i < 4; i++)
            {
                // Children of the scaled frame use frame-local units (±0.5 spans the frame).
                Primitive(PrimitiveType.Cube, $"Slat{i}", frame.transform, new Vector3(0f, -0.3f + 0.2f * i, 0.55f),
                    new Vector3(0.85f, 0.06f, 0.4f), a.Trim, collider: false);
            }

            var grate = new GameObject("Grate");
            grate.transform.SetParent(t, false);
            grate.transform.localPosition = new Vector3(0f, -1.7f, -0.2f); // zombie root (feet); head ends up at grate height

            var floor = new GameObject("FloorPoint");
            floor.transform.SetParent(t, false);
            floor.transform.localPosition = new Vector3(0f, -2.45f, 0.9f);

            vent.Configure(grate.transform, floor.transform, a.Vents, frame.transform);
            Layers.SetRecursively(root, Layers.VentIndex);
            return Save(root);
        }

        // ------------------------------------------------------------------ player

        private static GameObject CreatePlayer(GameAssets a)
        {
            var root = new GameObject("Player") { tag = Tags.Player };
            var cc = root.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.stepOffset = 0.4f;
            cc.slopeLimit = 45f;
            cc.skinWidth = 0.05f;

            var controller = root.AddComponent<FirstPersonController>();
            var look = root.AddComponent<PlayerLook>();
            var health = root.AddComponent<PlayerHealth>();
            var inventory = root.AddComponent<WeaponInventory>();
            var character = root.AddComponent<PlayerCharacter>();

            var pivot = new GameObject("CameraPivot");
            pivot.transform.SetParent(root.transform, false);
            pivot.transform.localPosition = new Vector3(0f, 1.6f, 0f);

            var camGo = new GameObject("MainCamera") { tag = Tags.MainCamera };
            camGo.transform.SetParent(pivot.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 75f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 120f;
            cam.cullingMask = ~(1 << Layers.WeaponViewIndex);
            camGo.AddComponent<AudioListener>();
            var motion = camGo.AddComponent<CameraMotion>();
            motion.Controller = controller;

            var weaponCamGo = new GameObject("WeaponCamera");
            weaponCamGo.transform.SetParent(camGo.transform, false);
            var weaponCam = weaponCamGo.AddComponent<Camera>();
            weaponCam.fieldOfView = 55f;
            weaponCam.nearClipPlane = 0.01f;
            weaponCam.farClipPlane = 4f;
            weaponCam.cullingMask = (1 << Layers.WeaponViewIndex) | (1 << Layers.PlayerIndex); // Player layer = lights only
            weaponCam.clearFlags = CameraClearFlags.Depth;

            // URP camera stacking: the weapon camera renders on top so the view-model never clips into walls.
            UniversalAdditionalCameraData baseData = cam.GetUniversalAdditionalCameraData();
            baseData.renderType = CameraRenderType.Base;
            baseData.renderPostProcessing = true;
            baseData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            UniversalAdditionalCameraData overlayData = weaponCam.GetUniversalAdditionalCameraData();
            overlayData.renderType = CameraRenderType.Overlay;
            baseData.cameraStack.Add(weaponCam);

            var socket = new GameObject("WeaponSocket");
            socket.transform.SetParent(camGo.transform, false);

            controller.Input = a.InputReader;
            look.Input = a.InputReader;
            look.PitchPivot = pivot.transform;
            health.HealthChanged = a.Health;
            health.Died = a.PlayerDied;
            inventory.Primary = a.Smg;
            inventory.Secondary = a.Pistol;
            inventory.ViewModelSocket = socket.transform;
            inventory.HudChannel = a.WeaponHud;
            inventory.LevelUpChannel = a.WeaponLevelUp;
            inventory.KillChannel = a.Kill;
            inventory.HitChannel = a.Hit;
            character.Configure(a.InputReader, cam, inventory, motion, a.Level);

            root.layer = Layers.PlayerIndex;
            pivot.layer = Layers.PlayerIndex;
            camGo.layer = Layers.PlayerIndex;
            weaponCamGo.layer = Layers.PlayerIndex;
            return Save(root);
        }

        // ------------------------------------------------------------------ helpers

        public static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 localScale, Material material, bool collider)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            if (!collider)
            {
                Object.DestroyImmediate(go.GetComponent<Collider>());
            }

            return go;
        }

        private static GameObject Save(GameObject instance)
        {
            string path = $"{Paths.Prefabs}/{instance.name}.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(instance, path, out bool success);
            Object.DestroyImmediate(instance);
            if (!success)
            {
                throw new System.InvalidOperationException($"Failed to save prefab {path}");
            }

            return prefab;
        }

        /// <summary>Set a private serialized field by name (keeps runtime classes free of editor-only setters).</summary>
        public static void SetPrivate(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
            {
                throw new System.ArgumentException($"{target.GetType().Name} has no serialized field '{field}'");
            }

            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetPrivate(Object target, string field, Vector3 value)
        {
            var so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
            {
                throw new System.ArgumentException($"{target.GetType().Name} has no serialized field '{field}'");
            }

            prop.vector3Value = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
