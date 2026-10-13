using NUnit.Framework;
using UnityEngine;

namespace UGUIEnhancerTests
{
    /// <summary>
    /// Awkward geometry: scaled and rotated parents, flipped elements, odd pivots, other canvas types,
    /// plus a randomized (seeded) test. The invariant in every case: the element never moves or resizes.
    /// </summary>
    public class GeometryEdgeCaseTests
    {
        GameObject canvasObject;
        RectTransform canvas;

        [SetUp]
        public void SetUp()
        {
            canvasObject = new GameObject("TestCanvas", typeof(Canvas));
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            canvas = (RectTransform)canvasObject.transform;
            canvas.sizeDelta = new Vector2(1000, 600);

            UGUIEnhancerSettings.ResetToDefaults();
            AutoAnchors.manual.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(canvasObject);
            UGUIEnhancerSettings.ResetToDefaults();
            AutoAnchors.manual.Clear();
        }

        // Makes a child of `parent`, anchored to the parent's bottom-left, with a given rect in parent space.
        static RectTransform Make(Transform parent, float x, float y, float w, float h, Vector2? pivot = null, string name = "Element")
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = pivot ?? new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            // Position so the rect's bottom-left lands at (x, y) whatever the pivot is.
            rt.anchoredPosition = new Vector2(x + w * rt.pivot.x, y + h * rt.pivot.y);
            return rt;
        }

        static Rect WorldRect(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            return Rect.MinMaxRect(
                Mathf.Min(c[0].x, c[2].x), Mathf.Min(c[0].y, c[2].y),
                Mathf.Max(c[0].x, c[2].x), Mathf.Max(c[0].y, c[2].y));
        }

        // Applies the tool, then checks the element is exactly where it was. Tolerance scales with the size.
        static void ApplyAndAssertSameRect(RectTransform rt, string context = "")
        {
            Rect before = WorldRect(rt);
            AutoAnchors.Apply(rt);
            Rect after = WorldRect(rt);
            float tolerance = 0.01f + 1e-4f * Mathf.Max(before.width, before.height);
            Assert.AreEqual(before.xMin, after.xMin, tolerance, context + " xMin moved");
            Assert.AreEqual(before.yMin, after.yMin, tolerance, context + " yMin moved");
            Assert.AreEqual(before.xMax, after.xMax, tolerance, context + " xMax moved");
            Assert.AreEqual(before.yMax, after.yMax, tolerance, context + " yMax moved");
        }

        // --- Scale ---

        [Test]
        public void ScaledCanvas_KeepsTheRect_AndFindsTheCorner()
        {
            canvas.localScale = Vector3.one * 0.01f; // typical World Space canvas
            var rt = Make(canvas, 900, 550, 100, 50);
            ApplyAndAssertSameRect(rt);
            Assert.AreEqual(Vector2.one, rt.anchorMin);
            Assert.AreEqual(Vector2.one, rt.anchorMax);
        }

        [Test]
        public void ScaledParent_KeepsTheRect()
        {
            var parent = Make(canvas, 100, 100, 400, 300, name: "Parent");
            parent.localScale = new Vector3(2f, 0.5f, 1f); // non-uniform
            var rt = Make(parent, 300, 250, 100, 50); // top-right corner of the parent
            ApplyAndAssertSameRect(rt);
            Assert.AreEqual(Vector2.one, rt.anchorMin);
        }

        [Test]
        public void ElementWithItsOwnScale_KeepsTheRect()
        {
            var rt = Make(canvas, 400, 250, 100, 50);
            rt.localScale = new Vector3(1.5f, 2f, 1f);
            ApplyAndAssertSameRect(rt);
        }

        [Test]
        public void FlippedElement_NegativeScale_KeepsTheRect()
        {
            var rt = Make(canvas, 400, 250, 100, 50);
            rt.localScale = new Vector3(-1f, 1f, 1f);
            ApplyAndAssertSameRect(rt);

            var rt2 = Make(canvas, 400, 250, 100, 50, name: "Flipped2");
            rt2.localScale = new Vector3(-1f, -1f, 1f);
            ApplyAndAssertSameRect(rt2);
        }

        // --- Rotation ---

        [Test]
        public void RotatedParent_KeepsTheRect_AndStillFindsTheCorner()
        {
            var parent = Make(canvas, 200, 100, 400, 300, name: "Parent");
            parent.localRotation = Quaternion.Euler(0, 0, 33f);
            var rt = Make(parent, 300, 250, 100, 50); // top-right of the (rotated) parent
            ApplyAndAssertSameRect(rt);
            Assert.AreEqual(Vector2.one, rt.anchorMin);
        }

        [Test]
        public void ElementRotated180_IsStillHandled()
        {
            var rt = Make(canvas, 900, 550, 100, 50);
            rt.localRotation = Quaternion.Euler(0, 0, 180f);
            ApplyAndAssertSameRect(rt);
        }

        [Test]
        public void ElementRotated90_IsLeftAlone()
        {
            var rt = Make(canvas, 400, 250, 100, 50);
            rt.localRotation = Quaternion.Euler(0, 0, 90f);
            AutoAnchors.Apply(rt);
            Assert.AreEqual(Vector2.zero, rt.anchorMin);
            Assert.AreEqual(Vector2.zero, rt.anchorMax);
        }

        // --- Pivots ---

        [TestCase(0f, 0f)]
        [TestCase(0f, 1f)]
        [TestCase(1f, 0f)]
        [TestCase(1f, 1f)]
        [TestCase(0.25f, 0.75f)]
        [TestCase(-0.5f, 2f)]
        public void AnyPivot_KeepsTheRect(float px, float py)
        {
            var rt = Make(canvas, 900, 550, 100, 50, new Vector2(px, py));
            ApplyAndAssertSameRect(rt, $"pivot ({px},{py})");
            Assert.AreEqual(Vector2.one, rt.anchorMin, "corner anchors do not depend on the pivot");
        }

