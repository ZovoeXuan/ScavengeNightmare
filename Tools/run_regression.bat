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
rem    d) Sources = the newest of the SIX hand-written extensions, scanned under
rem       Assets\_Game only:
rem         .cs   .prefab   .unity   .asset   .mat   .physicMaterial
rem       Assets\Settings is excluded on purpose: it holds URP global settings
rem       (UniversalRenderPipelineGlobalSettings.asset), which the editor
rem       rewrites on import - nothing to do with a code change. Measured once:
rem       that file sat in the SAME minute as the package, margin 0.
rem       Do NOT drop *.asset wholesale: Assets\_Game\Data holds the monster /
rem       item / shop ScriptableObjects and those DO need a rebuild.
rem    e) Package age = newest of the whole build payload, not the exe:
rem         exe + Managed\Assembly-CSharp.dll + globalgamemanagers
rem             + level* + sharedassets*.assets + resources*.assets
rem       Measured: an incremental build does NOT rewrite the exe stub, and a
rem       scene-only build does NOT rewrite Assembly-CSharp.dll. No single file
rem       tracks "the build", so the guard tracks all of them and takes the max.
rem    f) The payload scan fails closed. If it matched nothing, the guard would
rem       still "look like it ran" while its criteria were dead. Floors below.
rem
rem  CONCURRENCY DISCIPLINE (learned the hard way on 2026-10-31):
rem    1) Check for running processes BEFORE starting. If a game process or
rem       another runner is alive, exit 2 with already_running and its details.
rem       That day two runners coexisted; both do "taskkill /F /IM ..." (by
rem       image name, GLOBAL), so they killed each other's cases. The result was
rem       22 fake SocketException reds that hid the single real failure.
rem    2) NEVER edit this .bat or the case list while a run is in flight. cmd
rem       reads a batch file by byte offset as it executes; rewriting the file
rem       mid-run is swapping the engine while driving. That is exactly what
rem       corrupted a concurrent run that day: the victim printed
rem       "'b' is not recognized", picked up half-written lines, and restarted
rem       from an earlier case.
rem    3) Only one runner at a time. To run in parallel, use a different port
rem       AND a different copy of the script. Never share this file.
rem
rem  CMD TRAPS that have bitten this file (do not "clean them up"):
rem    - A bare ">" inside echo TEXT is a redirect: "echo a -> b" makes the line
rem      vanish and creates a file named "b". Same family as "echo 0>file",
rem      where "0>" is parsed as a stream handle. Use "^>" or reword.
rem    - Inside a rem line you may write the DOUBLE-tilde form of the timestamp
rem      operator, but NOT the single-tilde form: cmd still expands that one as
rem      a path operator and dies with "invalid path operator in the
rem      substitution". This comment used to spell both out literally and broke
rem      the whole script - the trap fires from inside a rem line too.
rem ===========================================================================
setlocal enabledelayedexpansion

set "ROOT=%~dp0.."
set "EXE=%ROOT%\Builds\Sprint4A\ScavengeNightmare.exe"
set "CASES=%~dp0regression_cases.txt"
set "STAMP=%DATE:~0,4%%DATE:~5,2%%DATE:~8,2%-%TIME:~0,2%%TIME:~3,2%%TIME:~6,2%"
set "STAMP=%STAMP: =0%"
set "BASE=%ROOT%\Logs\s4_reg\%STAMP%"

echo RUN_DIR=%BASE%

rem The run dir and the summary header are created BEFORE any guard runs, so a
rem guard verdict (case count, stale scan, already_running) is written into
rem summary.txt as well. Previously those evidence lines went to the console
rem only, so an archived run could not answer "did the gate actually judge
rem anything, and what did it scan?" after the terminal was gone.
if not exist "%BASE%" mkdir "%BASE%"
echo RUN_DIR=%BASE% cases="%CASES%" >"%BASE%\summary.txt"

if not exist "%EXE%" (
  echo S4_REGRESSION_FAIL reason=exe_missing path="%EXE%"
  exit /b 2
)

if not exist "%CASES%" (
  echo S4_REGRESSION_FAIL reason=cases_missing path="%CASES%"
  echo S4_REGRESSION_FAIL reason=cases_missing path="%CASES%" >>"%BASE%\summary.txt"
  exit /b 2
)

