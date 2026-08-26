using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Vent.Core.Damage;
using Vent.Core.Perks;
using Vent.Core.Pooling;
using Vent.Core.Utility;
using Vent.Enemies.Runtime;
using Vent.Enemies.Spawning;
using Vent.Player;
using Vent.Player.Camera;
using Vent.Player.Health;
using Vent.Player.Interaction;
using Vent.Player.Movement;
using Vent.Vehicles.Data;
using Vent.Vehicles.Runtime;
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
            a.MuzzleSmokePrefab = CreateMuzzleSmoke(a);
            a.MuzzleFlashPrefab = CreateMuzzleFlash(a);
            a.TracerPrefab = CreateTracer(a);
            a.ImpactPrefab = CreateImpact("VFX_Impact", a.Spark, count: 14, speedMin: 3f, speedMax: 6f, size: 0.03f, gravity: 1.5f);
            a.BloodImpactPrefab = CreateImpact("VFX_BloodImpact", a.Blood, count: 22, speedMin: 2f, speedMax: 5f, size: 0.045f, gravity: 2.5f);
            a.ShellCasingPrefab = CreateShellCasing(a);

            a.SmgViewModel = CreateSmgViewModel(a);
            a.PistolViewModel = CreatePistolViewModel(a);
            a.Smg.SetPresentation(a.SmgViewModel, a.MuzzleFlashPrefab, a.TracerPrefab, a.ImpactPrefab, a.BloodImpactPrefab, a.ShellCasingPrefab);
            a.Pistol.SetPresentation(a.PistolViewModel, a.MuzzleFlashPrefab, a.TracerPrefab, a.ImpactPrefab, a.BloodImpactPrefab, a.ShellCasingPrefab);
            EditorUtility.SetDirty(a.Smg);
            EditorUtility.SetDirty(a.Pistol);

            a.ZombiePrefab = CreateZombie(a);
            a.Zombie.SetPrefab(a.ZombiePrefab);
            EditorUtility.SetDirty(a.Zombie);

            a.VentPrefab = CreateVent(a);
            a.PlayerPrefab = CreatePlayer(a);
            a.PerkPickupPrefab = CreatePerkPickup(a);
            a.SedanPrefab = CreateVehicle(a, VehicleShape.Sedan);
            a.VanPrefab = CreateVehicle(a, VehicleShape.Van);
        }

        // ------------------------------------------------------------------ VFX

        /// <summary>
        /// The flash itself: three additive sprite planes — two crossed along the barrel and one facing
        /// forward at the tip — so it has volume from every angle, plus a point light. Smoke and
        /// sparks are a separate world-space prefab (<see cref="CreateMuzzleSmoke"/>) so they can
        /// outlive the flash and drift after the gun has moved on.
        /// </summary>
        private static GameObject CreateMuzzleFlash(GameAssets a)
        {
            var root = new GameObject("VFX_MuzzleFlash");
            root.AddComponent<PooledObject>();
            var flash = root.AddComponent<MuzzleFlash>();

            var lightGo = new GameObject("Light");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0f, 0.15f);
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.68f, 0.32f);
            light.range = 7f;
            light.intensity = 9f;
            light.shadows = LightShadows.None;

            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            var renderers = new List<Renderer>();
            // Side planes: quads lying along +Z, crossed at 90°, textured so the spikes point forward.
            for (int i = 0; i < 2; i++)
            {
                GameObject plane = Primitive(PrimitiveType.Quad, $"Side{i}", visual.transform, new Vector3(0f, 0f, 0.2f), new Vector3(0.44f, 0.26f, 1f), a.FlashSprite, false);
                plane.transform.localRotation = Quaternion.Euler(0f, 90f, i * 90f);
                renderers.Add(plane.GetComponent<Renderer>());
            }

            // Front plane: faces down the barrel so the flash reads head-on and in mirrors of the pistol slide.
            GameObject front = Primitive(PrimitiveType.Quad, "Front", visual.transform, new Vector3(0f, 0f, 0.06f), new Vector3(0.3f, 0.3f, 1f), a.FlashSprite, false);
            renderers.Add(front.GetComponent<Renderer>());
            foreach (Renderer r in renderers)
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }

            flash.Configure(light, visual.transform, renderers.ToArray(), a.MuzzleSmokePrefab);
            Layers.SetRecursively(root, Layers.WeaponViewIndex);
            // Lights are culled per camera by layer. The Player layer carries no renderers and is
            // visible to both the world camera and the weapon overlay camera, so a light placed
            // there illuminates the room and the gun alike.
            lightGo.layer = Layers.PlayerIndex;
            return Save(root);
        }

        /// <summary>Gun smoke and sparks, spawned at the muzzle in world space and left behind: a puff that drifts up and a few hot streaks forward.</summary>
        private static GameObject CreateMuzzleSmoke(GameAssets a)
        {
            var root = new GameObject("VFX_MuzzleSmoke");
            root.AddComponent<PooledObject>();
            root.AddComponent<AutoRelease>().Lifetime = 1.4f;

            var smoke = root.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = smoke.main;
            main.duration = 0.3f;
            main.loop = false;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = -0.04f; // hot: drifts up
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 16;
            ParticleSystem.EmissionModule emission = smoke.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)5, (short)7) });
            ParticleSystem.ShapeModule shape = smoke.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 18f;
            shape.radius = 0.01f;
            ParticleSystem.SizeOverLifetimeModule size = smoke.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 3.2f)));
            ParticleSystem.ColorOverLifetimeModule color = smoke.colorOverLifetime;
            color.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(new Color(0.9f, 0.85f, 0.75f), 0f), new GradientColorKey(new Color(0.6f, 0.6f, 0.62f), 0.25f), new GradientColorKey(new Color(0.5f, 0.5f, 0.52f), 1f) },
                new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0.45f, 0.3f), new GradientAlphaKey(0f, 1f) });
            color.color = grad;
            ParticleSystem.RotationOverLifetimeModule rot = smoke.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);
            ParticleSystem.LimitVelocityOverLifetimeModule drag = smoke.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.dampen = 0.35f;
            var smokeRenderer = root.GetComponent<ParticleSystemRenderer>();
            smokeRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            smokeRenderer.sharedMaterial = a.Smoke;
            smokeRenderer.shadowCastingMode = ShadowCastingMode.Off;
            smokeRenderer.sortingFudge = -10f; // draw after the world, before the additive flash

            var sparksGo = new GameObject("Sparks");
            sparksGo.transform.SetParent(root.transform, false);
            var sparks = sparksGo.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule sm = sparks.main;
            sm.duration = 0.2f;
            sm.loop = false;
            sm.playOnAwake = true;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
            sm.startSpeed = new ParticleSystem.MinMaxCurve(6f, 14f);
            sm.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.03f);
            sm.gravityModifier = 0.8f;
            sm.simulationSpace = ParticleSystemSimulationSpace.World;
            sm.maxParticles = 24;
            ParticleSystem.EmissionModule se = sparks.emission;
            se.rateOverTime = 0f;
            se.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)5, (short)10) });
            ParticleSystem.ShapeModule ss = sparks.shape;
            ss.shapeType = ParticleSystemShapeType.Cone;
            ss.angle = 12f;
            ss.radius = 0.01f;
            ParticleSystem.ColorOverLifetimeModule sc = sparks.colorOverLifetime;
            sc.enabled = true;
            var sg = new Gradient();
            sg.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.6f, 0.2f), 0.4f), new GradientColorKey(new Color(0.8f, 0.2f, 0.05f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            sc.color = sg;
            var sparkRenderer = sparksGo.GetComponent<ParticleSystemRenderer>();
            sparkRenderer.renderMode = ParticleSystemRenderMode.Stretch;
            sparkRenderer.velocityScale = 0.02f;
            sparkRenderer.lengthScale = 2.5f;
            sparkRenderer.sharedMaterial = a.SparkGlow;
            sparkRenderer.shadowCastingMode = ShadowCastingMode.Off;

            // World layer: the world camera draws and depth-tests it against the room; the gun overlays it.
            return Save(root);
        }

        private static GameObject CreateTracer(GameAssets a)
        {
            var root = new GameObject("VFX_Tracer");
            var line = root.AddComponent<LineRenderer>();
            line.material = a.Tracer;
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.widthMultiplier = 0.035f;
            // Fat at the muzzle, needle at the far end; white-hot fading to orange.
            line.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.15f, 0.7f), new Keyframe(1f, 0.15f));
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.85f, 0.55f), 0.2f), new GradientColorKey(new Color(1f, 0.55f, 0.2f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.3f), new GradientAlphaKey(0.35f, 1f) });
            line.colorGradient = gradient;
            line.textureMode = LineTextureMode.Stretch;
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

        private static GameObject CreateShellCasing(GameAssets a)
        {
            var root = new GameObject("VFX_ShellCasing");
            root.AddComponent<PooledObject>();
            root.AddComponent<ShellCasing>();
            GameObject body = Primitive(PrimitiveType.Cylinder, "Brass", root.transform, Vector3.zero, new Vector3(0.007f, 0.009f, 0.007f), a.Brass, collider: false);
            body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // cylinder axis along +Z (the gun's forward)
            body.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            return Save(root);
        }

        /// <summary>
        /// Greybox SMG built from primitives: polymer lower with a pistol grip and foregrip, steel
        /// upper with a shrouded barrel, iron sights, charging handle and ejection port, a canted
        /// magazine and a tube stock. Parts the view-model animates are wired by name.
        /// </summary>
        private static GameObject CreateSmgViewModel(GameAssets a)
        {
            var root = new GameObject("VM_SMG");
            var vm = root.AddComponent<WeaponViewModel>();
            Transform t = root.transform;

            // Upper receiver + rail
            Primitive(PrimitiveType.Cube, "Upper", t, new Vector3(0f, 0.01f, 0.02f), new Vector3(0.05f, 0.05f, 0.34f), a.GunSteel, false);
            Primitive(PrimitiveType.Cube, "Rail", t, new Vector3(0f, 0.04f, 0.04f), new Vector3(0.03f, 0.012f, 0.24f), a.GunMetal, false);
            // Lower receiver, mag well, trigger group
            Primitive(PrimitiveType.Cube, "Lower", t, new Vector3(0f, -0.03f, -0.02f), new Vector3(0.048f, 0.04f, 0.26f), a.GunPolymer, false);
            Primitive(PrimitiveType.Cube, "MagWell", t, new Vector3(0f, -0.06f, 0.03f), new Vector3(0.042f, 0.03f, 0.06f), a.GunPolymer, false);
            Primitive(PrimitiveType.Cube, "TriggerGuard", t, new Vector3(0f, -0.075f, -0.05f), new Vector3(0.012f, 0.004f, 0.06f), a.GunMetal, false);
            Primitive(PrimitiveType.Cube, "Trigger", t, new Vector3(0f, -0.062f, -0.04f), new Vector3(0.006f, 0.02f, 0.006f), a.GunMetal, false);
            GameObject grip = Primitive(PrimitiveType.Cube, "Grip", t, new Vector3(0f, -0.09f, -0.1f), new Vector3(0.034f, 0.1f, 0.036f), a.GunPolymer, false);
            grip.transform.localRotation = Quaternion.Euler(18f, 0f, 0f);
            GameObject foregrip = Primitive(PrimitiveType.Cube, "Foregrip", t, new Vector3(0f, -0.075f, 0.13f), new Vector3(0.026f, 0.06f, 0.026f), a.GunPolymer, false);
            foregrip.transform.localRotation = Quaternion.Euler(-8f, 0f, 0f);
            // Magazine (animated): canted forward, curved look approximated by two segments
            var mag = new GameObject("Magazine");
            mag.transform.SetParent(t, false);
            mag.transform.localPosition = new Vector3(0f, -0.075f, 0.03f);
            mag.transform.localRotation = Quaternion.Euler(-6f, 0f, 0f);
            Primitive(PrimitiveType.Cube, "MagBody", mag.transform, new Vector3(0f, -0.06f, 0f), new Vector3(0.032f, 0.12f, 0.05f), a.GunMetal, false);
            Primitive(PrimitiveType.Cube, "MagBase", mag.transform, new Vector3(0f, -0.125f, 0.004f), new Vector3(0.036f, 0.012f, 0.056f), a.GunPolymer, false);
            // Barrel: shroud, barrel, muzzle brake
            GameObject shroud = Primitive(PrimitiveType.Cylinder, "Shroud", t, new Vector3(0f, 0.012f, 0.24f), new Vector3(0.034f, 0.06f, 0.034f), a.GunMetal, false);
            shroud.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            GameObject barrel = Primitive(PrimitiveType.Cylinder, "Barrel", t, new Vector3(0f, 0.012f, 0.35f), new Vector3(0.016f, 0.05f, 0.016f), a.GunSteel, false);
            barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            GameObject brake = Primitive(PrimitiveType.Cylinder, "MuzzleBrake", t, new Vector3(0f, 0.012f, 0.405f), new Vector3(0.024f, 0.015f, 0.024f), a.GunMetal, false);
            brake.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            // Sights
            Primitive(PrimitiveType.Cube, "FrontSightBase", t, new Vector3(0f, 0.045f, 0.19f), new Vector3(0.014f, 0.012f, 0.014f), a.GunMetal, false);
            Primitive(PrimitiveType.Cube, "FrontSightPost", t, new Vector3(0f, 0.058f, 0.19f), new Vector3(0.003f, 0.016f, 0.003f), a.GunSteel, false);
            Primitive(PrimitiveType.Cube, "RearSightL", t, new Vector3(-0.006f, 0.055f, -0.09f), new Vector3(0.006f, 0.014f, 0.01f), a.GunMetal, false);
            Primitive(PrimitiveType.Cube, "RearSightR", t, new Vector3(0.006f, 0.055f, -0.09f), new Vector3(0.006f, 0.014f, 0.01f), a.GunMetal, false);
            // Bolt (animated) sits in the ejection port on the right side; charging handle rides with it
            var bolt = new GameObject("Bolt");
            bolt.transform.SetParent(t, false);
            bolt.transform.localPosition = new Vector3(0.026f, 0.012f, 0.0f);
            Primitive(PrimitiveType.Cube, "BoltFace", bolt.transform, Vector3.zero, new Vector3(0.004f, 0.02f, 0.05f), a.GunSteel, false);
            Primitive(PrimitiveType.Cube, "ChargingHandle", bolt.transform, new Vector3(0.01f, 0f, -0.01f), new Vector3(0.018f, 0.008f, 0.012f), a.GunMetal, false);
            // Stock: buffer tube + pad
            GameObject tube = Primitive(PrimitiveType.Cylinder, "StockTube", t, new Vector3(0f, 0.0f, -0.25f), new Vector3(0.026f, 0.08f, 0.026f), a.GunMetal, false);
            tube.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Primitive(PrimitiveType.Cube, "StockPad", t, new Vector3(0f, -0.005f, -0.34f), new Vector3(0.036f, 0.07f, 0.03f), a.GunPolymer, false);

            var muzzle = new GameObject("Muzzle");
            muzzle.transform.SetParent(t, false);
            muzzle.transform.localPosition = new Vector3(0f, 0.012f, 0.415f);
            var port = new GameObject("EjectionPort");
            port.transform.SetParent(t, false);
            port.transform.localPosition = new Vector3(0.03f, 0.02f, 0.0f);
            port.transform.localRotation = Quaternion.Euler(0f, 20f, 0f); // +X: out and a little back
            vm.SetMuzzle(muzzle.transform);
            vm.SetParts(port.transform, mag.transform, bolt.transform);
            Layers.SetRecursively(root, Layers.WeaponViewIndex);
            return Save(root);
        }

        /// <summary>Greybox pistol: serrated steel slide over a polymer frame, exposed barrel, hammer, sights, rail.</summary>
        private static GameObject CreatePistolViewModel(GameAssets a)
        {
            var root = new GameObject("VM_Pistol");
            var vm = root.AddComponent<WeaponViewModel>();
            Transform t = root.transform;

            // Slide (animated) with rear serrations and the barrel visible at the front
            var slide = new GameObject("Slide");
            slide.transform.SetParent(t, false);
            slide.transform.localPosition = new Vector3(0f, 0.022f, 0.03f);
            Primitive(PrimitiveType.Cube, "SlideBody", slide.transform, Vector3.zero, new Vector3(0.034f, 0.04f, 0.2f), a.GunSteel, false);
            for (int i = 0; i < 4; i++)
            {
                Primitive(PrimitiveType.Cube, $"Serration{i}", slide.transform, new Vector3(0f, 0.004f, -0.07f - i * 0.012f), new Vector3(0.036f, 0.028f, 0.004f), a.GunMetal, false);
            }

            Primitive(PrimitiveType.Cube, "RearSight", slide.transform, new Vector3(0f, 0.025f, -0.085f), new Vector3(0.024f, 0.01f, 0.01f), a.GunMetal, false);
            Primitive(PrimitiveType.Cube, "FrontSight", slide.transform, new Vector3(0f, 0.025f, 0.09f), new Vector3(0.004f, 0.01f, 0.006f), a.GunMetal, false);
            Primitive(PrimitiveType.Cube, "EjectionCut", slide.transform, new Vector3(0.017f, 0.006f, 0.0f), new Vector3(0.002f, 0.014f, 0.03f), a.GunPolymer, false);
            GameObject barrel = Primitive(PrimitiveType.Cylinder, "Barrel", t, new Vector3(0f, 0.022f, 0.135f), new Vector3(0.014f, 0.006f, 0.014f), a.GunMetal, false);
            barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            // Frame, rail, trigger group, hammer
            Primitive(PrimitiveType.Cube, "Frame", t, new Vector3(0f, -0.008f, 0.02f), new Vector3(0.03f, 0.024f, 0.16f), a.GunPolymer, false);
            Primitive(PrimitiveType.Cube, "Rail", t, new Vector3(0f, -0.024f, 0.07f), new Vector3(0.026f, 0.008f, 0.05f), a.GunPolymer, false);
            Primitive(PrimitiveType.Cube, "TriggerGuard", t, new Vector3(0f, -0.045f, -0.005f), new Vector3(0.01f, 0.004f, 0.05f), a.GunPolymer, false);
            Primitive(PrimitiveType.Cube, "TriggerGuardFront", t, new Vector3(0f, -0.03f, 0.02f), new Vector3(0.01f, 0.03f, 0.004f), a.GunPolymer, false);
            Primitive(PrimitiveType.Cube, "Trigger", t, new Vector3(0f, -0.032f, -0.002f), new Vector3(0.005f, 0.018f, 0.005f), a.GunMetal, false);
            Primitive(PrimitiveType.Cube, "Hammer", t, new Vector3(0f, 0.03f, -0.075f), new Vector3(0.008f, 0.02f, 0.008f), a.GunMetal, false);
            // Grip with the magazine (animated) inside it
            var grip = new GameObject("GripPivot");
            grip.transform.SetParent(t, false);
            grip.transform.localPosition = new Vector3(0f, -0.02f, -0.04f);
            grip.transform.localRotation = Quaternion.Euler(18f, 0f, 0f);
            Primitive(PrimitiveType.Cube, "Grip", grip.transform, new Vector3(0f, -0.05f, 0f), new Vector3(0.03f, 0.1f, 0.04f), a.GunPolymer, false);
            var mag = new GameObject("Magazine");
            mag.transform.SetParent(grip.transform, false);
            mag.transform.localPosition = new Vector3(0f, -0.1f, 0f);
            Primitive(PrimitiveType.Cube, "MagBase", mag.transform, new Vector3(0f, -0.006f, 0.002f), new Vector3(0.032f, 0.012f, 0.044f), a.GunMetal, false);

            var muzzle = new GameObject("Muzzle");
            muzzle.transform.SetParent(t, false);
            muzzle.transform.localPosition = new Vector3(0f, 0.022f, 0.145f);
            var port = new GameObject("EjectionPort");
            port.transform.SetParent(t, false);
            port.transform.localPosition = new Vector3(0.02f, 0.03f, 0.0f);
            port.transform.localRotation = Quaternion.Euler(0f, 25f, 0f);
            vm.SetMuzzle(muzzle.transform);
            vm.SetParts(port.transform, mag.transform, slide.transform);
            SetPrivate(vm, "hipPosition", new Vector3(0.2f, -0.18f, 0.38f));
            SetPrivate(vm, "aimPosition", new Vector3(0f, -0.11f, 0.28f));
            SetPrivate(vm, "slideTravel", 0.022f);
            SetPrivate(vm, "magazineDrop", 0.09f);
            SetPrivate(vm, "kickUpDegrees", 6f);
            Layers.SetRecursively(root, Layers.WeaponViewIndex);
            return Save(root);
        }

        // ------------------------------------------------------------------ zombie

        /// <summary>
        /// The zombie: a jointed humanoid from primitives. Hierarchy is pivot → mesh at every joint
        /// so <see cref="ZombieAnimator"/> rotates pivots and never scales a child by accident.
        /// Hitboxes: head 2.5×, torso 1×, limbs per <see cref="ZombieDefinition.LimbDamageMultiplier"/>.
        /// </summary>
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
            float limb = a.Zombie != null ? a.Zombie.LimbDamageMultiplier : 0.65f;

            var rigGo = new GameObject("Rig");
            rigGo.transform.SetParent(root.transform, false);
            var animator = rigGo.AddComponent<ZombieAnimator>();
            var rig = new ZombieRig();
            var skin = new System.Collections.Generic.List<Renderer>();

            // ---- Pelvis / torso ------------------------------------------------------------
            rig.Hips = Pivot("Hips", rigGo.transform, new Vector3(0f, 0.95f, 0f));
            GameObject pelvis = Primitive(PrimitiveType.Cube, "Pelvis", rig.Hips, new Vector3(0f, 0.02f, 0f), new Vector3(0.36f, 0.2f, 0.26f), a.ZombieClothes, false);
            var torsoCol = rig.Hips.gameObject.AddComponent<CapsuleCollider>();
            torsoCol.center = new Vector3(0f, 0.3f, 0.02f);
            torsoCol.radius = 0.26f;
            torsoCol.height = 0.95f;
            rig.Hips.gameObject.AddComponent<Hitbox>().Configure(1f, head: false);

            rig.Spine = Pivot("Spine", rig.Hips, new Vector3(0f, 0.1f, 0f));
            GameObject chest = Primitive(PrimitiveType.Capsule, "Chest", rig.Spine, new Vector3(0f, 0.32f, 0.02f), new Vector3(0.5f, 0.36f, 0.34f), a.ZombieClothes, false);
            GameObject shirtTear = Primitive(PrimitiveType.Cube, "TornShirt", rig.Spine, new Vector3(0.08f, 0.22f, 0.17f), new Vector3(0.16f, 0.18f, 0.02f), a.ZombieSkin, false);
            skin.Add(shirtTear.GetComponent<Renderer>());
            Primitive(PrimitiveType.Cube, "ChestWound", rig.Spine, new Vector3(-0.1f, 0.36f, 0.185f), new Vector3(0.1f, 0.12f, 0.01f), a.ZombieGore, false);

            // ---- Neck / head ---------------------------------------------------------------
            Transform neck = Pivot("Neck", rig.Spine, new Vector3(0f, 0.62f, 0.04f));
            GameObject neckMesh = Primitive(PrimitiveType.Cylinder, "NeckMesh", neck, new Vector3(0f, 0.05f, 0f), new Vector3(0.13f, 0.08f, 0.13f), a.ZombieSkin, false);
            skin.Add(neckMesh.GetComponent<Renderer>());
            rig.Head = Pivot("Head", neck, new Vector3(0f, 0.09f, 0f));
            var headCol = rig.Head.gameObject.AddComponent<SphereCollider>();
            headCol.center = new Vector3(0f, 0.12f, 0.01f);
            headCol.radius = 0.16f;
            rig.Head.gameObject.AddComponent<Hitbox>().Configure(2.5f, head: true);
            // Skull: a capsule lying along Z (long front-to-back), sunk onto the neck.
            GameObject skull = Primitive(PrimitiveType.Capsule, "Skull", rig.Head, new Vector3(0f, 0.12f, 0.0f), new Vector3(0.26f, 0.15f, 0.28f), a.ZombieHead, false);
            skull.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            skin.Add(skull.GetComponent<Renderer>());
            foreach (float x in new[] { -0.055f, 0.055f })
            {
                Primitive(PrimitiveType.Sphere, x < 0 ? "EyeL" : "EyeR", rig.Head, new Vector3(x, 0.14f, 0.135f), Vector3.one * 0.05f, a.ZombieEye, false);
                Primitive(PrimitiveType.Sphere, x < 0 ? "PupilL" : "PupilR", rig.Head, new Vector3(x, 0.14f, 0.158f), Vector3.one * 0.02f, a.ZombieClothes, false);
            }

            Primitive(PrimitiveType.Cube, "HeadWound", rig.Head, new Vector3(0.09f, 0.19f, 0.04f), new Vector3(0.08f, 0.05f, 0.1f), a.ZombieGore, false);
            rig.Jaw = Pivot("Jaw", rig.Head, new Vector3(0f, 0.04f, 0.03f));
            GameObject jawMesh = Primitive(PrimitiveType.Cube, "JawMesh", rig.Jaw, new Vector3(0f, -0.02f, 0.06f), new Vector3(0.16f, 0.055f, 0.15f), a.ZombieHead, false);
            skin.Add(jawMesh.GetComponent<Renderer>());
            Primitive(PrimitiveType.Cube, "Teeth", rig.Jaw, new Vector3(0f, 0.01f, 0.125f), new Vector3(0.11f, 0.018f, 0.02f), a.ZombieEye, false);

            // ---- Arms ----------------------------------------------------------------------
            (rig.LeftShoulder, rig.LeftElbow) = ArmChain("Left", rig.Spine, new Vector3(-0.27f, 0.5f, 0.02f), a, skin, limb);
            (rig.RightShoulder, rig.RightElbow) = ArmChain("Right", rig.Spine, new Vector3(0.27f, 0.5f, 0.02f), a, skin, limb);

            // ---- Legs ----------------------------------------------------------------------
            (rig.LeftHip, rig.LeftKnee) = LegChain("Left", rig.Hips, new Vector3(-0.12f, -0.05f, 0f), a, skin, limb);
            (rig.RightHip, rig.RightKnee) = LegChain("Right", rig.Hips, new Vector3(0.12f, -0.05f, 0f), a, skin, limb);

            // ---- Health bar ----------------------------------------------------------------
            var barGo = new GameObject("HealthBar");
            barGo.transform.SetParent(root.transform, false);
            barGo.transform.localPosition = new Vector3(0f, 2.25f, 0f);
            var bar = barGo.AddComponent<ZombieHealthBar>();
            GameObject track = Primitive(PrimitiveType.Quad, "Track", barGo.transform, Vector3.zero, new Vector3(0.62f, 0.07f, 1f), a.HealthBarTrack, false);
            GameObject fill = Primitive(PrimitiveType.Quad, "Fill", barGo.transform, new Vector3(0f, 0f, -0.001f), new Vector3(0.58f, 0.045f, 1f), a.HealthBarFill, false);
            foreach (GameObject q in new[] { track, fill })
            {
                var r = q.GetComponent<Renderer>();
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }

            bar.Configure(zombie, fill.transform, fill.GetComponent<Renderer>(), track.GetComponent<Renderer>());

            foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
            {
                bool isBar = r.gameObject.name is "Track" or "Fill";
                r.shadowCastingMode = isBar ? ShadowCastingMode.Off : ShadowCastingMode.On;
                // Zombies go outside now: the sun (exterior rendering layer) must be able to light them.
                r.renderingLayerMask = isBar ? 1u : (1u << 1) | 1u;
            }

            animator.Configure(rig, skin.ToArray());
            zombie.Configure(a.Zombie, a.Zombies, a.Kill, a.Noise, animator);
            Layers.SetRecursively(root, Layers.ZombieIndex);
            return Save(root);
        }

        private static Transform Pivot(string name, Transform parent, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            return go.transform;
        }

        /// <summary>Shoulder pivot → upper arm → elbow pivot → forearm → hand. Colliders on both segments (limb multiplier).</summary>
        private static (Transform shoulder, Transform elbow) ArmChain(string side, Transform parent, Vector3 shoulderPos, GameAssets a,
            System.Collections.Generic.List<Renderer> skin, float limbMultiplier)
        {
            Transform shoulder = Pivot($"{side}Shoulder", parent, shoulderPos);
            GameObject sleeve = Primitive(PrimitiveType.Capsule, "UpperArm", shoulder, new Vector3(0f, -0.15f, 0f), new Vector3(0.13f, 0.17f, 0.13f), a.ZombieClothes, false);
            LimbCollider(shoulder.gameObject, new Vector3(0f, -0.15f, 0f), 0.065f, 0.34f, limbMultiplier);

            Transform elbow = Pivot($"{side}Elbow", shoulder, new Vector3(0f, -0.3f, 0f));
            GameObject forearm = Primitive(PrimitiveType.Capsule, "Forearm", elbow, new Vector3(0f, -0.14f, 0f), new Vector3(0.11f, 0.16f, 0.11f), a.ZombieSkin, false);
            skin.Add(forearm.GetComponent<Renderer>());
            GameObject hand = Primitive(PrimitiveType.Cube, "Hand", elbow, new Vector3(0f, -0.31f, 0.01f), new Vector3(0.09f, 0.1f, 0.05f), a.ZombieSkin, false);
            skin.Add(hand.GetComponent<Renderer>());
            for (int i = 0; i < 3; i++)
            {
                GameObject finger = Primitive(PrimitiveType.Cube, $"Finger{i}", elbow, new Vector3(-0.025f + 0.025f * i, -0.39f, 0.02f), new Vector3(0.018f, 0.07f, 0.018f), a.ZombieSkin, false);
                finger.transform.localRotation = Quaternion.Euler(-25f, 0f, 0f);
                skin.Add(finger.GetComponent<Renderer>());
            }

            LimbCollider(elbow.gameObject, new Vector3(0f, -0.2f, 0f), 0.06f, 0.42f, limbMultiplier);
            return (shoulder, elbow);
        }

        /// <summary>Hip pivot → thigh → knee pivot → shin → foot.</summary>
        private static (Transform hip, Transform knee) LegChain(string side, Transform parent, Vector3 hipPos, GameAssets a,
            System.Collections.Generic.List<Renderer> skin, float limbMultiplier)
        {
            Transform hip = Pivot($"{side}Hip", parent, hipPos);
            Primitive(PrimitiveType.Capsule, "Thigh", hip, new Vector3(0f, -0.22f, 0f), new Vector3(0.17f, 0.24f, 0.17f), a.ZombieClothes, false);
            LimbCollider(hip.gameObject, new Vector3(0f, -0.22f, 0f), 0.085f, 0.46f, limbMultiplier);

            Transform knee = Pivot($"{side}Knee", hip, new Vector3(0f, -0.45f, 0f));
            GameObject shin = Primitive(PrimitiveType.Capsule, "Shin", knee, new Vector3(0f, -0.22f, 0f), new Vector3(0.14f, 0.23f, 0.14f), a.ZombieSkin, false);
            skin.Add(shin.GetComponent<Renderer>());
            Primitive(PrimitiveType.Cube, "Foot", knee, new Vector3(0f, -0.46f, 0.06f), new Vector3(0.12f, 0.07f, 0.26f), a.ZombieClothes, false);
            LimbCollider(knee.gameObject, new Vector3(0f, -0.24f, 0f), 0.07f, 0.5f, limbMultiplier);
            return (hip, knee);
        }

        private static void LimbCollider(GameObject go, Vector3 center, float radius, float height, float multiplier)
        {
            var col = go.AddComponent<CapsuleCollider>();
            col.center = center;
            col.radius = radius;
            col.height = height;
            go.AddComponent<Hitbox>().Configure(multiplier, head: false);
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
            var interactor = root.AddComponent<PlayerInteractor>();

            var pivot = new GameObject("CameraPivot");
            pivot.transform.SetParent(root.transform, false);
            pivot.transform.localPosition = new Vector3(0f, 1.6f, 0f);

            var camGo = new GameObject("MainCamera") { tag = Tags.MainCamera };
            camGo.transform.SetParent(pivot.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 75f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 500f; // the district is ~390 m across and the skyline sits beyond it
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
            inventory.NoiseChannel = a.Noise;
            character.Configure(a.InputReader, cam, inventory, motion, a.Level, a.PerkCollected, weaponCam);
            interactor.Configure(a.InputReader, cam, a.Prompt);

            root.layer = Layers.PlayerIndex;
            pivot.layer = Layers.PlayerIndex;
            camGo.layer = Layers.PlayerIndex;
            weaponCamGo.layer = Layers.PlayerIndex;
            return Save(root);
        }

        /// <summary>
        /// A perk orb: a glowing sphere with a core and a flat ring, plus a point light, all tinted per
        /// perk at runtime. No colliders: the pickup checks distance to the player itself, so the orb
        /// never blocks a bullet or a zombie.
        /// </summary>
        private static GameObject CreatePerkPickup(GameAssets a)
        {
            var root = new GameObject("PerkPickup");
            var pickup = root.AddComponent<PerkPickup>();

            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            GameObject orb = Primitive(PrimitiveType.Sphere, "Orb", visual.transform, Vector3.zero, Vector3.one * 0.34f, a.PerkOrb, false);
            GameObject core = Primitive(PrimitiveType.Sphere, "Core", visual.transform, Vector3.zero, Vector3.one * 0.16f, a.PerkOrb, false);
            GameObject ring = Primitive(PrimitiveType.Cylinder, "Ring", visual.transform, Vector3.zero, new Vector3(0.7f, 0.012f, 0.7f), a.PerkOrb, false);
            ring.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);
            var renderers = new List<Renderer>();
            foreach (GameObject part in new[] { orb, core, ring })
            {
                var r = part.GetComponent<Renderer>();
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.renderingLayerMask = (1u << 1) | 1u; // orbs drop on the street too
                renderers.Add(r);
            }

            var glowGo = new GameObject("Glow");
            glowGo.transform.SetParent(root.transform, false);
            var glow = glowGo.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.range = 4.5f;
            glow.intensity = 3f;
            glow.shadows = LightShadows.None;
            glowGo.layer = Layers.PlayerIndex; // lights live on a layer both cameras cull in (see CreatePlayer)

            pickup.Configure(a.PerkCollected, visual.transform, renderers.ToArray(), glow);
            return Save(root);
        }

        // ------------------------------------------------------------------ vehicles

        /// <summary>
        /// A car from boxes and cylinders: chassis and cabin (the two colliders), glass, lamps, mirrors,
        /// an interior you can see from the chase camera, four WheelColliders with visual wheels, the
        /// seat and exit points the driver uses, and the arm-and-pistol prop for drive-by. Root on the
        /// ground plane, nose along +Z, driver on the left (-X). Parked kinematic; see VehicleController.
        /// </summary>
        private static GameObject CreateVehicle(GameAssets a, VehicleShape shape)
        {
            bool van = shape == VehicleShape.Van;
            VehicleDefinition def = van ? a.Van : a.Sedan;
            var root = new GameObject(van ? "Vehicle_Van" : "Vehicle_Sedan");
            Transform t = root.transform;

            var body = root.AddComponent<Rigidbody>();
            body.mass = def.Mass;
            body.centerOfMass = def.CentreOfMass;
            body.linearDamping = 0.05f;
            body.angularDamping = 1f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.Continuous;
            body.solverIterations = 8;
            body.solverVelocityIterations = 2;
            body.maxAngularVelocity = 8f;
            body.isKinematic = true;

            float length = van ? 5.0f : 4.4f, width = van ? 1.95f : 1.8f;
            float wheelX = van ? 0.85f : 0.8f, wheelZ = van ? 1.7f : 1.4f, r = def.WheelRadius;
            Material paint = a.CarPaints[0];
            var paintRenderers = new List<Renderer>();

            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(t, false);
            Transform b = bodyGo.transform;
            GameObject Panel(string name, Vector3 pos, Vector3 size, bool collider)
            {
                GameObject go = Primitive(PrimitiveType.Cube, name, b, pos, size, paint, collider);
                paintRenderers.Add(go.GetComponent<Renderer>());
                return go;
            }

            Panel("Chassis", new Vector3(0f, van ? 0.7f : 0.62f, 0f), new Vector3(width, van ? 0.7f : 0.55f, length), collider: true);
            Panel("Cabin", van ? new Vector3(0f, 1.5f, -0.4f) : new Vector3(0f, 1.18f, -0.15f), van ? new Vector3(1.9f, 0.9f, 3.6f) : new Vector3(1.6f, 0.55f, 2.2f), collider: true);
            GameObject bonnet = Panel("Bonnet", van ? new Vector3(0f, 1.0f, 2.0f) : new Vector3(0f, 0.95f, 1.55f), van ? new Vector3(1.8f, 0.3f, 0.9f) : new Vector3(1.6f, 0.25f, 1.0f), collider: false);
            if (!van)
            {
                bonnet.transform.localRotation = Quaternion.Euler(-12f, 0f, 0f);
                Panel("Boot", new Vector3(0f, 0.95f, -1.65f), new Vector3(1.6f, 0.2f, 0.9f), collider: false);
            }

            Primitive(PrimitiveType.Cube, "Windscreen", b, van ? new Vector3(0f, 1.5f, 1.45f) : new Vector3(0f, 1.15f, 0.95f), van ? new Vector3(1.7f, 0.8f, 0.04f) : new Vector3(1.5f, 0.5f, 0.04f), a.CarGlass, false)
                .transform.localRotation = Quaternion.Euler(van ? 20f : 28f, 0f, 0f);
            Primitive(PrimitiveType.Cube, "RearWindow", b, van ? new Vector3(0f, 1.5f, -2.2f) : new Vector3(0f, 1.15f, -1.3f), van ? new Vector3(1.7f, 0.7f, 0.04f) : new Vector3(1.5f, 0.45f, 0.04f), a.CarGlass, false)
                .transform.localRotation = Quaternion.Euler(van ? 0f : -30f, 0f, 0f);
            foreach (float side in new[] { -1f, 1f })
            {
                string s = side < 0f ? "L" : "R";
                Primitive(PrimitiveType.Cube, $"Window{s}", b, van ? new Vector3(side * 0.96f, 1.55f, -0.4f) : new Vector3(side * 0.81f, 1.2f, -0.15f), van ? new Vector3(0.02f, 0.6f, 3.2f) : new Vector3(0.02f, 0.4f, 1.9f), a.CarGlass, false);
                Primitive(PrimitiveType.Cube, $"Headlight{s}", b, new Vector3(side * 0.6f, 0.7f, length / 2f + 0.01f), new Vector3(0.35f, 0.15f, 0.05f), a.Headlight, false);
                Primitive(PrimitiveType.Cube, $"Taillight{s}", b, new Vector3(side * 0.6f, 0.7f, -length / 2f - 0.01f), new Vector3(0.35f, 0.15f, 0.05f), a.Taillight, false);
                Panel($"Mirror{s}", new Vector3(side * (width / 2f + 0.08f), 1.05f, 0.8f), new Vector3(0.15f, 0.1f, 0.08f), collider: false);
                Primitive(PrimitiveType.Cube, $"ArchF{s}", b, new Vector3(side * (width / 2f + 0.02f), 0.5f, wheelZ), new Vector3(0.1f, 0.5f, 0.95f), a.MetalDark, false);
                Primitive(PrimitiveType.Cube, $"ArchR{s}", b, new Vector3(side * (width / 2f + 0.02f), 0.5f, -wheelZ), new Vector3(0.1f, 0.5f, 0.95f), a.MetalDark, false);
            }

            Primitive(PrimitiveType.Cube, "BumperF", b, new Vector3(0f, 0.42f, length / 2f + 0.05f), new Vector3(width - 0.1f, 0.25f, 0.15f), a.MetalDark, false);
            Primitive(PrimitiveType.Cube, "BumperR", b, new Vector3(0f, 0.42f, -length / 2f - 0.05f), new Vector3(width - 0.1f, 0.25f, 0.15f), a.MetalDark, false);
            Primitive(PrimitiveType.Cube, "CabinFloor", b, new Vector3(0f, 0.36f, -0.2f), new Vector3(width - 0.3f, 0.05f, 2.4f), a.CarInterior, false);
            Primitive(PrimitiveType.Cube, "Dashboard", b, new Vector3(0f, 0.95f, 0.65f), new Vector3(width - 0.3f, 0.3f, 0.4f), a.CarInterior, false);
            foreach (float x in new[] { -0.4f, 0.4f })
            {
                string s = x < 0f ? "Driver" : "Passenger";
                Primitive(PrimitiveType.Cube, $"Seat{s}", b, new Vector3(x, 0.75f, -0.2f), new Vector3(0.5f, 0.5f, 0.5f), a.CarInterior, false);
                Primitive(PrimitiveType.Cube, $"SeatBack{s}", b, new Vector3(x, 1.05f, -0.45f), new Vector3(0.5f, 0.6f, 0.12f), a.CarInterior, false);
            }

            Primitive(PrimitiveType.Cylinder, "SteeringWheel", b, new Vector3(-0.4f, 1.0f, 0.45f), new Vector3(0.36f, 0.02f, 0.36f), a.MetalDark, false)
                .transform.localRotation = Quaternion.Euler(70f, 0f, 0f);

            // Wheels: the collider is the physics, the Visual child is what you see; VehicleController poses one from the other.
            var wheelsGo = new GameObject("Wheels");
            wheelsGo.transform.SetParent(t, false);
            var wheels = new WheelCollider[4];
            var visuals = new Transform[4];
            (string name, float x, float z)[] corners = { ("FL", -wheelX, wheelZ), ("FR", wheelX, wheelZ), ("RL", -wheelX, -wheelZ), ("RR", wheelX, -wheelZ) };
            for (int i = 0; i < 4; i++)
            {
                var wheelGo = new GameObject($"Wheel{corners[i].name}");
                wheelGo.transform.SetParent(wheelsGo.transform, false);
                wheelGo.transform.localPosition = new Vector3(corners[i].x, r, corners[i].z);
                var wc = wheelGo.AddComponent<WheelCollider>();
                wc.radius = r;
                wc.suspensionDistance = def.SuspensionDistance;
                wc.mass = 25f;
                wc.wheelDampingRate = 1f;
                wc.forceAppPointDistance = 0.1f;
                wc.suspensionSpring = new JointSpring { spring = def.SuspensionSpring, damper = def.SuspensionDamper, targetPosition = 0.5f };
                wc.forwardFriction = new WheelFrictionCurve { extremumSlip = 0.4f, extremumValue = 1f, asymptoteSlip = 0.8f, asymptoteValue = 0.6f, stiffness = def.ForwardStiffness };
                wc.sidewaysFriction = new WheelFrictionCurve { extremumSlip = 0.2f, extremumValue = 1f, asymptoteSlip = 0.5f, asymptoteValue = 0.75f, stiffness = def.SidewaysStiffness };
                wheels[i] = wc;

                var visual = new GameObject("Visual");
                visual.transform.SetParent(wheelGo.transform, false);
                Primitive(PrimitiveType.Cylinder, "Tyre", visual.transform, Vector3.zero, new Vector3(r * 2f, 0.11f, r * 2f), a.Tyre, false).transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                Primitive(PrimitiveType.Cylinder, "Hub", visual.transform, Vector3.zero, new Vector3(0.3f, 0.12f, 0.3f), a.Chrome, false).transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                visuals[i] = visual.transform;
            }

            Transform Anchor(string name, Vector3 pos)
            {
                var go = new GameObject(name);
                go.transform.SetParent(t, false);
                go.transform.localPosition = pos;
                return go.transform;
            }

            Transform seatAnchor = Anchor("Seat", new Vector3(-0.4f, 0.45f, -0.2f));
            Transform exitL = Anchor("ExitL", new Vector3(-1.6f, 0f, 0.2f));
            Transform exitR = Anchor("ExitR", new Vector3(1.6f, 0f, 0.2f));
            Transform camTarget = Anchor("CameraTarget", new Vector3(0f, van ? 1.3f : 1.1f, 0f));

            // Drive-by: the driver's arm out of the window, pistol in hand. Off until someone sits down.
            Transform armT = Anchor("Arm", new Vector3(-(width / 2f + 0.02f), 1.05f, -0.1f));
            Primitive(PrimitiveType.Cube, "Sleeve", armT, new Vector3(0f, 0f, 0.2f), new Vector3(0.09f, 0.09f, 0.4f), a.Fabric, false);
            Primitive(PrimitiveType.Cube, "Hand", armT, new Vector3(0f, 0f, 0.45f), new Vector3(0.09f, 0.1f, 0.1f), a.Skin, false);
            Primitive(PrimitiveType.Cube, "Slide", armT, new Vector3(0f, 0.05f, 0.55f), new Vector3(0.03f, 0.04f, 0.18f), a.GunSteel, false);
            Primitive(PrimitiveType.Cube, "Grip", armT, new Vector3(0f, -0.02f, 0.5f), new Vector3(0.03f, 0.1f, 0.04f), a.GunPolymer, false);
            var muzzleOut = new GameObject("MuzzleOut");
            muzzleOut.transform.SetParent(armT, false);
            muzzleOut.transform.localPosition = new Vector3(0f, 0.05f, 0.65f);
            var portOut = new GameObject("PortOut");
            portOut.transform.SetParent(armT, false);
            portOut.transform.localPosition = new Vector3(0.02f, 0.07f, 0.5f);
            portOut.transform.localRotation = Quaternion.Euler(0f, 25f, 0f);
            var arm = armT.gameObject.AddComponent<VehicleDriveByArm>();
            armT.gameObject.SetActive(false);

            // Parked cars carve the NavMesh so zombies walk around them, not through them.
            var obstacle = root.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = new Vector3(0f, 0.7f, 0f);
            obstacle.size = new Vector3(width + 0.1f, 1.4f, length + 0.1f);
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;
            obstacle.carvingMoveThreshold = 0.5f;
            obstacle.carvingTimeToStationary = 0.5f;

            var engine = root.AddComponent<AudioSource>();
            engine.playOnAwake = false;
            engine.loop = true;
            engine.spatialBlend = 1f;
            engine.rolloffMode = AudioRolloffMode.Linear;
            engine.minDistance = 3f;
            engine.maxDistance = 45f;
            engine.dopplerLevel = 0f;

            var controller = root.AddComponent<VehicleController>();
            controller.Configure(def, wheels, visuals, paintRenderers.ToArray());
            var seat = root.AddComponent<VehicleSeat>();
            seat.Configure(seatAnchor, exitL, exitR, camTarget, arm, muzzleOut.transform, portOut.transform);
            var roadkill = root.AddComponent<VehicleRoadkill>();
            roadkill.Configure(a.BloodImpactPrefab, new Vector3(0f, 0.8f, 0f), new Vector3(width / 2f + 0.1f, 0.9f, length / 2f + 0.2f));
            root.AddComponent<VehicleAudio>().Configure(engine);

            foreach (Renderer rend in root.GetComponentsInChildren<Renderer>(true))
            {
                rend.renderingLayerMask = (1u << 1) | 1u; // sunlit
                rend.shadowCastingMode = rend.sharedMaterial == a.CarGlass ? ShadowCastingMode.Off : ShadowCastingMode.On;
            }

            Layers.SetRecursively(root, Layers.VehicleIndex);
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

        public static void SetPrivate(Object target, string field, float value)
        {
            var so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
            {
                throw new System.ArgumentException($"{target.GetType().Name} has no serialized field '{field}'");
            }

            prop.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
