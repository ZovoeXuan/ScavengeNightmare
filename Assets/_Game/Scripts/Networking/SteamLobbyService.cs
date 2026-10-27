using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Steamworks;
using Steamworks.Data;
using UnityEngine;

namespace ScavengeNightmare.Networking
{
    public readonly struct LobbyMemberInfo
    {
        public SteamId Id { get; }
        public string Name { get; }
        public bool IsOwner { get; }
        public bool IsMe { get; }

        public LobbyMemberInfo(SteamId id, string name, bool isOwner, bool isMe)
        {
            Id = id;
            Name = name;
            IsOwner = isOwner;
            IsMe = isMe;
        }
    }

    /// <summary>
    /// D2 Steam 大厅服务：初始化 Steam、创建/加入/邀请/离开大厅，4 人上限，房主标记。
    /// 仅负责大厅，不包含联机传输与游戏内容（D3 再接 Mirror）。
    /// </summary>
    public sealed class SteamLobbyService : MonoBehaviour
    {
        public const uint AppId = 480;
        public const int MaxMembers = 4;

        public static SteamLobbyService Instance { get; private set; }

        [SerializeField] private bool initializeOnAwake = true;

        public bool Initialized { get; private set; }
        public bool Busy { get; private set; }
        public string Status { get; private set; } = "Steam 未初始化";
        public string LastError { get; private set; } = string.Empty;

        public Lobby? CurrentLobby { get; private set; }
        public bool IsInLobby => CurrentLobby.HasValue && CurrentLobby.Value.Id.Value != 0UL;
        public bool IsHost => IsInLobby && CurrentLobby.Value.IsOwnedBy(SteamClient.SteamId);
        public int MemberCount => IsInLobby ? CurrentLobby.Value.MemberCount : 0;
        public int MaxLobbyMembers => IsInLobby ? CurrentLobby.Value.MaxMembers : MaxMembers;
        public string LobbyIdText => IsInLobby ? CurrentLobby.Value.Id.ToString() : "-";
        public IReadOnlyList<LobbyMemberInfo> Members => members;

        private readonly List<LobbyMemberInfo> members = new();
        private readonly ConcurrentQueue<Action> mainThreadActions = new();
        private bool subscribed;
        private bool pumpCallbacks;
        private float nextMemberRefreshAt;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (initializeOnAwake)
            {
                Initialize();
            }
        }

        private void Update()
        {
            // 手动泵 Steam 回调：避免 Facepunch 后台循环捕获 Unity 同步上下文导致阻塞/死锁。
            if (pumpCallbacks && SteamClient.IsValid)
            {
                try
                {
                    SteamClient.RunCallbacks();
                }
                catch (Exception ex)
                {
                    pumpCallbacks = false;
                    Debug.LogWarning("[SteamLobby] RunCallbacks failed; callback pump stopped.\n" + ex);
                }
            }

            while (mainThreadActions.TryDequeue(out Action action))
            {
                action();
            }

            if (IsInLobby && Time.unscaledTime >= nextMemberRefreshAt)
            {
                nextMemberRefreshAt = Time.unscaledTime + 1f;
                RefreshMembers();
            }
        }

        private void OnDestroy()
        {
            // 只有主实例负责关闭 Steam；重复实例被销毁时不能影响主实例。
            if (Instance != this)
            {
                return;
            }

            Instance = null;
            Shutdown();
        }

        public void Initialize()
        {
            if (Initialized)
            {
                return;
            }

            LastError = string.Empty;

            try
            {
                if (!SteamClient.IsValid)
                {
                    SteamClient.Init(AppId, asyncCallbacks: false);
                }

                pumpCallbacks = true;
                Initialized = true;
                SubscribeEvents();
                SetStatus($"Steam 已登录：{SteamClient.Name} ({SteamClient.SteamId})");
                Debug.Log($"[SteamLobby] init ok appId={AppId} user={SteamClient.Name} id={SteamClient.SteamId}");
            }
            catch (Exception ex)
            {
                Initialized = false;
                LastError = ex.Message;
                SetStatus($"Steam 初始化失败：{ex.Message}", true);
                Debug.LogError("[SteamLobby] Steam 初始化失败。检查：Steam 客户端是否在线、steam_appid.txt 是否为 480、steam_api64.dll 是否在 Plugins 下。\n" + ex);
            }
        }

