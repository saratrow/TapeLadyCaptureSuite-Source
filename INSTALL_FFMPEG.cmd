@echo off
title Install FFmpeg for Tape Lady Capture Suite
echo.
echo Installing FFmpeg with Windows Package Manager...
echo.
winget install --id Gyan.FFmpeg -e --accept-package-agreements --accept-source-agreements
echo.
echo Installation finished.
echo Close and reopen Tape Lady Capture Suite.
echo.
pause
