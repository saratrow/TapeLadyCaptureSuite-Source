# Tape Lady Capture Suite (TLCS) — Master Specification

**Status:** Authoritative working specification  
**Date:** August 16, 2026  
**Guiding principle:** **ArcSoft-simple on the surface. TLCS-safe underneath.**

## 1. Purpose

TLCS is a production tool for processing customer tapes quickly, safely, and with minimal operator friction. It is not intended to become a general-purpose video editor, CRM, invoicing system, or bookkeeping application.

The core workflow is:

**CAPTURE FAST → REVIEW IN BATCHES → DELIVER SAFELY → RETAIN FOR 6 MONTHS → CONFIRMED DELETION**

The primary data hierarchy is:

**Customer → Project/Order → Recording**

A recording belongs to its customer/project, not to the current capture session, workstation state, or processing order.

---

## 2. Proven Functionality — DO NOT BREAK

The following functionality is proven working and must be protected unless a task explicitly requires changing it:

- EZCAP Video Grabber capture.
- Composite/S-Video routing.
- Native DirectShow/VMR9 preview.
- Fresh DirectShow session lifecycle used to avoid black-preview failures.
- FFmpeg recording.
- Proven 720×480 UYVY input at 29.97 fps.
- Embedded EZCAP audio.
- Good saved MP4 video and audio.
- Normal preview returns after recording.
- Normal TLCS shutdown after recording.
- Completed recordings can enter Needs Review.
- Current Review & Trim functionality is usable.
- Fast/lossless trim works.
- Frame-accurate capability exists where needed.
- Originals preservation works.
- Transactional trim/file replacement behavior works.
- No Trim workflow works.
- Confirmation dialogs work.
- Review metadata persists.

### Known issues that are intentionally parked

**Recording preview choppiness:** The preview during recording is choppy, but the saved MP4 and audio are good. Do not risk the proven recording pipeline merely to improve this cosmetic issue.

**Review visual scrubbing:** The current Review & Trim implementation can change the selected time and resume playback from it, but the visible paused video does not reliably follow timeline dragging. This should be solved as part of Review & Trim 2.0 rather than by endlessly patching the existing arrangement.

---

## 3. Customer and Project Model

### Customer

TLCS needs only minimal customer information:

- **Customer Name** — required; normally the customer's full name.
- **Notes** — optional.

Phone, email, address, pricing, invoices, payments, and bookkeeping do not belong in TLCS. Those functions belong in bookkeeping/invoicing software and, later, TLBS integration.

### Project / Order

Each separate customer drop-off is a new project/order.

If John Smith brings 15 tapes today and returns six months later with 10 more, those are two projects under the same customer.

Projects default to the exact drop-off date:

`08-16-2026`

Example:

```text
John Smith
├── 08-16-2026    COMPLETE
└── 02-12-2027    ACTIVE
```

A returning customer should default to **New Project**. Completed projects remain closed/history by default. **Reopen Project** must be a deliberate action.

Projects may be renamed if necessary, but manual naming should not normally be required.

### Unlabeled numbering scope

`Unlabeled 1`, `Unlabeled 2`, etc. reset for each project/order, not merely for each customer.

---

## 4. Default Storage Structure

Default capture root:

`C:\Users\sarat\Videos\Tape Lady Captures`

The location must be configurable in Settings for future projects.

Changing the default storage location must not automatically move existing projects.

Folder structure:

```text
C:\Users\sarat\Videos\Tape Lady Captures\
└── John Smith\
    └── 08-16-2026\
        ├── Christmas 1998.mp4
        ├── Unlabeled 1.mp4
        ├── Unlabeled 2.mp4
        └── Originals\
            └── Christmas 1998.mp4
```

Do not add Active/Completed filesystem folders merely to duplicate database status. Moving large video files simply because project status changes is unnecessary.

---

## 5. Capture Workflow

Capture is optimized for throughput. Review happens later.

### Before capture

The operator selects/enters:

- **Customer Name**
- **Project/Order**
- **Tape Title**

Customer should become a searchable selector of existing/active customers, with a simple way to create a new customer/project.

### During capture

Customer, Project, and Tape Title are locked for the active recording.

