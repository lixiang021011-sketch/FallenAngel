using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 加载页的小贴士（前端清单 E5）。上游 `LoadingView.Setup(MasterTip tip)` 会给标题/说明/图 + 一条
    /// master data 里的小贴士；我们没有 master data，用语言表里的固定几条，
    /// **按曲名做确定性选择**（同一首歌每次进都显示同一条，不会随机跳；也便于复现问题）。
    ///
    /// 加载页展示时长：上游是"资源真的在加载"的时长；我们本地读谱很快，
    /// 所以固定给 `LoadingSeconds` 秒的展示窗口（见 GameManager 的开局流程），
    /// 不然加载页只会闪一帧，观感上等于没有。
    /// </summary>
    public static class LoadingTip
    {
        /// <summary>加载页最短展示时长（秒）—— 之后才进倒计时</summary>
        public const float LoadingSeconds = 0.8f;

        /// <summary>贴士条数（语言表 key：loading.tip.0 … loading.tip.N-1）</summary>
        public const int TipCount = 4;

        /// <summary>按"曲名 + 曲师"选一条贴士（确定性：同一首歌永远同一条）</summary>
        public static int TipIndex(string songName, string songArtist)
        {
            if (TipCount <= 1) return 0;
            string key = (songName ?? string.Empty) + "|" + (songArtist ?? string.Empty);
            int hash = 17;
            for (int i = 0; i < key.Length; i++)
                hash = unchecked(hash * 31 + key[i]);
            return Mathf.Abs(hash % TipCount);
        }

        public static string TipKey(int index)
        {
            int clamped = Mathf.Clamp(index, 0, TipCount - 1);
            return "loading.tip." + clamped;
        }
    }
}
