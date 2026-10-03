namespace FallenAngel.Core
{
    /// <summary>
    /// 切换确认策略：**双击确认**（作者 2026-10-03 定）。
    ///
    /// 为什么要双击：切换条在屏幕最底部，正是拇指常驻/滑过的地方。单点会在
    /// "想打谱面""拇指蹭到"时误切换；双击的第一下只把芯片点亮成"待确认"，
    /// 第二下（<see cref="ConfirmSeconds"/> 内）才真的换声部，误触代价降到几乎为零。
    ///
    /// 纯逻辑、不碰 UI：自检可以直接断言（含"第二下太慢要重新算第一下"）。
    /// </summary>
    public sealed class PartSwitchTap
    {
        /// <summary>两下之间的最长间隔；超过就作废、重新算第一下</summary>
        public const float ConfirmSeconds = 0.5f;

        /// <summary>当前"待确认"的声部；null 表示没有</summary>
        public string PendingPart { get; private set; }
        private float pendingTime;

        /// <summary>
        /// 记一次点击。返回 true = 这次点击构成确认（该换声部了）；
        /// 返回 false = 这是第一下（调用方把该芯片显示成"待确认"）。
        /// </summary>
        public bool Register(string part, float time)
        {
            if (string.IsNullOrEmpty(part)) return false;
            bool isConfirm = !string.IsNullOrEmpty(PendingPart) && PendingPart == part
                             && time - pendingTime <= ConfirmSeconds;
            if (isConfirm)
            {
                PendingPart = null;
                return true;
            }
            PendingPart = part;
            pendingTime = time;
            return false;
        }

        /// <summary>把过期的"待确认"清掉；返回 true = 状态变了（调用方需要刷新显示）</summary>
        public bool Tick(float time)
        {
            if (string.IsNullOrEmpty(PendingPart)) return false;
            if (time - pendingTime <= ConfirmSeconds) return false;
            PendingPart = null;
            return true;
        }

        public void Reset()
        {
            PendingPart = null;
        }
    }
}
