#!/usr/bin/env bash
# Sprint 4A · Story 2（P0SaveService）验收批测
#
# 纪律说明（写给以后改这个脚本的人）：
# 1) 强杀一律用外部 taskkill /F（TerminateProcess），不用进程内退出。
#    进程内 Exit 会跑 ProcessExit / OnApplicationQuit 托管回调，若有人在回调里刷盘，
#    测试会通过得很漂亮，而 V3.0 17.4 恰恰禁止「退出时统一保存」。所以两条都要跑并比对。
# 2) 金额一律写死（500/700/900），让 sumCommitted 能手算核对。
#    随机金额下「balance == sumCommitted」很容易在巧合里自洽通过。
# 3) 每个用例独立目录与独立日志，开跑前清空。绝不碰默认玩家存档目录。

set -u

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
EXE="${EXE:-$ROOT/Builds/Sprint4A/ScavengeNightmare.exe}"
STAMP=$(date +%Y%m%d-%H%M%S)
BASE="$ROOT/Logs/s4a_save/run-$STAMP"

PASS=0
FAIL=0

if [ ! -f "$EXE" ]; then
  echo "EXE_NOT_FOUND $EXE"
  exit 2
fi

# 不删旧目录：批量删除会被安全策略拦下，拦下之后脚本仍继续跑，
# 上一轮的余额会被当成本轮起点，整批断言全部错位（这个坑静默过一次）。
# 每次用独立时间戳目录，天然干净。
mkdir -p "$BASE"
echo "RUN_DIR=$BASE"

ok_mark() { PASS=$((PASS + 1)); printf '  PASS  %s\n' "$1"; }
bad_mark() { FAIL=$((FAIL + 1)); printf '  FAIL  %s\n' "$1"; }

expect() {
  local label="$1" file="$2"
  shift 2
  local missing=""
  for pat in "$@"; do
    grep -qF "$pat" "$file" 2>/dev/null || missing="$missing [$pat]"
  done
  if [ -z "$missing" ]; then ok_mark "$label"; else bad_mark "$label  missing:$missing"; fi
}

expect_absent() {
  local label="$1" file="$2" pat="$3"
  if grep -qF "$pat" "$file" 2>/dev/null; then bad_mark "$label (unexpected: $pat)"; else ok_mark "$label"; fi
}

expect_exit() {
  local label="$1" code="$2"
  if [ "$code" = "0" ]; then ok_mark "$label"; else bad_mark "$label exit=$code"; fi
}

# run <dir> <logname> <args...>
run() {
  local dir="$1" log="$2"
  shift 2
  "$EXE" -batchmode -logFile "$dir/$log" "$@" >/dev/null 2>&1
  return $?
}

wait_pid() {
  local log="$1"
  local pid=""
  for _ in $(seq 1 120); do
    pid=$(grep -o "S4A_SAVE_PID pid=[0-9]*" "$log" 2>/dev/null | head -1 | grep -o "[0-9]*")
    [ -n "$pid" ] && break
    sleep 0.3
  done
  printf '%s' "$pid"
}

echo "== Story 2 存档验收 =="
echo "EXE=$EXE"

# ---------------------------------------------------------------- C1 正常提交
echo "-- C1 三笔提交 + 立即读回（不依赖退出）"
D1="$BASE/c1"; mkdir -p "$D1"
run "$D1" player.log -s4asave -savedir="$D1" -port=17701
C=$?
expect_exit "C1 进程退出码" "$C"
expect "C1 落盘三笔" "$D1/player.log" "S4A_SAVE_OK txn=3 balance=2100" "ok=True"
expect "C1 首笔立即可读回" "$D1/player.log" "S4A_SAVE_IMMEDIATE value=500 disk_balance=500"
expect "C1 末笔立即可读回" "$D1/player.log" "S4A_SAVE_IMMEDIATE value=900 disk_balance=2100"
expect "C1 提交日志带 cargoId" "$D1/player.log" "S4A_SAVE_COMMIT txn=1 balance=500"
# 首次运行没有存档，LoadedFrom 必然是新档（new），不是 sav——这是「新建」而不是「读到」
expect "C1 钱包路径是真实目录" "$D1/player.log" "S4A_SAVE_PATH" "wallet.sav persisted=True" "loaded_from=new"

