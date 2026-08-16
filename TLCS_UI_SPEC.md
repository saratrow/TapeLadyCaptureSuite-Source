# Tape Lady Capture Suite — UI Specification

**Status:** Approved visual direction  
**Date:** August 16, 2026  
**Companion:** `TLCS_MASTER_SPEC.md`

This is the visual/UI authority for TLCS. `TLCS_MASTER_SPEC.md` remains authoritative for workflow, file handling, capture behavior, retention, and safety. If a mockup conflicts with the written specifications, the written specification wins.

## 1. Design Language
- Dark charcoal/black professional production interface.
- Tape Lady red for branding, recording, destructive actions, and critical warnings.
- Green for ready/complete/verified states and positive actions.
- Amber/yellow for Needs Review, Skip, Expiring Soon, and attention states.
- Blue/neutral for informational or Extended states.
- High-contrast, readable text and large controls.
- Do not rely on color alone; include labels/icons.
- Everyday screens should hide DirectShow, FFmpeg, VMR9, codec, pin-routing, and other engineering details. Put those under Diagnostics/Advanced.

Guiding feel: **ArcSoft-simple on the surface; TLCS-safe underneath.**

## 2. Persistent Background Recording Bar
Whenever capture is active, show a persistent bar throughout TLCS:

`● RECORDING   John Smith · Unlabeled 4   00:43:17   [RETURN TO CAPTURE]`

Show customer, tape title, elapsed time, red recording indicator, and Return to Capture. Active recording metadata/settings that could affect capture are locked. Other screens must never restart, reconfigure, or compete with the active capture session.

## 3. Main Capture
Capture remains the primary production screen.

Show:
- Customer Name selector and New Customer
- Current Project date
- Tape Title and suggested next `Unlabeled #`
- Optional tape notes
- Large live preview
- Large Start/Stop Recording control
- Simple input/audio readiness
- Needs Review and Ready for Delivery summaries
- Quick access to Review Videos and Prepare Delivery

During recording, lock Customer, Project, Tape Title, device/input settings, and emphasize preview plus elapsed time.

After recording: finalize MP4 → Needs Review → show a temporary non-modal confirmation such as `✓ Unlabeled 4 saved — Needs Review` → immediately prepare for the next tape. Never force Review & Trim open.

Default capture root:
`C:\Users\sarat\Videos\Tape Lady Captures`

## 4. Review & Trim 2.0
Approved workflow:

**Find beginning → Set Start → Find ending → Set End → Save Trim → Next**

Layout:
- Review queue visible on left.
- Large preview dominates.
- One primary visual timeline.
- Distinct playhead.
- Green Start marker and red End marker.
- Simple playback controls.
- Large Set Start, Set End, No Trim Needed, Skip for Now, and Save Trim actions.

Fine adjustment must include:
`−1 sec | −0.1 sec | +0.1 sec | +1 sec`

### Critical requirement
Dragging/scrubbing the primary timeline must make the **visible video follow the playhead while dragging**. Timestamp-only or lower-slider-only movement is not acceptable.

Batch behavior:
- Filter All Customers or one customer/project.
- Save Trim → confirm → complete → auto-load next Needs Review item.
- No Trim Needed → confirm → complete → next.
- Skip for Now → no changes → remains Needs Review → next.
- Blank / TV / Do Not Keep available with safe permanent-deletion confirmation.

Do not clutter routine review with permanent technical-information, marker-management, codec, or audio-meter panels.

## 5. Customers / Projects
Structure:

**Customer → Project/Order → Recordings**

Customers use full names. Projects are automatically dated, e.g. `08-16-2026`; on screen TLCS may show `John Smith · 08-16-2026`.

Show:
- Searchable customer list
- New Customer
- New Project
- Project history/status/counts
- Resume active project
- View completed project
- Deliberate Reopen Project

Returning customers get **New Project by default**. Completed projects remain closed/history unless deliberately reopened.

Do not prominently expose Delete Project. If supported, place it in a secondary menu and require confirmation.

Do not add phone, email, address, pricing, payments, invoices, or bookkeeping fields.

## 6. Prepare Delivery
Approved flow:

**1. Review Finished Videos → 2. Select TAPELADY USB → 3. Copy & Verify**

