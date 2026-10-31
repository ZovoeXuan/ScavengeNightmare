using System.Collections.Generic;
using UnityEngine;

namespace ScavengeNightmare.Audio
{
    /// <summary>
    /// 占位音总线：四类 cue 的合成与播放。
    ///
    /// 为什么不用场景里的 AudioSource 池：4A/4B 的自动化进程都是「场景里没摆音频」的形态，
    /// 一个静态入口 + PlayClipAtPoint 不需要任何装配，也不会让 30 条回归多出一个场景对象。
    /// D31 正式素材到位后只改 <see cref="BuildSamples"/> 与 Clip 来源，调用点一行不动。
    ///
    /// 三条硬约束：
    /// 1) 每类 cue 独立冷却 0.15s（风险表 R4：40 件货 + 4 只怪时不许刷爆）；
    /// 2) 日志必须带 cue 名 + 波形指纹 + 触发读数（speed / mass / lead），
    ///    否则「可区分」会退化成「都播过」；
    /// 3) 合成必须确定性：同一 cue 同一 pitch 每次生成同一段采样，测试才可复现。
    /// </summary>
    public static class AudioBus
    {
        /// <summary>同一 cue 的最小间隔（风险表 R4）。</summary>
        public const float CooldownSeconds = 0.15f;

        private const int SampleRate = 22050;

        private static readonly Dictionary<string, AudioClip> cache = new();
        private static readonly float[] lastPlayAt = new float[AudioCueInfo.Count];

        /// <summary>
        /// C5 反向注入：把四类 cue 压成同一段波形，判据必须报 distinguishable=1 并 FAIL。
        /// 只由 -s4caudioflat 打开，正式用例里恒为 false。
        /// </summary>
        public static bool FlattenForTest { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnLoad()
        {
            cache.Clear();
            FlattenForTest = false;

            for (int i = 0; i < lastPlayAt.Length; i++)
            {
                lastPlayAt[i] = -999f;
            }
        }

        /// <summary>
        /// 播一条占位音。extra 是触发读数（如 speed=2.4 / mass=42.0 / lead=1.5），
        /// 它既是给玩家看的依据，也是判据要抓的那一段原文。
        /// </summary>
        public static void Play(AudioCue cue, Vector3 position, float pitch = 1f, string extra = null)
        {
            AudioCueInfo.Spec spec = AudioCueInfo.Of(cue);

            int index = (int)cue;
            if (Time.unscaledTime - lastPlayAt[index] < CooldownSeconds)
            {
                return;
            }

            lastPlayAt[index] = Time.unscaledTime;

            // 注入时所有 cue 共用 cart_wheel 的波形，指纹随之相同；
            // 日志里的 cue 名仍是真实 cue，这样判据才能看清「四类都播了但只有一段波形」。
            AudioCueInfo.Spec shot = FlattenForTest ? AudioCueInfo.Of(AudioCue.CartWheel) : spec;
            float usedPitch = Mathf.Clamp(pitch, 0.6f, 1.6f);

            AudioClip clip = ClipFor(shot, usedPitch);
            if (clip != null)
            {
                AudioSource.PlayClipAtPoint(clip, position, shot.Gain);
            }

            string context = string.IsNullOrEmpty(extra) ? string.Empty : " " + extra;
            Debug.Log($"[S4C] S4C_AUDIO_PLAY cue={spec.Key} pitch={usedPitch:F2} {shot.Fingerprint}{context}");
        }

        private static AudioClip ClipFor(AudioCueInfo.Spec spec, float pitch)
        {
            string key = $"{spec.Key}|{spec.Wave}|{spec.BaseFrequency:F0}|{spec.Seconds:F2}|{Mathf.RoundToInt(pitch * 100f)}";

            if (cache.TryGetValue(key, out AudioClip clip) && clip != null)
            {
                return clip;
            }

            int count = Mathf.Max(1, (int)(SampleRate * spec.Seconds));
            clip = AudioClip.Create($"placeholder_{spec.Key}", count, 1, SampleRate, false);
            clip.SetData(BuildSamples(spec, pitch, count), 0);
            cache[key] = clip;
            return clip;
        }

        /// <summary>
        /// 四种波形互不相同（正弦 / 低频方波 / 噪声爆裂 / 频率下滑），
        /// 这是「可区分」在代码这侧的唯一来源；判据只认日志里的指纹，不认这里的注释。
        /// </summary>
        private static float[] BuildSamples(AudioCueInfo.Spec spec, float pitch, int count)
        {
            float[] samples = new float[count];
            float frequency = spec.BaseFrequency * pitch;

            // 噪声波形也要可复现：固定种子，同一 cue 同一 pitch 每次一样。
            System.Random noise = new System.Random(Mathf.RoundToInt(spec.BaseFrequency * 100f) + Mathf.RoundToInt(pitch * 100f));

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / SampleRate;
                float p = (float)i / count;
                float envelope = 1f - p;

                float value;
                switch (spec.Wave)
                {
                    case "thump":
                        // 低频「咚」：方波底 + 正弦，落地重量感靠它
                        value = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * frequency * t)) * 0.45f
                                + Mathf.Sin(2f * Mathf.PI * frequency * t) * 0.55f;
                        envelope *= envelope;
                        break;

                    case "noise":
                        // 碎裂：确定性噪声 + 快速衰减
                        value = ((float)noise.NextDouble() * 2f - 1f) * 0.9f;
                        envelope *= envelope;
                        break;

                    case "sweep":
                        // 诅咒前兆：320Hz 滑到 200Hz 左右，像一句没说完的笑
                        float sweeping = Mathf.Lerp(frequency, frequency * 0.62f, p);
                        value = Mathf.Sin(2f * Mathf.PI * sweeping * t);
                        break;

                    default:
                        // 轮声：纯正弦
                        value = Mathf.Sin(2f * Mathf.PI * frequency * t);
                        break;
                }

                samples[i] = value * envelope * spec.Gain;
            }

            return samples;
        }
    }
}
