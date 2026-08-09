TAPE LADY CAPTURE SUITE — MILESTONE 5
EZCAP AUDIO PIN SUPPORT

What changed
------------
• The Audio dropdown is now labeled Audio Source.
• The selected video capture device is inspected for built-in audio output pins.
• A pin such as "Audio Pin Source" should appear at the top of the list.
• Normal Windows recording devices remain available as fallback choices.
• Recording now tells FFmpeg which DirectShow audio pin to use.

Test
----
1. Close ArcSoft, Camera, OBS, and other capture programs.
2. Connect the red and white RCA audio cables to the EZCAP.
3. Play a tape that definitely contains sound.
4. Select the EZCAP under Video.
5. Under Audio Source, choose the entry beginning with "Audio Pin Source".
6. Record 15–30 seconds.
7. Stop and play the saved MP4.

Important
---------
The current milestone captures sound into the MP4. Live sound monitoring and
an audio level meter are planned next after capture audio is confirmed.
