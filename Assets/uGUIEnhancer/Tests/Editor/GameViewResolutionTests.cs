using NUnit.Framework;
using UnityEngine;

namespace UGUIEnhancerTests
{
    /// <summary>
    /// Edit Mode tests for the resolution tools. They change the Game View size, so the original
    /// size, orientation filter and base resolution are restored afterwards.
    /// </summary>
    public class GameViewResolutionTests
    {
        Vector2Int originalSize;
        Vector2Int originalBase;
        GameViewResolution.OrientationFilter originalFilter;

        [SetUp]
        public void SetUp()
        {
            Assume.That(GameViewResolution.Available, "Game View internals are not available in this Unity version");
            originalSize = GameViewResolution.Current;
            originalBase = GameViewResolution.Base;
            originalFilter = GameViewResolution.Orientation;
        }

        [TearDown]
        public void TearDown()
        {
            if (!GameViewResolution.Available)
                return;
            GameViewResolution.Orientation = originalFilter;
            GameViewResolution.Set(originalSize.x, originalSize.y);
            if (originalBase.x > 0)
                typeof(GameViewResolution).GetProperty("Base").SetValue(null, originalBase);
        }

        [Test]
        public void Set_ChangesTheGameViewSize()
        {
            GameViewResolution.Set(1280, 720);
            Assert.AreEqual(new Vector2Int(1280, 720), GameViewResolution.Current);
            GameViewResolution.Set(1080, 1920);
            Assert.AreEqual(new Vector2Int(1080, 1920), GameViewResolution.Current);
        }

        [Test]
        public void Set_AddsMissingResolutionsToTheGameViewList()
        {
            GameViewResolution.Set(1234, 777);
            Assert.IsTrue(GameViewResolution.GetPresets().Exists(p => p.width == 1234 && p.height == 777));
        }

        [Test]
        public void Swap_ExchangesWidthAndHeight()
        {
            GameViewResolution.Set(1920, 1080);
            GameViewResolution.Swap();
            Assert.AreEqual(new Vector2Int(1080, 1920), GameViewResolution.Current);
        }

        [Test]
        public void Describe_ShowsTheNameInBrackets_WhenTheListHasOne()
        {
            GameViewResolution.Set(1920, 1080);
            StringAssert.StartsWith("1920 x 1080", GameViewResolution.Describe(new Vector2Int(1920, 1080)));
            Assert.AreEqual("1 x 2", GameViewResolution.Describe(new Vector2Int(1, 2)), "unknown sizes have no brackets");
        }

        [Test]
        public void LandscapeFilter_NextAndPrevious_NeverLandOnPortrait()
        {
            GameViewResolution.Orientation = GameViewResolution.OrientationFilter.Landscape;
            GameViewResolution.Set(1920, 1080);
            for (int i = 0; i < 20; i++)
            {
                GameViewResolution.Cycle(i % 3 == 0 ? -1 : 1);
                var c = GameViewResolution.Current;
                Assert.GreaterOrEqual(c.x, c.y, $"landed on portrait {c.x}x{c.y}");
            }
        }

        [Test]
        public void PortraitFilter_NextAndPrevious_NeverLandOnLandscape()
        {
            GameViewResolution.Orientation = GameViewResolution.OrientationFilter.Portrait;
            GameViewResolution.Set(1080, 1920);
            for (int i = 0; i < 20; i++)
            {
                GameViewResolution.Cycle(i % 3 == 0 ? -1 : 1);
                var c = GameViewResolution.Current;
                Assert.GreaterOrEqual(c.y, c.x, $"landed on landscape {c.x}x{c.y}");
            }
        }

        [Test]
        public void Rotate_KeepsTheFilterInStepWithTheNewOrientation()
        {
            GameViewResolution.Orientation = GameViewResolution.OrientationFilter.Landscape;
            GameViewResolution.Set(1920, 1080);
            GameViewResolution.Swap();
            Assert.AreEqual(GameViewResolution.OrientationFilter.Portrait, GameViewResolution.Orientation);
        }

        [Test]
        public void GoToLast_AlternatesBetweenTheTwoLastResolutions()
        {
            GameViewResolution.Set(1920, 1080);
            GameViewResolution.Set(1280, 720);
            GameViewResolution.GoToLast();
            Assert.AreEqual(new Vector2Int(1920, 1080), GameViewResolution.Current);
            GameViewResolution.GoToLast();
            Assert.AreEqual(new Vector2Int(1280, 720), GameViewResolution.Current);
        }

        [Test]
        public void SetCurrentAsBase_ThenGoToBase_ReturnsToIt()
        {
            GameViewResolution.Set(1366, 768);
            GameViewResolution.SetCurrentAsBase();
            GameViewResolution.Set(1080, 1920);
            GameViewResolution.GoToBase();
            Assert.AreEqual(new Vector2Int(1366, 768), GameViewResolution.Current);
        }
    }
}
