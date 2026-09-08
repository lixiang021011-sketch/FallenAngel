using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 轨道布局约定（Canvas 本地坐标，原点在 Canvas 中心，参考分辨率 1080×1920）。
    /// SceneBuilder（按键视觉）/ NoteSpawner（音符）/ InputManager（输入判定）共用，
    /// 保证不同视图缩放、裁切下按键视觉与输入判定一致。
    /// 键数为运行时状态：由 GameManager.LoadChart 按谱面 LaneCount 设置（4K/5K）。
    /// </summary>
    public static class LaneLayout
    {
        /// <summary>支持的最大轨道数（v2 协议 5 键 lane 0-4）</summary>
        public const int MaxLaneCount = 5;

        /// <summary>当前生效轨道数（默认 4，加载谱面时更新）</summary>
        public static int ActiveLaneCount = 4;

        /// <summary>4 键轨道中心（v1 兼容，保持历史值）</summary>
        private static readonly float[] Centers4 = { -225f, -75f, 75f, 225f };

        /// <summary>5 键轨道中心（v2 协议 lane 0-4，左右对称）</summary>
        private static readonly float[] Centers5 = { -270f, -135f, 0f, 135f, 270f };

        /// <summary>轨道宽度（Canvas 单位）</summary>
        public const float LaneWidth = 140f;

        /// <summary>
        /// 按当前生效键数设置活动轨道数（钳制到 1..MaxLaneCount）
        /// </summary>
        public static void SetActiveLaneCount(int count)
        {
            int clamped = Mathf.Clamp(count, 1, MaxLaneCount);
            if (clamped == ActiveLaneCount) return;
            ActiveLaneCount = clamped;
            Debug.Log($"[LaneLayout] 活动轨道数: {ActiveLaneCount}");
        }

        /// <summary>
        /// 取指定键数的轨道中心数组（4 → 4 键布局；其他 → 5 键布局）
        /// </summary>
        public static float[] GetCentersX(int laneCount)
        {
            return laneCount == 4 ? Centers4 : Centers5;
        }

        /// <summary>
        /// 取当前活动布局中某轨道中心的 Canvas 本地 X 坐标（越界自动钳制）
        /// </summary>
        public static float GetCenterXForActive(int lane)
        {
            int clamped = Mathf.Clamp(lane, 0, ActiveLaneCount - 1);
            return GetCentersX(ActiveLaneCount)[clamped];
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
        /// 按当前活动布局循环判定（支持 4/5 键，不再硬编码 if 链）。
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
