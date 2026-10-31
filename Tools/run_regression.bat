@echo off
rem ===========================================================================
rem  Full regression runner: Sprint 4A + Sprint 4B (native Windows)
rem
rem  Rules for whoever edits this next:
rem  1) expected comes from the case-list file line count. NEVER hardcode it:
rem     a deleted case must turn the run red, not silently pass.
rem  2) Every case must match its single terminal marker (S4A_*_OK / S4B_*_OK).
rem  3) Every case gets its own save dir. A shared dir makes the previous
rem     balance the next run's starting point (Story 2 was bitten by this).
rem  4) No "clear the log dir first" step. Bulk deletes get blocked by policy
rem     and the script keeps going with stale data. Use a timestamp dir.
rem  5) ASCII only in this file. cmd reads .bat in the OEM codepage; UTF-8
rem     Chinese comments turn into garbage that can contain command separators
rem     (that actually broke a run: half the manifest was silently skipped).
rem  6) Every case is preceded by taskkill: a stale process still holds the
rem     KCP port and starves the first frames of the next case.
rem  7) Cases are separated by a ping cooldown so the port is really released
rem     before the next Unity instance binds it.
rem
rem  Added on top of run_regression_4a.bat for Sprint 4B:
rem  8) -s4bdownhost / -s4bdownclient are ONE acceptance case that needs TWO
rem     processes alive at the same time (host listens, client connects).
rem     The host row launches the exe DETACHED; the runner then waits until the
rem     host log shows "S4A_BOOT_OK spawned=1" (that line is only printed after
rem     NetworkServer.active is true, i.e. the host is really listening) before
rem     starting the client row. No taskkill happens between the two rows or
rem     the host would be killed mid-run. Both halves are then judged with the
rem     exact same rules as a single-process case: neutral precondition,
rem     exit code class (0 ok / 1 assert / 3 watchdog / 4 exception) and the
rem     terminal marker. The host exit code is captured through a generated
rem     wrapper because "start /B" does not give it back to us.
rem
rem  Baseline kept untouched on purpose: Tools/run_regression_4a.bat still
rem  runs the 4A-only matrix and is the rollback path if this file misbehaves.
rem
rem  EXIT CODES - three completely different situations, do not mix them up:
rem    exit 0 = every case green
rem    exit 1 = at least one case RED (real bug or a genuinely unstable case;
rem             go read the case and the implementation)
rem    exit 2 = STOPPED BY A GUARD, before any case ran (stale package, or the
rem             exe / case list is missing). This is NOT a failing case.
rem             Rebuild first, then run again.
rem    exit 4 = runtime exception caught by S4ATestGuard
rem  Anyone wiring this into a script or CI must branch on exit 2 separately:
rem  treating "you forgot to rebuild" as "a test failed" sends people hunting
rem  for a bug that does not exist.
rem  Related: git pull / git checkout refresh the mtime of the touched files,
rem  so the stale guard will fire right after a pull until you rebuild. That is
rem  the intended behaviour, not a false alarm.
rem
rem  STALE GUARD - known limits (do not assume it is stronger than it is):
rem    a) %%~tF only resolves to the MINUTE. If a source edit and the build land
rem       in the same minute, src == bin and the guard cannot detect staleness.
rem       This is a ceiling of %%~tF, not a bug.
rem    b) The 12-char key is built by slicing a fixed-width "YYYY/MM/DD HH:MM"
rem       string, so it also works on locales that print YYYY-MM-DD. It BREAKS
rem       on MM/DD/YYYY or on formats with a weekday prefix. %%~tF prints no
rem       weekday on this machine (verified); re-verify after moving to another
rem       machine or locale.
rem    c) .meta files are deliberately NOT scanned. Unity rewrites them on every
rem       import (839 of them here), so a plain editor launch would flag the
rem       package stale and stop all cases while the package is in fact fresh.
rem       Fail-closed is carried by "package age = newest of three build
rem       artifacts" + "sources = newest of five hand-written extensions" +
rem       limit (a) above, not by counting Unity's bookkeeping files.
rem ===========================================================================
setlocal enabledelayedexpansion

