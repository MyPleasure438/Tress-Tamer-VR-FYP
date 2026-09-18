# AI Handoff For Continuing The Unity Haircut VR Project

This Unity project is a VR barber training simulator called **Tress Tamer**. The player uses VR tools such as scissors, clipper, comb, and brush to cut/style hair card meshes on a customer head, then receives training feedback and scoring.

Before changing anything, inspect the actual scripts in this project because the user may have changed settings in the Unity Inspector after these systems were added.

## Important User Context

- The user is under FYP time pressure and wants practical, working features first.
- Keep explanations simple unless technical detail is needed.
- Do not delete old/backup methods unless the user explicitly asks. Some older cutting methods are kept intentionally as backup.
- Prefer adding new methods/fields over ripping out older systems.
- The current priority is a playable VR demo flow, not perfect production-level hair simulation.

## Main Current Goal

Polish a VR haircut training flow:

1. Start from a VR world-space menu.
2. Choose Stage 1 hairstyle: Induction, Butch, or Crew.
3. Choose Practice or Assessment mode.
4. Use scissor/clipper/comb/brush on hair cards.
5. Show guide colors and score by hair zones.
6. Display result screen.

## Key Script Areas

### Hair Cutting

Primary files:

- `Assets/Scriptings/Managers/CuttingManager.cs`
- `Assets/Scriptings/Tools/Scissor.cs`
- `Assets/Scriptings/Tools/Clipper.cs`

Current cutting design:

- Scissor and clipper use **cut area BoxColliders** instead of a sphere/cylinder.
- The user manually placed several cut boxes near the scissor/clipper blade.
- Cutting detects hair cards inside those boxes.
- Cutting can be triggered by mouse during desktop testing and should also be wired to XR Grab Interactable `Activated` events for VR.
- Cutting should support:
  - empty snip sound when no hair is cut,
  - hair cut sound when hair is successfully cut,
  - limiting max hair cards per snip,
  - optional spreading cuts over frames,
  - optional spawned fallen hair debris using object pooling.

Important setup:

- Scissor should have `Cut Area Boxes` assigned.
- The scissor/clipper trigger should call the tool's cut method, not only play sound.
- If using VR, the XR Grab Interactable `Activate` event should call the tool's cut trigger method.

Known issue:

- If `Cut Area Boxes` are too many or overlap too many hair colliders at once, VR stutters.
- Keep cutting boxes around 3 to 5 while testing.
- Keep max hair cards per snip low for VR.

### Hair Cards And Segments

Primary files:

- `Assets/Scriptings/HairMeshForHairGeneration/HairCardData.cs`
- `Assets/Scriptings/HairMeshForHairGeneration/SegmentedHairCard.cs`
- `Assets/Scriptings/HairMeshForHairGeneration/HairCardSegmentBatchSetup.cs`

Current design:

- Hair card roots are assumed to be at the transform gizmo.
- `HairCardData` stores root/growth/length/section/target data.
- Segmented hair cards are used so cutting can hide/remove hair sections instead of doing expensive mesh slicing every cut.
- `HairCardSegmentBatchSetup` can generate segment objects from hair cards by name prefix.

User hair section names:

- `Back`
- `BackMiddle`
- `BackNape`
- `BackUpper`
- `CrownBack`
- `CrownLeft`
- `CrownRight`
- `FringeLeft`
- `FringeMiddle`
- `FringeRight`
- `LeftSideSideburn`
- `LeftSideUpper`
- `RightSideSideburn`
- `RightSideUpper`
- `TopLeft`
- `TopMiddle`
- `TopRight`
- `TransitionBackLeftAtMiddleSection`
- `TransitionBackLeftAtUpperSection`
- `TransitionBackRightAtMiddleSection`
- `TransitionBackRightAtUpperSection`

Known issue:

- Left-side hair groups once had a problem where cutting the bottom also removed hair near the root. Right-side groups behaved correctly.
- Suspect mirrored growth direction/root direction on left-side cards. Check `HairCardData.growthDirection`, root position, and segment ordering.
- If a hair card has only one remaining segment, it cannot visually become shorter with the current segmented approach.

### Comb And Brush

Primary files:

- `Assets/Scriptings/Tools/Comb.cs`
- `Assets/Scriptings/Tools/Brush.cs`

Current design:

- Comb/brush touches hair and pushes/smooths it in the tool movement direction.
- Hair root should remain anchored while the rest bends/moves.
- A settling/downward relaxation behavior was added so combed hair slowly relaxes instead of floating forever.

Known issue:

- Because many hair cards are split into segments, combing can make separated pieces look slightly disconnected or zig-zag.
- This is acceptable for now if it is playable.

