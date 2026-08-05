@echo off
title OpenTransfer Release Publisher
powershell -ExecutionPolicy Bypass -File "%~dp0publish_release.ps1"
pause