Changing the currently selected customer later must never change ownership of an existing recording.

### After capture

A successful capture should:

1. Finalize the MP4.
2. Add the recording to **Needs Review**.
3. Show a brief non-modal confirmation such as `✓ Unlabeled 14 saved — Needs Review`.
4. Reset/prep Capture for the next physical tape.
5. Never automatically open Review & Trim.

The operator normally ejects the tape, inserts the next tape, enters/confirms the title, and records again.

### Customer switching

Multiple customer projects may be active simultaneously.

Example: while processing a 40+ tape Johnson project, Smith drops off two tapes. The operator can switch Capture to Smith, capture both, finish Smith's review/delivery, then return to Johnson without disturbing Johnson's recordings or queue.

The Customer/Tape Title fields describe the **next recording** until Record is pressed. At capture start, those values are stamped onto the recording.

### Edit Details

An existing recording must support deliberate **Edit Details** for operator mistakes.

At minimum:

- Change customer/project.
- Change tape title.

If changing details requires moving/renaming files, TLCS must do so safely and request confirmation when appropriate.

---

## 6. Tape Titles, Unlabeled Numbering, and Duplicate Safety

### Labeled tapes

Use the title the operator enters, normally based on the physical tape label.

Examples:

- `Christmas 1998`
- `Disney Vacation`
- `Grandma's 70th Birthday`

### Unlabeled tapes

TLCS should automatically suggest the next available title for that project:

- `Unlabeled 1`
- `Unlabeled 2`
- `Unlabeled 3`

The operator may replace the suggestion with a labeled title at any time before recording.

### Deleted captures release the number

If `Unlabeled 3` is determined to be blank, TV, or otherwise unwanted and its capture is deleted, `Unlabeled 3` becomes available again.

Final deliverables should not contain unexplained numbering gaps caused by discarded captures.

Internally, TLCS may use permanent unique recording IDs. The human-facing Unlabeled number is a deliverable naming sequence, not the database identity.

### Duplicate title / rewrite behavior

TLCS must never silently overwrite an existing recording.

If the same Customer + Project + Tape Title already exists, offer:

- **Rewrite Existing**
- **Rename New Recording**
- **Cancel**

**Rewrite Existing** means the operator is intentionally redoing that tape. Require an **Are you sure?** confirmation before replacement.

**Rename New Recording** allows a legitimate second recording, with a suggested title such as `Christmas 2` if useful.

This simple duplicate workflow replaces the need for a separate failed-capture system.

### Filename sanitization

The Tape Title remains the single operator-facing title field.

TLCS must safely sanitize disk filenames when necessary:

- Preserve capitalization, spaces, and wording where possible.
- Replace/remove Windows-invalid characters (`\ / : * ? " < > |`).
- Trim invalid trailing spaces/periods.
- Protect against reserved Windows names.
- Do not arbitrarily shorten normal titles.
- Never allow sanitization to cause a silent overwrite.
- Preserve the original operator-entered title in metadata even if the disk filename must differ slightly.

---

## 7. Blank, TV, and Do Not Keep

A physical tape may be blank, contain TV recordings, or otherwise contain material the operator does not want to deliver.

Allow statuses/actions such as:

- **Blank**
- **TV Recording**
- **Do Not Keep**

These may permanently delete the completed capture, but deletion must always require confirmation.

Suggested confirmation:

```text
Delete this capture permanently?

This video will be removed from disk and cannot be recovered from TLCS.

Delete Capture | Cancel
```

**Cancel is the safe/default action.**

After confirmed deletion:

- Delete the MP4.
- Remove it from Needs Review.
- Consider the physical tape processed.
- Do not create an Originals copy.
- Release any Unlabeled number for reuse.

A lightweight internal history record may remain without affecting deliverable numbering.

---

## 8. Review & Trim 2.0

The next major Review & Trim redesign should emulate the simplicity of ArcSoft rather than presenting a complex editor.

### Normal workflow

**Find beginning → SET START → find ending → SET END → SAVE TRIM → confirm → next video**

### Primary interface

