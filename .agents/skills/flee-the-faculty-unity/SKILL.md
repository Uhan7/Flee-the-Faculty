---
name: flee-the-faculty-unity
description: Unity workflow and project conventions for the Flee the Faculty repository only. Use for gameplay, UI, scene, prefab, dialogue, speech, and evaluation work in this exact project; do not activate in other Unity projects.
---

# Flee the Faculty Unity

Apply these conventions only inside the `Flee the Faculty` repository.

## Project shape

- Use Unity `6000.0.48f1`. The project is a 2D URP game targeting WebGL.
- Keep ordinary implementation changes under `Assets/`. Do not change `Packages/` or `ProjectSettings/` unless the user explicitly asks or the task cannot be completed otherwise.
- Treat `Assets/Scenes/Main Menu.unity`, `Classroom and Movement.unity`, and `Teacher Evaluation.unity` as the primary flow.
- Prefer reusable prefabs for shared UI and gameplay objects. Preserve runtime bootstrap patterns when the project already uses them for scene-independent systems.
- UI is UGUI plus TextMeshPro. Reuse existing sprites, audio, fonts, prefabs, and components before creating replacements.
- Browser microphone and speech behavior must remain WebGL-compatible and flow through the existing scripts and bridges under `Assets/~Others/!Plugins/WebGL/`.

## Coding and editing

- Follow the existing C# style: Allman braces, sealed MonoBehaviours where appropriate, `[SerializeField] private` inspector fields, and null-safe scene reference resolution.
- Preserve unrelated user changes in the working tree. Inspect overlapping files before patching.
- Prefer script-driven reusable behavior over repeating scene YAML edits. If scenes or prefabs are changed, identify them explicitly in the handoff.
- Keep player-facing copy plain and age-appropriate; this game is intended to be understandable by children.

## Validation

- Validate changed C# with the Unity editor when available.
- If batch mode cannot acquire the local Unity license, use Unity's bundled Roslyn compiler with the current `Library/Bee` response file as a compile check, and clearly report that limitation.
- Run `git diff --check` on files changed in the current task and report any pre-existing warnings separately.
