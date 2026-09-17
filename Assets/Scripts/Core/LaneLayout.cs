using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 轨道布局约定（Canvas 本地坐标，原点在 Canvas 中心，参考分辨率 1080×1920）。
    /// SceneBuilder（按键视觉）/ NoteSpawner（音符）/ InputManager（输入判定）共用，
    /// 保证不同视图缩放、裁切下按键视觉与输入判定一致。
    ///
    /// **2026-09-18 改动：轨道加宽到几乎占满屏幕宽。**此前 4 键轨道每组只有 590px（1080 的 55%），
    /// 视觉上挤在中间；现在两侧各留 20px，缝隙占节距 10%：
    ///   5 键：节距 208，轨宽 187，中心 ±416 / ±208 / 0
    ///   4 键：节距 260，轨宽 234，中心 ±390 / ±130
    /// 尺寸不再写死，一律由 <see cref="GetLaneWidth"/> / <see cref="GetCentersX"/> 按当前键数算出。
    /// </summary>
    public static class LaneLayout
    {
        /// <summary>支持的最大轨道数（v2 协议 5 键 lane 0-4）</summary>
        public const int MaxLaneCount = 5;

        /// <summary>当前生效轨道数（默认 4，加载谱面时更新）</summary>
        public static int ActiveLaneCount = 4;

        /// <summary>参考画布宽（竖屏）</summary>
        public const float CanvasWidth = 1080f;

        /// <summary>两侧留白：越小轨道越宽（20px ≈ 几乎占满）</summary>
        public const float SideMargin = 20f;

        /// <summary>轨间缝占节距的比例（0.10 = 每两条轨道之间有 10% 的缝）</summary>
        public const float LaneGapRatio = 0.10f;

        /// <summary>
        /// 外观尺寸的**参考轨道宽**：配表里的音符尺寸（如 130×28）是按这个宽度设计的。
        /// 实际尺寸 = 配表尺寸 × (当前轨宽 / 本值)，见 <see cref="SizeScale"/>。
        /// </summary>
        public const float ReferenceLaneWidth = 140f;

        private static readonly float[][] centersCache = new float[MaxLaneCount + 1][];

        /// <summary>指定键数下的可用总宽（画布宽 − 两侧留白）</summary>
        public static float GetUsableWidth(int laneCount)
        {
            return CanvasWidth - SideMargin * 2f;
        }

        /// <summary>指定键数下的轨道节距（中心到中心）</summary>
        public static float GetLanePitch(int laneCount)
        {
            int n = Mathf.Clamp(laneCount, 1, MaxLaneCount);
            return GetUsableWidth(n) / n;
        }

        /// <summary>指定键数下的轨道宽度（已扣掉缝）</summary>
        public static float GetLaneWidth(int laneCount)
        {
            return GetLanePitch(laneCount) * (1f - LaneGapRatio);
        }

        /// <summary>当前活动轨道宽（动态；原先是写死的常量 140）</summary>
        public static float LaneWidth
        {
            get { return GetLaneWidth(ActiveLaneCount); }
        }

        /// <summary>外观尺寸缩放：配表尺寸 × 本值 = 实际尺寸（音符宽度随轨道宽自适配）</summary>
        public static float SizeScale
        {
            get { return LaneWidth / ReferenceLaneWidth; }
        }

        /// <summary>按当前生效键数设置活动轨道数（钳制到 1..MaxLaneCount）</summary>
        public static void SetActiveLaneCount(int count)
        {
            int clamped = Mathf.Clamp(count, 1, MaxLaneCount);
            if (clamped == ActiveLaneCount) return;
            ActiveLaneCount = clamped;
            Debug.Log(string.Format("[LaneLayout] 活动轨道数: {0}（轨宽 {1:0.#}，缩放 {2:0.###}）",
                ActiveLaneCount, LaneWidth, SizeScale));
        }

        /// <summary>取指定键数的轨道中心数组（左右对称、等距、几乎铺满画布宽）</summary>
        public static float[] GetCentersX(int laneCount)
        {
            int n = Mathf.Clamp(laneCount, 1, MaxLaneCount);
            if (centersCache[n] == null)
            {
                float pitch = GetLanePitch(n);
                float first = -GetUsableWidth(n) * 0.5f + pitch * 0.5f;
                var centers = new float[n];
                for (int i = 0; i < n; i++) centers[i] = first + pitch * i;
                centersCache[n] = centers;
            }
            return centersCache[n];
        }

        /// <summary>取当前活动布局中某轨道中心的 Canvas 本地 X 坐标（越界自动钳制）</summary>
        public static float GetCenterXForActive(int lane)
        {
            float[] centers = GetCentersX(ActiveLaneCount);
            return centers[Mathf.Clamp(lane, 0, centers.Length - 1)];
        }

        /// <summary>
        /// 连续轨道坐标 → Canvas X（slide 路径用）：
        /// x=0 → 最左轨中心，x=(laneCount-1) → 最右轨中心，中间线性插值。
        /// </summary>
        public static float GetXFromLaneCoord(float x)
        {
            int count = ActiveLaneCount;
            if (count <= 1) return GetCenterXForActive(0);
            float t = Mathf.Clamp01(x / (count - 1f));
            return Mathf.Lerp(GetCenterXForActive(0), GetCenterXForActive(count - 1), t);
        }

        /// <summary>
        /// 根据 Canvas 本地 X 坐标计算所属轨道（按相邻轨道中点划分，两侧延伸到底）。
        /// </summary>
        public static int GetLaneFromCanvasX(float canvasX)
        {
            float[] centers = GetCentersX(ActiveLaneCount);
            for (int i = 0; i < centers.Length - 1; i++)
            {
                if (canvasX < (centers[i] + centers[i + 1]) * 0.5f) return i;
            }
            return centers.Length - 1;
        }
    }
}
