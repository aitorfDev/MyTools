using UnityEngine;

/// <summary>
/// Stops uGUI Enhancer from changing this RectTransform's anchors automatically.
/// Added/removed from the RectTransform context menu (uGUI Enhancer) or by hand.
/// Does nothing at runtime.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
[AddComponentMenu("UI/uGUI Enhancer/No Auto Anchors")]
public class NoAutoAnchors : MonoBehaviour
{
}
