using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 演奏区的「近大远小」透视基准（单一来源）。
    ///
    /// 来源（2026-09-20，参考 OpenSekai 复刻 Project Sekai 的音符渲染）：
    /// 原作音符的位置与缩放由同一条非线性曲线驱动——
    ///     viewProgress = 1.06^((min(p,2) − 1) × 45)
    ///     y     = lerp(spawnY, judgeY, viewProgress)     // LerpUnclamped：过线后继续飞
    ///     scale = viewProgress
    /// 于是音符从上方出发、边下落边放大，越靠近判定线越大。
    ///
    /// 本工程自定的两点差异（作者 2026-09-20 选型「B」）：
    ///  1. **尺寸有下限**：scale = max(MinScale, viewProgress)。原作起点只有 7%，
    ///     远处的音符在高密度段落会糊成小点；下限 0.35 保证远处仍看得清。
    ///  2. **位置仍走原曲线**（下限只作用于尺寸）——若把下限也套到位置上，
    ///     音符一"出生"就会被拉到屏幕中部，看起来像悬停而不是下落。
    ///
    /// 轨道（PerspectiveLaneGraphic）与音符（Note.UpdatePosition）都只读本类，
    /// 所以"轨道边缘在任意高度上的宽度"与"该高度上音符的宽度"用的是同一个 scale，
    /// 这就是"音符与轨道严格对齐"的保证；PlayPerspectiveChecks 会断言这条不变量。
    /// </summary>
    public static class PlayPerspective
    {
        /// <summary>曲线底数（原作口径）</summary>
        public const float CurveBase = 1.06f;

        /// <summary>曲线增益（原作口径）</summary>
        public const float CurveGain = 45f;

        /// <summary>尺寸下限（作者选型 B：0.35）</summary>
        public const float MinScale = 0.35f;

        /// <summary>总开关（关掉即回到"线性下落 + 恒定尺寸"，用于对比与回退）</summary>
        public static bool Enabled = true;

        /// <summary>当前演奏几何（画布中心系）：由 NoteSpawner 登记，轨道与音符共用</summary>
        public static float SpawnY { get; private set; } = 600f;
        public static float JudgeY { get; private set; } = -560f;

        /// <summary>几何变化通知（轨道网格重算用）</summary>
        public static event System.Action GeometryChanged;

        /// <summary>由 NoteSpawner 在启用时登记自己的生成点/判定线（保证轨道与音符同源）</summary>
        public static void SetGeometry(float spawnY, float judgeY)
        {
            if (Mathf.Approximately(SpawnY, spawnY) && Mathf.Approximately(JudgeY, judgeY)) return;
            SpawnY = spawnY;
            JudgeY = judgeY;
            GeometryChanged?.Invoke();
        }

        /// <summary>原始非线性进度（不含尺寸下限）；p=1 时恰为 1</summary>
        public static float ViewProgress(float p)
        {
            float clamped = Mathf.Min(p, 2f);           // 原作对 p>2 截断
            return Mathf.Pow(CurveBase, (clamped - 1f) * CurveGain);
        }

        /// <summary>
        /// 归一化到 [0,1] 的曲线（p=0 → 0、p=1 → 1）。
        /// 原作直接用未归一化的值，于是音符"出生"时就已在落程 7% 处；
        /// 本工程归一化，保证 p=0 恰好在生成点、整段落程都被用到（下落距离不变）。
        /// </summary>
        public static float ViewProgress01(float p)
        {
            float v0 = Mathf.Pow(CurveBase, -CurveGain);     // p=0 的原值
            float v = ViewProgress(p);
            return Mathf.Clamp01((v - v0) / (1f - v0));
        }

        /// <summary>尺寸比例：归一化曲线的下限版本。关掉透视时恒为 1</summary>
        public static float SizeScale(float p)
        {
            return Enabled ? Mathf.Max(MinScale, ViewProgress01(p)) : 1f;
        }

        /// <summary>
        /// 进度 → 画布中心系 Y。**过线继续飞**（前端清单 D4 的行为面）：
        /// p ≤ 1 走透视曲线；p &gt; 1 沿"判定线之后的直线"继续下移（上游是
        /// `Vector2.LerpUnclamped(spawn, center, progress)`，这里等价成"过了 1 就按同速继续"）。
        /// 裁剪（上游 shader 的 `_NoteShowRate`）按作者定案**不做**（属视觉项）。
        /// </summary>
        public static float PositionY(float p, float spawnY, float judgeY)
        {
            if (!Enabled) return Mathf.Lerp(spawnY, judgeY, Mathf.Clamp01(p));
            if (p <= 1f) return Mathf.LerpUnclamped(spawnY, judgeY, ViewProgress01(p));
            // 过线：以"整段落距"为单位继续下移（judgeY - spawnY 为负 → y 继续变小 = 继续往下）
            return judgeY + (judgeY - spawnY) * (p - 1f);
        }

        /// <summary>
        /// 画布中心系 Y → 该处的尺寸比例（轨道用）。
        /// 用二分反解 PositionY：曲线单调，30 次迭代已远超像素精度。
        /// </summary>
        public static float SizeScaleAtY(float y, float spawnY, float judgeY)
        {
            if (!Enabled) return 1f;
            if (y >= spawnY) return SizeScale(0f);      // 生成点以上：保持起点尺寸（轨道顶部竖直延伸）
            if (y <= judgeY) return SizeScale(1f);
            float lo = 0f, hi = 1f;
            for (int i = 0; i < 30; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (PositionY(mid, spawnY, judgeY) > y) lo = mid; else hi = mid;
            }
            return SizeScale((lo + hi) * 0.5f);
        }

        /// <summary>画布中心系 Y → 尺寸比例（用当前登记的画奏几何，轨道/音符/带子共用）</summary>
        public static float SizeScaleAtY(float y)
        {
            return SizeScaleAtY(y, SpawnY, JudgeY);
        }

        /// <summary>
        /// 尺寸下限的折点（画布 y）：高于此线尺寸恒为 MinScale，低于此线沿曲线放大。
        ///
        /// 为什么带子只在这里插一个点就够：位置与尺寸共用同一条归一化曲线 v = ViewProgress01(p)，
        /// 而 y = lerp(spawnY, judgeY, v)、尺寸 = max(MinScale, v)，所以
        ///     y ≤ 折点 时 尺寸 = (spawnY − y) / (spawnY − judgeY)  ← 对 y **精确线性**
        ///     y ≥ 折点 时 尺寸 = MinScale                        ← 常数
        /// 于是"轨道半宽随高度"是一条两段的折线，取点只要覆盖「起点 / 折点 / 终点」即可完全贴合
        /// （见 NoteBandGraphic：跨过折点的弦会外鼓，12 段时实测约 2px）。
        /// </summary>
        public static float FloorCornerY
        {
            get { return !Enabled ? JudgeY : Mathf.Lerp(SpawnY, JudgeY, MinScale); }
        }
    }
}