- One large video preview.
- One primary timeline.
- One playhead.
- One Start marker.
- One End marker.
- Play/Pause.
- **SET START**.
- **SET END**.
- **SAVE TRIM**.
- **NO TRIM NEEDED**.
- **SKIP FOR NOW**.

### Critical visual requirement

Dragging/scrubbing the timeline must make the visible video follow the playhead so the operator can visually locate the desired cut point.

The playhead and Start/End markers are independent. Moving around the video must not accidentally alter trim points.

### Fine adjustment

Fine adjustments should be available without permanently cluttering the interface, for example:

`−1 sec | −0.1 sec | +0.1 sec | +1 sec`

These can appear contextually for the selected Start or End marker.

### Simplify trim mode selection

The normal operator action should be **SAVE TRIM** rather than exposing large Fast Trim vs. Frame-Accurate buttons.

The appropriate trim implementation can be selected internally or exposed under Advanced only if genuinely necessary.

### Preserve proven safety underneath

Do not discard working backend behavior merely because the UI is simplified. Preserve:

- Originals preservation.
- Transactional replacement.
- Safe confirmations.
- Lossless/fast trimming capability.
- Frame-accurate capability where required.
- Review metadata/status.
- Customer/project/tape ownership.

---

## 9. Batch Review Queue

Review is global across active customers/projects, with filtering.

Example:

```text
NEEDS REVIEW (17)

Johnson Family
  Christmas 1998
  Unlabeled 1
  Unlabeled 2

Smith
  Birthday Party
  Unlabeled 1
```

Filter options:

- **All Customers**
- Individual customer/project filters.

This allows a small two-tape job to be finished quickly while a large 40+ tape project remains active.

### Automatic next item

After successful **Save Trim** or **No Trim Needed**, mark the recording complete and automatically load the next item in the current filtered Needs Review queue.

### Skip for Now

**SKIP FOR NOW** makes no file or status change beyond leaving the item Needs Review, then advances to the next item.

One difficult tape must not block the entire batch.

---

## 10. File Lifecycle

### Immediately after capture

```text
John Smith\08-16-2026\Christmas 1998.mp4
```

The database/status tracks **Needs Review**. A separate Needs Review filesystem folder is unnecessary.

### After trimming

```text
John Smith\08-16-2026\Christmas 1998.mp4
John Smith\08-16-2026\Originals\Christmas 1998.mp4
```

Top-level MP4 = finished customer deliverable.  
Originals = untouched original capture.

### No Trim Needed

The original capture itself is the final deliverable. Do not create an unnecessary Originals duplicate.

### Blank / TV / Do Not Keep

After confirmation, delete the capture. Do not create an Originals copy.

---

## 11. Project Completion

Do not infer project completion from a predetermined tape count. The operator decides when the customer/order is finished.

Provide **Finish Project**.

If work remains, warn clearly:

```text
John Smith
18 Complete
3 Needs Review

3 videos still need review.

View Needs Review | Finish Anyway | Cancel
```

When everything is complete:

```text
21 videos ready for delivery

Finish Project
```

Project completion must be reversible. If another tape is discovered for the same order, the project may be deliberately reopened.

For a later, separate drop-off, create a new project instead.

---

## 12. Delivery Workflow

Tape Lady normally provides the USB drive and labels it:

**TAPELADY**

Finished videos go directly to the **root of the USB**, not into a video/customer folder.

Example:

```text
TAPELADY (E:)\
├── Christmas 1998.mp4
├── Disney Vacation.mp4
├── Unlabeled 1.mp4
├── Audio CD 1\
├── Photos\
└── other customer material...
```

The USB may already contain audio-CD transfers, photo-CD content, scans, or other customer material added outside TLCS.

### Critical delivery rule

**TLCS adds finished videos to the USB. TLCS does not take ownership of or synchronize the USB.**

It must never delete, reorganize, or alter unrelated existing USB contents.

### Prepare Delivery

TLCS should:

1. Detect/select a removable drive labeled `TAPELADY`.
2. If multiple TAPELADY drives are connected, require explicit selection.
3. Show the number and total size of deliverable videos.
4. Verify sufficient free space.
5. Copy only completed customer-facing MP4s directly to the USB root.
6. Never copy `Originals`.
7. Never overwrite an existing filename silently.
8. Leave all unrelated existing USB contents untouched.
9. Verify copied files after transfer, ideally by hash comparison.
10. Report successful verification.

