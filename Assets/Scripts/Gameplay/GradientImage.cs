using UnityEngine;
using UnityEngine.UI;

namespace FallenAngel.Gameplay
{
    /// <summary>
    /// 纵向渐变图形（顶点色直接绘制，无纹理/精灵依赖）：
    /// 底部 bottomColor 渐变到顶部 topColor。
    /// 当前用途：长按音符身体——贴近头部一端实体，远端透明渐隐。
    /// </summary>
    public class GradientImage : Graphic
    {
        /// <summary>底部（贴近头部一端）颜色</summary>
        public Color bottomColor = Color.white;

        /// <summary>顶部（远端）颜色，默认完全透明</summary>
        public Color topColor = new Color(1f, 1f, 1f, 0f);

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect rect = rectTransform.rect;

            // 四个顶点：底部两个用 bottomColor，顶部两个用 topColor，
            // 三角形内顶点色线性插值，得到平滑纵向渐变。
            // 注意：UI 着色器采用预乘Alpha混合（Blend One OneMinusSrcAlpha），
            // 顶点色必须预乘（RGB×A），否则透明通道不生效、渲染成实色。
            vh.AddVert(new Vector3(rect.xMin, rect.yMin), Premultiply(bottomColor), Vector2.zero); // 0 左下
            vh.AddVert(new Vector3(rect.xMin, rect.yMax), Premultiply(topColor), Vector2.zero);    // 1 左上
            vh.AddVert(new Vector3(rect.xMax, rect.yMax), Premultiply(topColor), Vector2.zero);    // 2 右上
            vh.AddVert(new Vector3(rect.xMax, rect.yMin), Premultiply(bottomColor), Vector2.zero); // 3 右下

            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(2, 3, 0);
        }

        private static Color Premultiply(Color c)
        {
            return new Color(c.r * c.a, c.g * c.a, c.b * c.a, c.a);
        }
    }
}
