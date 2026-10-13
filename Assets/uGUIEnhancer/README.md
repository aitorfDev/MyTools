# uGUI Enhancer

Editor tools that make working with **uGUI** (Canvas + RectTransform) faster.

- **Auto Anchors** – when you move or scale a UI element, its anchors are set for you, without the element moving or changing size.
- **Resolution tools** – change the Game View resolution from a Scene View overlay or a window: presets, rotate, next/previous, last and base resolution.

Requires Unity 6 (developed on 6000.3) and the uGUI package (`com.unity.ugui`). It does **not** work with UI Toolkit.

---

## Quick start

1. Open a scene with a Canvas and select a Button (or any UI element inside a Canvas).
2. The **uGUI Enhancer** overlay appears in the Scene View (bottom right).
3. Move or scale the element with the Rect Tool. Its anchors are updated when you release the mouse, and a notice tells you what was chosen (for example *Anchors: top-right corner*).

Toggle Auto Anchors with the checkbox in the overlay, in the window, or with `Ctrl+Alt+T`.

## Auto Anchors

Anchors are chosen **per axis**:

| The element… | Anchors |
|---|---|
| touches both walls of its parent on an axis | stretch on that axis |
| touches one wall, and the other axis also has a wall or stretch | wall / corner anchor |
| touches one wall only | around the object (a lone wall does not count) |
| anything else | around the object (anchors hug the element) |

The element never moves or changes size: offsets are recalculated so its rect stays exactly where it was.

### What it leaves alone

- **Anchors you set by hand** (a preset, the anchor handles, the Inspector) are never overwritten. Move the element far enough away from them (see *Release distance* below) or reset it to take them back.
- **Axes that already have center anchors** are respected the same way.
- Elements **excluded** with the `No Auto Anchors` marker.
- Elements covered by the **"Never auto adjust"** list (children of a Layout Group, Scroll View parts, slider handles... see *Advanced settings*).
- Elements **rotated** on Z (axis-aligned anchors would not be exact).
- Root canvases and objects without a RectTransform parent.

### Excluding and resetting an element

Right-click the **RectTransform** header:

- **uGUI Enhancer: Exclude or Include in Auto Anchors** – adds/removes the `No Auto Anchors` component. Also available as a checkbox in the overlay and the window.
- **uGUI Enhancer: Reset to Auto Anchors** – gives an element with manual anchors back to the tool. Also in `Tools > uGUI Enhancer > Reset Selected To Auto Anchors`.

### Multi-selection, undo and prefabs

- Moving several elements at once processes each one and shows one notice (*Anchors updated on N objects*).
- Everything done by one move is a **single undo step**.
- Works in **Prefab Mode**, and on prefab instances the change is stored as an override.

## Resolution tools

The preset list **is the Game View's own size list** (the one you use for Play Mode): anything you add there appears here, and the other way round. The first time, a set of common resolutions is added to that list.

| Control | What it does |
|---|---|
| Resolutions dropdown | Pick any resolution from the list |
| `< Previous` / `Next >` | Go through the list |
| `Rotate` | Swap width and height |
| `Next/Prev` filter | `All`, `Landscape` or `Portrait`: Next/Previous skip the other orientation |
| `Last` | Go back to the previous resolution (press again to alternate) |
| `Base` / `Set base` | Save a resolution as your base and jump back to it |

The current resolution is shown with its name, for example `1920 x 1080 [Full HD]`.

In the window you can also type a **custom** resolution and use the **Reference Resolution** of the selected Canvas Scaler.

The overlay is only shown when a Canvas, or something inside one, is selected. If you hide it yourself (press `` ` `` in the Scene View and untick *uGUI Enhancer*), it stays hidden until you turn it on again from that same menu.

## Shortcuts

All of them can be changed in `Edit > Shortcuts…` (category *uGUI Enhancer*).

| Shortcut | Action |
|---|---|
| `Ctrl+Alt+T` | Toggle Auto Anchors |
| `Shift+Alt+→` / `Shift+Alt+←` | Next / previous resolution |
| `Shift+Alt+↑` | Rotate resolution |
| `Shift+Alt+↓` | Go to last resolution |
| `Shift+Alt+B` | Go to base resolution |
| `Shift+Alt+O` | Cycle the Next/Previous orientation filter |

## Menus and window

`Tools > uGUI Enhancer`:

- **Window** – the full control panel, including the **Advanced** section.
- **Reset Selected To Auto Anchors**

The overlay has an **Advanced settings…** button that opens the window with *Advanced* expanded.

### Advanced settings

Stored per project (in the editor preferences, not in the project files).

| Setting | Default | Meaning |
|---|---|---|
| Wall tolerance | 2 units | How close to a parent edge an element must be to count as touching it. Raise it to anchor to walls and corners from further away, for example when you leave a small margin. |
| Release distance | 20 % | How far (as a % of the parent size) you must move an element from its manual or centered anchors before Auto Anchors takes control again. |
| Inspector edit delay | 0.4 s | Wait after the last Inspector edit before anchors are applied. |

#### Never auto adjust

A foldout with a checklist of categories Auto Anchors skips. Each one is a checkbox, with a default:

| Category | Default |
|---|---|
| Children of a Layout Group | on |
| Elements with Content Size Fitter / Aspect Ratio Fitter | on |
| Viewport and Content of a Scroll View | on |
| Internal parts of Sliders, Scrollbars, Dropdowns, Toggles and Input Fields | on |
| Text elements (Text, TextMeshPro) | off |
| Image and Raw Image elements | off |
| Buttons | off |
| Elements with a Mask or Rect Mask 2D | off |

The first four are on because those elements are positioned by something else, and changing their anchors usually breaks the widget. The others are off because they are normal elements you probably do want anchored; turn them on if you never want them touched.

Below the checklist there is a **name list**: one pattern per line, `*` is a wildcard, not case sensitive, and the whole name must match. For example `Background*` skips `Background` and `BackgroundBlur` but not `MyBackground`.

**Reset to defaults** restores the checklist, the name list and the numbers above. Rotated elements and the `No Auto Anchors` marker are always skipped; they are not part of the list.

## Project layout

```
uGUIEnhancer/
  Runtime/   NoAutoAnchors marker component (UGUIEnhancer.Runtime)
  Editor/    the tool, editor only (UGUIEnhancer.Editor)
  Tests/     Edit Mode tests (UGUIEnhancer.Tests)
```

The marker component is the only part that can end up in a build; it does nothing at runtime.

## Compatibility notes

The resolution tools use **internal Unity APIs** of the Game View through reflection. On startup they check that everything they need exists; if a Unity version changes those internals, the resolution tools switch themselves off with a message (in the overlay, the window and the Console) and **Auto Anchors keeps working**.

## Tests

Open `Window > General > Test Runner > EditMode`, or run them from the command line:

```
unity test . --mode EditMode
```

They cover the anchor rules (corners, walls, stretch, tolerance, centered and manual anchors, exclusions and the Advanced checklist) and the resolution tools (set, rotate, orientation filter, last and base). The resolution tests change the Game View size and restore it afterwards.
