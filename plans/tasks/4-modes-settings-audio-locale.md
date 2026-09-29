---
artifact_type: task
artifact_id: task_4_modes_settings_audio_locale_v1
task_family_id: modes-settings-audio-locale
sequence_key: "4"
task_id: 4-modes-settings-audio-locale
title: "Implement Game Modes, LLM Settings, Web Audio SFX, and Tri-lingual Localization"
status: approved
phase: implementation
target_files:
  - index.html
  - src/audio/audio.ts
  - src/locale/i18n.ts
  - src/locale/strings.ts
  - src/ui/settings.ts
  - src/ui/help.ts
  - src/ui/controller.ts
  - src/ui/style.css
  - src/main.ts
  - tests/locale/i18n.test.ts
  - tests/audio/audio.test.ts
  - tests/ui/settings.test.ts
prd_ref: null
plan_ref: plans/2026-09-29-sahkku-ts-refactor-and-2d-frontend-plan.md
system_context_ref: null
owners:
  - "tarjeib"
doc_bubble_id: null
impl_bubble_id: 4-modes-settings-audio-locale
supersedes: []
superseded_by: null
archive_group: 2026-09-29-sahkku-ts-refactor-and-2d-frontend
---

# Task 4: Implement Game Modes, LLM Settings, Web Audio SFX, and Tri-lingual Localization

## L0: Context & Scope

### Problem Summary
With the TypeScript rules engine (`src/rules/`), agent framework (`src/agents/`), and 2D web board game interface (`src/ui/`) in place, the application needs the complete match management, customization, audio feedback, and cultural accessibility features that make Sáhkku fully playable and historically aligned:
1. **Game Modes**:
   - Single Player: Human (Women / P1) vs AI (Men / P2), selecting between Heuristic bot, Random bot, or LLM agent.
   - Local 2-Player Hotseat: Human vs Human with smooth turn transitions.
   - Spectator Mode: Bot vs Bot with adjustable simulation speed.
   - Match Options: Variant selection (Standard vs Even-Odds / Liká with 3 loose soldiers) and Starting Player selection (Throw-for-start vs Manual P1/P2).
2. **LLM Endpoint & Match Settings**:
   - In-game settings modal to configure OpenAI-compatible endpoint URL (`http://localhost:1234/v1` for LM Studio, Ollama, etc.), model name, request timeout, and fallback notifications.
   - Volume sliders and mute toggles for master, sound effects, and ambient audio.
   - Settings persisted in browser `localStorage`.
3. **Open Web Audio SFX**:
   - Web Audio API synthesizer / procedural sound generator (or permissive CC0/MIT audio clips) producing satisfying tactile feedback for piece selection, piece move, piece capture, dice rolling, king recruitment, and match victory.
   - Auto-unlocks on first user interaction without blocking or throwing autoplay policy errors.
4. **Tri-lingual Localization (i18n)**:
   - Full string tables and reactive locale switcher supporting North Sámi (`se`), Norwegian (`no` / `nb`), and English (`en`).
   - Translates all UI chrome, menu dialogs, turn banners, piece terminology (Nisu, Gonagas, etc.), dice faces (Sáhkku, Golbma, Guokte, etc.), game-over victory reasons, and help documentation.
5. **Interactive Rules & Help Modal**:
   - Built-in guide explaining the traditional rules, board track figure-of-8 traversal, piece moves, activation queue, king recruitment, and win conditions.

### Scope Reality Proof
- **Inspected Requirements**:
  - Builds upon existing `MatchController` in `src/ui/controller.ts` and `src/main.ts`.
  - Integrates with `LlmPlayerAgent`, `HeuristicPlayerAgent`, and `RandomPlayerAgent` in `src/agents/`.
  - Pure open-source / license-free assets only (no proprietary audio or closed libraries).
- **Bounded Slice**:
  - `src/audio/audio.ts`: Web Audio API sound manager providing procedural/synthesized SFX (`roll`, `select`, `move`, `capture`, `recruit`, `win`).
  - `src/locale/strings.ts` & `src/locale/i18n.ts`: Localization store, translation lookup, reactive listener, and string tables for `se`, `no`, `en`.
  - `src/ui/settings.ts`: Settings modal component for LLM URL/model, audio volume, and game preferences.
  - `src/ui/help.ts`: Illustrated rules help modal.
  - `src/ui/controller.ts` & `src/main.ts`: Wire match modes, audio triggers on rule events, and dynamic localization into the UI loop.
  - `index.html` & `src/ui/style.css`: Add modal styles, mode selector toolbar, language dropdown, and settings button.
  - `tests/`: Unit tests for i18n dictionary completeness, audio manager lifecycle, and settings persistence.

### Scoped Invariants
- `applies_to`: `src/audio/*`, `src/locale/*`, `src/ui/settings.ts`, `src/ui/help.ts`, `src/ui/controller.ts`, `src/main.ts`, `index.html`, `src/ui/style.css`, and related tests.
- `does_not_apply_to`: Pure rules engine algorithms in `src/rules/` (already complete).
- `proof_surface`: `npm test` passes all tests; `npm run build` generates static bundle; switching languages updates all DOM text immediately; LLM and audio settings persist across reloads.

