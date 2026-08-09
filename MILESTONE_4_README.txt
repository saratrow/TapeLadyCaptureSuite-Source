TAPE LADY CAPTURE SUITE — MILESTONE 4: ANALOG INPUT FIX

Purpose
- Fix the case where the webcam previews but the EZCAP opens without video.

Changes
- The Composite / RCA and S-Video dropdown is now functional.
- The program asks the EZCAP DirectShow crossbar to route the selected input.
- EZCAP preview requests the normal NTSC SD format: YUY2, 720x480, 30 fps.
- A clear warning appears when the device opens but supplies no frames.
- Device identity now includes the DirectShow device path for more reliable matching.

Test
1. Connect the EZCAP and VCR.
2. Connect yellow RCA video and select Composite / RCA.
3. Start the VCR playing a known-good tape.
4. Open the project and run it.
5. Select the EZCAP device if it is not selected automatically.
6. Click Start.
7. If no picture appears, select S-Video only when an S-Video cable is actually connected.

Do not have ArcSoft ShowBiz, Camera, OBS, or another capture program open during testing.