rem --- single instance guard -------------------------------------------------
rem Why this exists: on 2026-10-31 two runners coexisted. Both call
rem "taskkill /F /IM ScavengeNightmare.exe", which is GLOBAL by image name, so
rem each killed the other's cases. The visible result was 22 fake
rem SocketException reds that buried the one real failure.
rem
rem The runner's own cmd.exe lives for the whole run and every case spawns the
rem game, so "either one alive" means busy. wmic is gone on Windows 11 26200, so
rem this goes through PowerShell. The payload keeps single quotes only (batch
rem would shred nested double quotes) and writes to a file which "for /f" then
rem reads - embedding the whole thing in 'for /f ... in (''cmd'')' is a quoting
rem minefield that is not worth the elegance.
set "LOCK=%ROOT%\Logs\s4_reg\_runner.lock"
set "ABORT="
set "ABORTREASON="
set "PROBE=%BASE%\_probe.txt"
set "OTHER="

powershell -NoProfile -Command "(Get-CimInstance Win32_Process) | Where-Object { $_.Name -eq 'ScavengeNightmare.exe' -or ($_.Name -eq 'cmd.exe' -and $_.CommandLine -like '*run_regression*') } | ForEach-Object { 'pid=' + $_.ProcessId + ' start=' + $_.CreationDate + ' cmd=' + $_.CommandLine }" >"%PROBE%" 2>nul
if exist "%PROBE%" (
  for /f "usebackq delims=" %%L in ("%PROBE%") do if not defined OTHER set "OTHER=%%L"
  del "%PROBE%" >nul 2>&1
)

if defined OTHER (
  echo S4_REGRESSION_FAIL reason=already_running kind=process
  echo   other=!OTHER!
  echo S4_REGRESSION_FAIL reason=already_running kind=process >>"%BASE%\summary.txt"
  echo   other=!OTHER! >>"%BASE%\summary.txt"
  echo   Another runner or a game instance is alive. One runner at a time:
  echo   both would call taskkill /F /IM and kill each other's cases.
  echo   Wait for it to finish, or run a copy with its own port.
  set "OTHER="
  exit /b 2
)

rem Advisory lock. The process probe above is authoritative, so a lock left over
rem from a crashed run must NOT block forever - self-heal it instead of turning a
rem stale file into a gate that nobody can open.
if exist "%LOCK%" (
  set "LOCKOWNER="
  set /p LOCKOWNER=<"%LOCK%"
  echo S4_NOTE stale_lock_removed owner=!LOCKOWNER!
  echo S4_NOTE stale_lock_removed owner=!LOCKOWNER! >>"%BASE%\summary.txt"
  del "%LOCK%" >nul 2>&1
)
> "%LOCK%" echo %BASE%
echo RUN_LOCK_OK path="%LOCK%"
echo RUN_LOCK_OK path="%LOCK%" >>"%BASE%\summary.txt"

rem --- case list prescan -----------------------------------------------------
rem EXPECTED is pinned BEFORE any case runs, not accumulated while running.
rem Why: if the manifest is truncated or unreadable, counting while running
rem lowers the bar and the result together, and the run still prints a clean
rem "OK passes=0 expected=0". The header of this file records that a UTF-8
rem mangled manifest once swallowed half the cases; under the old verdict that
rem day would have been passes=11 expected=11 GREEN, not red. Preflight it.
set /a EXPECTED=0
for /f "usebackq eol=# tokens=1,2,3 delims=|" %%A in ("%CASES%") do (
  if not "%%~A"=="" set /a EXPECTED+=1
)

rem The floor lives in the manifest (data), not in this file, so adding a case
rem never means editing the bat. It is a FLOOR, not an exact count.
rem It must be present: if the comment is deleted by accident the run stops
rem instead of silently lowering the bar.
set "EXPMIN="
for /f "tokens=3 delims=: " %%M in ('findstr /b /c:"# expected_min:" "%CASES%"') do set "EXPMIN=%%M"

set /a EXPFLR=0
if defined EXPMIN set /a EXPFLR=0+!EXPMIN! 2>nul

