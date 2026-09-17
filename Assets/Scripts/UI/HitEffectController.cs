using System.Collections;
using System.Collections.Generic;
using FallenAngel.Core;
using FallenAngel.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace FallenAngel.UI
{
    /// <summary>
    /// 判定特效控制器：Miss 轨道压暗 + 命中闪光 + 连击脉冲。
    ///
    /// 对应需求表：
    ///   GP_FX_MISS（P1）—— 轨道压暗，持续 ≤0.3s
    ///   GP_FX_HIT_BURST（P2）—— 命中闪光，可关（低端机）
    ///   GP_FX_COMBO_PULSE（P2）—— 每 100 连一次脉冲
    ///
    /// 配色遵守已定方向：**全屏只有一个暖色**。Miss 用"去饱和 + 压暗"表达，
    /// 命中闪光取轨道色（青绿族），暖色（粉）只给连击脉冲这类正向事件。
    ///
    /// 全部为程序化图形 + 对象池，零贴图；只做视觉，不碰判定与音符位置。
    /// </summary>
    public class HitEffectController : MonoBehaviour
    {
        [Header("引用（SceneBuilder 注入）")]
        [Tooltip("五条轨道柱 Image，按 lane 顺序")]
        [SerializeField] private Image[] laneColumns;

        [Header("Miss 反馈")]
        [Tooltip("压暗目标色：去饱和的冷灰青")]
        [SerializeField] private Color missTint = new Color(0.28f, 0.36f, 0.40f, 1f);
        [Tooltip("压暗持续时间（需求 ≤0.3s）")]
        [SerializeField] private float missDimSeconds = 0.30f;

        [Header("命中闪光（可关）")]
        [Tooltip("低端机可关掉闪光，只保留命中环")]
        [SerializeField] private bool hitBurstEnabled = true;
        [SerializeField] private float burstDuration = 0.22f;
        [SerializeField] private float burstStartScale = 14f;
        [SerializeField] private float burstEndScale = 34f;
        [SerializeField] private int burstPoolSize = 12;
        [Tooltip("Perfect 相对其他判定的额外提亮")]
        [SerializeField] private float perfectBoost = 0.45f;

        [Header("连击脉冲")]
        [Tooltip("每多少连击触发一次（0 = 关闭）")]
        [SerializeField] private int comboPulseEvery = 100;
        [SerializeField] private Color comboPulseColor = new Color(0.941f, 0.659f, 0.800f, 1f); // #F0A8CC 方向唯一暖色
        [SerializeField] private float comboPulseDuration = 0.40f;
        [SerializeField] private float comboPulseStartScale = 40f;
        [SerializeField] private float comboPulseEndScale = 190f;
        [SerializeField] private int comboPulsePoolSize = 4;

        private Color[] baseColors;
        private Coroutine[] dimRoutines;

        private sealed class Entry
        {
            public RectTransform rt;
            public Graphic graphic;
            public CanvasGroup canvasGroup;
            public Coroutine routine;
        }

        private readonly List<Entry> bursts = new List<Entry>();
        private readonly List<Entry> pulses = new List<Entry>();
        private int burstCursor;
        private int pulseCursor;

        /// <summary>已绑定的非空轨道柱数量（场景装配自检用）</summary>
        public int BoundLaneColumnCount
        {
            get
            {
                if (laneColumns == null) return 0;
                int n = 0;
                for (int i = 0; i < laneColumns.Length; i++)
                    if (laneColumns[i] != null) n++;
                return n;
            }
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void Start()
        {
            CacheBaseColors();
            Subscribe();
        }

        private void OnDisable()
        {
            if (JudgeManager.Instance != null)
            {
                JudgeManager.Instance.OnJudgeResult -= HandleJudgeResult;
                JudgeManager.Instance.OnComboUpdate -= HandleComboUpdate;
            }
        }

        private void Subscribe()
        {
            if (JudgeManager.Instance == null) return;
            JudgeManager.Instance.OnJudgeResult -= HandleJudgeResult;
            JudgeManager.Instance.OnJudgeResult += HandleJudgeResult;
            JudgeManager.Instance.OnComboUpdate -= HandleComboUpdate;
            JudgeManager.Instance.OnComboUpdate += HandleComboUpdate;
        }

        private void BuildPools()
        {
            if (hitBurstEnabled && bursts.Count == 0)
            {
                for (int i = 0; i < burstPoolSize; i++)
                    bursts.Add(CreateEntry(typeof(BurstGraphic), "HitBurst_" + i, burstStartScale));
            }
            if (comboPulseEvery > 0 && pulses.Count == 0)
            {
                for (int i = 0; i < comboPulsePoolSize; i++)
                    pulses.Add(CreateEntry(typeof(HitRingGraphic), "ComboPulse_" + i, comboPulseStartScale));
            }
        }

        private Entry CreateEntry(System.Type graphicType, string name, float size)
        {
            GameObject go = new GameObject(name,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(CanvasGroup));
            go.transform.SetParent(transform, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            Graphic g = go.AddComponent(graphicType) as Graphic;
            if (g != null) g.raycastTarget = false;
            CanvasGroup cg = go.GetComponent<CanvasGroup>();
            go.SetActive(false);
            return new Entry { rt = rt, graphic = g, canvasGroup = cg };
        }

        private void CacheBaseColors()
        {
            if (laneColumns == null) return;
            baseColors = new Color[laneColumns.Length];
            dimRoutines = new Coroutine[laneColumns.Length];
            for (int i = 0; i < laneColumns.Length; i++)
            {
                if (laneColumns[i] != null) baseColors[i] = laneColumns[i].color;
            }
        }

        private void HandleJudgeResult(JudgeResultType result, int lane)
        {
            if (result == JudgeResultType.Miss)
            {
                HandleMiss(lane);
                return;
            }
            if (result == JudgeResultType.Perfect || result == JudgeResultType.Great)
                SpawnBurst(lane, result);
        }

        private void HandleComboUpdate(int combo, bool isFullComboNow)
        {
            if (comboPulseEvery <= 0 || combo <= 0) return;
            if (combo % comboPulseEvery != 0) return;
            SpawnPulse();
        }

        private void HandleMiss(int lane)
        {
            if (laneColumns == null || lane < 0 || lane >= laneColumns.Length) return;
            if (laneColumns[lane] == null) return;

            if (baseColors == null) CacheBaseColors();
            // 同轨连续 Miss 时重置计时，而不是叠两条协程抢同一个颜色
            if (dimRoutines[lane] != null) StopCoroutine(dimRoutines[lane]);
            dimRoutines[lane] = StartCoroutine(DimLane(lane));
        }

        /// <summary>命中闪光：在判定线上该轨道的位置放一组放射条，取轨道色（Perfect 提亮）</summary>
        private void SpawnBurst(int lane, JudgeResultType result)
        {
            if (!hitBurstEnabled) return;
            BuildPools();
            if (bursts.Count == 0) return;

            Color c = LaneColors.GetLaneColor(lane);
            if (result == JudgeResultType.Perfect)
                c = Color.Lerp(c, Color.white, perfectBoost);
            c.a = result == JudgeResultType.Perfect ? 0.85f : 0.6f;

            Vector2 pos = new Vector2(LaneLayout.GetCenterXForActive(lane), PlayVisualSpec.JudgeLineY);
            Play(bursts, ref burstCursor, pos, c, burstStartScale, burstEndScale, burstDuration);
        }

        /// <summary>连击脉冲：每 N 连在判定线中央放一个粉色扩散环（全屏唯一暖色的正向用法）</summary>
        private void SpawnPulse()
        {
            BuildPools();
            if (pulses.Count == 0) return;

            Color c = comboPulseColor;
            c.a = 0.55f;
            Play(pulses, ref pulseCursor, new Vector2(0f, PlayVisualSpec.JudgeLineY), c,
                 comboPulseStartScale, comboPulseEndScale, comboPulseDuration);
        }

        private void Play(List<Entry> pool, ref int cursor, Vector2 pos, Color color,
                          float startScale, float endScale, float duration)
        {
            Entry e = pool[cursor];
            cursor = (cursor + 1) % pool.Count;

            e.rt.anchoredPosition = pos;
            e.rt.sizeDelta = new Vector2(startScale, startScale);
            e.rt.localScale = Vector3.one;
            if (e.graphic is HitRingGraphic ring) ring.RingColor = color;
            else if (e.graphic != null) e.graphic.color = color;
            e.canvasGroup.alpha = 1f;
            e.canvasGroup.blocksRaycasts = false;
            e.canvasGroup.interactable = false;
            e.rt.gameObject.SetActive(true);

            if (e.routine != null) StopCoroutine(e.routine);
            e.routine = StartCoroutine(Animate(e, color, startScale, endScale, duration));
        }

        private IEnumerator Animate(Entry e, Color color, float startScale, float endScale, float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;   // 视觉反馈：暂停时不残留
                float k = Mathf.Clamp01(t / duration);
                float size = Mathf.Lerp(startScale, endScale, k);
                e.rt.sizeDelta = new Vector2(size, size);
                e.canvasGroup.alpha = 1f - k;
                yield return null;
            }
            e.rt.gameObject.SetActive(false);
            e.canvasGroup.alpha = 0f;
            e.routine = null;
        }

        private IEnumerator DimLane(int lane)
        {
            Image img = laneColumns[lane];
            Color from = baseColors != null && lane < baseColors.Length ? baseColors[lane] : img.color;
            Color dim = new Color(from.r * missTint.r * 2f, from.g * missTint.g * 2f, from.b * missTint.b * 2f, from.a);

            float half = Mathf.Max(0.05f, missDimSeconds * 0.35f);
            float t = 0f;
            while (t < half)
            {
                t += Time.unscaledDeltaTime;   // UI 反馈：暂停时也要能播完
                img.color = Color.Lerp(from, dim, t / half);
                yield return null;
            }
            img.color = dim;

            t = 0f;
            float back = Mathf.Max(0.05f, missDimSeconds - half);
            while (t < back)
            {
                t += Time.unscaledDeltaTime;
                img.color = Color.Lerp(dim, from, t / back);
                yield return null;
            }
            img.color = from;
            dimRoutines[lane] = null;
        }
    }
}