set "ROOT=%~dp0.."
set "EXE=%ROOT%\Builds\Sprint4A\ScavengeNightmare.exe"
set "CASES=%~dp0regression_cases.txt"
set "STAMP=%DATE:~0,4%%DATE:~5,2%%DATE:~8,2%-%TIME:~0,2%%TIME:~3,2%%TIME:~6,2%"
set "STAMP=%STAMP: =0%"
set "BASE=%ROOT%\Logs\s4_reg\%STAMP%"

echo RUN_DIR=%BASE%

if not exist "%EXE%" (
  echo S4_REGRESSION_FAIL reason=exe_missing path="%EXE%"
  exit /b 2
)

if not exist "%CASES%" (
  echo S4_REGRESSION_FAIL reason=cases_missing path="%CASES%"
  exit /b 2
)

rem --- stale binary guard ---------------------------------------------------
rem A green run against an old binary is worse than a red run: every case below
rem would be exercised on code that is not in the package. Turn "did you
rem rebuild?" from a discipline into a machine check.
rem
rem MEASURED ON THIS PROJECT: Unity's incremental build does NOT rewrite the
rem player .exe stub when only managed code changed - the stub stayed at
rem 2026/10/29 15:12 while ScavengeNightmare_Data\Managed\Assembly-CSharp.dll
rem moved to 2026/10/31 01:07. Comparing the exe alone would therefore flag
rem EVERY run as stale and lock the gate shut. The package age is taken as the
rem NEWEST of exe / managed assembly / globalgamemanagers, which all move on a
rem real rebuild.
rem
rem Timestamps come from %%~tF (fixed width "YYYY/MM/DD HH:MM"), folded into a
rem zero-padded 12-char key so a lexicographic compare is chronological.
set "BINDATA=%ROOT%\Builds\Sprint4A\ScavengeNightmare_Data"
set "BINKEY=0"
set "SRCKEY=0"

for %%F in ("%EXE%" "%BINDATA%\Managed\Assembly-CSharp.dll" "%BINDATA%\globalgamemanagers") do (
  if exist "%%~F" (
    set "RAW=%%~tF"
    set "KEY=!RAW:~0,4!!RAW:~5,2!!RAW:~8,2!!RAW:~11,2!!RAW:~14,2!"
    if "!KEY!" GTR "!BINKEY!" set "BINKEY=!KEY!"
  )
)

rem  Sources = the five kinds of hand-written asset. NO .meta here on purpose:
rem  Unity rewrites .meta on import, and 839 of them would turn this guard into
rem  a false-alarm machine that stops the whole matrix while the package is new.
for /r "%ROOT%\Assets" %%F in (*.cs *.prefab *.unity *.asset *.mat *.physicMaterial) do (
  set "RAW=%%~tF"
  set "KEY=!RAW:~0,4!!RAW:~5,2!!RAW:~8,2!!RAW:~11,2!!RAW:~14,2!"
  if "!KEY!" GTR "!SRCKEY!" set "SRCKEY=!KEY!"
)

if "%SRCKEY%" GTR "%BINKEY%" (
  echo S4_REGRESSION_FAIL reason=stale_binary src=%SRCKEY% bin=%BINKEY%
  echo   Assets are newer than the built package - rebuild first:
  echo   Unity -batchmode -quit -projectPath "%ROOT%" -executeMethod BuildWindows.Build -map=sprint4a
  exit /b 2
)

echo STALE_CHECK_OK src=%SRCKEY% bin=%BINKEY%

if not exist "%BASE%" mkdir "%BASE%"

