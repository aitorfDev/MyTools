using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UGUIEnhancerTests
{
    /// <summary>
    /// Generous upper bounds, to catch accidental slowdowns (not to benchmark): the tool runs on every
    /// change you make in the Scene View, so it must stay cheap even in big scenes.
    /// </summary>
    public class PerformanceTests
    {
        GameObject canvasObject;
        RectTransform canvas;

        [SetUp]
        public void SetUp()
        {
            canvasObject = new GameObject("TestCanvas", typeof(Canvas));
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            canvas = (RectTransform)canvasObject.transform;
            canvas.sizeDelta = new Vector2(2000, 1200);
            UGUIEnhancerSettings.ResetToDefaults();
            AutoAnchors.manual.Clear();
            AutoAnchors.pending.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(canvasObject);
            AutoAnchors.manual.Clear();
            AutoAnchors.pending.Clear();
        }

        RectTransform[] MakeMany(int count)
        {
            var random = new System.Random(7);
            var all = new RectTransform[count];
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("E" + i, typeof(RectTransform));
                var rt = (RectTransform)go.transform;
                rt.SetParent(canvas, false);
                rt.anchorMin = rt.anchorMax = Vector2.zero;
                rt.sizeDelta = new Vector2(100, 50);
                rt.anchoredPosition = new Vector2(random.Next(0, 1900) + 50, random.Next(0, 1100) + 25);
                all[i] = rt;
            }
            return all;
        }

        [Test]
        public void Applying_To_2000_Elements_IsFast()
        {
            var elements = MakeMany(2000);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            foreach (var rt in elements)
                AutoAnchors.Apply(rt);
            watch.Stop();

            TestContext.WriteLine($"Apply x2000: {watch.ElapsedMilliseconds} ms ({watch.Elapsed.TotalMilliseconds / 2000:F3} ms each)");
            Assert.Less(watch.ElapsedMilliseconds, 4000);
        }

        [Test]
        public void Flushing_A_Batch_Of_1000_Elements_IsFast_AndIsOneUndoStep()
        {
            var elements = MakeMany(1000);
            Undo.IncrementCurrentGroup();
            foreach (var rt in elements)
                AutoAnchors.pending.Add(rt);

            var watch = System.Diagnostics.Stopwatch.StartNew();
            AutoAnchors.Flush();
            watch.Stop();

            TestContext.WriteLine($"Flush x1000: {watch.ElapsedMilliseconds} ms");
            Assert.Less(watch.ElapsedMilliseconds, 5000);

            Undo.PerformUndo();
            int untouched = 0;
            foreach (var rt in elements)
                if (rt.anchorMin == Vector2.zero && rt.anchorMax == Vector2.zero)
                    untouched++;
            Assert.AreEqual(elements.Length, untouched, "a single undo reverts the whole batch");
        }

        [Test]
        public void Applying_Again_To_Already_Anchored_Elements_Is_Cheap()
        {
            var elements = MakeMany(2000);
            foreach (var rt in elements)
                AutoAnchors.Apply(rt);

            var watch = System.Diagnostics.Stopwatch.StartNew();
            foreach (var rt in elements)
                AutoAnchors.Apply(rt);
            watch.Stop();

            TestContext.WriteLine($"Re-apply x2000: {watch.ElapsedMilliseconds} ms");
            Assert.Less(watch.ElapsedMilliseconds, 3000);
        }

        [Test]
        public void ExclusionCheck_On_A_Deep_Hierarchy_Is_Fast()
        {
            Transform parent = canvas;
            for (int i = 0; i < 60; i++)
            {
                var go = new GameObject("L" + i, typeof(RectTransform));
                go.transform.SetParent(parent, false);
                parent = go.transform;
            }

            var leaf = (RectTransform)parent;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 2000; i++)
                UGUIEnhancerExclusions.IsExcluded(leaf);
            watch.Stop();

            TestContext.WriteLine($"IsExcluded x2000 (60 levels): {watch.ElapsedMilliseconds} ms");
            Assert.Less(watch.ElapsedMilliseconds, 3000);
        }
    }
}
