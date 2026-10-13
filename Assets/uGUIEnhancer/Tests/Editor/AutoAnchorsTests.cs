using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace UGUIEnhancerTests
{
    /// <summary>
    /// Edit Mode tests for the Auto Anchors rules. Every test builds a 1000x600 canvas and checks the
    /// anchors the tool picks for a 100x50 element, and that the element never moves or changes size.
    /// </summary>
    public class AutoAnchorsTests
    {
        const float CanvasW = 1000f, CanvasH = 600f;
        const float W = 100f, H = 50f;

        GameObject canvasObject;
        RectTransform canvas;

        [SetUp]
        public void SetUp()
        {
            canvasObject = new GameObject("TestCanvas", typeof(Canvas));
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            canvas = (RectTransform)canvasObject.transform;
            canvas.sizeDelta = new Vector2(CanvasW, CanvasH);

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

        // Creates an element with its pivot centered and the rect at (x, y) measured from the canvas bottom-left.
        RectTransform Make(float x, float y, float width = W, float height = H, string name = "Element")
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(canvas, false);
            rt.anchorMin = rt.anchorMax = Vector2.zero; // anchored to the bottom-left corner
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(x + width * 0.5f, y + height * 0.5f);
            return rt;
        }

        static Rect WorldRect(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            return new Rect(c[0].x, c[0].y, c[2].x - c[0].x, c[2].y - c[0].y);
        }

        static void AssertSame(Rect before, Rect after)
        {
            Assert.AreEqual(before.xMin, after.xMin, 0.01f, "xMin moved");
            Assert.AreEqual(before.yMin, after.yMin, 0.01f, "yMin moved");
            Assert.AreEqual(before.width, after.width, 0.01f, "width changed");
            Assert.AreEqual(before.height, after.height, 0.01f, "height changed");
        }

        static void AssertAnchors(RectTransform rt, Vector2 min, Vector2 max)
        {
            Assert.AreEqual(min.x, rt.anchorMin.x, 0.001f, "anchorMin.x");
            Assert.AreEqual(min.y, rt.anchorMin.y, 0.001f, "anchorMin.y");
            Assert.AreEqual(max.x, rt.anchorMax.x, 0.001f, "anchorMax.x");
            Assert.AreEqual(max.y, rt.anchorMax.y, 0.001f, "anchorMax.y");
        }

        // Applies the tool and verifies the element did not move.
        void Apply(RectTransform rt)
        {
            Rect before = WorldRect(rt);
            AutoAnchors.Apply(rt);
            AssertSame(before, WorldRect(rt));
        }

        // --- Around the object ---

        [Test]
        public void FloatingElement_AnchorsAroundTheObject()
        {
            var rt = Make(400, 250);
            Apply(rt);
            AssertAnchors(rt, new Vector2(0.4f, 250f / CanvasH), new Vector2(0.5f, 300f / CanvasH));
            Assert.That(rt.offsetMin.magnitude, Is.LessThan(0.01f));
            Assert.That(rt.offsetMax.magnitude, Is.LessThan(0.01f));
        }

        [Test]
        public void LoneWall_DoesNotCount_AnchorsAroundTheObject()
        {
            var rt = Make(0, 250); // touches the left wall only
            Apply(rt);
            AssertAnchors(rt, new Vector2(0f, 250f / CanvasH), new Vector2(0.1f, 300f / CanvasH));
        }

        // --- Corners and walls ---

        [TestCase(0f, 0f, 0f, 0f, TestName = "BottomLeft")]
        [TestCase(900f, 550f, 1f, 1f, TestName = "TopRight")]
        [TestCase(0f, 550f, 0f, 1f, TestName = "TopLeft")]
        [TestCase(900f, 0f, 1f, 0f, TestName = "BottomRight")]
        public void Corner_AnchorsToThatCorner(float x, float y, float ax, float ay)
        {
            var rt = Make(x, y);
            Apply(rt);
            AssertAnchors(rt, new Vector2(ax, ay), new Vector2(ax, ay));
        }

        [Test]
        public void FullWidthBarAtTheTop_StretchesHorizontally_AnchorsTop()
        {
            var rt = Make(0, 550, CanvasW, H);
            Apply(rt);
            AssertAnchors(rt, new Vector2(0f, 1f), new Vector2(1f, 1f));
        }

        [Test]
        public void FullWidthBarFloating_StretchesHorizontally_VerticalAroundTheObject()
        {
            var rt = Make(0, 250, CanvasW, H);
            Apply(rt);
            AssertAnchors(rt, new Vector2(0f, 250f / CanvasH), new Vector2(1f, 300f / CanvasH));
        }

        [Test]
        public void ElementTheSizeOfTheParent_StretchesBothWays()
        {
            var rt = Make(0, 0, CanvasW, CanvasH);
            Apply(rt);
            AssertAnchors(rt, Vector2.zero, Vector2.one);
        }

        // --- Settings ---

        [Test]
        public void WallTolerance_DecidesWhetherANearbyCornerCounts()
        {
            // 20 units away from the top-right corner.
            var near = Make(880, 530, name: "Near");

            UGUIEnhancerSettings.WallTolerance = 2f;
            Apply(near);
            Assert.AreNotEqual(1f, near.anchorMin.x, "With tolerance 2 a 20-unit margin must not count as touching");

            var near2 = Make(880, 530, name: "Near2");
            UGUIEnhancerSettings.WallTolerance = 25f;
            Apply(near2);
            AssertAnchors(near2, Vector2.one, Vector2.one);
        }

        [Test]
        public void Settings_ResetToDefaults()
        {
            UGUIEnhancerSettings.WallTolerance = 30f;
            UGUIEnhancerSettings.BreakAwayFraction = 0.5f;
            UGUIEnhancerSettings.ResetToDefaults();
            Assert.AreEqual(UGUIEnhancerSettings.DefaultWallTolerance, UGUIEnhancerSettings.WallTolerance, 0.0001f);
            Assert.AreEqual(UGUIEnhancerSettings.DefaultBreakAwayFraction, UGUIEnhancerSettings.BreakAwayFraction, 0.0001f);
        }

        // --- Centered and manual anchors ---

        [Test]
        public void CenteredAnchors_AreRespected_WhenMovedALittle()
        {
            var rt = Make(450, 275);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(50, 20); // a little off center
            Apply(rt);
            AssertAnchors(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        }

        [Test]
        public void CenteredAnchors_AreReleased_WhenMovedFarAway()
        {
            var rt = Make(450, 275);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(350, 0); // > 20% of the width from the center
            Apply(rt);
            Assert.That(Mathf.Abs(rt.anchorMin.x - 0.5f), Is.GreaterThan(0.001f), "x axis should have been recalculated");
            Assert.AreEqual(0.5f, rt.anchorMin.y, 0.001f, "y axis is still close to the center and stays centered");
        }

        [Test]
        public void ManualAnchors_AreNeverOverwritten_WhileCloseToThem()
        {
            var rt = Make(400, 250);
            rt.anchorMax = new Vector2(0.6f, 0.5f); // custom anchors, not a preset (max first: Unity keeps min <= max)
            rt.anchorMin = new Vector2(0.2f, 0.3f);
            rt.offsetMin = new Vector2(10f, 10f); // the element sits inside the anchor range
            rt.offsetMax = new Vector2(-10f, -10f);
            AutoAnchors.manual.Add(rt);

            Apply(rt);
            AssertAnchors(rt, new Vector2(0.2f, 0.3f), new Vector2(0.6f, 0.5f));
        }

        [Test]
        public void ResetToAuto_GivesTheElementBackToTheTool()
        {
            var rt = Make(400, 250);
            rt.anchorMax = new Vector2(0.6f, 0.5f); // max first: Unity keeps min <= max
            rt.anchorMin = new Vector2(0.2f, 0.3f);
            rt.offsetMin = new Vector2(10f, 10f); // the element sits inside the anchor range
            rt.offsetMax = new Vector2(-10f, -10f);
            AutoAnchors.manual.Add(rt);

            Rect before = WorldRect(rt);
            AutoAnchors.ResetToAuto(rt);

            Assert.IsFalse(AutoAnchors.manual.Contains(rt));
            AssertSame(before, WorldRect(rt));
            Assert.That(rt.offsetMin.magnitude, Is.LessThan(0.01f), "anchors should now hug the element");
        }

        // --- Things the tool must leave alone ---

        [Test]
        public void ExcludedElement_IsNotTouched()
        {
            var rt = Make(400, 250);
            rt.gameObject.AddComponent<NoAutoAnchors>();
            AutoAnchors.Apply(rt);
            AssertAnchors(rt, Vector2.zero, Vector2.zero);
        }

        [Test]
        public void ChildOfALayoutGroup_IsNotTouched()
        {
            canvasObject.AddComponent<VerticalLayoutGroup>();
            var rt = Make(400, 250);
            AutoAnchors.Apply(rt);
            AssertAnchors(rt, Vector2.zero, Vector2.zero);
        }

        [Test]
        public void RotatedElement_IsNotTouched()
        {
            var rt = Make(400, 250);
            rt.localRotation = Quaternion.Euler(0, 0, 30);
            AutoAnchors.Apply(rt);
            AssertAnchors(rt, Vector2.zero, Vector2.zero);
        }

        [Test]
        public void RootCanvas_IsNotTouched()
        {
            AutoAnchors.Apply(canvas); // no RectTransform parent
            Assert.Pass();
        }

        // --- Idempotence ---

        [Test]
        public void ApplyingTwice_GivesTheSameResult()
        {
            var rt = Make(900, 550);
            AutoAnchors.Apply(rt);
            Vector2 min = rt.anchorMin, max = rt.anchorMax, offMin = rt.offsetMin, offMax = rt.offsetMax;
            AutoAnchors.Apply(rt);
            Assert.AreEqual(min, rt.anchorMin);
            Assert.AreEqual(max, rt.anchorMax);
            Assert.AreEqual(offMin, rt.offsetMin);
            Assert.AreEqual(offMax, rt.offsetMax);
        }
    }
}