Example:

```text
Delivery Verified ✓
21 of 21 videos copied
21 of 21 files verified

John Smith is ready for pickup.
```

Delivery must never automatically delete computer-side master files.

---

## 13. Retention and Deletion

Retention begins from the **delivery date**, not capture date or project creation date.

Defaults:

- **Retention:** 6 months after delivery.
- **Advance warning:** 7 days before retention expiration.
- **Permanent deletion:** always requires final operator confirmation.
- **Extensions:** allowed.

### Advance warning

Approximately seven days before deletion is due, flag the project on the dashboard:

```text
Retention Ending Soon — John Smith
Scheduled deletion: February 20

Customer copy has been retained for six months.

Customer Contacted | Extend Retention | View Project
```

The purpose of the warning is to give the operator time to contact the customer and offer another USB copy before the retained media is gone.

### Expiration

Do not silently auto-delete customer media in the background.

At expiration, the project becomes **due for deletion** and requires final confirmation:

```text
Retention Period Expired — John Smith

This project's retained files are scheduled for deletion.
This will permanently delete the finished videos and Originals from this computer.

Delete Now | Extend 30 Days | Keep Until... | Cancel
```

After confirmed deletion:

- Delete finished video media.
- Delete Originals.
- Retain lightweight project history such as customer, project date, tape titles, delivery date, and deletion date.

Dashboard should show concise retention counts such as:

- Expiring Soon.
- Ready for Deletion.

---

## 14. Main Dashboard

TLCS is a media-production application, not a business-management dashboard.

Primary everyday actions:

- **CAPTURE**
- **REVIEW VIDEOS**
- **PREPARE DELIVERY**

Capture remains front and center.

Useful operational summaries may include:

```text
NEEDS REVIEW
Johnson       5
Smith         2

READY FOR DELIVERY
Williams      8 videos

ACTIVE CUSTOMERS
Johnson       12 Complete · 5 Review
Smith          0 Complete · 2 Review
Williams       8 Complete · Finished

RETENTION
Expiring Soon       2
Ready for Deletion  1
```

Technical/engineering diagnostics should not clutter the everyday dashboard.

### While recording

Show the active customer/project/title clearly, but lock them.

Review & Trim may be opened separately for completed recordings while another tape is recording, provided this does not interfere with capture stability.

---

## 15. Settings

Keep Settings intentionally small.

### Storage

**Capture Storage Location**  
Default: `C:\Users\sarat\Videos\Tape Lady Captures`

Changing this affects future projects and does not automatically move existing projects.

### Capture

- Default Video Device: `ezcap Video Grabber`
- Default Input: `Composite`

Known-good codec, FFmpeg, DirectShow pin, pixel-format, frame-rate, and routing implementation details should not be exposed as normal settings.

### Delivery

- Expected USB Label: `TAPELADY`
- Verify files after copying: **ON by default**.

### Retention

- Keep customer media: **6 months**.
- Warn before deletion: **7 days**.
- Show retention reminders on dashboard: **ON**.
- Permanent deletion always requires confirmation; do not offer an unsafe silent-delete mode.

### Application workflow

Useful defaults:

- Automatically suggest next Unlabeled number: **ON**.
- Automatically load next video during batch review: **ON**.
- Remember recent customer/project for convenience, but do not silently assume the last customer when starting a new capture session.

A safe startup affordance is:

`Recent Project: John Smith · 08-16-2026 — Resume`

rather than silently selecting it.

### Advanced diagnostics

If technical controls are ever required, keep them behind **Advanced Diagnostics**, separate from normal operator settings.

---

## 16. TLBS Boundary / Later Integration

TLCS should remain independently usable.

Eventually TLCS may integrate with Tape Lady Business Suite (TLBS) for customer/project synchronization and broader business workflow.

TLCS should not duplicate:

- Invoicing.
- Payments.
- Pricing/accounting.
- Full CRM/contact details.

Potential later integration may track that an order contains other media such as audio CDs or photo CDs, but TLCS does not need to process every media type merely because those files may share the delivery USB.

