TAPE LADY CAPTURE SUITE — MILESTONE 5.1

Fixes in this build:

1. The Audio dropdown now always offers:
   Audio Pin Source — <selected EZCAP device>
   for EZCAP/USB analog grabbers, even when the driver hides that pin from
   normal DirectShow enumeration.

2. Recording now requests the same 720x480 YUY2 NTSC format used by the
   working live preview. This targets the green/corrupted strip at the bottom.

3. The recording-time preview is reduced to 8 fps at 480x360 and the main
   encoder uses the ultrafast preset. This lowers CPU load and should make the
   preview substantially smoother while preserving the recorded MP4 quality
   settings.

TEST:
- Select EZCAP Video Grabber.
- Select Audio Pin Source — EZCAP Video Grabber.
- Record 20-30 seconds.
- Confirm the preview has no green strip.
- Play the MP4 and confirm picture and sound.
