# Samurai (Unity 2D Prototype)

This repository contains a Unity 2D prototype project focused on a samurai character entrance, attack transition, and slash FX timing.

## 1. Project Overview

- Project name: samurai
- Engine: Unity 6
- Unity version: 6000.4.3f1
- Type: 2D prototype / animation-state test scene
- Main goal: Verify stable flow of Idle -> Run (entrance) -> Attack and trigger slash FX at the attack transition point.

## 2. Core Features

- Character entrance movement before allowing attack input.
- Attack button based state transition.
- Animator state resolution with fallback behavior.
- Event/delegate based attack transition notification.
- FX spawn request and runtime FX lifetime management.
- Runtime logging for debugging state and input behavior.

## 3. Scene And Entry

- Primary scene: Assets/Scenes/SampleScene.unity
- Template scene (URP): Assets/Settings/Scenes/URP2DSceneTemplate.unity

Open `SampleScene` and press Play to test the current flow.

## 4. Controls

- UI attack button: starts attack sequence (after entrance completes).
- Optional keyboard fallback: Space key (only when enabled in inspector setting `enableKeyboardFallback`).

## 5. Key Scripts

### Assets/gamemain.cs

Main flow controller.

Responsibilities:
- Validates and caches references.
- Resolves animator states for idle/run/attack.
- Handles attack button click and attack routine timing.
- Invokes attack transition event with FX spawn position.
- Writes runtime logs to help debug missing animation/input wiring.

### Assets/FxManager.cs

FX lifecycle manager.

Responsibilities:
- Singleton-style global access (`FxManager.Instance`).
- Spawns slash FX object at requested position.
- Forces FX render visibility and starts animation immediately.
- Destroys FX after animation/particle duration.

## 6. Package Highlights

From `Packages/manifest.json`:

- Universal Render Pipeline: `com.unity.render-pipelines.universal`
- Input System: `com.unity.inputsystem`
- 2D animation/sprite/tilemap related packages

## 7. Logging

The runtime writes attack/debug logs to:

- Persistent data log: `samurai_attack_log.txt`
- Project root runtime log: `samurai_attack_log_runtime.txt`

These logs are useful when checking whether button input, state transition, and FX requests are firing correctly.

## 8. Repository Notes

- Unity-generated folders are ignored using `.gitignore`.
- This repository tracks source assets, scripts, package config, and project settings required to reopen and continue development.

## 9. Suggested Evaluation Checklist (For Instructor Review)

- Project opens without missing package errors.
- `SampleScene` plays and entrance motion runs.
- Attack button triggers attack state after entrance completion.
- Slash FX appears at attack transition timing.
- Attack returns to idle after configured duration.
- Logs are generated when actions are performed.

## 10. Next Improvements

- Add HP/enemy interaction and hit validation.
- Expand combo attacks and state-machine readability.
- Add UI feedback and cooldown display.
- Add automated play-mode test coverage for attack transition timing.