XR grabbing notes:

- A grabbable tool needs a collider.
- Dynamic Rigidbody with non-convex MeshCollider is not allowed in Unity.
- Use convex MeshCollider, simple BoxCollider, or make Rigidbody kinematic depending desired physics.
- If using BoxCollider for grab, make sure the collider is included in XR Grab Interactable colliders or is on the same object.

### Undo

Primary file:

- `Assets/Scriptings/Managers/HairUndoManager.cs`

Current design:

- Undo should restore the last hair change from cutting or brushing.
- It likely snapshots hair card/segment state before a change.
- Check integration points in `CuttingManager.cs`, `Brush.cs`, and `SegmentedHairCard.cs`.

### Haircut Style And Target Lengths

Primary file:

- `Assets/Scriptings/Hairstyle/HaircutManager.cs`

Current design:

- This replaced the earlier `Stage1HaircutManager` idea.
- It groups hairstyles by stage.
- It stores target length settings per hairstyle.
- Switching hairstyle should reload the latest saved settings for that hairstyle.
- It supports target length modes:
  - built-in haircut defaults,
  - uniform length for all sections,
  - custom per-section lengths.
- It can apply target lengths to all hair cards.
- It can optionally show the perfect haircut at play start based on target lengths.

Haircuts currently planned:

Stage 1:

- `Stage1_InductionCut`
- `Stage1_ButchCut`
- `Stage1_CrewCut`

Stage 2:

- `Stage2_StandardBob`
- `Stage2_LongBob`
- `Stage2_ALineBob`

Stage 3:

- `Stage3_DisconnectedUndercut`
- `Stage3_SlickedBackUndercut`
- `Stage3_HardPartUndercut`

Stage 4:

- `Stage4_LowFade`
- `Stage4_MidFade`
- `Stage4_HighFade`

Stage 5:

- `Stage5_AsymmetricalBob`
- `Stage5_SideSweptPixie`
- `Stage5_AvantGardeAngle`

Training modes:

- Practice mode: target length acts as a hard limiter. Player cannot cut shorter than target.
- Assessment mode: target length is only for scoring. Player can overcut.

Typical Stage 1 lengths:

- Induction: about `0.005m` to `0.01m`
- Butch: about `0.02m` to `0.03m`
- Crew: sides/nape/sideburn around `0.01m`, top/crown/fringe around `0.03m`

### Evaluation And Feedback

Primary files:

- `Assets/Scriptings/Evaluation/EvaluationSystem.cs`
- `Assets/Scriptings/Evaluation/Stage1ResultPanel.cs`
- `Assets/Scriptings/Evaluation/Stage1HairFeedbackVisualizer.cs`

Current design:

- `EvaluationSystem` scores hair cards based on their target length and current length.
- It reports correct/too long/too short/deviation count.
- `Stage1ResultPanel` displays result text on a panel.
- `Stage1HairFeedbackVisualizer` colors hair according to target status:
  - green = near correct,
  - yellow = too long,
  - red = too short / danger.

Important fix already made:

- `Stage1HairFeedbackVisualizer` must not create `MaterialPropertyBlock` in a field initializer/constructor. It should be created in `Awake` or `Start`.
- The project uses Unity Input System, so avoid old `UnityEngine.Input.GetKeyDown` unless active input handling supports it.

Known setup issue:

- If the result panel only shows `New Text`, check that the TMP text reference is assigned and that `Stage1ResultPanel` is calling the update/display method after evaluation.

### Hair Card Shaders

Important files/assets:

- `Assets/Scriptings/Hairstyle/HairCapStubble.shader`
- `Assets/Scriptings/Hairstyle/HairCapController.cs`
- Shader Graphs/materials for hair cards, including `HairCardShaderNewEnvironmentAdjustable`

Current hair card shader situation:

- The original hair card shader looked good on the front side.
- Back side became light blue when render face was set to Both.
- This is likely caused by backface normals/environment lighting/reflection behavior.
- A shader graph/environment-adjustable version was made so environment lighting/reflection behavior can be controlled per material.
- Do not assume Blender looking correct means Unity will render both sides the same. Unity lighting can expose backface normal issues.

Current hair cap/stubble situation:

- A hair cap shell exists, but it is only a scalp shell, not real hair.
- A procedural/texture stubble shader was attempted.
- It compiles now after previous shader syntax fixes, but the user is not happy with the look. It often looks like moles/dots or flat scalp rather than realistic induction hair.
- Treat hair cap stubble as optional visual support, not the main reliable haircut solution.

Important visual reality:

- A shader alone cannot create fully realistic 3D induction hair unless the texture/normal/noise is very good.
- For FYP, acceptable direction is: short segmented hair cards plus optional subtle scalp/stubble texture.