if not defined EXPMIN (
  echo S4_REGRESSION_FAIL reason=floor_missing expected=%EXPECTED%
  echo   The case list must carry a line "# expected_min: N" - see the header
  echo   of Tools\regression_cases.txt. Without it, a truncated manifest would
  echo   pass with a smaller expected= and still look green.
  echo S4_REGRESSION_FAIL reason=floor_missing expected=%EXPECTED% >>"%BASE%\summary.txt"
  call :release_lock
  exit /b 2
)
if !EXPFLR! LSS 1 (
  echo S4_REGRESSION_FAIL reason=floor_missing expected=%EXPECTED% floor="!EXPMIN!"
  echo S4_REGRESSION_FAIL reason=floor_missing expected=%EXPECTED% floor="!EXPMIN!" >>"%BASE%\summary.txt"
  call :release_lock
  exit /b 2
)
if %EXPECTED% LSS 1 (
  echo S4_REGRESSION_FAIL reason=no_cases expected=0 floor=!EXPFLR!
  echo S4_REGRESSION_FAIL reason=no_cases expected=0 floor=!EXPFLR! >>"%BASE%\summary.txt"
  call :release_lock
  exit /b 2
)
if %EXPECTED% LSS !EXPFLR! (
  echo S4_REGRESSION_FAIL reason=case_count_below_floor expected=%EXPECTED% floor=!EXPFLR!
  echo   cmd's "for /f" silently drops a final line that has no trailing
  echo   newline, so a lost CRLF makes the last case vanish. Fix the manifest;
  echo   do not lower the floor.
  echo S4_REGRESSION_FAIL reason=case_count_below_floor expected=%EXPECTED% floor=!EXPFLR! >>"%BASE%\summary.txt"
  call :release_lock
  exit /b 2
)
echo CASES_CHECK_OK expected=%EXPECTED% floor=!EXPFLR!
echo CASES_CHECK_OK expected=%EXPECTED% floor=!EXPFLR! >>"%BASE%\summary.txt"

rem --- stale binary guard ---------------------------------------------------
rem A green run against an old binary is worse than a red run: every case below
rem would be exercised on code that is not in the package. Turn "did you
rem rebuild?" from a discipline into a machine check.
rem
rem MEASURED ON THIS PROJECT - no single artifact tracks "the build":
rem   exe stub         2026-10-29 15:12  (incremental build does NOT rewrite it)
rem   Assembly-CSharp  2026-10-31 02:53  (a scene-only build does NOT rewrite it)
rem   level* / sharedassets* / resources*  (these DO move on a scene-only build)
rem So the package age is the NEWEST of the whole payload. Tracking only the
rem exe would lock the gate shut forever; tracking only exe+dll would miss a
rem scene-only rebuild entirely.
rem
rem Timestamps come from %%~tF (fixed width "YYYY/MM/DD HH:MM"), folded into a
rem zero-padded 12-char key so a lexicographic compare is chronological.
set "BINDATA=%ROOT%\Builds\Sprint4A\ScavengeNightmare_Data"
set "BINKEY=0"
set "SRCKEY=0"
set /a BINLIT=0
set /a BINPAY=0
set /a BINLEVEL=0
set /a BINSHARED=0
set /a BINRES=0

for %%F in ("%EXE%" "%BINDATA%\Managed\Assembly-CSharp.dll" "%BINDATA%\globalgamemanagers") do (
  if exist "%%~F" (
    set /a BINLIT+=1
    set "RAW=%%~tF"
    set "KEY=!RAW:~0,4!!RAW:~5,2!!RAW:~8,2!!RAW:~11,2!!RAW:~14,2!"
    if "!KEY!" GTR "!BINKEY!" set "BINKEY=!KEY!"
  )
)

