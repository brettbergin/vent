using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Vent.Core.Audio;
using Vent.Core.Events;
using Vent.Core.Pooling;
using Vent.Core.Services;
using Vent.Core.Utility;
using Vent.Gameplay.Persistence;

namespace Vent.Tests.EditMode
{
    public sealed class EventChannelTests
    {
        [Test]
        public void RaiseDeliversPayloadToEverySubscriber()
        {
            var channel = ScriptableObject.CreateInstance<IntEventChannel>();
            int a = 0, b = 0;
            channel.Subscribe(v => a = v);
            channel.Subscribe(v => b = v * 2);
            channel.Raise(21);
            Assert.AreEqual(21, a);
            Assert.AreEqual(42, b);
            UnityEngine.Object.DestroyImmediate(channel);
        }

        [Test]
        public void UnsubscribeStopsDelivery()
        {
            var channel = ScriptableObject.CreateInstance<VoidEventChannel>();
            int calls = 0;
            Action listener = () => calls++;
            channel.Subscribe(listener);
            channel.Raise();
            channel.Unsubscribe(listener);
            channel.Raise();
            Assert.AreEqual(1, calls);
            Assert.AreEqual(0, channel.ListenerCount);
            UnityEngine.Object.DestroyImmediate(channel);
        }

        [Test]
        public void ThrowingListenerDoesNotStarveOthers()
        {
            var channel = ScriptableObject.CreateInstance<IntEventChannel>();
            bool reached = false;
            channel.Subscribe(_ => throw new InvalidOperationException("boom"));
            channel.Subscribe(_ => reached = true);
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("boom"));
            channel.Raise(1);
            Assert.IsTrue(reached);
            UnityEngine.Object.DestroyImmediate(channel);
        }
    }

    public sealed class GameServicesTests
    {
        private sealed class Foo { }

        [SetUp]
        public void SetUp() => GameServices.Clear();

        [Test]
        public void RegisterThenGet()
        {
            var foo = new Foo();
            GameServices.Register(foo);
            Assert.AreSame(foo, GameServices.Get<Foo>());
            Assert.IsTrue(GameServices.Has<Foo>());
        }

        [Test]
        public void GetMissingThrowsAndTryGetReturnsFalse()
        {
            Assert.Throws<InvalidOperationException>(() => GameServices.Get<Foo>());
            Assert.IsFalse(GameServices.TryGet(out Foo _));
        }

        [Test]
        public void UnregisterOnlyRemovesTheSameInstance()
        {
            var first = new Foo();
            var second = new Foo();
            GameServices.Register(first);
            GameServices.Register(second);
            GameServices.Unregister(first);
            Assert.AreSame(second, GameServices.Get<Foo>());
        }
    }

    public sealed class CooldownTests
    {
        [Test]
        public void ConsumeRespectsDuration()
        {
            var cd = new Cooldown();
            Assert.IsTrue(cd.TryConsume(10f, 0.5f));
            Assert.IsFalse(cd.TryConsume(10.4f, 0.5f));
            Assert.AreEqual(0.1f, cd.Remaining(10.4f), 1e-4f);
            Assert.IsTrue(cd.TryConsume(10.5f, 0.5f));
        }
    }

    public sealed class PrefabPoolTests
    {
        private GameObject prefab;

        [SetUp]
        public void SetUp() => prefab = new GameObject("PoolPrefab");

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(prefab);

        [Test]
        public void PrewarmCreatesInactiveInstances()
        {
            var container = new GameObject("Container").transform;
            var pool = new PrefabPool(prefab, container, prewarm: 4);
            Assert.AreEqual(4, pool.CountInactive);
            Assert.AreEqual(0, pool.CountActive);
            Assert.AreEqual(4, container.childCount);
            foreach (Transform child in container)
            {
                Assert.IsFalse(child.gameObject.activeSelf);
            }

            pool.Dispose();
            UnityEngine.Object.DestroyImmediate(container.gameObject);
        }

        [Test]
        public void GetActivatesAndReleaseReturns()
        {
            var container = new GameObject("Container").transform;
            var pool = new PrefabPool(prefab, container, prewarm: 1);
            PooledObject instance = pool.Get(new Vector3(1, 2, 3), Quaternion.identity);
            Assert.IsTrue(instance.gameObject.activeSelf);
            Assert.IsTrue(instance.IsActiveInPool);
            Assert.AreEqual(new Vector3(1, 2, 3), instance.transform.position);
            Assert.AreEqual(1, pool.CountActive);

            bool releasedEvent = false;
            instance.Released += () => releasedEvent = true;
            instance.Release();
            Assert.IsTrue(releasedEvent);
            Assert.IsFalse(instance.gameObject.activeSelf);
            Assert.AreEqual(0, pool.CountActive);
            Assert.AreEqual(1, pool.CountInactive);

            instance.Release(); // double release is a no-op
            Assert.AreEqual(1, pool.CountInactive);

            pool.Dispose();
            UnityEngine.Object.DestroyImmediate(container.gameObject);
        }
    }

    public sealed class ProceduralSoundBankTests
    {
        [Test]
        public void EverySoundSynthesises()
        {
            foreach (SoundId id in Enum.GetValues(typeof(SoundId)))
            {
                if (id == SoundId.None)
                {
                    Assert.IsNull(ProceduralSoundBank.Get(id));
                    continue;
                }

                AudioClip clip = ProceduralSoundBank.Get(id);
                Assert.IsNotNull(clip, id.ToString());
                Assert.Greater(clip.samples, 0, id.ToString());
                Assert.AreSame(clip, ProceduralSoundBank.Get(id), "clips are cached");
            }
        }

        [Test]
        public void SamplesAreWithinRange()
        {
            AudioClip clip = ProceduralSoundBank.Get(SoundId.SmgShot);
            var data = new float[clip.samples];
            clip.GetData(data, 0);
            float peak = 0f;
            foreach (float s in data)
            {
                peak = Mathf.Max(peak, Mathf.Abs(s));
            }

            Assert.Greater(peak, 0.1f);
            Assert.LessOrEqual(peak, 1f);
        }
    }

    public sealed class HighScoreStoreTests
    {
        private string dir;

        [SetUp]
        public void SetUp()
        {
            dir = Path.Combine(Path.GetTempPath(), "vent-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Test]
        public void FirstRunIsARecordAndPersists()
        {
            var store = new HighScoreStore(dir);
            Assert.IsTrue(store.Record(levelReached: 4, kills: 40, seconds: 120f));
            Assert.IsFalse(store.Record(levelReached: 3, kills: 10, seconds: 30f));
            Assert.IsTrue(store.Record(levelReached: 5, kills: 12, seconds: 10f));

            var reloaded = new HighScoreStore(dir);
            Assert.AreEqual(5, reloaded.Data.BestLevel);
            Assert.AreEqual(40, reloaded.Data.BestKills);
            Assert.AreEqual(3, reloaded.Data.TotalRuns);
            Assert.AreEqual(62, reloaded.Data.TotalKills);
            Assert.AreEqual(120f, reloaded.Data.LongestRunSeconds, 1e-3f);
        }

        [Test]
        public void CorruptFileFallsBackToDefaults()
        {
            File.WriteAllText(Path.Combine(dir, "highscores.json"), "{ not json");
            LogAssert.ignoreFailingMessages = true;
            var store = new HighScoreStore(dir);
            LogAssert.ignoreFailingMessages = false;
            Assert.AreEqual(0, store.Data.BestLevel);
        }
    }
}
