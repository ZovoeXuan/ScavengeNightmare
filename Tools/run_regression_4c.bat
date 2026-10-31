@echo off
rem ===========================================================================
rem  Sprint 4C regression runner.
rem
rem  Thin wrapper around run_regression.bat: same guards, same pair handling
rem  (s4bdownhost + s4bdownclient, s4clegclienthost + s4clegclient), same
rem  accounting. Only two things differ - the manifest and the terminal OK
rem  marker.
rem
rem  Rules:
rem  1) expected comes from the case-list row count. NEVER hardcode it here.
rem  2) The terminal marker of this gate is S4C_REGRESSION_OK.
rem  3) ASCII only in this file (cmd reads .bat in the OEM codepage).
rem ===========================================================================
setlocal

set "CASES=%~dp0regression_4c_cases.txt"
set "OKMARK=S4C_REGRESSION_OK"

call "%~dp0run_regression.bat" %*
set "GATE_CODE=%ERRORLEVEL%"

endlocal & exit /b %GATE_CODE%
