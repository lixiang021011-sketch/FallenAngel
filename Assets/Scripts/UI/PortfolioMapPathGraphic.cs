using UnityEngine;
using UnityEngine.UI;

namespace FallenAngel.UI
{
    /// <summary>地图曲线与方向箭头；随地图视口裁剪，不接收点击。</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class PortfolioMapPathGraphic : MaskableGraphic
    {
        private Vector2 start, end;
        public void SetPath(Vector2 from, Vector2 to, Color tint)
        {
            start = from; end = to; color = tint;
            raycastTarget = false;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Vector2 controlA = start + new Vector2(0, (end.y - start.y) * .5f);
            Vector2 controlB = end - new Vector2(0, (end.y - start.y) * .5f);
            Vector2 previous = start;
            for (int i = 1; i <= 40; i++)
            {
                float t = i / 40f, u = 1 - t;
                Vector2 point = u*u*u*start + 3*u*u*t*controlA + 3*u*t*t*controlB + t*t*t*end;
                Vector2 normal = new Vector2(-(point-previous).y, (point-previous).x).normalized * 2.5f;
                int index = mesh.currentVertCount;
                mesh.AddVert(previous-normal, color, Vector2.zero);
                mesh.AddVert(previous+normal, color, Vector2.zero);
                mesh.AddVert(point+normal, color, Vector2.zero);
                mesh.AddVert(point-normal, color, Vector2.zero);
                mesh.AddTriangle(index,index+1,index+2);
                mesh.AddTriangle(index,index+2,index+3);
                previous = point;
            }
            int arrow = mesh.currentVertCount;
            mesh.AddVert(end + new Vector2(0, 10), color, Vector2.zero);
            mesh.AddVert(end + new Vector2(-11, 29), color, Vector2.zero);
            mesh.AddVert(end + new Vector2(11, 29), color, Vector2.zero);
            mesh.AddTriangle(arrow, arrow+1, arrow+2);
        }
    }
}
