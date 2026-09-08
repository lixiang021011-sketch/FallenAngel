using UnityEngine;
using UnityEngine.UI;
using FallenAngel.Core;

namespace FallenAngel.UI
{
    /// <summary>
    /// 轨道面板控制器（挂 LanesBG，始终激活）。
    /// 场景恒建 5 轨（SceneBuilder），本控制器在每次 OnGameStart 按谱面键数
    /// 重定位轨道条/按键区并隐藏多余轨道：
    ///   - 4K 谱：隐藏第 5 轨，全部轨道重定位到 4 键中心（否则相对 5 轨布局偏 45px）
    ///   - 5K 谱：5 轨全亮，重定位到 5 键中心
    /// 同时重刷轨道配色（LaneColors 按键数选鼓谱/吉他谱，SceneBuilder 静态色不适用）。
    /// </summary>
    public class LanePanelController : MonoBehaviour
    {
        private RectTransform[] laneBGs;
        private RectTransform[] laneKeys;
        private Image[] laneBGImages;
        private Image[] laneKeyImages;
        private Image[] laneKeyIcons;

        private void CacheRefs()
        {
            if (laneBGs != null) return;

            laneBGs = new RectTransform[LaneLayout.MaxLaneCount];
            laneKeys = new RectTransform[LaneLayout.MaxLaneCount];
            laneBGImages = new Image[LaneLayout.MaxLaneCount];
            laneKeyImages = new Image[LaneLayout.MaxLaneCount];
            laneKeyIcons = new Image[LaneLayout.MaxLaneCount];

            for (int i = 0; i < LaneLayout.MaxLaneCount; i++)
            {
                Transform bg = transform.Find($"Lane{i}_BG");
                if (bg != null)
                {
                    laneBGs[i] = bg as RectTransform;
                    laneBGImages[i] = bg.GetComponent<Image>();
                }

                Transform key = transform.Find($"KeyArea/Lane{i}_Key");
                if (key != null)
                {
                    laneKeys[i] = key as RectTransform;
                    laneKeyImages[i] = key.GetComponent<Image>();
                    Transform icon = key.Find("KeyIcon");
                    if (icon != null) laneKeyIcons[i] = icon.GetComponent<Image>();
                }
            }
        }

        private void OnEnable()
        {
            SubscribeEvents();
            // 补追：GamePanel 可能在 Loading 状态切换之后才激活（OnStateChanged 事件已错过），
            // 直接按当前状态重定位，保证倒计时期间轨道数即正确（修复：此前倒计时显示 5 轨、GO 才切回 4 轨）
            CatchUpReposition();
        }

        private void Start()
        {
            SubscribeEvents();
            CatchUpReposition();
        }

        private void OnDisable()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnStateChanged -= OnStateChanged;
                GameManager.Instance.OnGameStart -= OnGameStart;
            }
        }

        private void SubscribeEvents()
        {
            if (GameManager.Instance == null) return;
            GameManager.Instance.OnStateChanged -= OnStateChanged;
            GameManager.Instance.OnStateChanged += OnStateChanged;
            GameManager.Instance.OnGameStart -= OnGameStart;
            GameManager.Instance.OnGameStart += OnGameStart;
        }

        /// <summary>当前已处于 Loading/Playing 时立即重定位（事件错过后补执行）</summary>
        private void CatchUpReposition()
        {
            if (GameManager.Instance == null) return;
            GameState state = GameManager.Instance.CurrentState;
            if (state == GameState.Loading || state == GameState.Playing)
                RepositionForLaneCount();
        }

        private void OnStateChanged(GameState state)
        {
            // 加载谱面时（倒计时前）就重定位：倒计时 3 秒期间轨道即按谱面键数显示
            if (state == GameState.Loading || state == GameState.Playing)
                RepositionForLaneCount();
        }

        private void OnGameStart()
        {
            // 兜底：Playing 再刷一次（事件时序异常时）
            RepositionForLaneCount();
        }

        /// <summary>
        /// 按当前活动键数重定位轨道并刷新配色（4K/5K 布局中心不同，仅隐藏第 5 轨会错位）
        /// </summary>
        public void RepositionForLaneCount()
        {
            CacheRefs();
            int count = LaneLayout.ActiveLaneCount;

            for (int i = 0; i < LaneLayout.MaxLaneCount; i++)
            {
                bool active = i < count;
                float x = active ? LaneLayout.GetCenterXForActive(i) : 0f;
                Color c = LaneColors.GetLaneColor(i);

                if (laneBGs[i] != null)
                {
                    laneBGs[i].gameObject.SetActive(active);
                    if (active)
                    {
                        laneBGs[i].anchoredPosition = new Vector2(x, laneBGs[i].anchoredPosition.y);
                        if (laneBGImages[i] != null) { Color cc = c; cc.a = 0.12f; laneBGImages[i].color = cc; }
                    }
                }

                if (laneKeys[i] != null)
                {
                    laneKeys[i].gameObject.SetActive(active);
                    if (active)
                    {
                        laneKeys[i].anchoredPosition = new Vector2(x, laneKeys[i].anchoredPosition.y);
                        if (laneKeyImages[i] != null) { Color cc = c; cc.a = 0.3f; laneKeyImages[i].color = cc; }
                        if (laneKeyIcons[i] != null) laneKeyIcons[i].color = c;
                    }
                }
            }

            Debug.Log($"[LanePanelController] 轨道布局更新: {count} 键");
        }
    }
}
