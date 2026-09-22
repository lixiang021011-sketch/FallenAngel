using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 开局倒计时的节奏与曲线（前端清单 E1）。
    ///
    /// 上游 `Sekai/CountdownView.cs` 的 `CountdownCore()`：把**特效对象数组从后往前**逐个激活，
    /// 每个之间 `WaitForSeconds(1f)`，并且**每一步都播一次** `SE_LIVE_COUNT_DOWN` ——
    /// 也就是"3、2、1 各 1 秒、每步一声"。
    /// 我们沿用这个节奏（3 → 2 → 1 各 1 秒 + GO! 0.5 秒），并把"每步一声 + 每步一个缩放弹出"
    /// 补上（原来只有纯文本，没声音也没动画）。特效本身按作者定案**不抄**，用程序化的缩放/淡出等价。
    /// </summary>
    public static class CountdownTiming
    {
        /// <summary>开头数字（含）</summary>
        public const int From = 3;
        /// <summary>最后数字（含）</summary>
        public const int To = 1;
        /// <summary>每个数字停留时长（秒，上游 WaitForSeconds(1f)）</summary>
        public const float StepSeconds = 1f;
        /// <summary>"GO!" 停留时长（秒）</summary>
        public const float GoSeconds = 0.5f;

        /// <summary>倒计时总时长（秒）：3 步 + GO</summary>
        public static float TotalSeconds
        {
            get { return (From - To + 1) * StepSeconds + GoSeconds; }
        }

        /// <summary>第几步该显示什么字（step 从 0 起）：3、2、1、GO!</summary>
        public static string LabelAt(int step)
        {
            int number = From - step;
            return number >= To ? number.ToString() : "GO!";
        }

        /// <summary>一步之内的缩放：0 → 1.35（放大出现），0.15s 后收敛到 1（三次缓出）</summary>
        public static float ScaleAt(float tInStep)
        {
            float t = Mathf.Clamp01(tInStep / 0.25f);
            float e = 1f - Mathf.Pow(1f - t, 3f);
            return Mathf.Lerp(1.35f, 1f, e);
        }

        /// <summary>一步之内的透明度：后 25% 淡出（交给下一步接上）</summary>
        public static float AlphaAt(float tInStep)
        {
            float fadeStart = StepSeconds * 0.75f;
            if (tInStep <= fadeStart) return 1f;
            return Mathf.Clamp01(1f - (tInStep - fadeStart) / (StepSeconds - fadeStart));
        }

        /// <summary>这一步是不是 "GO!"（用高音/强调色）</summary>
        public static bool IsGo(int step)
        {
            return From - step < To;
        }
    }
}