### VR Training Menu

Primary file:

- `Assets/Scriptings/UI/VRTrainingMenuScreen.cs`

Purpose:

- Builds an external/world-space VR menu screen at runtime.
- Inspired by the user's reference screenshots.
- Lets the player start training, choose module/stage/haircut/mode, open settings, view records, and evaluate current haircut.

Current screens:

- Main Menu:
  - `Tress Tamer`
  - `VR Training Simulator`
  - `Start Training`
  - `Calibration and Settings`
  - `Performance Records`
  - `Exit`
- Module Select:
  - Stage 01 Buzz Cut unlocked
  - Stage 02 to Stage 05 locked placeholders
- Stage 01:
  - Induction, Butch, Crew
  - Practice / Assessment
  - Begin Training
- Calibration And Settings:
  - Recenter View
  - Floor Height plus/minus
  - Left/Right handed
  - Vibration feedback on/off
- Performance Records:
  - Latest score
  - Evaluate current haircut
  - Back to main menu

Recent UI polish:

- Added options to hide the soft center surface.
- Added backdrop/soft surface size and offset controls.
- Added dark rectangle offsets for different pages.
- Settings and Stage 1 layout were moved further inside the dark rectangle.
- Floor helper text should be single row.

Important setup:

- Add `VRTrainingMenuScreen` to an empty world-space menu object.
- Assign:
  - HaircutManager,
  - EvaluationSystem,
  - Stage1ResultPanel,
  - player camera / XR main camera,
  - optional gameplay root to hide/show gameplay.
- Make sure XR Ray Interactor UI interaction is enabled.
- The script tries to use `InputSystemUIInputModule` and XR UI raycaster support.

### Imported Environment Assets

Known issue:

- Pink imported materials usually mean unsupported shaders, especially imported `Autodesk Interactive` shaders in URP.
- Convert those materials to `Universal Render Pipeline/Lit`.
- Assign maps manually:
  - BaseColor/Diffuse to Base Map,
  - Normal map to Normal,
  - Metallic to Metallic,
  - Roughness usually maps inverse to Smoothness.

Performance:

- A 272 polygon / 300 vertex air-conditioner asset is fine for VR.
- Bigger issues are usually transparent hair, too many colliders, mesh slicing, real-time shadows, and too many active physics objects.

### Character / Mixamo / Hair Attachment

Important advice:

- Do not upload 800 hair cards into Mixamo with the body. Mixamo may fail or ignore/strip them.
- Recommended workflow:
  1. Export body/head only from Blender.
  2. Rig/animate with Mixamo.
  3. Import animated character into Unity.
  4. Attach hair card parent to the character head bone in Unity.

If aligning one character to another using eye positions:

- Do not only set the eye child position.
- Compute offset:
  - `offset = targetEye.position - movingEye.position`
  - then add that offset to the moving model root position.
- That moves the whole character so the eye ends up aligned.

## Recommended Next Steps

1. Stabilize Stage 1 only:
   - Induction, Butch, Crew.
   - Do not chase all 15 hairstyles yet.

2. Make Stage 1 look acceptable:
   - Use hair cards/segments for visible hair.
   - Use hair cap stubble only as subtle scalp support.
   - Do not rely on hair cap shader alone for realistic induction hair.

3. Verify VR tool loop:
   - Grab scissor.
   - Activate cut with XR trigger.
   - Hair shortens.
   - One correct cut sound plays.
   - Hair debris prefab spawns/falls/fades if enabled.
   - Undo works.

4. Verify scoring loop:
   - Pick Stage 1 haircut in menu.
   - Practice mode enforces target.
   - Assessment mode allows overcut.
   - Feedback colors update.
   - Result panel shows score.

5. Polish menu:
   - World-space VR menu should be readable from player view.
   - Keep buttons inside dark panel.
   - Start Training should reliably hide menu or begin flow.

## Current Known Risks

- Realistic induction cut is hard with long hair cards chopped into strips. It can look like chopped sheets instead of stubble.
- Shader-only hair cap stubble has not produced a satisfying result yet.
- Hair card cutting on mirrored left-side sections may have root/growth direction issues.
- Too many active colliders/segments can cause VR frame drops.
- Transparent hair cards can become expensive in URP.

## Good Prompt To Use With Another AI

Please read `Assets/AI_HANDOFF_FOR_CLAUDE.md` first, then inspect the listed scripts before editing. Continue from the current Unity project state. Preserve existing backup methods unless explicitly asked to remove them. Focus on making the VR Stage 1 haircut flow playable and stable before expanding to other hairstyles.

