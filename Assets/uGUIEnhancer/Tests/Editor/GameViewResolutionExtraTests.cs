using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UGUIEnhancerTests
{
    /// <summary>
    /// More resolution-tool cases: limits, idempotence, orientation corner cases and the compatibility check.
    /// The Game View size, orientation filter and base resolution are restored afterwards.
    /// </summary>
    public class GameViewResolutionExtraTests
    {
        Vector2Int originalSize;
        string originalBase;
        GameViewResolution.OrientationFilter originalFilter;

        static string BaseKey => "UGUIEnhancer.Base." + Application.dataPath;

        [SetUp]
        public void SetUp()
        {
            Assume.That(GameViewResolution.Available, "Game View internals are not available in this Unity version");
            originalSize = GameViewResolution.Current;
            originalBase = EditorPrefs.GetString(BaseKey, "");
            originalFilter = GameViewResolution.Orientation;
            GameViewResolution.Orientation = GameViewResolution.OrientationFilter.All;
        }

        [TearDown]
        public void TearDown()
        {
            if (!GameViewResolution.Available)
                return;
            GameViewResolution.Orientation = originalFilter;
            GameViewResolution.Set(originalSize.x, originalSize.y);
            if (originalBase == "") EditorPrefs.DeleteKey(BaseKey);
            else EditorPrefs.SetString(BaseKey, originalBase);
        }

        // --- Compatibility check ---

        [Test]
        public void Available_IsTrue_AndHasNoReason()
        {
            Assert.IsTrue(GameViewResolution.Available);
            Assert.IsNull(GameViewResolution.UnavailableReason);
        }

        // --- Set ---

        [TestCase(0, 100)]
        [TestCase(100, 0)]
        [TestCase(-5, 5)]
        [TestCase(5, -5)]
        [TestCase(0, 0)]
        public void Set_WithInvalidSizes_ChangesNothing(int w, int h)
        {
            GameViewResolution.Set(1280, 720);
            GameViewResolution.Set(w, h);
            Assert.AreEqual(new Vector2Int(1280, 720), GameViewResolution.Current);
        }

        [Test]
        public void Set_ToTheSameSize_DoesNotChangeLast()
        {
            GameViewResolution.Set(1920, 1080);
            GameViewResolution.Set(1280, 720);
            var lastBefore = GameViewResolution.Last;
            GameViewResolution.Set(1280, 720);
            Assert.AreEqual(lastBefore, GameViewResolution.Last);
        }

        [Test]
        public void Set_TheSameNewSizeTwice_AddsItToTheListOnce()
        {
            int before = GameViewResolution.GetPresets().Count;
            GameViewResolution.Set(1357, 791);
            GameViewResolution.Set(1920, 1080);
            GameViewResolution.Set(1357, 791);
            int occurrences = GameViewResolution.GetPresets().FindAll(p => p.width == 1357 && p.height == 791).Count;
            Assert.AreEqual(1, occurrences);
            Assert.GreaterOrEqual(GameViewResolution.GetPresets().Count, before);
        }

        [Test]
        public void Set_ShowsANotice()
        {
            int before = UGUIEnhancerNotify.ShownCount;
            GameViewResolution.Set(1366, 768);
            Assert.AreEqual(before + 1, UGUIEnhancerNotify.ShownCount);
            StringAssert.Contains("1366", UGUIEnhancerNotify.LastText);
        }

        // --- Presets ---

        [Test]
        public void AddingTheDefaults_IsIdempotent()
        {
            GameViewResolution.TryAddDefaults();
            int count = GameViewResolution.GetPresets().Count;
            GameViewResolution.TryAddDefaults();
            GameViewResolution.TryAddDefaults();
            Assert.AreEqual(count, GameViewResolution.GetPresets().Count);
        }

        [Test]
        public void AfterAddingTheDefaults_TheCommonResolutionsAreInTheList()
        {
            GameViewResolution.TryAddDefaults();
            var presets = GameViewResolution.GetPresets();
            foreach (var size in new[] { new Vector2Int(1920, 1080), new Vector2Int(1280, 720), new Vector2Int(2560, 1440), new Vector2Int(1080, 1920) })
                Assert.IsTrue(presets.Exists(p => p.width == size.x && p.height == size.y), size.ToString());
        }

        [Test]
        public void Presets_AreAllFixedResolutions_WithPositiveSizes()
        {
            foreach (var p in GameViewResolution.GetPresets())
            {
                Assert.Greater(p.width, 0, p.Label);
                Assert.Greater(p.height, 0, p.Label);
            }
        }

        // --- Cycling ---

        [Test]
        public void Cycling_ForwardOnce_Per_Preset_ComesBackToTheStart()
        {
            GameViewResolution.Set(1920, 1080);
            int count = GameViewResolution.GetPresets().Count;
            for (int i = 0; i < count; i++)
                GameViewResolution.Cycle(1);
            Assert.AreEqual(new Vector2Int(1920, 1080), GameViewResolution.Current);
        }

        [Test]
        public void Cycling_BackwardsIsTheReverseOfForwards()
        {
            GameViewResolution.Set(1920, 1080);
            GameViewResolution.Cycle(1);
            GameViewResolution.Cycle(1);
            GameViewResolution.Cycle(-1);
            GameViewResolution.Cycle(-1);
            Assert.AreEqual(new Vector2Int(1920, 1080), GameViewResolution.Current);
        }

        [Test]
        public void Cycling_FromASizeThatIsNotInTheFilteredList_StartsAtAnEnd()
        {
            GameViewResolution.Orientation = GameViewResolution.OrientationFilter.Portrait;
            GameViewResolution.Set(1920, 1080); // landscape: not in the portrait list
            Assert.DoesNotThrow(() => GameViewResolution.Cycle(1));
            var c = GameViewResolution.Current;
            Assert.GreaterOrEqual(c.y, c.x);
        }

        [Test]
        public void SquareResolutions_CountForBothOrientationFilters()
        {
            GameViewResolution.Set(1000, 1000);
            foreach (var filter in new[] { GameViewResolution.OrientationFilter.Landscape, GameViewResolution.OrientationFilter.Portrait })
            {
                GameViewResolution.Orientation = filter;
                GameViewResolution.Set(1000, 1000);
                var visited = new System.Collections.Generic.HashSet<Vector2Int>();
                for (int i = 0; i < 40; i++)
                {
                    GameViewResolution.Cycle(1);
                    visited.Add(GameViewResolution.Current);
                }
                Assert.IsTrue(visited.Contains(new Vector2Int(1000, 1000)), filter + " should include the square size");
            }
        }

        [Test]
        public void Swap_OfASquare_StaysTheSame()
        {
            GameViewResolution.Set(1000, 1000);
            GameViewResolution.Swap();
            Assert.AreEqual(new Vector2Int(1000, 1000), GameViewResolution.Current);
        }

        [Test]
        public void Swap_Twice_IsTheIdentity()
        {
            GameViewResolution.Set(1366, 768);
            GameViewResolution.Swap();
            GameViewResolution.Swap();
            Assert.AreEqual(new Vector2Int(1366, 768), GameViewResolution.Current);
        }

        [Test]
        public void OrientationFilter_CyclesThroughAllThreeValues()
        {
            var seen = new System.Collections.Generic.HashSet<GameViewResolution.OrientationFilter>();
            for (int i = 0; i < 3; i++)
            {
                seen.Add(GameViewResolution.Orientation);
                GameViewResolution.Orientation = (GameViewResolution.OrientationFilter)(((int)GameViewResolution.Orientation + 1) % 3);
            }
            Assert.AreEqual(3, seen.Count);
        }

        // --- Base and Last ---

        [Test]
        public void Base_IsStored_AndGoToBase_Works()
        {
            GameViewResolution.Set(1280, 720);
            GameViewResolution.SetCurrentAsBase();
            Assert.AreEqual(new Vector2Int(1280, 720), GameViewResolution.Base);
            GameViewResolution.Set(1080, 1920);
            GameViewResolution.GoToBase();
            Assert.AreEqual(new Vector2Int(1280, 720), GameViewResolution.Current);
        }

        [Test]
        public void GoToBase_WithNoBase_ShowsANoticeAndChangesNothing()
        {
            EditorPrefs.DeleteKey(BaseKey);
            GameViewResolution.Set(1366, 768);
            int before = UGUIEnhancerNotify.ShownCount;
            GameViewResolution.GoToBase();
            Assert.AreEqual(new Vector2Int(1366, 768), GameViewResolution.Current);
            Assert.AreEqual(before + 1, UGUIEnhancerNotify.ShownCount);
            StringAssert.Contains("No base", UGUIEnhancerNotify.LastText);
        }

        [Test]
        public void Base_Garbage_IsTreatedAsNotSet()
        {
            EditorPrefs.SetString(BaseKey, "not,a number");
            Assert.AreEqual(Vector2Int.zero, GameViewResolution.Base);
            EditorPrefs.SetString(BaseKey, "1,2,3");
            Assert.AreEqual(Vector2Int.zero, GameViewResolution.Base);
        }

        // --- Describe ---

        [Test]
        public void Describe_WithPlaceholderName_HasNoBrackets()
        {
            GameViewResolution.Set(1234, 777); // added by the tool with the placeholder name "UI"
            Assert.AreEqual("1234 x 777", GameViewResolution.Describe(new Vector2Int(1234, 777)));
        }

        [Test]
        public void Describe_NamedSize_UsesTheNameInBrackets()
        {
            GameViewResolution.TryAddDefaults();
            string text = GameViewResolution.Describe(new Vector2Int(2560, 1440));
            StringAssert.StartsWith("2560 x 1440", text);
            StringAssert.Contains("[", text);
        }
    }
}
