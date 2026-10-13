using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace UGUIEnhancerTests
{
    /// <summary>The less obvious cases of the "Never auto adjust" list and the name patterns.</summary>
    public class ExclusionEdgeTests
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

        // A child sitting in the top-right corner of its parent: normally it would get corner anchors.
        static RectTransform Corner(Transform parent, string name = "Element")
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.sizeDelta = new Vector2(100, 50);
            rt.anchoredPosition = new Vector2(950, 575);
            return rt;
        }

        static RectTransform Sized(Transform parent, string name, params System.Type[] components)
        {
            var go = new GameObject(name, components);
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.sizeDelta = new Vector2(1000, 600);
            rt.anchoredPosition = new Vector2(500, 300);
            return rt;
        }

        static bool WasChanged(RectTransform rt) => rt.anchorMin != Vector2.zero || rt.anchorMax != Vector2.zero;

        static UGUIEnhancerExclusions.Rule Rule(string id) => System.Array.Find(UGUIEnhancerExclusions.Rules, r => r.id == id);

        // --- Widget parts ---

        [TestCase(typeof(Slider))]
        [TestCase(typeof(Scrollbar))]
        [TestCase(typeof(Toggle))]
        [TestCase(typeof(InputField))]
        [TestCase(typeof(Dropdown))]
        public void PartsOfWidgets_AreSkipped(System.Type widget)
        {
            var parent = Sized(canvas, "Widget", typeof(RectTransform), widget);
            var part = Corner(parent, "Part");
            AutoAnchors.Apply(part);
            Assert.IsFalse(WasChanged(part), widget.Name + " part should be skipped");
        }

        [Test]
        public void WidgetPart_ManyLevelsDeep_IsStillSkipped()
        {
            var slider = Sized(canvas, "Slider", typeof(RectTransform), typeof(Slider));
            var area = Sized(slider, "Handle Slide Area", typeof(RectTransform));
            var handle = Corner(area, "Handle");
            AutoAnchors.Apply(handle);
            Assert.IsFalse(WasChanged(handle));
        }

        [Test]
        public void WidgetRule_Off_LetsThePartsThrough()
        {
            UGUIEnhancerSettings.SetRuleEnabled(Rule("WidgetParts"), false);
            var slider = Sized(canvas, "Slider", typeof(RectTransform), typeof(Slider));
            var handle = Corner(slider, "Handle");
            AutoAnchors.Apply(handle);
            Assert.IsTrue(WasChanged(handle));
        }

        [Test]
        public void ButtonInsideAPlainPanel_IsNotMistakenForAWidgetPart()
        {
            var panel = Sized(canvas, "Panel", typeof(RectTransform), typeof(Image));
            var button = Corner(panel, "Button");
            AutoAnchors.Apply(button);
            Assert.IsTrue(WasChanged(button));
        }

        // --- Scroll views ---

        [Test]
        public void ScrollView_WithoutAViewport_DoesNotThrow()
        {
            var scroll = Sized(canvas, "Scroll", typeof(RectTransform), typeof(ScrollRect));
            var content = Corner(scroll, "Content");
            scroll.GetComponent<ScrollRect>().content = content;
            Assert.DoesNotThrow(() => AutoAnchors.Apply(content));
            Assert.IsFalse(WasChanged(content));
        }

        [Test]
        public void ScrollViewViewport_IsSkipped()
        {
            var scroll = Sized(canvas, "Scroll", typeof(RectTransform), typeof(ScrollRect));
            var viewport = Corner(scroll, "Viewport");
            scroll.GetComponent<ScrollRect>().viewport = viewport;
            AutoAnchors.Apply(viewport);
            Assert.IsFalse(WasChanged(viewport));
        }

        [Test]
        public void ScrollViewItself_IsStillHandled()
        {
            var scroll = Corner(canvas, "Scroll");
            scroll.gameObject.AddComponent<ScrollRect>();
            AutoAnchors.Apply(scroll);
            Assert.IsTrue(WasChanged(scroll));
        }

        // --- Fitters, masks, TextMeshPro ---

        [Test]
        public void AspectRatioFitter_IsSkipped()
        {
            var rt = Corner(canvas);
            rt.gameObject.AddComponent<AspectRatioFitter>();
            AutoAnchors.Apply(rt);
            Assert.IsFalse(WasChanged(rt));
        }

        [Test]
        public void Masks_AreSkipped_OnlyWhenTheRuleIsOn()
        {
            var plain = Corner(canvas, "Plain");
            plain.gameObject.AddComponent<RectMask2D>();
            AutoAnchors.Apply(plain);
            Assert.IsTrue(WasChanged(plain), "off by default");

            UGUIEnhancerSettings.SetRuleEnabled(Rule("Masks"), true);
            var masked = Corner(canvas, "Masked");
            masked.gameObject.AddComponent<RectMask2D>();
            AutoAnchors.Apply(masked);
            Assert.IsFalse(WasChanged(masked));
        }

        [Test]
        public void TextMeshPro_IsRecognizedAsText()
        {
            var tmpType = System.Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro");
            Assume.That(tmpType, Is.Not.Null, "TextMeshPro is not available");

            UGUIEnhancerSettings.SetRuleEnabled(Rule("Texts"), true);
            var rt = Corner(canvas);
            rt.gameObject.AddComponent(tmpType);
            AutoAnchors.Apply(rt);
            Assert.IsFalse(WasChanged(rt));
        }

        [Test]
        public void ElementWithSeveralMatchingRules_IsSkipped_AndWithNoneIsNot()
        {
            canvasObject.AddComponent<VerticalLayoutGroup>();
            UGUIEnhancerSettings.SetRuleEnabled(Rule("Images"), true);
            var rt = Corner(canvas);
            rt.gameObject.AddComponent<Image>();
            rt.gameObject.AddComponent<ContentSizeFitter>();
            Assert.IsTrue(UGUIEnhancerExclusions.IsExcluded(rt));

            UGUIEnhancerSettings.ResetToDefaults();
            canvasObject.GetComponent<VerticalLayoutGroup>().enabled = true;
            Object.DestroyImmediate(canvasObject.GetComponent<VerticalLayoutGroup>());
            Object.DestroyImmediate(rt.GetComponent<ContentSizeFitter>());
            Assert.IsFalse(UGUIEnhancerExclusions.IsExcluded(rt));
        }

        // --- Name patterns ---

        [TestCase("Button (3)", "Button (3)", true)]
        [TestCase("Button (3)", "Button (4)", false)]
        [TestCase("a.b", "a.b", true)]
        [TestCase("a.b", "axb", false)]
        [TestCase("[x]", "[x]", true)]
        [TestCase("a+b", "a+b", true)]
        [TestCase("a+b", "aab", false)]
        [TestCase("$cost", "$cost", true)]
        [TestCase("^start", "^start", true)]
        [TestCase("*", "Anything", true)]
        [TestCase("*", "", true)]
        [TestCase("*a*", "banana", true)]
        [TestCase("*a*", "xyz", false)]
        [TestCase("ABC", "abc", true)]
        [TestCase("Back*", "BACKGROUND", true)]
        [TestCase("Back*", "NotBack", false)]
        [TestCase("**", "double", true)]
        public void NamePattern_Matches(string pattern, string objectName, bool expected)
        {
            UGUIEnhancerSettings.ExcludedNames = pattern;
            Assert.AreEqual(expected, UGUIEnhancerExclusions.MatchesExcludedName(objectName));
        }

        [Test]
        public void NamePatterns_SeveralLines_AnyMatchCounts()
        {
            UGUIEnhancerSettings.ExcludedNames = "First\n\n  Second  \nThird*";
            Assert.IsTrue(UGUIEnhancerExclusions.MatchesExcludedName("First"));
            Assert.IsTrue(UGUIEnhancerExclusions.MatchesExcludedName("Second"), "lines are trimmed");
            Assert.IsTrue(UGUIEnhancerExclusions.MatchesExcludedName("Third and more"));
            Assert.IsFalse(UGUIEnhancerExclusions.MatchesExcludedName("Fourth"));
        }

        [Test]
        public void NamePatterns_WithWindowsLineEndings_Work()
        {
            UGUIEnhancerSettings.ExcludedNames = "One\r\nTwo\r\n";
            Assert.IsTrue(UGUIEnhancerExclusions.MatchesExcludedName("One"));
            Assert.IsTrue(UGUIEnhancerExclusions.MatchesExcludedName("Two"));
        }

        [Test]
        public void NamePatterns_VeryLongNames_AreFast()
        {
            UGUIEnhancerSettings.ExcludedNames = "*needle*\n" + new string('a', 200) + "*";
            string name = new string('x', 50000);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            bool result = UGUIEnhancerExclusions.MatchesExcludedName(name);
            watch.Stop();
            Assert.IsFalse(result);
            Assert.Less(watch.ElapsedMilliseconds, 1000);
        }
    }
}