# ---------------------------------------------------------------- C2 重启读回
echo "-- C2 重启后金额级校验（HK-04 / HK-06）"
run "$D1" verify.log -s4asave-verify -savedir="$D1" -expect=2100
C=$?
expect_exit "C2 校验进程退出码" "$C"
expect "C2 金额反算一致" "$D1/verify.log" "S4A_SAVE_VERIFY_OK txn=3 dup=0 balance=2100 sumCommitted=2100 from=sav"
expect "C2 外部期望值对齐" "$D1/verify.log" "GATE_WALLET expected=2100 actual=2100 delta=0"
expect "C2 校验通过" "$D1/verify.log" "S4A_SAVE_VERIFY_PASS"

# ---------------------------------------------------------------- C3 外部强杀
echo "-- C3 外部 taskkill /F 强杀恢复（A8 强杀场景）"
D3="$BASE/c3"; mkdir -p "$D3"
"$EXE" -batchmode -logFile "$D3/slow.log" -s4asave-slow -savedir="$D3" -port=17703 -stopafter=3 >/dev/null 2>&1 &
SLOW_PID=$(wait_pid "$D3/slow.log")

if [ -z "$SLOW_PID" ]; then
  bad_mark "C3 拿不到进程 PID（日志没出现 S4A_SAVE_PID）"
else
  ok_mark "C3 拿到进程 PID（$SLOW_PID）"
  HELD=0
  for _ in $(seq 1 80); do
    if grep -q "S4A_SAVE_HOLD" "$D3/slow.log" 2>/dev/null; then HELD=1; break; fi
    sleep 0.25
  done

  if [ "$HELD" != "1" ]; then
    bad_mark "C3 没停在第 3 笔事务点，强杀窗口未固定"
    taskkill //F //PID "$SLOW_PID" >/dev/null 2>&1
  else
    ok_mark "C3 已停在固定事务点（txn=3）"
    taskkill //F //PID "$SLOW_PID" >/dev/null 2>&1
    sleep 0.6
    run "$D3" verify.log -s4asave-verify -savedir="$D3" -expect=1500
    expect "C3 强杀后 = 最后成功事务" "$D3/verify.log" "S4A_SAVE_VERIFY_OK txn=3 dup=0 balance=1500 sumCommitted=1500"
    expect "C3 强杀后结构自洽" "$D3/verify.log" "S4A_SAVE_VERIFY_PASS"
  fi
fi

# ---------------------------------------------------------------- C4 进程内退出对照
echo "-- C4 进程内 Environment.Exit 对照（与 C3 停在同一事务点）"
D4="$BASE/c4"; mkdir -p "$D4"
run "$D4" kill.log -s4asave-kill -savedir="$D4" -killafter=3 -port=17704
C=$?
expect_exit "C4 对照进程退出码" "$C"
expect "C4 进程内退出同样只认最后成功事务" "$D4/kill.log" "S4A_SAVE_EXIT_INPROC txn=3 balance=1500"
run "$D4" verify.log -s4asave-verify -savedir="$D4" -expect=1500
expect "C4 结果与 C3 逐字一致（证明 ProcessExit 未刷盘）" "$D4/verify.log" "S4A_SAVE_VERIFY_OK txn=3 dup=0 balance=1500 sumCommitted=1500"
expect_absent "C4 没有多出一笔" "$D4/verify.log" "txn=4"

# ---------------------------------------------------------------- C5 sav 损坏回退
echo "-- C5 sav 改坏 → 自动回退 bak"
D5="$BASE/c5"; mkdir -p "$D5"
run "$D5" player.log -s4asave -savedir="$D5" -port=17705 >/dev/null 2>&1
run "$D5" corrupt.log -s4acorrupt -savedir="$D5" >/dev/null 2>&1
expect "C5 只破坏 sav" "$D5/corrupt.log" "broken=1"
run "$D5" verify.log -s4asave-verify -savedir="$D5" -expect=1200
expect "C5 回退到 bak（上一笔）" "$D5/verify.log" "S4A_SAVE_VERIFY_OK txn=2 dup=0 balance=1200 sumCommitted=1200 from=bak"
expect "C5 报告回退来源" "$D5/verify.log" "S4A_SAVE_RECOVERED from=bak"
if ls "$D5"/corrupt-*-sav.sav >/dev/null 2>&1; then ok_mark "C5 坏档已留证"; else bad_mark "C5 坏档未留证"; fi

