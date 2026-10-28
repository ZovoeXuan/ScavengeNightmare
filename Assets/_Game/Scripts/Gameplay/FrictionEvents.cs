using System;
using System.Collections.Generic;
using UnityEngine;

namespace ScavengeNightmare.Gameplay
{
    /// <summary>
    /// 摩擦事件的统一入口：数据表读取、日志、屏幕提示、占位音效。
    ///
    /// 「可感知」在 D6 只要求到这一层：日志（供自动化验收与 D7 数笑点）
    /// + 屏幕提示（供真人看懂发生了什么）+ 合成短音（占位，D31 换正式音效）。
    /// </summary>
    public static class FrictionEvents
    {
        public const int MaxRecent = 4;
        private static readonly List<string> recent = new();
        private static FrictionTuning tuning;
        private static Dictionary<string, AudioClip> toneCache;

        /// <summary>当前摩擦表；未配置时返回一份内存默认值，保证任何场景都不会空引用。</summary>
        public static FrictionTuning Tuning
        {
            get
            {
                if (tuning == null)
                {
                    tuning = ScriptableObject.CreateInstance<FrictionTuning>();
                }

                return tuning;
            }
        }

        public static void Use(FrictionTuning value)
        {
            if (value != null)
            {
                tuning = value;
            }
        }

        public static IReadOnlyList<string> Recent => recent;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnLoad()
        {
            tuning = null;
            recent.Clear();
            toneCache = null;
        }

        public static void Report(string code, string message, float toneFrequency = 0f)
        {
            string text = $"[{code}] {message}";
            Debug.Log($"[FRICTION] {text}");

            recent.Add(text);
            if (recent.Count > MaxRecent)
            {
                recent.RemoveAt(0);
            }

            if (toneFrequency > 0f && Tuning.enablePlaceholderAudio)
            {
                PlayTone(toneFrequency);
            }
        }

        /// <summary>
        /// 占位音效：程序化合成 0.12 秒正弦短音，替代还没做出来的惨叫 / 门夹 / 碎裂音。
        /// D31 正式音效接入后整体替换。
        /// </summary>
        private static void PlayTone(float frequency)
        {
            if (toneCache == null)
            {
                toneCache = new Dictionary<string, AudioClip>();
            }

            string key = frequency.ToString("F0");
            if (!toneCache.TryGetValue(key, out AudioClip clip))
            {
                const int sampleRate = 22050;
                int count = (int)(sampleRate * 0.12f);
                clip = AudioClip.Create($"friction_{key}", count, 1, sampleRate, false);
                float[] samples = new float[count];
                for (int i = 0; i < count; i++)
                {
                    float envelope = 1f - (float)i / count;
                    samples[i] = Mathf.Sin(2f * Mathf.PI * frequency * i / sampleRate) * envelope * 0.35f;
                }

                clip.SetData(samples, 0);
                toneCache[key] = clip;
            }

            if (Camera.main != null)
            {
                AudioSource.PlayClipAtPoint(clip, Camera.main.transform.position, 0.7f);
            }
        }
    }
}
