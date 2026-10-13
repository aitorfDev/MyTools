using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace UGUIEnhancerTests
{
    /// <summary>
    /// Tests for the "never change these GameObjects" categories and name patterns (Advanced settings).
    /// An element at the top-right corner would normally get corner anchors, so unchanged anchors
    /// mean the element was skipped.
    /// </summary>
    public class ExclusionTests
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

        // An element in the top-right corner of its parent, anchored to the parent's bottom-left.
        RectTransform MakeCornerElement(Transform parent, string name = "Element")
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.sizeDelta = new Vector2(100, 50);
            rt.anchoredPosition = new Vector2(950, 575);
            return rt;
        }

        static bool WasChanged(RectTransform rt) => rt.anchorMin != Vector2.zero || rt.anchorMax != Vector2.zero;

        static UGUIEnhancerExclusions.Rule Rule(string id) => System.Array.Find(UGUIEnhancerExclusions.Rules, r => r.id == id);

        // --- Defaults ---

        [Test]
        public void Defaults_AreAsDocumented()
        {
            foreach (string on in new[] { "LayoutGroupChildren", "SelfSizeControllers", "ScrollViewParts", "WidgetParts" })
                Assert.IsTrue(UGUIEnhancerSettings.IsRuleEnabled(Rule(on)), on + " should be on by default");
            foreach (string off in new[] { "Texts", "Images", "Buttons", "Masks" })
                Assert.IsFalse(UGUIEnhancerSettings.IsRuleEnabled(Rule(off)), off + " should be off by default");
        }

        [Test]
        public void NormalElement_IsChangedByDefault()
        {
            var rt = MakeCornerElement(canvas);
            AutoAnchors.Apply(rt);
            Assert.IsTrue(WasChanged(rt));
        }

        // --- Categories that are on by default ---

        [Test]
        public void LayoutGroupChild_IsSkipped()
        {
            canvasObject.AddComponent<HorizontalLayoutGroup>();
            var rt = MakeCornerElement(canvas);
            AutoAnchors.Apply(rt);
            Assert.IsFalse(WasChanged(rt));
        }

        [Test]
        public void LayoutGroupChild_IsChanged_WhenTheRuleIsTurnedOff()
        {
            UGUIEnhancerSettings.SetRuleEnabled(Rule("LayoutGroupChildren"), false);
            canvasObject.AddComponent<HorizontalLayoutGroup>();
            var rt = MakeCornerElement(canvas);
            AutoAnchors.Apply(rt);
            Assert.IsTrue(WasChanged(rt));
        }

        [Test]
        public void ElementWithContentSizeFitter_IsSkipped()
        {
            var rt = MakeCornerElement(canvas);
            rt.gameObject.AddComponent<ContentSizeFitter>();
            AutoAnchors.Apply(rt);
            Assert.IsFalse(WasChanged(rt));
        }

        [Test]
        public void ScrollViewContent_IsSkipped_ButItsChildrenAreNot()
        {
            var scrollObject = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect));
            var scrollRt = (RectTransform)scrollObject.transform;
            scrollRt.SetParent(canvas, false);
            scrollRt.sizeDelta = new Vector2(1000, 600);

            var content = MakeCornerElement(scrollRt, "Content");
            scrollObject.GetComponent<ScrollRect>().content = content;
            var item = MakeCornerElement(content, "Item");
            item.sizeDelta = new Vector2(10, 10); // keep it inside the content rect

            AutoAnchors.Apply(content);
            Assert.IsFalse(WasChanged(content), "Content keeps its anchors");
        }

        [Test]
        public void PartOfASlider_IsSkipped_ButTheSliderItselfIsNot()
        {
            var sliderObject = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
            var sliderRt = (RectTransform)sliderObject.transform;
            sliderRt.SetParent(canvas, false);
            sliderRt.anchorMin = sliderRt.anchorMax = Vector2.zero;
            sliderRt.sizeDelta = new Vector2(100, 50);
            sliderRt.anchoredPosition = new Vector2(950, 575);

            var handle = MakeCornerElement(sliderRt, "Handle");
            AutoAnchors.Apply(handle);
            Assert.IsFalse(WasChanged(handle), "a part of the slider is skipped");

            AutoAnchors.Apply(sliderRt);
            Assert.IsTrue(WasChanged(sliderRt), "the slider itself is still handled");
        }

        // --- Categories that are off by default ---

        [Test]
        public void Text_IsChangedByDefault_AndSkippedWhenTheRuleIsOn()
        {
            var rt = MakeCornerElement(canvas);
            rt.gameObject.AddComponent<Text>();
            AutoAnchors.Apply(rt);
            Assert.IsTrue(WasChanged(rt));

            var other = MakeCornerElement(canvas, "Other");
            other.gameObject.AddComponent<Text>();
            UGUIEnhancerSettings.SetRuleEnabled(Rule("Texts"), true);
            AutoAnchors.Apply(other);
            Assert.IsFalse(WasChanged(other));
        }

        [Test]
        public void Image_IsSkipped_WhenTheRuleIsOn()
        {
            UGUIEnhancerSettings.SetRuleEnabled(Rule("Images"), true);
            var rt = MakeCornerElement(canvas);
            rt.gameObject.AddComponent<Image>();
            AutoAnchors.Apply(rt);
            Assert.IsFalse(WasChanged(rt));
        }

        [Test]
        public void Button_IsSkipped_WhenTheRuleIsOn()
        {
            UGUIEnhancerSettings.SetRuleEnabled(Rule("Buttons"), true);
            var rt = MakeCornerElement(canvas);
            rt.gameObject.AddComponent<Image>();
            rt.gameObject.AddComponent<Button>();
            AutoAnchors.Apply(rt);
            Assert.IsFalse(WasChanged(rt));
        }

        // --- Names ---

        [Test]
        public void NamePatterns_SkipMatchingObjects()
        {
            UGUIEnhancerSettings.ExcludedNames = "Background*\n*_Label\nExact";

            foreach (string skipped in new[] { "Background", "BackgroundBlur", "background", "Title_Label", "Exact" })
            {
                var rt = MakeCornerElement(canvas, skipped);
                AutoAnchors.Apply(rt);
                Assert.IsFalse(WasChanged(rt), skipped + " should be skipped");
            }

            foreach (string handled in new[] { "MyBackground", "Label", "Exactly", "Other" })
            {
                var rt = MakeCornerElement(canvas, handled);
                AutoAnchors.Apply(rt);
                Assert.IsTrue(WasChanged(rt), handled + " should be handled");
            }
        }

        [Test]
        public void EmptyAndBlankLines_InNamePatterns_AreIgnored()
        {
            UGUIEnhancerSettings.ExcludedNames = "\n   \n\n";
            var rt = MakeCornerElement(canvas);
            AutoAnchors.Apply(rt);
            Assert.IsTrue(WasChanged(rt));
        }

        [Test]
        public void ResetToDefaults_RestoresRulesAndNames()
        {
            UGUIEnhancerSettings.SetRuleEnabled(Rule("Texts"), true);
            UGUIEnhancerSettings.SetRuleEnabled(Rule("LayoutGroupChildren"), false);
            UGUIEnhancerSettings.ExcludedNames = "Something";

            UGUIEnhancerSettings.ResetToDefaults();

            Assert.IsFalse(UGUIEnhancerSettings.IsRuleEnabled(Rule("Texts")));
            Assert.IsTrue(UGUIEnhancerSettings.IsRuleEnabled(Rule("LayoutGroupChildren")));
            Assert.AreEqual("", UGUIEnhancerSettings.ExcludedNames);
        }
    }
}
