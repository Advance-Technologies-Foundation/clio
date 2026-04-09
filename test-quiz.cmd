@echo off
REM Test script for Creatio Quiz Game
REM This opens a new console window with the correct size

cd /d "%~dp0clio\bin\Debug\net8.0"

REM Set console size: width=96, height=36
mode con: cols=96 lines=36

REM Clear screen
cls

REM Run the quiz game
clio.exe quiz

REM Pause at the end if there was an error
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Command failed with exit code: %ERRORLEVEL%
    pause
)