rem The payload scans below are WILDCARD-ONLY on purpose. Mixing a literal name
rem into a "for /r" set makes cmd emit that name once per subdirectory whether
rem the file exists there or not; %%~tF is then empty, the folded key becomes
rem the literal text "~0,4~5,2...", and because "~" sorts above digits that
rem string would poison BINKEY. Wildcards never produce those phantom rows.
for /r "%BINDATA%" %%F in (level*) do (
  set /a BINLEVEL+=1
  set /a BINPAY+=1
  set "RAW=%%~tF"
  set "KEY=!RAW:~0,4!!RAW:~5,2!!RAW:~8,2!!RAW:~11,2!!RAW:~14,2!"
  if "!KEY!" GTR "!BINKEY!" set "BINKEY=!KEY!"
)
for /r "%BINDATA%" %%F in (sharedassets*.assets) do (
  set /a BINSHARED+=1
  set /a BINPAY+=1
  set "RAW=%%~tF"
  set "KEY=!RAW:~0,4!!RAW:~5,2!!RAW:~8,2!!RAW:~11,2!!RAW:~14,2!"
  if "!KEY!" GTR "!BINKEY!" set "BINKEY=!KEY!"
)
for /r "%BINDATA%" %%F in (resources*.assets) do (
  set /a BINRES+=1
  set /a BINPAY+=1
  set "RAW=%%~tF"
  set "KEY=!RAW:~0,4!!RAW:~5,2!!RAW:~8,2!!RAW:~11,2!!RAW:~14,2!"
  if "!KEY!" GTR "!BINKEY!" set "BINKEY=!KEY!"
)

rem Floors: a scan that matched nothing still "looks like it ran", while its
rem criteria are dead. That is the "looks fine, judges nothing" state, so it has
rem to fail closed instead of comparing against 0.
rem   literal >= 3 : exe + dll + globalgamemanagers are guaranteed build outputs.
rem                  A typo'd BINDATA drops exactly the latter two (the exe sits
rem                  in the parent dir), so literally 1 left - this floor is what
rem                  catches a wrong path. Verified by pointing BINDATA at a
rem                  non-existent dir: literal=1 payload=0 -> exit 2.
rem   payload >= 3 : currently 5 (level 2, sharedassets 2, resources 1).
rem   level   >= 1 : this project always ships scenes.
if !BINPAY! EQU 0 (
  echo S4_REGRESSION_FAIL reason=payload_scan_empty literal=!BINLIT! payload=0
  echo S4_REGRESSION_FAIL reason=payload_scan_empty literal=!BINLIT! payload=0 >>"%BASE%\summary.txt"
  call :release_lock
  exit /b 2
)
if !BINLIT! LSS 3 (
  echo S4_REGRESSION_FAIL reason=payload_scan_thin literal=!BINLIT! payload=!BINPAY! bindata="%BINDATA%"
  echo S4_REGRESSION_FAIL reason=payload_scan_thin literal=!BINLIT! payload=!BINPAY! >>"%BASE%\summary.txt"
  call :release_lock
  exit /b 2
)
if !BINPAY! LSS 3 (
  echo S4_REGRESSION_FAIL reason=payload_scan_thin literal=!BINLIT! payload=!BINPAY!
  echo S4_REGRESSION_FAIL reason=payload_scan_thin literal=!BINLIT! payload=!BINPAY! >>"%BASE%\summary.txt"
  call :release_lock
  exit /b 2
)
if !BINLEVEL! LSS 1 (
  echo S4_REGRESSION_FAIL reason=payload_scan_thin level=0 payload=!BINPAY!
  echo S4_REGRESSION_FAIL reason=payload_scan_thin level=0 payload=!BINPAY! >>"%BASE%\summary.txt"
  call :release_lock
  exit /b 2
)

rem  Sources = the SIX hand-written extensions, under Assets\_Game only.
rem  NO .meta on purpose: Unity rewrites .meta on import, and 839 of them would
rem  turn this guard into a false-alarm machine that stops the whole matrix
rem  while the package is in fact new.
rem  Assets\Settings is excluded on purpose too (URP global settings get
rem  rewritten by the editor on import).
rem  Do NOT drop *.asset wholesale: Assets\_Game\Data holds the monster / item /
rem  shop ScriptableObjects, and those DO need a rebuild when edited.
for /r "%ROOT%\Assets\_Game" %%F in (*.cs *.prefab *.unity *.asset *.mat *.physicMaterial) do (
  set "RAW=%%~tF"
  set "KEY=!RAW:~0,4!!RAW:~5,2!!RAW:~8,2!!RAW:~11,2!!RAW:~14,2!"
  if "!KEY!" GTR "!SRCKEY!" set "SRCKEY=!KEY!"
)