# ---------------------------------------------------------------- C6 三档全坏
echo "-- C6 sav/bak/bak2 全坏 → 留证 + 新建空档"
D6="$BASE/c6"; mkdir -p "$D6"
run "$D6" player.log -s4asave -savedir="$D6" -port=17706 >/dev/null 2>&1
run "$D6" corrupt.log -s4acorrupt-all -savedir="$D6" >/dev/null 2>&1
expect "C6 破坏三档" "$D6/corrupt.log" "broken=3"
run "$D6" recover.log -s4asave -savedir="$D6" -port=17707
expect "C6 全坏后留证并新建" "$D6/recover.log" "S4A_SAVE_CORRUPT_RECOVERED"
KEPT=$(ls "$D6"/corrupt-*-*.sav 2>/dev/null | wc -l)
if [ "$KEPT" -ge 3 ]; then ok_mark "C6 三个坏档全部留证（$KEPT）"; else bad_mark "C6 坏档留证不足（$KEPT/3）"; fi
run "$D6" verify.log -s4asave-verify -savedir="$D6" -expect=2100
expect "C6 新建后继续可用" "$D6/verify.log" "S4A_SAVE_VERIFY_OK txn=3" "S4A_SAVE_VERIFY_PASS"

# ---------------------------------------------------------------- C7 写失败注入
echo "-- C7 写盘失败注入（HK-05，证明原子性与回滚真实存在）"
D7="$BASE/c7"; mkdir -p "$D7"
run "$D7" player.log -s4asave -savedir="$D7" -port=17708 >/dev/null 2>&1
run "$D7" failio.log -s4asave-failio -savedir="$D7" -port=17709
expect "C7 注入后失败而非静默" "$D7/failio.log" "S4A_SAVE_FAIL reason=io_error" "S4A_SAVE_FAILIO_OK"
expect "C7 内存与磁盘均未变" "$D7/failio.log" "memory_same=True disk_same=True txn_same=True"
expect "C7 该笔被拒绝" "$D7/failio.log" "S4A_SAVE_FAILIO_RESULT rejected=True"

# ---------------------------------------------------------------- C8 不可写路径
echo "-- C8 不可写存档路径（HK-05 第二条制造方式）"
D8="$BASE/c8"; mkdir -p "$D8"
touch "$D8/notadir"
run "$D8" player.log -s4asave -savedir="$D8/notadir" -port=17710
expect "C8 路径不可用时报 io_error" "$D8/player.log" "S4A_SAVE_FAIL reason=io_error"
expect "C8 不得静默降级为内存入账" "$D8/player.log" "state=Rejected reason=save_unavailable"
expect "C8 用例自身判失败（不是假通过）" "$D8/player.log" "ok=False"

# ---------------------------------------------------------------- C9 同机双开
echo "-- C9 同机双开 → 第二进程只读并拒绝入账"
D9="$BASE/c9"; mkdir -p "$D9"
"$EXE" -batchmode -logFile "$D9/holder.log" -s4asave-slow -savedir="$D9" -port=17711 >/dev/null 2>&1 &
HOLDER_PID=$(wait_pid "$D9/holder.log")

if [ -z "$HOLDER_PID" ]; then
  bad_mark "C9 持锁进程未起来"
else
  sleep 1.6
  run "$D9" double.log -s4asave-double -savedir="$D9" -port=17712
  expect "C9 第二进程明确被拒" "$D9/double.log" "S4A_SAVE_LOCKED" "S4A_SAVE_DOUBLE_RESULT locked=True rejected=True" "S4A_SAVE_DOUBLE_OK second_process_readonly=1"
  taskkill //F //PID "$HOLDER_PID" >/dev/null 2>&1
fi

# ---------------------------------------------------------------- 汇总
echo
echo "== 汇总：PASS=$PASS FAIL=$FAIL =="
if [ "$FAIL" -ne 0 ]; then
  echo "S4A_STORY2_SUITE_FAIL"
  exit 1
fi

echo "S4A_STORY2_SUITE_OK"
exit 0
