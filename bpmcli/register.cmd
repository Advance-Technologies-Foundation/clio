@echo off
setlocal EnableDelayedExpansion

set "pathToInsert=%cd%"

rem Check if pathToInsert is not already in system path
if "!path:%pathToInsert%=!" equ "%path%" (
   setx PATH "%PATH%;%pathToInsert%"
)