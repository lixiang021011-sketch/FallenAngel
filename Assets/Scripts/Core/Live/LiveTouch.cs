using UnityEngine;

namespace FallenAngel.Live
{
    /// <summary>触摸相位（与 EnhancedTouch / UnityEngine.TouchPhase 同构）。</summary>
    public enum LiveTouchPhase
    {
        Began = 0,
        Moved = 1,
        Stationary = 2,
        Ended = 3,
        Canceled = 4,
        None = 5
    }

    /// <summary>
    /// 一次输入（= 一根手指在某一时刻的状态）。逐字对应 OpenSekai 的 LiveTouch：
    /// fingerId / touchId / **滑动增量** / 世界坐标 / 相位 / **音乐时间戳**。
    ///
    /// 本工程扩展的两个字段（都在输入层就已经算好，判定簇只读）：
    /// · <see cref="lane"/>：屏幕坐标 → **小数轨道**的结果（&lt;0 = 判定区之外），对应原作 OnProcessTouch
    ///   里 GetJudgmentLane 的落点；
    /// · <see cref="isDirectionTrusted"/>：滑动方向是否可信（触屏 true；键盘/编辑器鼠标 false，
    ///   甩键方向判定据此放宽——原作没有键盘输入路径）。
    /// </summary>
    public struct LiveTouch
    {
        public int fingerId;

        public int touchId;

        public Vector2 delta;

        public Vector3 worldPosition;

        public LiveTouchPhase phase;

        public float musicTime;

        /// <summary>小数轨道（&lt;0 = 判定区之外）</summary>
        public float lane;

        /// <summary>屏幕坐标（视图/涟漪反馈用）</summary>
        public Vector2 screenPosition;

        /// <summary>滑动方向是否可信（触屏 true；键盘/鼠标 false）</summary>
        public bool isDirectionTrusted;

        public LiveTouch(int fingerId, int touchId, Vector2 delta, Vector3 worldPosition,
            LiveTouchPhase phase, float musicTime)
        {
            this.fingerId = fingerId;
            this.touchId = touchId;
            this.delta = delta;
            this.worldPosition = worldPosition;
            this.phase = phase;
            this.musicTime = musicTime;
            lane = -1f;
            screenPosition = Vector2.zero;
            isDirectionTrusted = false;
        }

        public LiveTouch(int fingerId, int touchId, Vector2 delta, Vector3 worldPosition,
            LiveTouchPhase phase, float musicTime, float lane, Vector2 screenPosition,
            bool isDirectionTrusted)
        {
            this.fingerId = fingerId;
            this.touchId = touchId;
            this.delta = delta;
            this.worldPosition = worldPosition;
            this.phase = phase;
            this.musicTime = musicTime;
            this.lane = lane;
            this.screenPosition = screenPosition;
            this.isDirectionTrusted = isDirectionTrusted;
        }

        /// <summary>逐字对应原作 InputExtensions.IsEndedOrCanceled。</summary>
        public bool IsEndedOrCanceled()
        {
            return phase == LiveTouchPhase.Ended || phase == LiveTouchPhase.Canceled;
        }

        public bool IsBegan()
        {
            return phase == LiveTouchPhase.Began;
        }
    }
}