rem --- detach wrapper -------------------------------------------------------
rem "start /B" swallows the child exit code, so the host writes its own into a
rem file. ">" and "&" are emitted through delayed expansion (!GT!) because a
rem literal one would be eaten as a redirection operator while generating.
set "GT=>"
set "HOSTWRAP=%BASE%\_hostwrap.cmd"
>  "%HOSTWRAP%" echo @echo off
>> "%HOSTWRAP%" echo "%EXE%" %%RG_ARGS%% !GT!nul 2!GT!^&1
>> "%HOSTWRAP%" echo !GT!!GT!"%%RG_CODE%%" echo %%ERRORLEVEL%%

set /a PASS=0
set /a FAIL=0
set /a EXPECTED=0
set /a PASS4A=0
set /a FAIL4A=0
set /a PASS4B=0
set /a FAIL4B=0
set "PAIRPENDING="

echo RUN_DIR=%BASE% cases="%CASES%" >"%BASE%\summary.txt"

for /f "usebackq eol=# tokens=1,2,3 delims=|" %%A in ("%CASES%") do (
  if not "%%~A"=="" (
    set /a EXPECTED+=1
    call :run_case "%%~A" "%%~B" "%%~C"
  )
)

if defined PAIRPENDING (
  echo.
  echo   FAIL  %HOSTNAME%  reason=host_row_without_client_row
  set /a FAIL+=1
  set /a FAIL4B+=1
  echo   FAIL  %HOSTNAME%  reason=host_row_without_client_row >>"%BASE%\summary.txt"
  set "PAIRPENDING="
)

echo.
echo ============================================================
echo S4_REGRESSION_BREAKDOWN s4a pass=!PASS4A! fail=!FAIL4A! s4b pass=!PASS4B! fail=!FAIL4B!
if %FAIL% EQU 0 if %PASS% EQU %EXPECTED% (
  echo S4_REGRESSION_OK passes=%PASS% expected=%EXPECTED%
  endlocal
  exit /b 0
)

echo S4_REGRESSION_FAIL passes=%PASS% expected=%EXPECTED% failures=%FAIL%
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

rem Pair: the row right after -s4bdownhost is the client that drives it.
if defined PAIRPENDING goto :pair_client

if not "%ARGS:-s4bdownhost=%"=="%ARGS%" (
  call :start_host
  goto :eof
)

taskkill /F /IM ScavengeNightmare.exe >nul 2>&1
ping -n 3 127.0.0.1 >nul
call :run_and_check
goto :eof

rem ---------------------------------------------------------------------------
rem Half 1 of the revive pair: launch the host detached, wait until it listens.
:start_host
taskkill /F /IM ScavengeNightmare.exe >nul 2>&1
ping -n 3 127.0.0.1 >nul

set "HOSTNAME=%NAME%"
set "HOSTLOG=%LOG%"
set "HOSTMARK=%MARK%"
set "HOSTCODE=%BASE%\%NAME%.exitcode"
set "RG_ARGS=%ARGS% -feel=light -s4afixedyaw -savedir="%BASE%\%NAME%_save" -logFile "%LOG%""
set "RG_CODE=%HOSTCODE%"

if exist "%HOSTCODE%" del "%HOSTCODE%"
start "" /B cmd /c "%HOSTWRAP%"

set "PAIRPENDING=1"
set /a READYWAIT=0

:host_ready
findstr /c:"S4A_BOOT_OK spawned=1" "%HOSTLOG%" >nul 2>&1
if not errorlevel 1 goto :host_ready_done
if exist "%HOSTCODE%" goto :host_ready_done
ping -n 2 127.0.0.1 >nul
set /a READYWAIT+=1
if !READYWAIT! LSS 60 goto :host_ready

echo   FAIL  %HOSTNAME%  reason=host_never_reached_server_active
set /a FAIL+=1
set /a FAIL4B+=1
echo   FAIL  %HOSTNAME%  reason=host_never_reached_server_active >>"%BASE%\summary.txt"
set "PAIRPENDING="
goto :eof

:host_ready_done
echo   host listening (S4A_BOOT_OK spawned=1), client row next
goto :eof

