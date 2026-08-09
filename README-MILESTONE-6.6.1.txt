Tape Lady Capture Suite — Milestone 6.6.1

This patch contains only four changed files:
- MainForm.cs
- Services/DeviceService.cs
- DirectShowPreviewForm.cs
- Services/Capture/DirectShowPreviewSession.cs

Changes:
1. DirectShow test preview now preserves a centered 4:3 NTSC display aspect ratio.
2. The main Audio Source dropdown discovers embedded audio output pins on the selected video device by inspecting their DirectShow media types.
3. EZCAP will appear as "Audio Pin Source (ezcap Video Grabber)" and carry its real pin name (for this device, Output3).
4. DirectShow COM objects are released more aggressively when the preview window closes.
5. The PCM subtype uses an explicit GUID for compatibility with this DirectShowLib version.

Apply:
- Close Visual Studio.
- Copy these files into the folder containing TapeLadyCaptureSuite.csproj.
- Allow Windows to merge folders and replace the four destination files.
- Reopen TapeLadyCaptureSuite.csproj and run Build > Rebuild Solution.

Test:
- Open Hardware Diagnostics > Test DirectShow Preview and confirm the picture is no longer stretched.
- In the main window select ezcap Video Grabber and confirm the Audio Source list includes Audio Pin Source (ezcap Video Grabber).