---

## L1: Implementation Contract

### 1. Functional Requirements

- **REQ_LOCALE_I18N**: In `src/locale/strings.ts` and `src/locale/i18n.ts`:
  - Provide complete translations for North Sámi (`se`), Norwegian (`no`), and English (`en`).
  - Key sections: `menu` (start, restart, mode names, options), `status` (whose turn, roll phase, move phase, reroll prompt, thinking), `pieces` (Soldier/Women/Men, Queen/Nisu, King/Gonagas), `dice` (Sáhkku, 3, 2, blank), `events` (captured, recruited, won), `settings` (LLM endpoint, model, timeout, sound volume), and `help` (rules overview).
  - `t(key, params?)` helper with fallback to `en` if missing.
  - Language change event dispatched to subscribers to re-render UI labels reactively.
- **REQ_AUDIO_SFX**: In `src/audio/audio.ts`:
  - `AudioManager` class utilizing Web Audio API (`AudioContext`).
  - Procedural sound synthesis (oscillators, noise bursts, envelopes) or bundled CC0 audio for:
    - `playRoll()`: dice rattle sound.
    - `playSelect()`: gentle high click.
    - `playMove()`: wood slide/thud sound.
    - `playCapture()`: distinct capture snap.
    - `playRecruit()`: regal brass/bell harmonic chime.
    - `playVictory()`: celebratory ascending melodic sequence.
  - Safely handles suspended `AudioContext` on page load; resumes on first user click.
  - Volume control and mute toggle.
- **REQ_SETTINGS_MODAL**: In `src/ui/settings.ts`:
  - Modal dialog accessible via gear icon on header/toolbar.
  - Configurable fields:
    - LLM endpoint URL (defaults to `http://localhost:1234/v1/chat/completions`).
    - LLM model name (defaults to `pairflow-player`).
    - LLM request timeout (seconds).
    - Sound effects volume (slider 0-100%).
    - Sound mute checkbox.
  - Saved to `localStorage` under `sahkku_settings`.
- **REQ_GAME_MODES**: In `src/ui/controller.ts` and `src/main.ts`:
  - Mode selector supporting:
    - `human_vs_bot`: Player 1 is Human, Player 2 is Bot (`heuristic`, `random`, or `llm`).
    - `hotseat_2p`: Player 1 and Player 2 are both Human on the same device.
    - `bot_vs_bot`: Spectator mode between two selected bots with speed control (slow, normal, fast).
  - Variant toggle: Standard (0 active soldiers) vs Even-Odds (3 active soldiers).
  - Start player option: Throw for starting player vs fixed P1/P2.
- **REQ_HELP_MODAL**: In `src/ui/help.ts`:
  - Illustrated modal explaining traditional Sáhkku background, figure-of-8 track, dice values, piece functions, and win conditions.
  - Available in all 3 supported languages.

### 2. Call-Site Matrix

| Target File | Entity | Description |
|---|---|---|
| `src/locale/strings.ts` | Translation dictionaries | Tri-lingual string tables (`se`, `no`, `en`). |
| `src/locale/i18n.ts` | `I18n` class & `t` | Locale state manager, fallback resolution, reactive listener. |
| `src/audio/audio.ts` | `AudioManager` | Web Audio sound effects synthesizer and volume manager. |
| `src/ui/settings.ts` | `SettingsModal` | Settings UI dialog and `localStorage` syncing. |
| `src/ui/help.ts` | `HelpModal` | Rules explainer modal. |
| `src/ui/controller.ts` | `MatchController` | Wire audio on `RuleEvent`s, wire agents for selected game modes. |
| `src/main.ts` | Top-level wiring | Mount toolbar, modal triggers, language dropdown, mode switcher. |
| `index.html` | Layout template | Header controls (locale select, settings, help, restart) and modal containers. |
| `src/ui/style.css` | Styling | Modal overlays, responsive toolbar buttons, slider styling. |

### 3. Error and Fallback Handling

- If `AudioContext` is unavailable or blocked by browser permissions, `AudioManager` methods safely no-op without error.
- If `localStorage` is disabled (e.g. private browsing storage quota), settings fall back to in-memory defaults.
- Missing translation keys cleanly fall back to English string values.

### 4. Test Matrix

| Test ID | Input / Scenario | Expected Outcome |
|---|---|---|
| `T4_I18N_COMPLETENESS` | Validate all translation keys in `se`, `no`, `en` | Every key exists in all three dictionaries without missing placeholders. |
| `T4_AUDIO_LIFECYCLE` | Initialize `AudioManager`, trigger sounds, test mute | Methods execute without exceptions; mute suppresses output. |
| `T4_SETTINGS_STORAGE` | Save and reload LLM settings from `localStorage` | Values round-trip correctly and update active agent configurations. |
| `T4_BUILD_VALIDATION` | `npm run build` && `npm test` | Build succeeds; all unit and integration tests pass. |

---

## L2: Hardening Backlog

- Add touch vibration (Haptic Feedback API) for piece moves on mobile devices.
- Add additional regional Sámi dialects (e.g., Lule Sámi, South Sámi) in a future localization pass.
