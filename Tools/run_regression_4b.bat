@echo off
rem ===========================================================================
rem  Sprint 4B regression runner.
rem
rem  Thin wrapper around run_regression.bat: same guards, same pair handling
rem  (s4bdownhost + s4bdownclient), same accounting. Only two things differ -
rem  the manifest and the terminal OK marker.
rem
rem  Rules:
rem  1) expected comes from the case-list row count. NEVER hardcode it here.
rem  2) The terminal marker of this gate is S4B_REGRESSION_OK.
rem  3) ASCII only in this file (cmd reads .bat in the OEM codepage).
rem ===========================================================================
setlocal

set "CASES=%~dp0regression_4b_cases.txt"
set "OKMARK=S4B_REGRESSION_OK"

call "%~dp0run_regression.bat" %*
set "GATE_CODE=%ERRORLEVEL%"

endlocal & exit /b %GATE_CODE%