        // --- Outside and degenerate cases ---

        [Test]
        public void ElementPartlyOutsideTheParent_KeepsTheRect()
        {
            var rt = Make(canvas, -80, 560, 200, 100);
            ApplyAndAssertSameRect(rt);
        }

        [Test]
        public void ElementCompletelyOutsideTheParent_KeepsTheRect()
        {
            var rt = Make(canvas, 3000, -2000, 100, 50);
            ApplyAndAssertSameRect(rt);
        }

        [Test]
        public void ElementWithZeroSize_DoesNotThrow()
        {
            var rt = Make(canvas, 400, 250, 0, 0);
            Assert.DoesNotThrow(() => AutoAnchors.Apply(rt));
        }

        [Test]
        public void ZeroSizeParent_IsLeftAlone()
        {
            canvas.sizeDelta = Vector2.zero;
            var rt = Make(canvas, 0, 0, 100, 50);
            Assert.DoesNotThrow(() => AutoAnchors.Apply(rt));
            Assert.AreEqual(Vector2.zero, rt.anchorMax);
        }

        [Test]
        public void VeryLargeCoordinates_KeepTheRect()
        {
            canvas.sizeDelta = new Vector2(100000, 60000);
            var rt = Make(canvas, 99900, 59950, 100, 50);
            ApplyAndAssertSameRect(rt);
        }

        // --- Nesting ---

        [Test]
        public void DeeplyNestedHierarchy_UsesTheDirectParent()
        {
            Transform parent = canvas;
            for (int i = 0; i < 6; i++)
                parent = Make(parent, 10, 10, 800 - i * 50, 500 - i * 40, name: "Level" + i);

            var parentRt = (RectTransform)parent;
            var rt = Make(parentRt, parentRt.rect.width - 100, parentRt.rect.height - 50, 100, 50);
            ApplyAndAssertSameRect(rt);
            Assert.AreEqual(Vector2.one, rt.anchorMin);
        }

        // --- Other canvas types ---

        [Test]
        public void ScreenSpaceOverlayCanvas_KeepsTheRect()
        {
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var rt = Make(canvas, 10, 10, 100, 50);
            Assert.DoesNotThrow(() => AutoAnchors.Apply(rt));
        }

        [Test]
        public void ScreenSpaceCameraCanvas_KeepsTheRect()
        {
            var cameraObject = new GameObject("Cam", typeof(Camera));
            try
            {
                var c = canvasObject.GetComponent<Canvas>();
                c.renderMode = RenderMode.ScreenSpaceCamera;
                c.worldCamera = cameraObject.GetComponent<Camera>();
                c.planeDistance = 10f;
                var rt = Make(canvas, 10, 10, 100, 50);
                Assert.DoesNotThrow(() => ApplyAndAssertSameRect(rt));
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
            }
        }

        // --- Randomized ---

        [Test]
        public void Fuzz_RandomElements_NeverMove_AreStable_AndKeepAnchorsOrdered()
        {
            var random = new System.Random(12345);
            float R(float min, float max) => min + (float)random.NextDouble() * (max - min);

            for (int i = 0; i < 400; i++)
            {
                // A random parent, sometimes scaled or rotated, with a random child inside or around it.
                var parent = Make(canvas, R(0, 200), R(0, 100), R(200, 700), R(150, 450), new Vector2(R(0, 1), R(0, 1)), "Parent" + i);
                if (random.Next(3) == 0) parent.localScale = new Vector3(R(0.5f, 2.5f), R(0.5f, 2.5f), 1f);
                if (random.Next(4) == 0) parent.localRotation = Quaternion.Euler(0, 0, R(-80f, 80f));

                float w = R(1, parent.rect.width * 1.2f), h = R(1, parent.rect.height * 1.2f);
                // Often snap to a wall or corner so those branches get exercised.
                float x = random.Next(3) == 0 ? 0f : random.Next(3) == 0 ? parent.rect.width - w : R(-20, parent.rect.width);
                float y = random.Next(3) == 0 ? 0f : random.Next(3) == 0 ? parent.rect.height - h : R(-20, parent.rect.height);
                var rt = Make(parent, x, y, w, h, new Vector2(R(0, 1), R(0, 1)), "Child" + i);
                if (random.Next(5) == 0) rt.localScale = new Vector3(R(0.5f, 2f), R(0.5f, 2f), 1f);

                string context = $"case {i}";
                ApplyAndAssertSameRect(rt, context);

                Assert.LessOrEqual(rt.anchorMin.x, rt.anchorMax.x + 1e-5f, context + " anchors out of order (x)");
                Assert.LessOrEqual(rt.anchorMin.y, rt.anchorMax.y + 1e-5f, context + " anchors out of order (y)");
                Assert.IsFalse(float.IsNaN(rt.anchorMin.x) || float.IsNaN(rt.anchorMax.y), context + " NaN anchors");

                // Applying again changes nothing.
                Vector2 min = rt.anchorMin, max = rt.anchorMax;
                ApplyAndAssertSameRect(rt, context + " (second apply)");
                Assert.AreEqual(min.x, rt.anchorMin.x, 1e-4f, context + " not idempotent");
                Assert.AreEqual(min.y, rt.anchorMin.y, 1e-4f, context + " not idempotent");
                Assert.AreEqual(max.x, rt.anchorMax.x, 1e-4f, context + " not idempotent");
                Assert.AreEqual(max.y, rt.anchorMax.y, 1e-4f, context + " not idempotent");
            }
        }
    }
}
