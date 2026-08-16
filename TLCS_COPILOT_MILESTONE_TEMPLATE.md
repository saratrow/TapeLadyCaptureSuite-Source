# TLCS Copilot Milestone Prompt Template

Use this template after Copilot usage resets. Replace the bracketed milestone section with the specific milestone from `TLCS_MASTER_SPEC.md`.

---

Read `TLCS_MASTER_SPEC.md` in the repository before making any changes. Treat it as the authoritative product/workflow specification.

## Task

Implement **[MILESTONE NUMBER — MILESTONE NAME] only**.

Required scope:

[PASTE ONLY THE BULLETS FOR THIS MILESTONE FROM TLCS_MASTER_SPEC.md]

Do not implement later milestones, even if they seem related or easy to add.

## Mandatory protections

**DO NOT BREAK PROVEN CAPTURE FUNCTIONALITY.**

Do not modify working capture/recording behavior unless this milestone explicitly requires it.

Protect:

- EZCAP DirectShow handling.
- VMR9 preview.
- Fresh DirectShow session lifecycle.
- Composite/S-Video routing.
- Proven 720×480 UYVY 29.97 input.
- Embedded EZCAP audio.
- FFmpeg recording and finalization.
- Return to normal preview after recording.
- Shutdown stability.
- Working Originals preservation.
- Transactional trim behavior.

Prefer narrow changes. Do not refactor unrelated working code. Do not perform speculative cleanup.

## Before editing

1. Inspect the current Git status.
2. Trace only the code paths relevant to this milestone.
3. Identify the smallest set of files that actually needs to change.
4. Briefly state the intended change boundary before editing.
5. If the working tree contains unrelated changes, leave them untouched.

## Implementation

Implement the milestone using the existing architecture wherever practical. Do not redesign proven systems merely to make the code cleaner.

Where the master specification defines operator behavior, follow that behavior exactly rather than inventing additional workflow, dialogs, fields, or settings.

## Validation

After editing:

1. Run `git diff --check`.
2. Build `TapeLadyCaptureSuite.csproj` without launching the application.
3. Report build result.
4. Report the exact files changed.
5. Summarize the exact behavior added/changed.
6. Give me a short, concrete manual acceptance test for this milestone only.

## Stop point

**Do not commit. Do not push. Do not launch TLCS.**

Stop after the build and acceptance-test instructions. I will perform the real hardware/operator test and report the results. Only after I explicitly confirm that the milestone passes should you prepare a bounded commit.