        public async Task<bool> CreateLobbyAsync()
        {
            if (!Initialized)
            {
                SetStatus("Steam 未初始化，无法创建大厅", true);
                return false;
            }

            if (Busy)
            {
                return false;
            }

            Busy = true;
            try
            {
                if (IsInLobby)
                {
                    LeaveLobby();
                }

                Lobby? lobby = await SteamMatchmaking.CreateLobbyAsync(MaxMembers);
                if (!lobby.HasValue)
                {
                    SetStatus("创建大厅失败：Steam 未返回大厅（可能离线或 AppID 无权限）", true);
                    return false;
                }

                CurrentLobby = lobby.Value;
                lobby.Value.SetFriendsOnly();
                lobby.Value.SetJoinable(true);
                lobby.Value.SetData("host", SteamClient.SteamId.ToString());
                lobby.Value.SetData("hostName", SteamClient.Name);
                lobby.Value.SetData("app", AppId.ToString());

                SetStatus($"大厅已创建：{lobby.Value.Id}（{lobby.Value.MemberCount}/{lobby.Value.MaxMembers}）");
                RefreshMembers();
                return true;
            }
            catch (Exception ex)
            {
                SetStatus($"创建大厅异常：{ex.Message}", true);
                Debug.LogError("[SteamLobby] CreateLobbyAsync failed.\n" + ex);
                return false;
            }
            finally
            {
                Busy = false;
            }
        }

        public async Task<bool> JoinLobbyAsync(string lobbyIdText)
        {
            if (!Initialized)
            {
                SetStatus("Steam 未初始化，无法加入大厅", true);
                return false;
            }

            if (Busy)
            {
                return false;
            }

            string trimmed = (lobbyIdText ?? string.Empty).Trim();
            if (!ulong.TryParse(trimmed, out ulong lobbyId) || lobbyId == 0UL)
            {
                SetStatus("大厅 ID 格式不正确（应为纯数字）", true);
                return false;
            }

            Busy = true;
            try
            {
                if (IsInLobby)
                {
                    LeaveLobby();
                }

                Lobby? lobby = await SteamMatchmaking.JoinLobbyAsync(new SteamId { Value = lobbyId });
                if (!lobby.HasValue)
                {
                    SetStatus("加入大厅失败：大厅不存在、已满或未开放", true);
                    return false;
                }

                CurrentLobby = lobby.Value;
                SetStatus($"已加入大厅：{lobby.Value.Id}（{lobby.Value.MemberCount}/{lobby.Value.MaxMembers}）");
                RefreshMembers();
                return true;
            }
            catch (Exception ex)
            {
                SetStatus($"加入大厅异常：{ex.Message}", true);
                Debug.LogError("[SteamLobby] JoinLobbyAsync failed.\n" + ex);
                return false;
            }
            finally
            {
                Busy = false;
            }
        }

        /// <summary>打开 Steam 内置好友邀请面板（房主/成员都可邀请）。</summary>
        public void InviteFriend()
        {
            if (!IsInLobby)
            {
                SetStatus("先创建或加入大厅，才能邀请好友", true);
                return;
            }

            try
            {
                SteamFriends.OpenGameInviteOverlay(CurrentLobby.Value.Id);
                SetStatus("已打开 Steam 好友邀请面板");
            }
            catch (Exception ex)
            {
                SetStatus($"打开邀请面板失败：{ex.Message}", true);
                Debug.LogWarning("[SteamLobby] OpenGameInviteOverlay failed.\n" + ex);
            }
        }

        public void LeaveLobby()
        {
            if (!IsInLobby)
            {
                SetStatus("当前不在大厅");
                return;
            }

            try
            {
                CurrentLobby.Value.Leave();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[SteamLobby] Leave failed.\n" + ex);
            }

            CurrentLobby = null;
            members.Clear();
            SetStatus("已离开大厅");
        }

