namespace FallenAngel.Core
{
    /// <summary>
    /// 轨道布局约定（Canvas 本地坐标，原点在 Canvas 中心，参考分辨率 1080×1920）。
    /// SceneBuilder（按键视觉）/ NoteSpawner（音符）/ InputManager（输入判定）共用，
    /// 保证不同视图缩放、裁切下按键视觉与输入判定一致。
    /// </summary>
    public static class LaneLayout
    {
        /// <summary>轨道数量</summary>
        public const int LaneCount = 4;

        /// <summary>4 个轨道中心的 Canvas 本地 X 坐标</summary>
        public static readonly float[] CentersX = { -225f, -75f, 75f, 225f };

        /// <summary>轨道宽度（Canvas 单位）</summary>
        public const float LaneWidth = 140f;

        /// <summary>
        /// 根据 Canvas 本地 X 坐标计算所属轨道（按相邻轨道中点划分，两侧延伸到底）
        /// </summary>
        public static int GetLaneFromCanvasX(float canvasX)
        {
            if (canvasX < (CentersX[0] + CentersX[1]) * 0.5f) return 0;
            if (canvasX < (CentersX[1] + CentersX[2]) * 0.5f) return 1;
            if (canvasX < (CentersX[2] + CentersX[3]) * 0.5f) return 2;
            return 3;
        }
    }
}