rem ---------------------------------------------------------------------------
rem Half 2 of the revive pair: run the client, then judge the host log too.
:pair_client
set "PAIRPENDING="
call :run_and_check
call :finish_host
goto :eof

rem ---------------------------------------------------------------------------
:finish_host
set /a HWAIT=0

:host_wait
if exist "%HOSTCODE%" goto :host_done
ping -n 2 127.0.0.1 >nul
set /a HWAIT+=1
if !HWAIT! LSS 150 goto :host_wait

echo   FAIL  %HOSTNAME%  reason=host_never_exited
set /a FAIL+=1
set /a FAIL4B+=1
echo   FAIL  %HOSTNAME%  reason=host_never_exited >>"%BASE%\summary.txt"
goto :eof

:host_done
set "HCODE="
set /p HCODE=<"%HOSTCODE%"
if "%HCODE%"=="" set "HCODE=empty"
set "NAME=%HOSTNAME%"
set "LOG=%HOSTLOG%"
set "MARK=%HOSTMARK%"
echo   host exited code=%HCODE%
call :check_log "%HCODE%"
goto :eof

rem ---------------------------------------------------------------------------
:run_and_check
"%EXE%" %ARGS% -feel=light -s4afixedyaw -savedir="%BASE%\%NAME%_save" -logFile "%LOG%" >nul 2>&1
set "CODE=%ERRORLEVEL%"
call :check_log "%CODE%"
goto :eof

rem ---------------------------------------------------------------------------
:check_log
set "CODE=%~1"
set "GROUP=4A"
if /i "%NAME:~0,3%"=="s4b" set "GROUP=4B"

findstr /c:"S4A_NEUTRAL_OK traits=none feel=light" "%LOG%" >nul 2>&1
if errorlevel 1 (
  rem Triage hint only, verdict stays FAIL: tell a missing harness precondition
  rem line apart from a genuinely broken acceptance run.
  findstr /c:"%MARK%" "%LOG%" >nul 2>&1
  if not errorlevel 1 (
    echo   NOTE  %NAME%  acceptance_marker_present=yes ^(logic passed, only the harness precondition line is absent^)
  )
  echo   FAIL  %NAME%  reason=neutral_precondition_missing
  set /a FAIL+=1
  if "%GROUP%"=="4B" (set /a FAIL4B+=1) else (set /a FAIL4A+=1)
  echo   FAIL  %NAME%  reason=neutral_precondition_missing >>"%BASE%\summary.txt"
  goto :eof
)

if not "%CODE%"=="0" (
  if "%CODE%"=="4" (
    echo   FAIL  %NAME%  class=exception exit=4
    echo   FAIL  %NAME%  class=exception exit=4 >>"%BASE%\summary.txt"
  ) else (
    if "%CODE%"=="3" (
      echo   FAIL  %NAME%  class=watchdog exit=3
      echo   FAIL  %NAME%  class=watchdog exit=3 >>"%BASE%\summary.txt"
    ) else (
      echo   FAIL  %NAME%  class=assert exit=%CODE%
      echo   FAIL  %NAME%  class=assert exit=%CODE% >>"%BASE%\summary.txt"
    )
  )
  set /a FAIL+=1
  if "%GROUP%"=="4B" (set /a FAIL4B+=1) else (set /a FAIL4A+=1)
  goto :eof
)

findstr /c:"%MARK%" "%LOG%" >nul 2>&1
if errorlevel 1 (
  echo   FAIL  %NAME%  class=marker_missing mark=%MARK%
  set /a FAIL+=1
  if "%GROUP%"=="4B" (set /a FAIL4B+=1) else (set /a FAIL4A+=1)
  echo   FAIL  %NAME%  class=marker_missing mark=%MARK% >>"%BASE%\summary.txt"
  goto :eof
)

echo   PASS  %NAME%
set /a PASS+=1
if "%GROUP%"=="4B" (set /a PASS4B+=1) else (set /a PASS4A+=1)
echo   PASS  %NAME% >>"%BASE%\summary.txt"
goto :eof
