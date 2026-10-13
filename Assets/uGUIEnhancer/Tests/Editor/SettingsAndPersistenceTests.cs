using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UGUIEnhancerTests
{
    /// <summary>Settings values and limits, and the manual-anchors list that survives script reloads.</summary>
    public class SettingsAndPersistenceTests
    {
        [SetUp]
        public void SetUp()
        {
            UGUIEnhancerSettings.ResetToDefaults();
            AutoAnchors.manual.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            UGUIEnhancerSettings.ResetToDefaults();
            AutoAnchors.manual.Clear();
            SessionState.EraseString("UGUIEnhancer.ManualAnchors");
        }

        // --- Settings ---

        [Test]
        public void Defaults_AreTheDocumentedOnes()
        {
            Assert.AreEqual(2f, UGUIEnhancerSettings.WallTolerance, 1e-5f);
            Assert.AreEqual(0.2f, UGUIEnhancerSettings.BreakAwayFraction, 1e-5f);
            Assert.AreEqual(0.4, UGUIEnhancerSettings.IdleDelay, 1e-5);
            Assert.AreEqual("", UGUIEnhancerSettings.ExcludedNames);
        }

        [Test]
        public void Values_AreStored()
        {
            UGUIEnhancerSettings.WallTolerance = 12.5f;
            UGUIEnhancerSettings.BreakAwayFraction = 0.35f;
            UGUIEnhancerSettings.IdleDelay = 1.25;
            Assert.AreEqual(12.5f, UGUIEnhancerSettings.WallTolerance, 1e-5f);
            Assert.AreEqual(0.35f, UGUIEnhancerSettings.BreakAwayFraction, 1e-5f);
            Assert.AreEqual(1.25, UGUIEnhancerSettings.IdleDelay, 1e-5);
        }

        [Test]
        public void WallTolerance_IsClamped()
        {
            UGUIEnhancerSettings.WallTolerance = -50f;
            Assert.AreEqual(0f, UGUIEnhancerSettings.WallTolerance);
            UGUIEnhancerSettings.WallTolerance = 99999f;
            Assert.AreEqual(200f, UGUIEnhancerSettings.WallTolerance);
        }

        [Test]
        public void BreakAwayFraction_IsClamped()
        {
            UGUIEnhancerSettings.BreakAwayFraction = 0f;
            Assert.AreEqual(0.02f, UGUIEnhancerSettings.BreakAwayFraction, 1e-5f);
            UGUIEnhancerSettings.BreakAwayFraction = 50f;
            Assert.AreEqual(1f, UGUIEnhancerSettings.BreakAwayFraction, 1e-5f);
        }

        [Test]
        public void IdleDelay_IsClamped()
        {
            UGUIEnhancerSettings.IdleDelay = -3;
            Assert.AreEqual(0.05, UGUIEnhancerSettings.IdleDelay, 1e-5);
            UGUIEnhancerSettings.IdleDelay = 999;
            Assert.AreEqual(5.0, UGUIEnhancerSettings.IdleDelay, 1e-5);
        }

        [Test]
        public void ExcludedNames_KeepLineBreaksAndNull()
        {
            UGUIEnhancerSettings.ExcludedNames = "A*\nB*\n*C";
            Assert.AreEqual("A*\nB*\n*C", UGUIEnhancerSettings.ExcludedNames);
            UGUIEnhancerSettings.ExcludedNames = null;
            Assert.AreEqual("", UGUIEnhancerSettings.ExcludedNames);
        }

        [Test]
        public void EveryRule_HasAnId_ALabel_AndAMatcher()
        {
            var ids = new System.Collections.Generic.HashSet<string>();
            foreach (var rule in UGUIEnhancerExclusions.Rules)
            {
                Assert.IsFalse(string.IsNullOrEmpty(rule.id));
                Assert.IsFalse(string.IsNullOrEmpty(rule.label), rule.id);
                Assert.IsFalse(string.IsNullOrEmpty(rule.tooltip), rule.id);
                Assert.IsNotNull(rule.matches, rule.id);
                Assert.IsTrue(ids.Add(rule.id), "duplicate rule id " + rule.id);
            }
        }

        [Test]
        public void Rules_AreStoredIndependently()
        {
            var rules = UGUIEnhancerExclusions.Rules;
            UGUIEnhancerSettings.SetRuleEnabled(rules[0], !rules[0].enabledByDefault);
            Assert.AreEqual(!rules[0].enabledByDefault, UGUIEnhancerSettings.IsRuleEnabled(rules[0]));
            for (int i = 1; i < rules.Length; i++)
                Assert.AreEqual(rules[i].enabledByDefault, UGUIEnhancerSettings.IsRuleEnabled(rules[i]), rules[i].id + " must be untouched");
        }

        // --- Manual anchors list ---

        [Test]
        public void SaveManual_WithNoObjects_StoresNothing()
        {
            AutoAnchors.SaveManual();
            Assert.AreEqual("", SessionState.GetString("UGUIEnhancer.ManualAnchors", "?"));
        }

        [Test]
        public void SaveManual_WithDestroyedObjects_DoesNotThrow()
        {
            var go = new GameObject("Temp", typeof(RectTransform));
            AutoAnchors.manual.Add((RectTransform)go.transform);
            Object.DestroyImmediate(go);
            Assert.DoesNotThrow(AutoAnchors.SaveManual);
        }

        [TestCase("")]
        [TestCase(";")]
        [TestCase("garbage")]
        [TestCase("GlobalObjectId_V1-1-00000000000000000000000000000000-0-0")]
        [TestCase("a;b;c;;d")]
        public void LoadManual_WithBrokenData_DoesNotThrowAndLoadsNothing(string stored)
        {
            SessionState.SetString("UGUIEnhancer.ManualAnchors", stored);
            Assert.DoesNotThrow(AutoAnchors.LoadManual);
            Assert.AreEqual(0, AutoAnchors.manual.Count);
        }

        [Test]
        public void SaveThenLoad_WithUnsavedSceneObjects_DoesNotThrow()
        {
            var go = new GameObject("Unsaved", typeof(RectTransform));
            try
            {
                AutoAnchors.manual.Add((RectTransform)go.transform);
                AutoAnchors.SaveManual();
                AutoAnchors.manual.Clear();
                Assert.DoesNotThrow(AutoAnchors.LoadManual);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // --- Auto Anchors on/off ---

        [Test]
        public void Enabled_IsStored()
        {
            bool original = AutoAnchors.Enabled;
            try
            {
                AutoAnchors.Enabled = false;
                Assert.IsFalse(AutoAnchors.Enabled);
                AutoAnchors.Enabled = true;
                Assert.IsTrue(AutoAnchors.Enabled);
            }
            finally
            {
                AutoAnchors.Enabled = original;
            }
        }
    }
}
