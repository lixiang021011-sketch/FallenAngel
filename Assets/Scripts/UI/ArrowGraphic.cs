using UnityEngine;
using UnityEngine.UI;
using FallenAngel.Data;

namespace FallenAngel.UI
{
    /// <summary>
    /// Flick 方向箭头图形（零纹理/精灵，顶点色程序化绘制）。
    /// 单三角形：up 尖朝上、down 尖朝下，白色半透明叠加在音符头部色块上。
    /// </summary>
    public class ArrowGraphic : Graphic
    {
        [SerializeField] private FlickDirection direction = FlickDirection.Up;
        [SerializeField] private Color arrowColor = new Color(1f, 1f, 1f, 0.9f);

        /// <summary>箭头方向，设置后重建网格</summary>
        public FlickDirection Direction
        {
            get => direction;
            set { direction = value; SetVerticesDirty(); }
        }

        /// <summary>箭头颜色（含透明度），设置后重建网格</summary>
        public Color ArrowColor
        {
            get => arrowColor;
            set { arrowColor = value; SetVerticesDirty(); }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect rect = rectTransform.rect;
            float w = rect.width * 0.5f;
            float h = rect.height * 0.5f;
            Color c = Premultiply(arrowColor);

            if (direction == FlickDirection.Up)
            {
                vh.AddVert(new Vector3(-w, -h), c, Vector2.zero); // 0 左下
                vh.AddVert(new Vector3(w, -h), c, Vector2.zero);  // 1 右下
                vh.AddVert(new Vector3(0, h), c, Vector2.zero);   // 2 尖顶
            }
            else
            {
                vh.AddVert(new Vector3(-w, h), c, Vector2.zero);  // 0 左上
                vh.AddVert(new Vector3(w, h), c, Vector2.zero);   // 1 右上
                vh.AddVert(new Vector3(0, -h), c, Vector2.zero);  // 2 尖底
            }
            vh.AddTriangle(0, 1, 2);
        }

        /// <summary>UI 默认着色器为 premultiplied alpha，顶点色必须预乘</summary>
        private static Color Premultiply(Color col)
        {
            return new Color(col.r * col.a, col.g * col.a, col.b * col.a, col.a);
        }
    }
}