---

## 17. Implementation Milestones

Do **not** give Copilot this entire specification as a request to implement everything at once. Use it as authoritative context, then implement one milestone at a time.

### Milestone 1 — Customer + Project Foundation

- Customer → Project → Recording ownership.
- Exact-date projects.
- Returning customer defaults to New Project.
- Customer/project switching.
- Deliberate Edit Details.
- Preserve existing working capture behavior.

### Milestone 2 — Capture Naming & Safety

- Labeled/unlabeled titles.
- Per-project Unlabeled numbering.
- Deleted captures release numbers.
- Duplicate detection.
- Rewrite Existing / Rename New Recording / Cancel.
- Safe filename sanitization.

### Milestone 3 — Capture Workflow Polish

- Lock customer/project/title during recording.
- Completed captures quietly enter Needs Review.
- Non-modal saved confirmation.
- Immediately prepare for next tape.
- No forced review interruption.

### Milestone 4 — Review & Trim 2.0

- ArcSoft-style simplified UI.
- Reliable visual scrubbing.
- One timeline/playhead.
- Set Start / Set End.
- Fine adjustments.
- Save Trim.
- No Trim Needed.
- Skip for Now.
- Preserve proven trim/Originals safety underneath.

### Milestone 5 — Batch Review

- Global Needs Review queue.
- Customer/project filtering.
- Automatic next item.
- Blank / TV / Do Not Keep with safe deletion confirmation.

### Milestone 6 — Project Completion

- Finish Project.
- Warn if Needs Review remains.
- Reopen completed project deliberately.
- New project remains default for returning customers.

### Milestone 7 — Delivery

- Detect/select TAPELADY removable drive.
- Copy completed MP4s directly to USB root.
- Preserve existing USB contents.
- Exclude Originals.
- Capacity check.
- Collision/overwrite protection.
- Verification after copying.

### Milestone 8 — Retention

- Delivery-date retention tracking.
- Six-month default.
- Seven-day warning.
- Customer-contact reminder/status.
- Extensions.
- Final confirmation before media deletion.
- Retain lightweight history after deletion.

### Milestone 9 — Dashboard & Settings Polish

- Capture-first dashboard.
- Needs Review / Ready for Delivery / Active Project summaries.
- Retention reminders.
- Small Settings screen using the defaults in this specification.
- Keep diagnostics out of normal workflow.

### Later

- TLCS ↔ TLBS integration.
- Archive/vault integration.
- Expanded media tracking.
- Additional capture hardware.
- Enhancement/quality options.
- Revisit recording-preview choppiness only when it can be done without risking proven recording stability.

---

## 18. Mandatory Copilot Guardrails

Include these protections in every substantial implementation prompt:

> **DO NOT BREAK PROVEN CAPTURE FUNCTIONALITY.**
>
> Do not modify working capture/recording behavior unless this milestone explicitly requires it.
>
> Protect EZCAP DirectShow handling, VMR9 preview, fresh DirectShow session lifecycle, Composite/S-Video routing, proven 720×480 UYVY 29.97 input, embedded EZCAP audio, FFmpeg recording/finalization, return to normal preview, shutdown stability, Originals preservation, and transactional trim behavior.
>
> Prefer narrow changes. Do not refactor unrelated working code. Do not perform speculative cleanup. Do not launch the application unless explicitly instructed. Build after changes and report exact files changed. Do not commit or push until hardware acceptance testing passes and the operator explicitly approves it.

---

## 19. Development Method

For each milestone:

1. Start from a clean Git working tree.
2. Ask Copilot to read this specification and implement **one milestone only**.
3. Require a narrow pre-change audit of relevant files.
4. Preserve the DO NOT BREAK areas.
5. Build without launching.
6. Perform the smallest meaningful hardware/operator acceptance test.
7. If the test fails, fix only the demonstrated defect.
8. When the test passes, review the diff.
9. Commit only the milestone's substantive files.
10. Push the proven milestone.
11. Return to a clean Git state before beginning the next milestone.

The priority is not maximum feature velocity. The priority is **reliable production throughput with reversible, testable changes**.
