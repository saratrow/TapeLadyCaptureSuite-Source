Tape Lady Capture Suite — Milestone 6.5.1
NTSC video-standard correction

FILES IN THIS PATCH
- DirectShowPreviewForm.cs (replace existing file)
- Services\Capture\DirectShowPreviewSession.cs (replace existing file)

WHAT CHANGED
The DirectShow preview now asks the EZCAP driver to use NTSC-M before the
preview graph is rendered. A PAL/NTSC height mismatch commonly produces a
large solid-green band below otherwise-correct NTSC video.

The preview status line also reports the video standard selected by the driver.

INSTALL
1. Close Visual Studio.
2. Copy these files into the project folder containing TapeLadyCaptureSuite.csproj.
3. Allow Windows to merge folders and replace the two existing files.
4. Reopen TapeLadyCaptureSuite.csproj.
5. Build > Rebuild Solution.
6. Run Hardware Diagnostics > Test DirectShow Preview.

TEST
Confirm the error count first. Then start the EZCAP preview and report:
- the video-standard text shown at the bottom
- whether the green band is gone, smaller, or unchanged
