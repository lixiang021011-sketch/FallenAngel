using FallenAngel.Core;
using UnityEngine;
using UnityEngine.UI;

namespace FallenAngel.UI
{
    /// <summary>
    /// 演奏层可见性：把 <see cref="PlayRules"/> 的 JudgeLineVisible / NotesVisible 逐帧落到渲染上。
    ///
    /// 只影响显示——判定时机、音符位置照常（见 docs/architecture.md §9）。
    /// 音符用 CanvasGroup 的 alpha 隐身，而不是 SetActive：后者会打断对象池与正在跑的协程。
    /// </summary>
    public class PlayVisibilityController : MonoBehaviour
    {
        [Header("引用")]
        [Tooltip("判定线 Graphic（SceneBuilder 注入）")]
        [SerializeField] private Graphic judgeLine;

        [Tooltip("音符容器 CanvasGroup（SceneBuilder 注入；alpha 控制隐身）")]
        [SerializeField] private CanvasGroup notesGroup;

        /// <summary>
        /// 取实例；场景里没有就地建一个并自行解析引用（按 SceneBuilder 的命名约定）。
        /// 让"判定线消失/谱面隐身"在旧场景里也能生效，不再依赖重建。
        /// </summary>
        public static PlayVisibilityController EnsureInstance()
        {
            PlayVisibilityController found =
                UnityEngine.Object.FindObjectOfType<PlayVisibilityController>(true);
            if (found != null) return found;

            var go = new GameObject("PlayVisibilityController(Runtime)");
            return go.AddComponent<PlayVisibilityController>();
        }

        /// <summary>引用为空时按命名解析（层级与 SceneBuilder 的产出一致）</summary>
        private void ResolveRefsIfNeeded()
        {
            if (judgeLine != null && notesGroup != null) return;
            Transform root = transform.root;
            Transform lanes = root != null ? root.Find("Canvas/GamePanel/LanesBG") : null;
            if (lanes == null) return;

            if (judgeLine == null)
            {
                Transform jl = lanes.Find("JudgeLine");
                if (jl != null) judgeLine = jl.GetComponent<Graphic>();
            }
            if (notesGroup == null)
            {
                Transform nc = lanes.Find("NotesContainer");
                if (nc != null) notesGroup = nc.GetComponent<CanvasGroup>();
            }
        }

        private void Start()
        {
            ResolveRefsIfNeeded();
        }

        private void Update()
        {
            ResolveRefsIfNeeded();
            bool lineVisible = PlayRules.JudgeLineVisible;
            if (judgeLine != null && judgeLine.enabled != lineVisible)
                judgeLine.enabled = lineVisible;

            if (notesGroup != null)
            {
                float target = PlayRules.NotesVisible ? 1f : 0f;
                if (!Mathf.Approximately(notesGroup.alpha, target))
                    notesGroup.alpha = target;
            }
        }
    }
}
