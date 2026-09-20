using FallenAngel.Core;
using UnityEngine;
using UnityEngine.UI;

namespace FallenAngel.UI
{
    /// <summary>
    /// 透视轨道条：把轨道背景从"等宽矩形"画成"上窄下宽的梯形带"，
    /// 宽度曲线与音符**同一个来源**（<see cref="PlayPerspective"/>），
    /// 所以任意高度上「轨道半宽」恒等于「该处音符半宽 ÷ 填充比」——即音符与轨道严格对齐。
    ///
    /// 为什么不用 Image 的矩形：矩形轨道配缩放音符，远处音符会比轨道窄一大截，
    /// 看起来像"音符飘在轨道外"。作者 2026-09-20 的要求就是严格对齐。
    ///
    /// 仍然是 Image 的子类（不是自绘 MaskableGraphic），因为命中特效
    /// （HitEffectController）是按 Image 组件取轨道柱改色的，换基类会断掉那条链路。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class PerspectiveLaneGraphic : Image
    {
        /// <summary>梯形带的采样段数：32 段在 1080 宽下每段约 70px，肉眼看不出折角</summary>
        [SerializeField] private int segments = 32;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect rect = GetPixelAdjustedRect();
            float halfWidth = rect.width * 0.5f;
            float spawnY = PlayPerspective.SpawnY;
            float judgeY = PlayPerspective.JudgeY;

            // 1) 生成点以上：保持起点尺寸竖直延伸到轨道矩形顶边（不做"无限收窄到 0"）
            float topScale = PlayPerspective.SizeScale(0f);
            if (rect.yMax > PlayPerspective.PositionY(0f, spawnY, judgeY))
            {
                AddQuad(vh, rect.yMax, halfWidth * topScale,
                        PlayPerspective.PositionY(0f, spawnY, judgeY), halfWidth * topScale,
                        rect.center.x);
            }

            // 2) 生成点 → 判定线：按同一曲线逐段插值
            int n = Mathf.Max(4, segments);
            float prevY = PlayPerspective.PositionY(0f, spawnY, judgeY);
            float prevHalf = halfWidth * PlayPerspective.SizeScale(0f);
            for (int i = 1; i <= n; i++)
            {
                float p = i / (float)n;
                float y = PlayPerspective.PositionY(p, spawnY, judgeY);
                float half = halfWidth * PlayPerspective.SizeScale(p);
                AddQuad(vh, prevY, prevHalf, y, half, rect.center.x);
                prevY = y;
                prevHalf = half;
            }
            // 判定线以下不再延伸（下方是按键区 Lane*_Key）
        }

        /// <summary>一条水平段的四边形（左右对称于 centerX）</summary>
        private void AddQuad(VertexHelper vh, float yA, float halfA, float yB, float halfB, float centerX)
        {
            int baseIndex = vh.currentVertCount;
            Color32 c = color;
            vh.AddVert(new Vector3(centerX - halfA, yA, 0f), c, new Vector2(0f, 0f));
            vh.AddVert(new Vector3(centerX - halfB, yB, 0f), c, new Vector2(0f, 1f));
            vh.AddVert(new Vector3(centerX + halfB, yB, 0f), c, new Vector2(1f, 1f));
            vh.AddVert(new Vector3(centerX + halfA, yA, 0f), c, new Vector2(1f, 0f));
            vh.AddTriangle(baseIndex, baseIndex + 1, baseIndex + 2);
            vh.AddTriangle(baseIndex, baseIndex + 2, baseIndex + 3);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            PlayPerspective.GeometryChanged += MarkDirty;
        }

        protected override void OnDisable()
        {
            PlayPerspective.GeometryChanged -= MarkDirty;
            base.OnDisable();
        }

        private void MarkDirty()
        {
            SetVerticesDirty();
        }
    }
}
