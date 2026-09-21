using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FallenAngel.UI
{
    /// <summary>测试场：两轨、判定线、几何 tap/hold、命中特效。不碰 NoteSpawner。</summary>
    public sealed class ArtWorkbenchField : MonoBehaviour
    {
        public ArtWorkbenchPreset Preset { get; private set; }
        public ArtWorkbenchBus Bus { get; private set; }

        RectTransform laneRoot;
        RectTransform noteRoot;
        RectTransform fxRoot;
        Image[] lanes = new Image[2];
        Image judgeLine;
        readonly List<SandboxNote> taps = new List<SandboxNote>();
        SandboxNote hold;
        bool holdActive;
        int holdLane;
        float spawnClock;
        int sandboxCombo;
        int sandboxScore;

        sealed class SandboxNote
        {
            public RectTransform rt;
            public ArtWorkbenchMark mark;
            public int lane;
            public float y;
            public bool holdBody;
        }

        public void Build(ArtWorkbenchBus bus, ArtWorkbenchPreset preset)
        {
            Bus = bus;
            Preset = preset;
            var bg = ArtWorkbenchUi.Panel(transform, "FieldBg", ArtWorkbenchTokens.Background,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            bg.raycastTarget = false;

            laneRoot = ArtWorkbenchUi.Root(transform, "Lanes", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            noteRoot = ArtWorkbenchUi.Root(transform, "Notes", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            fxRoot = ArtWorkbenchUi.Root(transform, "Fx", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            for (int i = 0; i < 2; i++)
            {
                lanes[i] = ArtWorkbenchUi.Panel(laneRoot, "Lane" + i, preset.laneFill,
                    new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
            }

            judgeLine = ArtWorkbenchUi.Panel(laneRoot, "JudgeLine", preset.judgeLine,
                new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, Vector2.zero);
            var jrt = judgeLine.rectTransform;
            jrt.pivot = new Vector2(0.5f, 0.5f);

            Subscribe();
            Apply(preset);
        }

        void OnEnable() { Subscribe(); }
        void Start() { Subscribe(); }
        void OnDisable() { Unsubscribe(); }

        void Subscribe()
        {
            if (Bus == null) return;
            Unsubscribe();
            Bus.OnHit += HandleHit;
            Bus.OnMiss += HandleMiss;
            Bus.OnHoldStart += HandleHoldStart;
            Bus.OnHoldEnd += HandleHoldEnd;
            Bus.OnSongStart += ResetSandbox;
        }

        void Unsubscribe()
        {
            if (Bus == null) return;
            Bus.OnHit -= HandleHit;
            Bus.OnMiss -= HandleMiss;
            Bus.OnHoldStart -= HandleHoldStart;
            Bus.OnHoldEnd -= HandleHoldEnd;
            Bus.OnSongStart -= ResetSandbox;
        }

        public void Apply(ArtWorkbenchPreset preset)
        {
            Preset = preset ?? ArtWorkbenchTokens.DefaultPreset();
            if (lanes[0] == null) return;
            for (int i = 0; i < lanes.Length; i++)
            {
                if (lanes[i] == null) continue;
                lanes[i].color = Preset.laneFill;
                var rt = lanes[i].rectTransform;
                float pitch = 280f;
                rt.anchoredPosition = new Vector2((i - 0.5f) * pitch, 0f);
                rt.sizeDelta = new Vector2(220f, 0f);
            }
            if (judgeLine != null)
            {
                judgeLine.color = Preset.judgeLine;
                var rt = judgeLine.rectTransform;
                rt.anchoredPosition = new Vector2(0f, ArtWorkbenchTokens.JudgeFromBottom);
                rt.sizeDelta = new Vector2(0f, 8f);
            }
        }

        void Update()
        {
            if (Preset == null || !isActiveAndEnabled) return;
            float dt = Time.unscaledDeltaTime;
            spawnClock += dt;
            if (spawnClock >= 1.15f)
            {
                spawnClock = 0f;
                SpawnTap(taps.Count % 2);
            }

            float judgeY = ArtWorkbenchTokens.JudgeFromBottom;
            for (int i = taps.Count - 1; i >= 0; i--)
            {
                var n = taps[i];
                if (n.rt == null) { taps.RemoveAt(i); continue; }
                n.y -= Preset.fallSpeed * dt;
                n.rt.anchoredPosition = new Vector2(LaneX(n.lane), n.y);
                if (n.y < judgeY - 80f)
                {
                    Destroy(n.rt.gameObject);
                    taps.RemoveAt(i);
                    sandboxCombo = 0;
                    Bus.Hit(n.lane, ArtWorkbenchGrade.Miss);
                    Bus.Hud(sandboxCombo, sandboxScore);
                }
            }

            if (holdActive && hold != null && hold.rt != null)
            {
                float pulse = 0.82f + 0.18f * Mathf.Sin(Time.unscaledTime * Preset.holdPulseSpeed * Mathf.PI * 2f);
                hold.mark.color = new Color(Preset.holdBody.r, Preset.holdBody.g, Preset.holdBody.b, Preset.holdBody.a * pulse);
                float headY = judgeY;
                float top = Mathf.Min(1400f, headY + 420f + 40f * Mathf.Sin(Time.unscaledTime * Preset.holdPulseSpeed));
                hold.rt.anchoredPosition = new Vector2(LaneX(holdLane), (headY + top) * 0.5f);
                hold.rt.sizeDelta = new Vector2(Preset.noteWidth * 0.72f, Mathf.Max(80f, top - headY));
            }
        }

        public void SpawnTap(int lane)
        {
            var n = MakeNote("Tap", lane, Preset.tapFill, false);
            n.y = 1500f;
            n.rt.anchoredPosition = new Vector2(LaneX(lane), n.y);
            n.rt.sizeDelta = new Vector2(Preset.noteWidth, Preset.noteHeight);
            taps.Add(n);
        }

        public void TriggerGrade(ArtWorkbenchGrade grade, int lane)
        {
            ConsumeNearest(lane);
            if (grade == ArtWorkbenchGrade.Perfect) sandboxScore += 300;
            else if (grade == ArtWorkbenchGrade.Great) sandboxScore += 200;
            sandboxCombo = grade == ArtWorkbenchGrade.Miss ? 0 : sandboxCombo + 1;
            Bus.Hit(lane, grade);
            Bus.Hud(sandboxCombo, sandboxScore);
        }

        void ConsumeNearest(int lane)
        {
            int best = -1;
            float bestDist = 9999f;
            float judgeY = ArtWorkbenchTokens.JudgeFromBottom;
            for (int i = 0; i < taps.Count; i++)
            {
                if (taps[i].lane != lane || taps[i].rt == null) continue;
                float d = Mathf.Abs(taps[i].y - judgeY);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            if (best >= 0)
            {
                Destroy(taps[best].rt.gameObject);
                taps.RemoveAt(best);
            }
        }

        void HandleHit(int lane, ArtWorkbenchGrade grade)
        {
            StartCoroutine(Burst(lane, ColorOf(grade), grade != ArtWorkbenchGrade.Miss));
        }

        void HandleMiss(int lane)
        {
            if (lane >= 0 && lane < lanes.Length && lanes[lane] != null)
                StartCoroutine(DimLane(lanes[lane]));
        }

        void HandleHoldStart(int lane)
        {
            holdActive = true;
            holdLane = lane;
            if (hold != null && hold.rt != null) Destroy(hold.rt.gameObject);
            hold = MakeNote("HoldBody", lane, Preset.holdBody, true);
            var head = MakeNote("HoldHead", lane, Preset.holdFill, false);
            head.rt.anchoredPosition = new Vector2(LaneX(lane), ArtWorkbenchTokens.JudgeFromBottom);
            head.rt.sizeDelta = new Vector2(Preset.noteWidth, Preset.noteHeight);
            Destroy(head.rt.gameObject, 0.35f);
        }

        void HandleHoldEnd(int lane)
        {
            holdActive = false;
            if (hold != null && hold.rt != null) Destroy(hold.rt.gameObject);
            hold = null;
            StartCoroutine(Burst(lane, Preset.holdFill, true));
        }

        void ResetSandbox()
        {
            sandboxCombo = 0;
            sandboxScore = 0;
            spawnClock = 0f;
            for (int i = 0; i < taps.Count; i++)
                if (taps[i].rt != null) Destroy(taps[i].rt.gameObject);
            taps.Clear();
        }

        SandboxNote MakeNote(string name, int lane, Color color, bool body)
        {
            var rt = ArtWorkbenchUi.Root(noteRoot, name, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, Vector2.zero);
            rt.pivot = new Vector2(0.5f, 0.5f);
            var mark = rt.gameObject.AddComponent<ArtWorkbenchMark>();
            mark.holdBody = body;
            mark.color = color;
            return new SandboxNote { rt = rt, mark = mark, lane = lane, holdBody = body };
        }

        float LaneX(int lane)
        {
            return (Mathf.Clamp(lane, 0, 1) - 0.5f) * 280f;
        }

        Color ColorOf(ArtWorkbenchGrade grade)
        {
            if (grade == ArtWorkbenchGrade.Perfect) return Preset.perfect;
            if (grade == ArtWorkbenchGrade.Great) return Preset.great;
            return Preset.miss;
        }

        IEnumerator Burst(int lane, Color color, bool rings)
        {
            var pos = new Vector2(LaneX(lane), ArtWorkbenchTokens.JudgeFromBottom);
            if (rings)
            {
                var ringGo = new GameObject("HitRing", typeof(RectTransform), typeof(CanvasRenderer), typeof(HitRingGraphic));
                ringGo.transform.SetParent(fxRoot, false);
                var rt = (RectTransform)ringGo.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = pos;
                var ring = ringGo.GetComponent<HitRingGraphic>();
                ring.raycastTarget = false;
                float t = 0f;
                float dur = Mathf.Max(0.08f, Preset.hitBurstDuration);
                float scale = Mathf.Max(0.35f, Preset.hitBurstScale);
                while (t < dur)
                {
                    t += Time.unscaledDeltaTime;
                    float k = t / dur;
                    float size = Mathf.Lerp(70f, 240f * scale, k);
                    rt.sizeDelta = new Vector2(size, size);
                    ring.RingColor = new Color(color.r, color.g, color.b, (1f - k) * 0.9f);
                    yield return null;
                }
                Destroy(ringGo);
            }

            var burstGo = new GameObject("HitBurst", typeof(RectTransform), typeof(CanvasRenderer), typeof(BurstGraphic));
            burstGo.transform.SetParent(fxRoot, false);
            var brt = (RectTransform)burstGo.transform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0f);
            brt.pivot = new Vector2(0.5f, 0.5f);
            brt.anchoredPosition = pos;
            var burst = burstGo.GetComponent<BurstGraphic>();
            burst.raycastTarget = false;
            burst.color = color;
            float bt = 0f;
            float bdur = Mathf.Max(0.08f, Preset.hitBurstDuration * 0.85f);
            while (bt < bdur)
            {
                bt += Time.unscaledDeltaTime;
                float k = bt / bdur;
                float size = Mathf.Lerp(40f, 180f * Preset.hitBurstScale, k);
                brt.sizeDelta = new Vector2(size, size);
                burst.color = new Color(color.r, color.g, color.b, 1f - k);
                yield return null;
            }
            Destroy(burstGo);
        }

        IEnumerator DimLane(Image lane)
        {
            Color orig = Preset.laneFill;
            Color dim = new Color(orig.r * 0.45f, orig.g * 0.45f, orig.b * 0.45f, orig.a);
            float t = 0f;
            while (t < 0.28f && lane != null)
            {
                t += Time.unscaledDeltaTime;
                lane.color = Color.Lerp(dim, orig, t / 0.28f);
                yield return null;
            }
            if (lane != null) lane.color = orig;
        }
    }
}
