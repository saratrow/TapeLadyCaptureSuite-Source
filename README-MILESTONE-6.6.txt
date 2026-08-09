Tape Lady Capture Suite — Milestone 6.6
LIVE EZCAP PCM AUDIO VERIFICATION

Files in this patch:
- DirectShowPreviewForm.cs (replace existing file)
- Services\Capture\DirectShowPreviewSession.cs (replace existing file)

What this adds:
- Connects the EZCAP embedded PCM audio pin in the same DirectShow graph as video.
- Displays a live audio level meter.
- Sends audio to a Null Renderer, so it will not play through speakers or create feedback.
- Does not record yet.

Test:
1. Close Visual Studio.
2. Copy these files into the folder containing TapeLadyCaptureSuite.csproj.
3. Reopen the project and select Build > Rebuild Solution.
4. Run TLCS > Hardware Diagnostics > Test DirectShow Preview.
5. Start a tape that contains sound.
6. Confirm video appears and the EZCAP AUDIO LEVEL meter moves.
