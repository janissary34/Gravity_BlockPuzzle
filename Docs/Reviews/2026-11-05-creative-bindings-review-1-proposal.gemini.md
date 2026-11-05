# Senior Technical Review: Scene1 Creative UI Binding Authoring Tool Fix

## 1. Executive Summary

This review evaluates the proposal to fix `CreativeSceneBindingsAuthoring.cs` in `Assets/Editor/Creative/`. The proposal addresses an editor-only authoring tool defect that has prevented `CreativeSceneBindings` from ever being generated for `Scene1.unity`.

The investigation and proposal are exceptionally well-researched, evidenced by concrete batch-mode scene dumps and editor logs. The proposed solution adheres strictly to the repository engineering principles in `AGENTS.md`: it introduces no runtime overhead, leaves gameplay code untouched, avoids fragile scene renames, and produces deterministic, inspectable serialized data so Creative Mode requires zero runtime hierarchy searching.

---

## 2. Evidence Evaluation & Problem Verification

The probe files and execution logs fully corroborate the three issues identified in the proposal:

1. **Hierarchy Path Separator Collision (`/` in GameObject name)**:
   * *Evidence*: `editor-log-failed-run.txt` reports `MISSING TutorialAndRewardPopups: InGameUI/NewBooster_featurePanel`.
   * *Evidence*: `probe-1-InGameUI-children.txt` shows child `[11]` is literally named `'New_Booster/feature_Panel_Dark_bg'` (with a child `'NewBooster_featurePanel'`).
   * *Analysis*: In Unity, `Transform.Find(string path)` treats `/` strictly as a delimiter. Passing a path containing an object whose name contains `/` fails irrecoverably. Furthermore, `NewBooster_featurePanel` is not a direct child of `InGameUI`; it is nested under `New_Booster/feature_Panel_Dark_bg`.
   * *Conclusion*: The failure is 100% verified.

2. **First-Match Sibling Masking (`FullscreenSuccess`)**:
   * *Evidence*: `probe-1-InGameUI-children.txt` shows two sibling instances of `FullscreenSuccess`: index `[13]` (16 graphics, part of `InGameUI.prefab`) and index `[27]` (15 graphics, scene-added prefab instance from `FullscreenSuccess.prefab`).
   * *Evidence*: `probe-2-New_Panels-duplicates-references.txt` confirms `PrototypeBoard.winPanel` points specifically to sibling index `[27]`.
   * *Analysis*: `Transform.Find` returns the first child matching the given name (index `[13]`). Because index `[27]` is the active instance wired to `PrototypeBoard`, binding index `[13]` alone leaves index `[27]` unmanaged, causing visual leaks during takes where the win panel is supposed to be hidden.
   * *Conclusion*: The defect is 100% verified.

3. **Parallel Hierarchies for Panels (`InGameUI` vs. `New_Panels`)**:
   * *Evidence*: `probe-1` and `probe-2` reveal duplicate panel instances: `Settings_Panel` (`#21` vs `#28 > #2`), `Fail_Panel` (`#22` vs `#28 > #0`), `RevivePanel` (`#19` vs `#28 > #1`), and `Retry_Panel` (`#16` vs `#28 > #3`).
   * *Evidence*: `RuntimePieceFactoryBootstrap` binds direct children (`Retry_Panel#16`, `Fail_Panel#22`), while other systems or UI triggers may open the `New_Panels` hierarchy.
   * *Analysis*: Leaving either set unmapped risks unmanaged graphics appearing during takes.
   * *Conclusion*: Dual-binding both sets is the only safe approach without invasive refactoring of scene UI prefabs.

---

## 3. Architectural & AGENTS.md Contract Assessment

* **Runtime Restrictions**: The changes are strictly isolated to `Assets/Editor/Creative/CreativeSceneBindingsAuthoring.cs` and documentation. No gameplay code, runtime MonoBehaviour, or production asset is modified.
* **No Runtime Hierarchy Discovery**: In alignment with `AGENTS.md` ("Avoid hidden runtime dependency discovery"), the tool resolves hierarchy references in Edit Mode and serializes explicit component references onto `CreativeSceneBindings`. At runtime, `CreativeSession.ApplyUIProfile` iterates pre-resolved arrays with zero scene searching or polling.
* **Preservation of Playable Behaviour & Scene Integrity**: The proposal explicitly rejects renaming scene objects to suit the tool. This respects the `AGENTS.md` mandate: *"Preserve existing playable behaviour unless the task explicitly requires changing it"* and avoids breaking existing prefab linkages or animation tracks.
* **State & Dependency Flow**: The tool operates through `SerializedObject` and `SerializedProperty` within Unity's Undo and Scene Management framework, ensuring safe serialization and clean dirty-flag handling.

---

## 4. Technical Analysis of Proposed Changes

