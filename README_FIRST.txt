TAPE LADY CAPTURE SUITE — MILESTONE 2
=====================================

THIS BUILD RECORDS REAL MP4 FILES
---------------------------------
Milestone 2 adds:

• Real MP4 recording from a DirectShow video capture device
• Audio capture from the selected DirectShow audio device
• Live preview while recording
• Pause and Resume
• Automatic joining of paused recording segments
• Customer folders and tape-label filenames
• Recording timer
• File-size display
• Dropped-frame display
• Automatic preference for EZCAP / USB video devices
• Safe finalization when Stop is pressed
• A warning if the program is closed while recording

REQUIRED RECORDING ENGINE
-------------------------
This build uses FFmpeg as the dependable recording engine.

The program checks automatically. If FFmpeg is missing:

1. Click "Install FFmpeg" inside Tape Lady Capture Suite.
2. Follow the Windows Package Manager window.
3. Close and reopen the app if it does not detect FFmpeg immediately.

You can also double-click:
    INSTALL_FFMPEG.cmd

OPEN IN VISUAL STUDIO
---------------------
1. Extract the ZIP to a normal folder, such as:
      C:\Projects\TapeLadyCaptureSuite_Milestone2

2. Open:
      TapeLadyCaptureSuite.csproj

3. Allow Visual Studio to restore the NuGet packages.

4. Select:
      Build > Rebuild Solution

5. Press:
      Ctrl + F5

SAFE FIRST TEST
---------------
Do NOT use an irreplaceable customer tape for the first test.

1. Connect EZCAP.
2. Close ArcSoft, Camera, OBS, and all other capture programs.
3. Open Tape Lady Capture Suite.
4. Confirm EZCAP is selected under Video.
5. Select the EZCAP/USB audio device under Audio.
6. Enter:
      Customer: TEST
      Tape Label: TEST CAPTURE 001
7. Choose a save folder.
8. Start a tape that is safe to test.
9. Record for 60 seconds.
10. Test Pause, wait 5 seconds, and Resume.
11. Press Stop.
12. Open and watch the completed MP4.
13. Confirm picture, sound, sync, pause join, and filename.

OUTPUT
------
Default format:
• MP4
• H.264 video
• AAC audio
• 640 × 480 square-pixel 4:3 SD
• Deinterlaced for normal playback on modern TVs/computers
• Quality-based encoding with a 3.5 Mbps ceiling
• 160 kbps stereo audio

The original VHS is SD. This build intentionally does not inflate it to HD.

PAUSE / RESUME
--------------
Pause safely closes the current MP4 segment.
Resume begins another matching segment.
Stop joins all segments without re-encoding them.

TEMPORARY FILES
---------------
A hidden ".tlcapture_..." working folder is created next to the output file
while recording. It is deleted after the MP4 is successfully finalized.

Do not manually delete that folder while recording.

KEYBOARD
--------
F11       Full-screen preview
Escape    Leave full-screen preview
Space     Record / Pause / Resume

IMPORTANT MILESTONE LIMIT
-------------------------
The Composite / RCA and S-Video selector is present in the interface, but
some EZCAP drivers do not expose crossbar switching consistently through
modern APIs. Confirm that your EZCAP is physically connected to the input
you intend to use. Driver-level input switching will be finalized after
testing your exact device.

FIRST CUSTOMER USE
------------------
Do not use this build for customer work until the 60-second test and at
least one full-length expendable tape have completed successfully.