if "%SRCKEY%" GTR "%BINKEY%" (
  echo S4_REGRESSION_FAIL reason=stale_binary src=%SRCKEY% bin=%BINKEY% literal=!BINLIT! payload=!BINPAY!
  echo   Assets are newer than the built package - rebuild first:
  echo   Unity -batchmode -quit -projectPath "%ROOT%" -executeMethod BuildWindows.Build -map=sprint4a
  echo S4_REGRESSION_FAIL reason=stale_binary src=%SRCKEY% bin=%BINKEY% literal=!BINLIT! payload=!BINPAY! >>"%BASE%\summary.txt"
  call :release_lock
  exit /b 2
)

echo STALE_CHECK_OK src=%SRCKEY% bin=%BINKEY% literal=!BINLIT! payload=!BINPAY! level=!BINLEVEL! sharedassets=!BINSHARED! resources=!BINRES!
echo STALE_CHECK_OK src=%SRCKEY% bin=%BINKEY% literal=!BINLIT! payload=!BINPAY! level=!BINLEVEL! sharedassets=!BINSHARED! resources=!BINRES! >>"%BASE%\summary.txt"

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
set /a PASS4A=0
set /a FAIL4A=0
set /a PASS4B=0
set /a FAIL4B=0
set "PAIRPENDING="
set "CURNAME="
set "PREVNAME="

rem NOTE: summary.txt must NOT be re-created here ("echo ... >") - that would
rem truncate the guard evidence written above, and the archived file would again
rem only contain the per-case rows. Appends only from here on.

