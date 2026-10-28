using ScavengeNightmare.Gameplay;
using ScavengeNightmare.Traits;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ScavengeNightmare.EditorTools
{
    /// <summary>
    /// D8 缺点牌灰盒装配（幂等）：
    ///   1. 生成 4 个角色身份 + 5 张缺点牌 + 1 个 TraitDatabase 资产
    ///   2. Player prefab 挂 PlayerTraits + TraitHUD
    ///   3. 场景挂 TraitBootstrap（引用数据库）+ D8AutoTest
    /// 批处理入口：-executeMethod ScavengeNightmare.EditorTools.D8TraitSetup.Setup
    /// </summary>
    public static class D8TraitSetup
    {
        private const string ScenePath = "Assets/_Game/Scenes/Main.unity";
        private const string PlayerPrefabPath = "Assets/_Game/Prefabs/Player.prefab";
        private const string DataFolder = "Assets/_Game/Data/Traits";
        private const string DbPath = DataFolder + "/TraitDatabase.asset";

        // 4 个固定角色：每人一强一弱，弱项能被缺点牌放大
        private sealed class RoleSpec
        {
            public string Id, Name, Desc;
            public Color Color;
            public Vector3 Scale;
            public float Speed, Grab, Throw, Range, Stun, CarryOverride;
            public bool Sprint;
        }

        private static readonly RoleSpec[] Roles =
        {
            new RoleSpec { Id="brute", Name="大力士", Desc="力大无穷，扔得远、抱得动，但手笨又慢，精细活干不了。",
                Color=new Color(0.90f,0.30f,0.28f), Scale=new Vector3(1.25f,1.1f,1.25f),
                Speed=0.85f, Grab=0.7f, Throw=1.5f, Range=1f, Stun=0.8f, CarryOverride=110f, Sprint=true },
            new RoleSpec { Id="nearsight", Name="近视眼", Desc="看得清近处的钱，远处的小件一律抓瞎；跑得不慢。",
                Color=new Color(0.35f,0.62f,0.92f), Scale=Vector3.one,
                Speed=1f, Grab=1f, Throw=1f, Range=0.55f, Stun=1f, CarryOverride=0f, Sprint=true },
            new RoleSpec { Id="cripple", Name="瘸子", Desc="不能冲刺、走得慢，但下盘稳、抗揍，重物砸不趴。",
                Color=new Color(0.55f,0.78f,0.42f), Scale=new Vector3(0.9f,1f,0.9f),
                Speed=0.72f, Grab=1f, Throw=0.95f, Range=1f, Stun=0.5f, CarryOverride=90f, Sprint=false },
            new RoleSpec { Id="agile", Name="灵敏", Desc="又快又稳、抓得牢，但身板单薄、力气小，扔不远也扛不住砸。",
                Color=new Color(0.95f,0.80f,0.35f), Scale=new Vector3(0.85f,1f,0.85f),
                Speed=1.25f, Grab=1.35f, Throw=0.7f, Range=1.15f, Stun=1.4f, CarryOverride=0f, Sprint=true },
        };

        // 5 张缺点牌：代价与补偿分处不同轴；每张标注最强冲突角色
        private sealed class CardSpec
        {
            public string Id, Name, Comedy, Comp, Conflict;
            public int Base, Bonus;
            public float Grab, Speed, Interact, Stun, CarryDiv;
            public bool BlocksSmall, Shaky, Panic, Scent;
            public float SmallLimit, ShakeAmp, PanicScale;
            public float CompThrow, CompSpeed, CompGrab, CompCarry;
            public bool ExclSlower, ExclClumsier;
        }

        private static readonly CardSpec[] Cards =
        {
            // 手抖：抓握刚度↓，补偿是扔得更远。与大力士冲突最强（力气最大却抓不稳）
            // ExclClumsier=false：大力士手笨是身份特征，手抖正是要放大它（headline 冲突），不排除
            new CardSpec { Id="shaky", Name="手抖", Comedy="力气最大的人，端着最轻的东西也在抖。", Comp="扔得比谁都远", Conflict="brute",
                Base=10, Bonus=30, Grab=0.55f, Speed=1f, Interact=1f, Stun=1f, CarryDiv=1f,
                BlocksSmall=false, Shaky=true, Panic=false, Scent=false,
                SmallLimit=0f, ShakeAmp=0.12f, PanicScale=1f,
                CompThrow=1.6f, CompSpeed=1f, CompGrab=1f, CompCarry=1f, ExclSlower=false, ExclClumsier=false },
            // 看不清小件：交互距离↓ + 小件抓不到，补偿是「闻香定位值钱货」
            new CardSpec { Id="blursmall", Name="看不清小件", Comedy="罐头、钥匙全糊成一片，只有大件的香味闻得到。", Comp="能闻到最贵的货在哪", Conflict="nearsight",
                Base=10, Bonus=30, Grab=1f, Speed=1f, Interact=0.7f, Stun=1f, CarryDiv=1f,
                BlocksSmall=true, Shaky=false, Panic=false, Scent=true,
                SmallLimit=6f, ShakeAmp=0f, PanicScale=1f,
                CompThrow=1f, CompSpeed=1f, CompGrab=1f, CompCarry=1f, ExclSlower=false, ExclClumsier=false },
            // 腿软：速度↓ 且不能冲刺，补偿是抗揍。与灵敏冲突最强（最快的却腿软）
            new CardSpec { Id="weaklegs", Name="腿软", Comedy="本来跑得最快，结果一紧张就迈不开腿。", Comp="皮糙，被砸不易趴", Conflict="agile",
                Base=10, Bonus=30, Grab=1f, Speed=0.75f, Interact=1f, Stun=0.7f, CarryDiv=1f,
                BlocksSmall=false, Shaky=false, Panic=false, Scent=false,
                SmallLimit=0f, ShakeAmp=0f, PanicScale=1f,
                CompThrow=1f, CompSpeed=1f, CompGrab=1f, CompCarry=1f, ExclSlower=true, ExclClumsier=false },
            // 闻香定位（正向牌也做成「缺点+补偿」结构：嗅觉灵敏= distract 但能找到货）
            new CardSpec { Id="sniffer", Name="贪财", Comedy="一看到值钱货就走不动道，非得凑上去闻闻。", Comp="全场最贵货的位置对你可见", Conflict="brute",
                Base=8, Bonus=20, Grab=1f, Speed=0.92f, Interact=1f, Stun=1f, CarryDiv=1f,
                BlocksSmall=false, Shaky=false, Panic=false, Scent=true,
                SmallLimit=0f, ShakeAmp=0f, PanicScale=1f,
                CompThrow=1f, CompSpeed=1f, CompGrab=1f, CompCarry=1f, ExclSlower=false, ExclClumsier=false },
            // 窄门恐惧：进门洞降速，补偿是冲刺更快（平地能跑）
            // 注意 ExclSlower=false：本牌全局 speedScale=1，代价是「门洞内」行为型减速，
            // 与瘸子的全局减速不同轴，不构成叠 buff；瘸子+窄门恐惧正是要的高频冲突组合。
            new CardSpec { Id="claustro", Name="窄门恐惧", Comedy="一钻门洞就发怵，磨磨蹭蹭；可一到开阔地就撒腿狂奔。", Comp="平地冲刺更快", Conflict="cripple",
                Base=10, Bonus=25, Grab=1f, Speed=1f, Interact=1f, Stun=1f, CarryDiv=1f,
                BlocksSmall=false, Shaky=false, Panic=true, Scent=false,
                SmallLimit=0f, ShakeAmp=0f, PanicScale=0.45f,
                CompThrow=1f, CompSpeed=1.25f, CompGrab=1f, CompCarry=1f, ExclSlower=false, ExclClumsier=false },
        };

        [MenuItem("拾荒惊魂/D8 · 装配缺点牌灰盒")]
        public static void Setup()
        {
            EnsureFolder();

            CharacterIdentity[] identities = new CharacterIdentity[Roles.Length];
            for (int i = 0; i < Roles.Length; i++) identities[i] = UpsertRole(Roles[i]);

            DrawbackCard[] cards = new DrawbackCard[Cards.Length];
            for (int i = 0; i < Cards.Length; i++) cards[i] = UpsertCard(Cards[i]);

            TraitDatabase db = UpsertDatabase(identities, cards);
            PatchPlayerPrefab();
            PatchScene(db);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[D8] D8_SETUP_OK identities={identities.Length} cards={cards.Length}");
        }

        [MenuItem("拾荒惊魂/D8 · 校验缺点牌灰盒")]
        public static void Validate()
        {
            int failures = 0;
            void Check(bool ok, string name) { if (!ok) { failures++; Debug.LogError($"[D8] D8_VALIDATE_FAIL check={name}"); } }

            TraitDatabase db = AssetDatabase.LoadAssetAtPath<TraitDatabase>(DbPath);
            Check(db != null, "database");
            if (db != null)
            {
                Check(db.identities != null && db.identities.Length == 4, $"identities_{db.identities?.Length}");
                Check(db.cards != null && db.cards.Length == 5, $"cards_{db.cards?.Length}");
            }

            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            Check(player != null, "player_prefab");
            if (player != null)
            {
                Check(player.GetComponent<PlayerTraits>() != null, "player_traits");
                Check(player.GetComponent<TraitHUD>() != null, "player_trait_hud");
            }

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Check(Object.FindAnyObjectByType<TraitBootstrap>() != null, "scene_bootstrap");
            Check(Object.FindAnyObjectByType<D8AutoTest>() != null, "scene_autotest");

            if (failures == 0) Debug.Log("[D8] D8_VALIDATE_OK identities=4 cards=5 traits=1 hud=1 bootstrap=1");
            else Debug.LogError($"[D8] D8_VALIDATE_FAILED count={failures}");
        }

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Game/Data")) AssetDatabase.CreateFolder("Assets/_Game", "Data");
            if (!AssetDatabase.IsValidFolder(DataFolder)) AssetDatabase.CreateFolder("Assets/_Game/Data", "Traits");
        }

        private static CharacterIdentity UpsertRole(RoleSpec s)
        {
            string path = $"{DataFolder}/Role_{s.Id}.asset";
            CharacterIdentity r = AssetDatabase.LoadAssetAtPath<CharacterIdentity>(path);
            if (r == null) { r = ScriptableObject.CreateInstance<CharacterIdentity>(); AssetDatabase.CreateAsset(r, path); }
            r.id = s.Id; r.displayName = s.Name; r.description = s.Desc;
            r.bodyColor = s.Color; r.bodyScale = s.Scale;
            r.speedScale = s.Speed; r.canSprint = s.Sprint; r.carryDivisorOverride = s.CarryOverride;
            r.grabStiffnessScale = s.Grab; r.throwScale = s.Throw; r.interactRangeScale = s.Range; r.stunScale = s.Stun;
            EditorUtility.SetDirty(r);
            return r;
        }

        private static DrawbackCard UpsertCard(CardSpec s)
        {
            string path = $"{DataFolder}/Drawback_{s.Id}.asset";
            DrawbackCard c = AssetDatabase.LoadAssetAtPath<DrawbackCard>(path);
            if (c == null) { c = ScriptableObject.CreateInstance<DrawbackCard>(); AssetDatabase.CreateAsset(c, path); }
            c.id = s.Id; c.displayName = s.Name; c.comedyNotes = s.Comedy; c.compensationText = s.Comp;
            c.strongestConflictId = s.Conflict; c.baseWeight = s.Base; c.conflictWeightBonus = s.Bonus;
            c.grabStiffnessScale = s.Grab; c.speedScale = s.Speed; c.interactRangeScale = s.Interact;
            c.stunScale = s.Stun; c.carryDivisorScale = s.CarryDiv;
            c.blocksSmallItems = s.BlocksSmall; c.shakyHands = s.Shaky; c.narrowDoorPanic = s.Panic; c.valueScent = s.Scent;
            c.smallItemMassLimit = s.SmallLimit; c.shakeAmplitude = s.ShakeAmp; c.panicSpeedScale = s.PanicScale;
            c.compensationThrowScale = s.CompThrow; c.compensationSpeedScale = s.CompSpeed;
            c.compensationGrabScale = s.CompGrab; c.compensationCarryDivisorScale = s.CompCarry;
            c.excludeIfIdentitySlower = s.ExclSlower; c.excludeIfIdentityClumsier = s.ExclClumsier;
            EditorUtility.SetDirty(c);
            return c;
        }

        private static TraitDatabase UpsertDatabase(CharacterIdentity[] ids, DrawbackCard[] cards)
        {
            TraitDatabase db = AssetDatabase.LoadAssetAtPath<TraitDatabase>(DbPath);
            if (db == null) { db = ScriptableObject.CreateInstance<TraitDatabase>(); AssetDatabase.CreateAsset(db, DbPath); }
            db.identities = ids; db.cards = cards; db.seed = 0;
            EditorUtility.SetDirty(db);
            return db;
        }

        private static void PatchPlayerPrefab()
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);

            PlayerTraits traits = contents.GetComponent<PlayerTraits>();
            if (traits == null) traits = contents.AddComponent<PlayerTraits>();

            SerializedObject so = new SerializedObject(traits);
            SerializedProperty dbProp = so.FindProperty("database");
            if (dbProp != null) dbProp.objectReferenceValue = AssetDatabase.LoadAssetAtPath<TraitDatabase>(DbPath);
            SerializedProperty rootProp = so.FindProperty("bodyRoot");
            if (rootProp != null)
            {
                Transform mesh = contents.transform.Find("Mesh");
                rootProp.objectReferenceValue = mesh;
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            if (contents.GetComponent<TraitHUD>() == null) contents.AddComponent<TraitHUD>();

            PrefabUtility.SaveAsPrefabAsset(contents, PlayerPrefabPath);
            PrefabUtility.UnloadPrefabContents(contents);
        }

        private static void PatchScene(TraitDatabase db)
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            GameObject host = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "TraitBootstrap") { host = root; break; }
            }
            if (host == null) host = new GameObject("TraitBootstrap");

            TraitBootstrap bootstrap = host.GetComponent<TraitBootstrap>();
            if (bootstrap == null) bootstrap = host.AddComponent<TraitBootstrap>();
            SerializedObject bso = new SerializedObject(bootstrap);
            SerializedProperty dbProp = bso.FindProperty("database");
            if (dbProp != null) dbProp.objectReferenceValue = db;
            bso.ApplyModifiedPropertiesWithoutUndo();

            if (host.GetComponent<D8AutoTest>() == null) host.AddComponent<D8AutoTest>();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
    }
}
