using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UGUIEnhancerTests
{
    /// <summary>
    /// The real path the tool uses while you work: an Undo-recorded change is detected, queued,
    /// flushed, applied as one undo step, and reported once. These tests drive that path with
    /// Undo.RecordObject + FlushUndoRecordObjects, which is what the Scene View tools do.
    /// </summary>
    public class PipelineTests
    {
        GameObject canvasObject;
        RectTransform canvas;
        bool wasEnabled;

        [SetUp]
        public void SetUp()
        {
            canvasObject = new GameObject("TestCanvas", typeof(Canvas));
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            canvas = (RectTransform)canvasObject.transform;
            canvas.sizeDelta = new Vector2(1000, 600);

            wasEnabled = AutoAnchors.Enabled;
            AutoAnchors.Enabled = true;
            UGUIEnhancerSettings.ResetToDefaults();
            AutoAnchors.manual.Clear();
            AutoAnchors.pending.Clear();
            Undo.IncrementCurrentGroup();
        }

        [TearDown]
        public void TearDown()
        {
            AutoAnchors.Enabled = wasEnabled;
            Object.DestroyImmediate(canvasObject);
            UGUIEnhancerSettings.ResetToDefaults();
            AutoAnchors.manual.Clear();
            AutoAnchors.pending.Clear();
        }

        RectTransform Make(string name, float x, float y, float w = 100, float h = 50)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(canvas, false);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x + w * 0.5f, y + h * 0.5f);
            return rt;
        }

        // Moves an element the way the Rect Tool does: recorded in Undo, then flushed.
        static void UserMoves(RectTransform rt, Vector2 anchoredPosition)
        {
            Undo.RecordObject(rt, "Move");
            rt.anchoredPosition = anchoredPosition;
            Undo.FlushUndoRecordObjects();
        }

        // --- Detection and queueing ---

        [Test]
        public void MovingAnElement_QueuesIt()
        {
            var rt = Make("A", 400, 250);
            UserMoves(rt, new Vector2(500, 300));
            Assert.IsTrue(AutoAnchors.pending.Contains(rt));
        }

        [Test]
        public void ResizingAnElement_QueuesIt()
        {
            var rt = Make("A", 400, 250);
            Undo.RecordObject(rt, "Resize");
            rt.sizeDelta = new Vector2(200, 80);
            Undo.FlushUndoRecordObjects();
            Assert.IsTrue(AutoAnchors.pending.Contains(rt));
        }

        [Test]
        public void ScalingAnElement_QueuesIt()
        {
            var rt = Make("A", 400, 250);
            Undo.RecordObject(rt, "Scale");
            rt.localScale = new Vector3(2, 2, 1);
            Undo.FlushUndoRecordObjects();
            Assert.IsTrue(AutoAnchors.pending.Contains(rt));
        }

        [Test]
        public void WhenDisabled_NothingIsQueued()
        {
            AutoAnchors.Enabled = false;
            var rt = Make("A", 400, 250);
            UserMoves(rt, new Vector2(500, 300));
            Assert.IsFalse(AutoAnchors.pending.Contains(rt));
        }

        [Test]
        public void ChangesToOtherComponents_AreIgnored()
        {
            var go = new GameObject("NotUI");
            var other = new GameObject("Other");
            Undo.RecordObject(other.transform, "Move");
            other.transform.position = Vector3.one;
            Undo.FlushUndoRecordObjects();
            Assert.AreEqual(0, AutoAnchors.pending.Count);
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(other);
        }

        // --- Flush ---

        [Test]
        public void Flush_AppliesTheAnchors_WithoutMovingTheElement()
        {
            var rt = Make("A", 900, 550);
            UserMoves(rt, rt.anchoredPosition + new Vector2(1f, 1f)); // 1 unit off the corner: still within the tolerance
            Vector3[] before = new Vector3[4];
            rt.GetWorldCorners(before); // where it is right before the tool runs

            AutoAnchors.Flush();

            Assert.AreEqual(Vector2.one, rt.anchorMin);
            Assert.AreEqual(Vector2.one, rt.anchorMax);
            Vector3[] after = new Vector3[4];
            rt.GetWorldCorners(after);
            for (int i = 0; i < 4; i++)
                Assert.Less((before[i] - after[i]).magnitude, 0.01f);

            // Our own change may queue the element once more; flushing again must be a harmless no-op.
            Vector2 min = rt.anchorMin, max = rt.anchorMax;
            int shown = UGUIEnhancerNotify.ShownCount;
            AutoAnchors.Flush();
            Assert.AreEqual(min, rt.anchorMin);
            Assert.AreEqual(max, rt.anchorMax);
            Assert.AreEqual(shown, UGUIEnhancerNotify.ShownCount, "no second notice");
            Assert.AreEqual(0, AutoAnchors.pending.Count, "and then the queue stays empty");
        }

        [Test]
        public void Flush_WithNothingQueued_DoesNothing()
        {
            int shown = UGUIEnhancerNotify.ShownCount;
            Assert.DoesNotThrow(AutoAnchors.Flush);
            Assert.AreEqual(shown, UGUIEnhancerNotify.ShownCount);
        }

        [Test]
        public void Flush_SkipsElementsDestroyedMeanwhile()
        {
            var rt = Make("A", 400, 250);
            var alive = Make("B", 900, 550);
            AutoAnchors.pending.Add(rt);
            AutoAnchors.pending.Add(alive);
            Object.DestroyImmediate(rt.gameObject);

            Assert.DoesNotThrow(AutoAnchors.Flush);
            Assert.AreEqual(Vector2.one, alive.anchorMin, "the others are still processed");
        }

        // --- Undo ---

        [Test]
        public void OneUndo_RevertsTheWholeBatch()
        {
            var a = Make("A", 900, 550);
            var b = Make("B", 0, 0);
            var c = Make("C", 400, 250);
            AutoAnchors.pending.Add(a);
            AutoAnchors.pending.Add(b);
            AutoAnchors.pending.Add(c);
            AutoAnchors.Flush();
            Assert.AreEqual(Vector2.one, a.anchorMin);

            Undo.PerformUndo();

            foreach (var rt in new[] { a, b, c })
            {
                Assert.AreEqual(Vector2.zero, rt.anchorMin, rt.name + " anchorMin");
                Assert.AreEqual(Vector2.zero, rt.anchorMax, rt.name + " anchorMax");
            }
        }

        [Test]
        public void UndoThenRedo_ReappliesTheAnchors()
        {
            var rt = Make("A", 900, 550);
            AutoAnchors.pending.Add(rt);
            AutoAnchors.Flush();
            Undo.PerformUndo();
            Assert.AreEqual(Vector2.zero, rt.anchorMin);
            Undo.PerformRedo();
            Assert.AreEqual(Vector2.one, rt.anchorMin);
        }

        // --- Our own changes versus yours ---

        [Test]
        public void OurOwnAnchorChange_IsNotTakenForAManualOne()
        {
            var rt = Make("A", 900, 550);
            AutoAnchors.pending.Add(rt);
            AutoAnchors.Flush();
            Undo.FlushUndoRecordObjects(); // the flush of our own recording reaches the detector here

            Assert.IsFalse(AutoAnchors.manual.Contains(rt));
        }

        [Test]
        public void ChangingTheAnchorsByHand_MarksTheElementAsManual()
        {
            var rt = Make("A", 400, 250);
            Undo.RecordObject(rt, "Anchors");
            rt.anchorMax = new Vector2(0.7f, 0.7f);
            Undo.FlushUndoRecordObjects();

            Assert.IsTrue(AutoAnchors.manual.Contains(rt));
        }

        [Test]
        public void ManualElement_KeepsItsAnchors_WhenMovedLater()
        {
            var rt = Make("A", 400, 250);
            Undo.RecordObject(rt, "Anchors");
            rt.anchorMax = new Vector2(0.7f, 0.7f);
            rt.anchorMin = new Vector2(0.2f, 0.2f);
            rt.offsetMin = new Vector2(10, 10);
            rt.offsetMax = new Vector2(-10, -10);
            Undo.FlushUndoRecordObjects();
            Assert.IsTrue(AutoAnchors.manual.Contains(rt));

            Vector2 min = rt.anchorMin, max = rt.anchorMax;
            Undo.RecordObject(rt, "Move");
            rt.anchoredPosition += new Vector2(5, 5);
            Undo.FlushUndoRecordObjects();
            AutoAnchors.Flush();

            Assert.AreEqual(min, rt.anchorMin);
            Assert.AreEqual(max, rt.anchorMax);
        }

        // --- Notices ---

        [Test]
        public void OneChange_ShowsOneNotice()
        {
            var rt = Make("A", 900, 550);
            int before = UGUIEnhancerNotify.ShownCount;
            AutoAnchors.pending.Add(rt);
            AutoAnchors.Flush();

            Assert.AreEqual(before + 1, UGUIEnhancerNotify.ShownCount);
            StringAssert.Contains("top-right corner", UGUIEnhancerNotify.LastText);
        }

        [Test]
        public void TheSameNoticeForTheSameObject_IsNotRepeated()
        {
            var rt = Make("A", 300, 200);
            AutoAnchors.pending.Add(rt);
            AutoAnchors.Flush();
            int afterFirst = UGUIEnhancerNotify.ShownCount;

            // Move it somewhere else that is still "around the object": same description, same object.
            UserMoves(rt, new Vector2(400, 300));
            AutoAnchors.Flush();

            Assert.AreEqual(afterFirst, UGUIEnhancerNotify.ShownCount);
        }

        [Test]
        public void TheSameNoticeForAnotherObject_IsShown()
        {
            var a = Make("A", 300, 200);
            var b = Make("B", 500, 300);
            AutoAnchors.pending.Add(a);
            AutoAnchors.Flush();
            int afterFirst = UGUIEnhancerNotify.ShownCount;

            AutoAnchors.pending.Add(b);
            AutoAnchors.Flush();

            Assert.AreEqual(afterFirst + 1, UGUIEnhancerNotify.ShownCount);
        }

        [Test]
        public void SeveralObjectsInOneBatch_ShowASingleSummaryNotice()
        {
            var a = Make("A", 300, 200);
            var b = Make("B", 500, 300);
            var c = Make("C", 700, 100);
            int before = UGUIEnhancerNotify.ShownCount;
            AutoAnchors.pending.Add(a);
            AutoAnchors.pending.Add(b);
            AutoAnchors.pending.Add(c);
            AutoAnchors.Flush();

            Assert.AreEqual(before + 1, UGUIEnhancerNotify.ShownCount);
            StringAssert.Contains("3 objects", UGUIEnhancerNotify.LastText);
        }

        [Test]
        public void OffsetOnlyChange_ShowsNoNotice()
        {
            var rt = Make("A", 900, 550);
            AutoAnchors.pending.Add(rt);
            AutoAnchors.Flush(); // now anchored to the top-right corner

            // Resize it keeping the corner: only the offsets change, the anchors stay.
            Undo.RecordObject(rt, "Resize");
            rt.sizeDelta = new Vector2(60, 30);
            rt.anchoredPosition = new Vector2(-30, -15); // keeps the top-right edges on the walls
            Undo.FlushUndoRecordObjects();
            int before = UGUIEnhancerNotify.ShownCount;
            AutoAnchors.Flush();

            Assert.AreEqual(before, UGUIEnhancerNotify.ShownCount);
        }

        // --- Reset to auto ---

        [Test]
        public void ResetToAuto_AlsoPersistsTheChange_AndShowsTheNotice()
        {
            var rt = Make("A", 900, 550);
            AutoAnchors.manual.Add(rt);
            AutoAnchors.ResetToAuto(rt);
            Assert.AreEqual(Vector2.one, rt.anchorMin);
            Assert.IsFalse(AutoAnchors.manual.Contains(rt));
        }
    }
}
