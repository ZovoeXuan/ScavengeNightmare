@echo off
rem ===========================================================================
rem  Sprint 4A regression runner (native Windows)
rem
rem  Rules for whoever edits this next:
rem  1) expected comes from the case-list file line count. NEVER hardcode it:
rem     a deleted case must turn the run red, not silently pass.
rem  2) Every case must match its single terminal marker (S4A_*_OK).
rem  3) Every case gets its own save dir. A shared dir makes the previous
rem     balance the next run's starting point (Story 2 was bitten by this).
rem  4) No "clear the log dir first" step. Bulk deletes get blocked by policy
rem     and the script keeps going with stale data. Use a timestamp dir.
rem  5) ASCII only in this file. cmd reads .bat in the OEM codepage; UTF-8
rem     Chinese comments turn into garbage that can contain command separators
rem     (that actually broke a run: half the manifest was silently skipped).
rem ===========================================================================
setlocal enabledelayedexpansion

set "ROOT=%~dp0.."
set "EXE=%ROOT%\Builds\Sprint4A\ScavengeNightmare.exe"
set "CASES=%~dp0regression_4a_cases.txt"
set "STAMP=%DATE:~0,4%%DATE:~5,2%%DATE:~8,2%-%TIME:~0,2%%TIME:~3,2%%TIME:~6,2%"
set "STAMP=%STAMP: =0%"
set "BASE=%ROOT%\Logs\s4a_reg\%STAMP%"

echo RUN_DIR=%BASE%

if not exist "%EXE%" (
  echo S4A_REGRESSION_FAIL reason=exe_missing path="%EXE%"
  exit /b 2
)

if not exist "%CASES%" (
  echo S4A_REGRESSION_FAIL reason=cases_missing path="%CASES%"
  exit /b 2
)

if not exist "%BASE%" mkdir "%BASE%"

set /a PASS=0
set /a FAIL=0
set /a EXPECTED=0

for /f "usebackq eol=# tokens=1,2,3 delims=|" %%A in ("%CASES%") do (
  if not "%%~A"=="" (
    set /a EXPECTED+=1
    call :run_case "%%~A" "%%~B" "%%~C"
  )
)

echo.
echo ============================================================
if %FAIL% EQU 0 if %PASS% EQU %EXPECTED% (
  echo S4A_REGRESSION_OK passes=%PASS% expected=%EXPECTED%
  endlocal
  exit /b 0
)

echo S4A_REGRESSION_FAIL passes=%PASS% expected=%EXPECTED% failures=%FAIL%
endlocal
exit /b 1

rem ---------------------------------------------------------------------------
:run_case
set "NAME=%~1"
set "ARGS=%~2"
set "MARK=%~3"
set "LOG=%BASE%\%NAME%.log"

echo.
echo --- %NAME%  %ARGS%

rem Kill leftovers before starting: a stale process still holds the KCP port and starves
rem the first frames of the next case (seen as A4 pushing only 0.48m).
taskkill /F /IM ScavengeNightmare.exe >nul 2>&1

"%EXE%" %ARGS% -feel=light -s4afixedyaw -savedir="%BASE%\%NAME%_save" -logFile "%LOG%" >nul 2>&1
set "CODE=%ERRORLEVEL%"

findstr /c:"S4A_NEUTRAL_OK traits=none feel=light" "%LOG%" >nul 2>&1
if errorlevel 1 (
  echo   FAIL  %NAME%  reason=neutral_precondition_missing
  set /a FAIL+=1
  goto :eof
)

if not "%CODE%"=="0" (
  if "%CODE%"=="4" (
    echo   FAIL  %NAME%  class=exception exit=4
  ) else (
    echo   FAIL  %NAME%  class=assert exit=%CODE%
  )
  set /a FAIL+=1
  echo   FAIL  %NAME%  class=assert exit=%CODE% >>"%BASE%\summary.txt"
  goto :eof
)

findstr /c:"%MARK%" "%LOG%" >nul 2>&1
if errorlevel 1 (
  echo   FAIL  %NAME%  class=marker_missing mark=%MARK%
  set /a FAIL+=1
  echo   FAIL  %NAME%  class=marker_missing mark=%MARK% >>"%BASE%\summary.txt"
  goto :eof
)

echo   PASS  %NAME%
set /a PASS+=1
echo   PASS  %NAME% >>"%BASE%\summary.txt"
goto :eof