        public void Shutdown()
        {
            UnsubscribeEvents();

            try
            {
                if (IsInLobby)
                {
                    CurrentLobby.Value.Leave();
                }
            }
            catch
            {
                // 退出时 Steam 可能已关闭，忽略。
            }

            CurrentLobby = null;
            members.Clear();
            pumpCallbacks = false;

            try
            {
                if (SteamClient.IsValid)
                {
                    SteamClient.Shutdown();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[SteamLobby] Steam shutdown failed.\n" + ex);
            }

            Initialized = false;
        }

        private void RefreshMembers()
        {
            members.Clear();

            if (!IsInLobby)
            {
                return;
            }

            try
            {
                Lobby lobby = CurrentLobby.Value;
                SteamId ownerId = lobby.Owner.Id;

                foreach (Friend friend in lobby.Members)
                {
                    string name = string.IsNullOrEmpty(friend.Name) ? friend.Id.ToString() : friend.Name;
                    members.Add(new LobbyMemberInfo(friend.Id, name, friend.Id == ownerId, friend.IsMe));
                }

                members.Sort((a, b) => b.IsOwner.CompareTo(a.IsOwner));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[SteamLobby] RefreshMembers failed.\n" + ex);
            }
        }

        private void SubscribeEvents()
        {
            if (subscribed)
            {
                return;
            }

            subscribed = true;
            SteamMatchmaking.OnLobbyCreated += HandleLobbyCreated;
            SteamMatchmaking.OnLobbyEntered += HandleLobbyEntered;
            SteamMatchmaking.OnLobbyMemberJoined += HandleLobbyMemberJoined;
            SteamMatchmaking.OnLobbyMemberLeave += HandleLobbyMemberLeave;
            SteamMatchmaking.OnLobbyMemberDisconnected += HandleLobbyMemberDisconnected;
            SteamMatchmaking.OnLobbyDataChanged += HandleLobbyDataChanged;
            SteamFriends.OnGameLobbyJoinRequested += HandleGameLobbyJoinRequested;
        }

        private void UnsubscribeEvents()
        {
            if (!subscribed)
            {
                return;
            }

            subscribed = false;
            SteamMatchmaking.OnLobbyCreated -= HandleLobbyCreated;
            SteamMatchmaking.OnLobbyEntered -= HandleLobbyEntered;
            SteamMatchmaking.OnLobbyMemberJoined -= HandleLobbyMemberJoined;
            SteamMatchmaking.OnLobbyMemberLeave -= HandleLobbyMemberLeave;
            SteamMatchmaking.OnLobbyMemberDisconnected -= HandleLobbyMemberDisconnected;
            SteamMatchmaking.OnLobbyDataChanged -= HandleLobbyDataChanged;
            SteamFriends.OnGameLobbyJoinRequested -= HandleGameLobbyJoinRequested;
        }

        // Steam 回调在后台线程触发：只入队，统一在主线程处理。
        private void Enqueue(Action action)
        {
            if (action != null)
            {
                mainThreadActions.Enqueue(action);
            }
        }

        private void HandleLobbyCreated(Result result, Lobby lobby)
        {
            Enqueue(() =>
            {
                if (result != Result.OK)
                {
                    SetStatus($"创建大厅失败：{result}", true);
                    return;
                }

                Debug.Log($"[SteamLobby] OnLobbyCreated {lobby.Id}");
            });
        }

        private void HandleLobbyEntered(Lobby lobby)
        {
            Enqueue(() =>
            {
                CurrentLobby = lobby;
                SetStatus($"已进入大厅：{lobby.Id}（{lobby.MemberCount}/{lobby.MaxMembers}）");
                RefreshMembers();
            });
        }

        private void HandleLobbyMemberJoined(Lobby lobby, Friend friend)
        {
            Enqueue(() =>
            {
                SetStatus($"{friend.Name} 加入了大厅（{lobby.MemberCount}/{lobby.MaxMembers}）");
                RefreshMembers();
            });
        }

        private void HandleLobbyMemberLeave(Lobby lobby, Friend friend)
        {
            Enqueue(() =>
            {
                SetStatus($"{friend.Name} 离开了大厅");
                RefreshMembers();
            });
        }

        private void HandleLobbyMemberDisconnected(Lobby lobby, Friend friend)
        {
            Enqueue(() =>
            {
                SetStatus($"{friend.Name} 断开了连接");
                RefreshMembers();
            });
        }

        private void HandleLobbyDataChanged(Lobby lobby)
        {
            Enqueue(RefreshMembers);
        }

        private void HandleGameLobbyJoinRequested(Lobby lobby, SteamId friendId)
        {
            Enqueue(() =>
            {
                SetStatus($"收到好友邀请，正在加入大厅 {lobby.Id}…");
                _ = JoinLobbyAsync(lobby.Id.ToString());
            });
        }

        private void SetStatus(string message, bool isError = false)
        {
            Status = message;

            if (isError)
            {
                LastError = message;
                Debug.LogError($"[SteamLobby] {message}");
            }
            else
            {
                Debug.Log($"[SteamLobby] {message}");
            }
        }
    }
}