### 4.1 Segment Array Paths (`string[]`)
Replacing parsed string paths with explicit segment arrays (e.g., `new[] { "New_Booster/feature_Panel_Dark_bg" }` or `new[] { "UI_Level", "Level_Display_txt" }`) eliminates path-separator ambiguity. Unity `Transform.name` comparisons treat `/` as literal character data.

### 4.2 Multi-Match Level Walk
A breadth-first / level-by-level walk that evaluates `child.name == segmentName` across all candidates at step $N$ correctly accumulates duplicate siblings (such as both `FullscreenSuccess` instances). Aborting if any path resolves to zero candidates preserves the existing strict validation invariant.

### 4.3 Binding `New_Booster/feature_Panel_Dark_bg` Root
Binding `New_Booster/feature_Panel_Dark_bg` (39 graphics) rather than just the nested `NewBooster_featurePanel` (38 graphics) ensures that the full-screen dark backdrop image on the parent is also disabled when popups are suppressed. Because `ApplyUIProfile` disables `Graphic.enabled`, this cleanly prevents visual backdrop tinting during clean takes.

### 4.4 Graphics Deduplication
Preserving order while deduplicating (`HashSet<Graphic>` insertion check or equivalent) ensures deterministic array serialization. This prevents artificial git diff churn across repeated authoring runs.

### 4.5 Scene Dirty Guard (`scene.isDirty`)
Aborting with a clear error if `scene.isDirty == true` protects developers from having unrelated in-progress scene edits silently committed by the tool's `EditorSceneManager.SaveScene`.

---

## 5. Findings and Recommendations

| ID | Category | Severity | Description & Actionable Recommendation |
|---|---|---|---|
| **F-01** | Ergonomics / Code Quality | **Low** | **Map Data Structure Readability**: Declaring `string[][]` paths inline across all groups can become noisy. <br/>*Recommendation*: Use a lightweight helper method or struct (e.g., `static string[] Path(params string[] steps) => steps;` or a `HierarchyPath` struct) to keep `Map` clean, declarative, and easily scannable. |
| **F-02** | Serialization / Idempotency | **Low** | **Deterministic Sibling Traversal**: When collecting matching siblings across multiple parent transforms, traversal must strictly follow hierarchy sibling order (`GetChild(i)` from `0` to `childCount - 1`) so that the resulting `Graphic[]` array order is 100% deterministic between runs. |
| **F-03** | Data Integrity | **Informational** | **Preservation of `backdropRenderers`**: The current authoring logic only mutates the `uiGroups` `SerializedProperty`, leaving `backdropRenderers` intact. The implementation must ensure that any serialized property adjustments continue targeting only `uiGroups` to avoid wiping designer-configured backdrops. |
| **F-04** | Scope / Documentation | **Informational** | **Unmapped UI in `BoardOnly`**: As documented in the proposal, elements like `Timer_obj`, `Freeze_Slider`, `Vapor`, and `FreezeFXRoot` are not mapped to any group. While not required for `CreativeUI_GameplayClean`, ensure `Docs/` accurately notes that `BoardOnly` will require expanding `CreativeUIGroup` if a completely stripped HUD is needed in future takes. |

---

## 6. Review of Alternatives

1. **Renaming Scene Objects**: *Correctly rejected*. Renaming objects in `Scene1` (e.g. removing the slash from `New_Booster/feature_Panel_Dark_bg`) risks breaking animation clips, third-party packages, or unindexed script references.
2. **Sibling Index Paths**: *Correctly rejected*. Sibling indices are notoriously brittle in Unity and break whenever UI layouts or canvas sorting orders are adjusted.
3. **Choosing Only the "Live" Copy**: *Correctly rejected*. Because `InGameUI` contains overlapping references from different systems, attempting to guess the single active copy in Edit Mode is fragile. Binding both copies is safe because disabling `Graphic.enabled` on an already-dormant panel is a no-op, whereas missing a live panel causes visual defects.

---

## 7. Validation Plan Assessment

The proposed validation plan is thorough and well-sequenced:
1. Assembly compilation verification (`compile_check.py --overlay`).
2. Batch-mode execution against real `Scene1.unity` in an isolated copy:
   - Verifying zero `MISSING` errors.
   - Confirming duplicate instances (`FullscreenSuccess#13`, `#27` and `New_Panels`) are present in serialized properties.
   - Verifying scene diff against `HEAD` contains strictly the new `CreativeSceneBindings` object and the bootstrap reference.
   - Verifying idempotency (second run produces zero git diff).
   - Verifying dirty-scene refusal.
3. Normal Play Mode regression test execution.

---

## 8. Final Verdict

### **APPROVE**

The proposal is sound, precise, and completely aligned with the project's engineering contract. It resolves all verified defects with zero runtime risk. Implement according to the proposal, incorporating the minor formatting and deterministic ordering recommendations in **F-01** and **F-02**.
