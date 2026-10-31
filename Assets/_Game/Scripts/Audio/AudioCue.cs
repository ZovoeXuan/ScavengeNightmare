namespace ScavengeNightmare.Audio
{
    /// <summary>
    /// R6 四类占位音的 cue 名。D31 正式素材接入后换的是波形定义（见 <see cref="AudioCueInfo"/>），
    /// 不是这里的枚举，也不是任何调用点。
    /// </summary>
    public enum AudioCue
    {
        CartWheel = 0,
        HeavyDrop = 1,
        GlassBreak = 2,
        CurseOmen = 3
    }

    /// <summary>
    /// 占位音的四条波形定义。
    /// 「可区分」不是「都播了」，而是基频 / 时长 / 波形三组读数逐条不同：
    /// 判据取日志里的 fingerprint 原文去重（C5），所以这四行一旦写重，用例必须红。
    /// </summary>
    public static class AudioCueInfo
    {
        public const int Count = 4;

        public readonly struct Spec
        {
            public readonly string Key;
            public readonly float BaseFrequency;
            public readonly float Seconds;
            public readonly string Wave;
            public readonly float Gain;

            public Spec(string key, float baseFrequency, float seconds, string wave, float gain)
            {
                Key = key;
                BaseFrequency = baseFrequency;
                Seconds = seconds;
                Wave = wave;
                Gain = gain;
            }

            public string Fingerprint => $"freq={BaseFrequency:F0} dur={Seconds:F2} wave={Wave}";
        }

        // 四条定义必须互不相同；唯一性由 AudioBus 的播放日志与 -s4baudio 的判据共同兜住。
        private static readonly Spec[] Specs =
        {
            new("cart_wheel", 180f, 0.18f, "sine", 0.25f),
            new("heavy_drop", 90f, 0.22f, "thump", 0.50f),
            new("glass_break", 900f, 0.30f, "noise", 0.45f),
            new("curse_omen", 320f, 0.50f, "sweep", 0.35f)
        };

        public static Spec Of(AudioCue cue)
        {
            return Specs[(int)cue];
        }

        public static string KeyOf(AudioCue cue)
        {
            return Of(cue).Key;
        }
    }
}
