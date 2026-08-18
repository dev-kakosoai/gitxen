@echo off
rem Puts `gitxen` on the command line for every install channel. `start` hands the window off and
rem returns, so the shell it was launched from is not held open for the life of the application.
start "" "%~dp0GitExtensions.WinUI.exe" %*
