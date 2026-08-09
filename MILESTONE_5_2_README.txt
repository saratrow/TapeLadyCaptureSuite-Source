TAPE LADY CAPTURE SUITE — MILESTONE 5.2

What changed
- Removed the fake "Audio Pin Source — EZCAP Video Grabber" device.
- The prior build treated ArcSoft's pin label as an FFmpeg audio device name.
  This caused video=ezcap Video Grabber:audio=ezcap Video Grabber to fail.
- Audio dropdown now combines DirectShow audio devices with FFmpeg's own
  device scan, so only audio sources FFmpeg can actually address are offered.

Test
1. Build and run.
2. Click Refresh.
3. Open the Audio dropdown and note every option shown.
4. Select the EZCAP-related entry if one appears and make a short recording.
5. If no EZCAP audio entry appears, send a screenshot of the full dropdown.
