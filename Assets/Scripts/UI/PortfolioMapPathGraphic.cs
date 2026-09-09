using UnityEngine;
using UnityEngine.UI;

namespace FallenAngel.UI
{
    /// <summary>地图曲线与方向箭头；随地图视口裁剪，不接收点击。支持延展动画（从起点逐步画出曲线）。</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class PortfolioMapPathGraphic : MaskableGraphic
    {
        private Vector2 start, end;
        private float progress = 1f;
        private float animSpeed; // progress/秒，0=静止

        public void SetPath(Vector2 from, Vector2 to, Color tint)
        {
            start = from; end = to; color = tint;
            progress = 1f; animSpeed = 0f;
            raycastTarget = false;
            SetVerticesDirty();
        }

        /// <summary>路线延展动画：曲线从起点向终点逐步画出，箭头在完成时出现。</summary>
        public void AnimatePath(Vector2 from, Vector2 to, Color tint, float duration)
        {
            start = from; end = to; color = tint;
            progress = 0f;
            animSpeed = duration > 0.001f ? 1f / duration : 0f;
            raycastTarget = false;
            SetVerticesDirty();
        }

        private void Update()
        {
            if (animSpeed <= 0f || progress >= 1f) return;
            progress = Mathf.Min(1f, progress + animSpeed * Time.unscaledDeltaTime);
            SetVerticesDirty();
            if (progress >= 1f) animSpeed = 0f;
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            float p = Mathf.Clamp01(progress);
            Vector2 controlA = start + new Vector2(0, (end.y - start.y) * .5f);
            Vector2 controlB = end - new Vector2(0, (end.y - start.y) * .5f);
            Vector2 previous = start;
            int segments = Mathf.CeilToInt(40 * p);
            for (int i = 1; i <= segments; i++)
            {
                float t = Mathf.Min(i / 40f, p), u = 1 - t;
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
            if (p < 1f) return; // 动画未完成时不画箭头
            int arrow = mesh.currentVertCount;
            mesh.AddVert(end + new Vector2(0, 10), color, Vector2.zero);
            mesh.AddVert(end + new Vector2(-11, 29), color, Vector2.zero);
            mesh.AddVert(end + new Vector2(11, 29), color, Vector2.zero);
            mesh.AddTriangle(arrow, arrow+1, arrow+2);
        }
    }
}