rem The run loop no longer counts rows: EXPECTED was pinned by the prescan, so a
rem manifest edited mid-run shows up as an accounting mismatch instead of
rem quietly redefining the bar halfway through.
for /f "usebackq eol=# tokens=1,2,3 delims=|" %%A in ("%CASES%") do (
  if not "%%~A"=="" (
    call :run_case "%%~A" "%%~B" "%%~C"
    if defined ABORT goto :abort_round
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

goto :final_verdict

rem ---------------------------------------------------------------------------
rem A case died in a way that leaves the next ones unable to bind the port. Stop
rem the round instead of printing one fake red per remaining row: one fault must
rem produce one failure, otherwise the real first failure is buried under 20+
rem copies of itself.
:abort_round
echo.
echo ============================================================
echo S4_REGRESSION_ABORT reason=!ABORTREASON! at="!CURNAME!"
echo   previous="!PREVNAME!" current="!CURNAME!"
echo   A case failed to start its host (SocketException: address in use). The
echo   previous case did not release the KCP port. Fix that case first; the rows
echo   that never ran are NOT additional failures.
echo S4_REGRESSION_ABORT reason=!ABORTREASON! at="!CURNAME!" previous="!PREVNAME!" >>"%BASE%\summary.txt"
call :release_lock
endlocal
exit /b 2

:final_verdict

echo.
echo ============================================================
echo S4_REGRESSION_BREAKDOWN s4a pass=!PASS4A! fail=!FAIL4A! s4b pass=!PASS4B! fail=!FAIL4B!

rem Accounting first. Every row must end in exactly one verdict. If PASS+FAIL
rem does not add up to the prescanned EXPECTED, a row was consumed without being
rem judged - a runner bug. Report it with a reason instead of printing
rem "failures=0" next to a FAIL line, which reads like a contradiction.
set /a ACCOUNTED=PASS+FAIL

if %ACCOUNTED% NEQ %EXPECTED% (
  echo S4_REGRESSION_FAIL reason=row_accounting passes=%PASS% failures=%FAIL% expected=%EXPECTED%
  echo S4_REGRESSION_FAIL reason=row_accounting passes=%PASS% failures=%FAIL% expected=%EXPECTED% >>"%BASE%\summary.txt"
  call :release_lock
  endlocal
  exit /b 1
)

if %FAIL% EQU 0 (
  echo S4_REGRESSION_OK passes=%PASS% expected=%EXPECTED%
  echo S4_REGRESSION_OK passes=%PASS% expected=%EXPECTED% >>"%BASE%\summary.txt"
  call :release_lock
  endlocal
  exit /b 0
)

echo S4_REGRESSION_FAIL passes=%PASS% expected=%EXPECTED% failures=%FAIL%
echo S4_REGRESSION_FAIL passes=%PASS% expected=%EXPECTED% failures=%FAIL% >>"%BASE%\summary.txt"
call :release_lock
endlocal
exit /b 1

rem ---------------------------------------------------------------------------
:release_lock
if exist "%LOCK%" del "%LOCK%" >nul 2>&1
goto :eof

rem ---------------------------------------------------------------------------
:run_case
set "NAME=%~1"
set "ARGS=%~2"
set "MARK=%~3"
set "LOG=%BASE%\%NAME%.log"

rem PREVNAME is the row judged just before this one. The port-busy abort names
rem both, because "the previous case did not release the port" is only actionable
rem if you know which case that was.
set "PREVNAME=%CURNAME%"
set "CURNAME=%NAME%"

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
rem ARGS must be saved too: :host_done restores NAME/LOG/MARK before judging the
rem host, and :check_log now makes decisions based on %ARGS% (the -s4asyncbreak
rem self-check). Without this the host half would be judged with the CLIENT
rem row's arguments - a red that is nearly impossible to trace back.
set "HOSTARGS=%ARGS%"
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
set "ARGS=%HOSTARGS%"
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

rem No log at all means the exe never ran (bad savedir, missing dependency, an
rem instant crash). Without this check findstr returns errorlevel 2 and we would
rem report neutral_precondition_missing - i.e. blame the fixture for a missing
rem log line when the process never started. The verdict was already red either
rem way; only the reason was wrong, and a wrong reason is what makes a red hard
rem to act on.
if not exist "%LOG%" (
  echo   FAIL  %NAME%  reason=no_log path="%LOG%"
  set /a FAIL+=1
  if "%GROUP%"=="4B" (set /a FAIL4B+=1) else (set /a FAIL4A+=1)
  echo   FAIL  %NAME%  reason=no_log >>"%BASE%\summary.txt"
  goto :eof
)

rem Port-busy detection runs BEFORE the neutral check on purpose. A case that
rem could not bind the KCP port never reaches its precondition line, so the old
rem order reported "neutral_precondition_missing" and blamed the fixture for what
rem is really a port problem. One fault must produce one failure: mark this case,
rem then abort the whole round instead of letting every remaining row fail the
rem same way. On 2026-10-31 this turned 1 real fault into 22 fake reds.
findstr /c:"type=SocketException" "%LOG%" >nul 2>&1
if not errorlevel 1 (
  echo   FAIL  %NAME%  class=port_busy exit=%CODE% previous="%PREVNAME%"
  set /a FAIL+=1
  if "%GROUP%"=="4B" (set /a FAIL4B+=1) else (set /a FAIL4A+=1)
  echo   FAIL  %NAME%  class=port_busy exit=%CODE% previous="%PREVNAME%" >>"%BASE%\summary.txt"
  set "ABORT=1"
  set "ABORTREASON=port_busy_by_previous_case name=%PREVNAME% port=7777"
  goto :eof
)

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

rem --- per-case extra assertions -------------------------------------------
rem s4aloop and s4aloop-asyncbreak BOTH end with S4A_LOOP_OK, so the terminal
rem marker cannot tell them apart: what separates them is only the args column,
rem and nothing used to check that column. Pin the harness self-check line
rem instead, in both directions, so a dropped or misplaced flag turns red.
rem Assert on "match=False"/"match=True" ONLY. The server=/client= numbers move
rem between runs (2700 vs 2701), so including them would be a permanent false
rem red - which is worse than no assertion at all.
set "EXTRA="
if not "%ARGS:-s4asyncbreak=%"=="%ARGS%" set "EXTRA=match=False"
if not defined EXTRA if /i "%ARGS%"=="-s4aloop" set "EXTRA=match=True"
if defined EXTRA (
  findstr /c:"%EXTRA%" "%LOG%" >nul 2>&1
  if errorlevel 1 (
    echo   FAIL  %NAME%  class=extra_missing expect=%EXTRA%
    set /a FAIL+=1
    if "%GROUP%"=="4B" (set /a FAIL4B+=1) else (set /a FAIL4A+=1)
    echo   FAIL  %NAME%  class=extra_missing expect=%EXTRA% >>"%BASE%\summary.txt"
    goto :eof
  )
)

echo   PASS  %NAME%
set /a PASS+=1
if "%GROUP%"=="4B" (set /a PASS4B+=1) else (set /a PASS4A+=1)
echo   PASS  %NAME% >>"%BASE%\summary.txt"
goto :eof
