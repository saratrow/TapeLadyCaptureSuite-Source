TAPE LADY CAPTURE SUITE — MILESTONE 5 FIX

This build fixes three issues reported during testing:

1. Typing spaces in Customer, Tape Label, or Notes no longer starts recording.
   Recording and stopping are now controlled only by the on-screen buttons.

2. Finalizing a recording no longer writes a hidden UTF-8 BOM into segments.txt.
   That hidden character caused FFmpeg to report “Line 1: unknown keyword” and fail to join segments.

3. The recording-time preview output now explicitly uses yuvj420p for MJPEG.
   This is intended to eliminate the green line/corrupted strip seen at the bottom of the preview.

TEST:
- Rebuild and run.
- Type a customer name containing spaces; recording should not begin.
- Record 20–30 seconds, then click Stop.
- Confirm the MP4 saves and plays.
- Confirm whether the green strip is gone.