Show customer/project, deliverable-video list, count/size, destination USB/free space, large Copy & Verify action, progress, and verification result.

Clearly state:
**Existing files and folders will remain untouched.**

Rules reflected in UI:
- Expected USB label: `TAPELADY`
- Finished MP4s copy directly to USB root
- No customer/video folder
- No playback menu
- No PDF video list unless requested later
- Never copy `Originals`
- Never silently overwrite
- Never synchronize/delete unrelated USB contents
- Verify copied files
- If multiple TAPELADY drives exist, require explicit selection

Photos, audio-CD material, documents, and other existing customer files remain untouched.

## 7. Settings
Keep Settings intentionally small.

### Storage
Default: `C:\Users\sarat\Videos\Tape Lady Captures`
Changing it affects future projects only; do not automatically move existing projects.

### Capture
Remember known-good device/input configuration. Do not casually expose working capture internals.

### Delivery
- USB label: `TAPELADY`
- Verify after copy: ON by default

### Retention
- 6 months **after delivery**
- Warn 7 days before
- Dashboard reminders ON
- Final confirmation before permanent deletion mandatory

### Workflow
- Suggest next Unlabeled number ON
- Auto-load next batch-review item ON
- Remember recent customer/project for visible Resume convenience

Do not silently select yesterday's customer/project in a way that could misfile a new capture.

## 8. Retention & Reminders
Retention is based on **delivery date**, not capture date, project start date, or last activity.

States:
- Safe — green
- Expiring Soon — amber/yellow
- Due for Deletion — red
- Extended — blue/neutral

Useful information:
Customer, project date, delivery date, expiration date, status, time remaining/overdue, storage size, actions.

Actions as appropriate:
- View Project
- Customer Contacted
- Extend Retention
- Delete Now

“Auto deletion” means automatically becoming **due for deletion**, never silent background erasure. At expiration require Delete Now / Extend 30 Days / Keep Until... / Cancel. After confirmed media deletion, retain lightweight project history.

Any mockup saying “6 months after last activity” is incorrect. The rule is **6 months after delivery with a 7-day warning**.

## 9. Dashboard
Operational, not managerial. It should answer:
- What am I recording?
- What needs review?
- Which projects are active?
- What is ready for delivery?
- What is approaching retention expiration?

Primary navigation:
Capture; Review Videos; Prepare Delivery; Customers / Projects; Retention / Reminders; Settings; Diagnostics.

## 10. Confirmation Philosophy
Require confirmations where an accidental click could destroy or materially alter customer work: destructive trim replacement where applicable, Delete Capture, Blank/TV/Do Not Keep, Rewrite Existing, retained-media deletion, and consequential project deletion/reopen actions.

For destructive operations, Cancel/safe choice is default. Do not add confirmation dialogs to harmless repetitive actions that slow throughput.

## 11. Background Work
While a tape records, the operator may review/trim completed recordings, work another customer's queue, view projects, prepare another customer's delivery, use safe settings/screens, and use other Windows apps.

Nothing elsewhere in TLCS may restart, reconfigure, take ownership of, or otherwise interfere with the active capture device/session.

## 12. Approved Screen Set
Approved visual directions:
1. Main Capture
2. Review & Trim 2.0
3. Prepare Delivery
4. Customers / Projects
5. Settings
6. Retention & Reminders
7. Persistent Background Recording Bar

These are visual references, not pixel-perfect contracts.

## 13. DO NOT BREAK
UI work must not casually refactor the proven capture/recording pipeline. Protect:
- EZCAP DirectShow handling
- VMR9 preview
- Fresh DirectShow session lifecycle
- Composite/S-Video routing
- Known-good 720×480 UYVY 29.97 recording path
- Embedded EZCAP audio
- FFmpeg recording/finalization
- Return to normal preview
- Shutdown stability
- Originals preservation
- Transactional trim behavior

Prefer narrow UI/data-model changes. Do not change proven capture internals merely to make the interface easier to implement.

## 14. Guiding Principle
TLCS should feel like a purpose-built Tape Lady production workstation:

**Capture fast. Review simply. Deliver safely.**

The operator sees what is needed to do the job; complicated technical machinery stays safely underneath.
