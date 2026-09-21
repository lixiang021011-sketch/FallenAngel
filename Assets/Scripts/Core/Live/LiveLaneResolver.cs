using UnityEngine;
using FallenAngel.Core;

namespace FallenAngel.Live
{
    /// <summary>
    /// 屏幕/判定坐标 → **小数轨道**的解析器。逐字移植 OpenSekai Sekai/Core/Live/LiveLogic.cs
    /// 的 GetJudgmentLane（`:984-1023`）：
    ///
    ///   · 判定线上方的触摸不算（Y 闸门，容差 judgmentOffsetY）；
    ///   · 左右越界（超出 judgmentOffsetX）不算；
    ///   · 落在两轨中心之间 → 线性插值出**小数轨道**（整数 = 轨道中心）。
    ///
    /// 与上游的差别只在"坐标从哪来"：原作读 LiveConfig.JudgmentPositions（世界坐标折线，
    /// 由 AB 包里的 BuildData 决定），本工程按 Core/LaneLayout 的轨道中心现算——
    /// 所以轨道加宽/4↔5 键切换时，输入与判定自动跟着走。
    /// </summary>
    public class LiveLaneResolver
    {
        /// <summary>
        /// 判定线上的取点：0..LaneCount-1 = 各轨道中心，最后一项 = 最右轨中心再往右一个节距
        /// （与上游 LiveConfig.JudgmentPositions 的"12 条 lane 线"同构，多出的一项是右边界）。
        /// 坐标 = Canvas 本地坐标（原点在画布中心），与 LaneLayout / NoteSpawner 同源。
        /// </summary>
        public Vector2[] JudgmentPositions;

        /// <summary>左右判定区外扩（Canvas 像素；默认半个节距 = 轨道可视边）</summary>
        public float JudgmentOffsetX;

        /// <summary>判定线上方的容差（Canvas 像素；0 = 必须落在判定线上或下方，上游默认）</summary>
        public float JudgmentOffsetY;

        /// <summary>
        /// 是否启用"判定线上方的触摸不算"这道闸门（上游默认 true）。
        ///
        /// 本工程把它关掉（由接线层显式设置）：上游的 JudgmentOffsetY 来自 AB 包里的 BuildData，
        /// 实际值通常足以覆盖整个玩法区，所以"手指在滑条/drag 上往上游走"不会被判成"出区"；
        /// 我们按 0 照搬会把判定线当成硬墙——实机表现就是**滑条一往上滑就失去手指、drag 碰不到**
        /// （玩家口中的"不跟手/拖沓"）。我们的输入层已经用"屏幕下半部"做区域闸门了，
        /// 这一层只负责把 X 换算成小数轨道。
        /// </summary>
        public bool EnforceJudgmentYGate = true;

        /// <summary>按当前 LaneLayout 造一个解析器（判定线 Y 取 Core/PlayVisualSpec）。</summary>
        public static LiveLaneResolver CreateFromLaneLayout(float judgeLineY,
            int laneCount = 0, float judgmentOffsetY = 0f)
        {
            var count = laneCount > 0 ? laneCount : LaneLayout.ActiveLaneCount;
            var pitch = LaneLayout.GetLanePitch(count);
            var positions = new Vector2[count + 1];
            for (var i = 0; i < count; i++)
            {
                positions[i] = new Vector2(LaneLayout.GetCentersX(count)[i], judgeLineY);
            }

            positions[count] = new Vector2(positions[count - 1].x + pitch, judgeLineY);

            return new LiveLaneResolver
            {
                JudgmentPositions = positions,
                JudgmentOffsetX = pitch * 0.5f,
                JudgmentOffsetY = judgmentOffsetY
            };
        }

        /// <summary>判定线 Y（Canvas 本地坐标）；谱面判定坐标与它对齐</summary>
        public float JudgeLineY
        {
            get { return JudgmentPositions != null && JudgmentPositions.Length > 0 ? JudgmentPositions[0].y : 0f; }
        }

        /// <summary>逐字对应原作 GetJudgmentLane。返回 &lt;0 表示判定区之外。</summary>
        public float Resolve(Vector3 touchPosition, bool checkJudgmentY)
        {
            var positions = JudgmentPositions;
            if (positions == null || positions.Length == 0)
            {
                return -1f;
            }

            if (EnforceJudgmentYGate && checkJudgmentY && touchPosition.y - positions[0].y > JudgmentOffsetY)
            {
                return -1f;
            }

            if (positions[0].x - touchPosition.x > JudgmentOffsetX)
            {
                return -1f;
            }

            var lastIndex = positions.Length - 1;
            if (touchPosition.x - positions[lastIndex].x > JudgmentOffsetX)
            {
                return -1f;
            }

            for (var i = 0; i < positions.Length; i++)
            {
                if (touchPosition.x - positions[i].x <= 0f)
                {
                    if (i == 0)
                    {
                        return 0f;
                    }

                    var previousX = positions[i - 1].x;
                    var currentX = positions[i].x;
                    return (touchPosition.x - previousX) / (currentX - previousX) + i - 1;
                }
            }

            return lastIndex;
        }

        /// <summary>整数轨道 → 该轨中心的 Canvas X（与 Resolve 同一坐标系，便于自检对照）</summary>
        public float CenterXOfLane(int lane)
        {
            if (JudgmentPositions == null || JudgmentPositions.Length == 0)
            {
                return 0f;
            }

            var clamped = Mathf.Clamp(lane, 0, JudgmentPositions.Length - 1);
            return JudgmentPositions[clamped].x;
        }
    }
}
