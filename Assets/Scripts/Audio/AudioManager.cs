using UnityEngine;
using FallenAngel.Data;
using FallenAngel.Core;

namespace FallenAngel.Audio
{
    /// <summary>
    /// 音频管理器 - 负责背景音乐和音效播放
    /// 同时也是游戏时间的唯一权威供时方：
    ///   - 有音频：以 bgmSource.time 为准（与人耳听到的音乐天然对齐）
    ///   - 无音频：内部虚拟钟（unscaledTime 推进）兜底，保证开发期可测
    /// GameManager 只从这里读取时间，不自己计时（架构约定 §5）。
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [Header("音源引用")]
        [SerializeField] private AudioSource bgmSource;     // 背景音乐
        [SerializeField] private AudioSource sfxSource;     // 音效
        [SerializeField] private AudioSource hitSource;     // 打击音（独立音效，可多个重叠）

        [Header("音效Clip")]
        public AudioClip perfectHitSfx;     // Perfect判定音
        public AudioClip greatHitSfx;       // Great判定音
        public AudioClip goodHitSfx;        // Good判定音
        public AudioClip missSfx;           // Miss判定音
        public AudioClip longNoteHoldSfx;   // 长按持续音
        public AudioClip buttonClickSfx;    // 按钮点击音

        [Header("音量设置")]
        [Range(0f, 1f)] public float bgmVolume = 0.8f;
        [SerializeField, Range(0f, 1f)] private float sfxVolume = 0.7f;   // 字段名不变，Inspector 序列化保留
        [Range(0f, 1f)] public float hitVolume = 0.6f;

        /// <summary>音效音量 0~1：设置页调节入口（set 走 GameSettings 持久化并即时应用）</summary>
        public float SfxVolume
        {
            get => sfxVolume;
            set
            {
                sfxVolume = Mathf.Clamp01(value);
                GameSettings.SfxVolume = sfxVolume;
                ApplyVolumeSettings();
            }
        }

        [Header("曲终淡出")]
        [Tooltip("结算时BGM淡出时长（秒）")]
        [SerializeField] private float endFadeOutDuration = 1.5f;

        /// <summary>真实音频是否正在播放</summary>
        public bool IsPlaying => audioStarted && bgmSource.isPlaying;

        /// <summary>
        /// 真实音频自然播完。必须靠近 clip 末尾：切后台/拔耳机时 isPlaying 也会变 false，
        /// 不能把「没在播」当成曲终。Play() 后首帧 time=0 也不会误判。
        /// </summary>
        public bool HasAudioFinished
        {
            get
            {
                if (!audioStarted || bgmSource == null || bgmSource.clip == null) return false;
                if (bgmSource.isPlaying) return false;
                return bgmSource.time >= bgmSource.clip.length - 0.05f;
            }
        }

        /// <summary>
        /// 当前权威游戏时间（秒）：
        /// 真实音频播放中返回播放位置；否则返回虚拟钟时间（无音频兜底）
        /// </summary>
        public float CurrentTime => audioStarted ? bgmSource.time : virtualTime;

        private bool isInitialized;
        private bool audioStarted;   // 真实音频是否已开始播放（clip 已装载且 Play 成功）
        private float virtualTime;   // 虚拟钟累计时间（仅无音频时推进）
        private Coroutine fadeOutCoroutine;  // 曲终淡出协程引用（可中断）

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // 如果没手动指定，自动创建所需的 AudioSource
            if (bgmSource == null) bgmSource = GetOrCreateAudioSource("BGM_Source");
            if (sfxSource == null) sfxSource = GetOrCreateAudioSource("SFX_Source");
            if (hitSource == null) hitSource = GetOrCreateAudioSource("Hit_Source");

            bgmSource.loop = false;
            bgmSource.playOnAwake = false;
            EnsureDefaultSfx();
            // 用持久化设置覆盖 Inspector 初值（首次运行即默认值）
            sfxVolume = GameSettings.SfxVolume;
            ApplyVolumeSettings();
            isInitialized = true;
        }

        private void OnEnable()
        {
            SubscribeGameStart();
        }

        private void Start()
        {
            // 先退订再订阅，防止 Awake 执行顺序导致漏订/重订（架构约定 §3）
            SubscribeGameStart();
        }

        private void OnDisable()
        {
            if (GameManager.Instance != null)
                GameManager.Instance.OnGameStart -= PlayBGM;
        }

        private void SubscribeGameStart()
        {
            if (GameManager.Instance == null) return;
            GameManager.Instance.OnGameStart -= PlayBGM;
            GameManager.Instance.OnGameStart += PlayBGM;
        }

        private void Update()
        {
            // 虚拟钟推进：仅在无真实音频时进行（真实音频以播放位置为准）
            if (audioStarted) return;

            if (GameManager.Instance != null &&
                GameManager.Instance.CurrentState == GameState.Playing)
            {
                virtualTime += Time.unscaledDeltaTime;
            }
        }

        /// <summary>
        /// 装载谱面对应的BGM（不播放）。
        /// 实际播放由 GameManager.OnGameStart 统一触发（倒计时结束后），
        /// 保证开局时刻游戏时间从 0 起与音乐对齐。
        /// 找不到音频时进入虚拟钟模式，游戏仍可正常测试。
        /// </summary>
        public void LoadBGM(ChartData chart)
        {
            audioStarted = false;
            virtualTime = 0f;

            bgmSource.Stop();
            bgmSource.clip = null;

            if (chart == null || chart.metadata == null ||
                string.IsNullOrEmpty(chart.metadata.audioFileName))
            {
                Debug.LogWarning("[AudioManager] 谱面未配置音频文件，使用虚拟钟模式");
                return;
            }

            AudioClip clip = Resources.Load<AudioClip>($"Audio/{chart.metadata.audioFileName}");
            if (clip == null)
            {
                Debug.LogWarning($"[AudioManager] 找不到音频文件: Audio/{chart.metadata.audioFileName}，使用虚拟钟模式");
                return;
            }

            bgmSource.clip = clip;
        }

        /// <summary>
        /// 从 0 开始播放已装载的BGM（由 GameManager.OnGameStart 触发）
        /// </summary>
        private void PlayBGM()
        {
            if (!isInitialized) return;

            if (bgmSource.clip != null)
            {
                bgmSource.time = 0f;
                bgmSource.volume = bgmVolume;  // 复位上一局淡出期间被改动的音量
                bgmSource.Play();
                audioStarted = true;
                Debug.Log("[AudioManager] BGM 开始播放（音频时钟模式）");
            }
            else
            {
                Debug.Log("[AudioManager] 无音频，虚拟钟模式运行");
            }
        }

        /// <summary>
        /// BGM 淡出（曲终结算时调用）。
        /// 音频已停止时直接复位；淡出完成后停止音源并恢复默认音量。
        /// </summary>
        public void FadeOutBGM()
        {
            if (fadeOutCoroutine != null)
            {
                StopCoroutine(fadeOutCoroutine);
                fadeOutCoroutine = null;
            }

            if (!bgmSource.isPlaying)
            {
                // 音频已播完（曲终信号路径），无需淡出，仅确保状态干净
                bgmSource.Stop();
                bgmSource.volume = bgmVolume;
                return;
            }

            fadeOutCoroutine = StartCoroutine(FadeOutCoroutine());
        }

        private System.Collections.IEnumerator FadeOutCoroutine()
        {
            float startVolume = bgmSource.volume;
            float duration = Mathf.Max(0.05f, endFadeOutDuration);
            float timer = 0f;

            // 用 unscaledDeltaTime：淡出属于演出表现，与游戏时间缩放解耦（架构约定 §7）
            while (timer < duration)
            {
                timer += Time.unscaledDeltaTime;
                bgmSource.volume = Mathf.Lerp(startVolume, 0f, Mathf.Clamp01(timer / duration));
                yield return null;
            }

            bgmSource.Stop();
            bgmSource.volume = bgmVolume;   // 恢复默认音量，供下一局使用
            audioStarted = false;           // 真实音频已停止，供时状态复位
            fadeOutCoroutine = null;
        }

        /// <summary>
        /// 判定音效兜底：clip 字段为空时用程序合成音（零资源依赖，虚拟钟模式下也立即可用）。
        /// 未来替换为真实采样时直接给字段赋值即可，合成音自动失效。
        /// </summary>
        private void EnsureDefaultSfx()
        {
            // 需求 2026-09-01：命中反馈统一为无音调打击声（判定越准越短促清脆）；
            // Bad/Miss 不再合成音效（PlayHitSfx 门控不出声），missSfx 字段保留给未来真实采样。
            if (perfectHitSfx == null) perfectHitSfx = SynthesizedSfx.CreatePercussionClip(0.12f);
            if (greatHitSfx == null) greatHitSfx = SynthesizedSfx.CreatePercussionClip(0.10f);
            if (goodHitSfx == null) goodHitSfx = SynthesizedSfx.CreatePercussionClip(0.08f);
            if (buttonClickSfx == null) buttonClickSfx = SynthesizedSfx.CreateHitClip(1000f, 0.05f, 24f);
        }

        /// <summary>
        /// 播放判定音效。需求 2026-09-01：
        ///   命中（Perfect/Great/Good）→ 无音调打击声；
        ///   Bad/Miss → 不出声（无音效）。
        /// </summary>
        public void PlayHitSfx(JudgeResultType result)
        {
            AudioClip clip = result switch
            {
                JudgeResultType.Perfect => perfectHitSfx,
                JudgeResultType.Great => greatHitSfx,
                JudgeResultType.Good => goodHitSfx,
                _ => null // Bad/Miss：无音效
            };
            if (clip == null) return;
            PlaySfx(clip, hitVolume, hitSource);
        }

        /// <summary>
        /// 播放通用音效
        /// </summary>
        public void PlaySfx(AudioClip clip, float volumeScale = 1f, AudioSource source = null)
        {
            if (clip == null) return;
            AudioSource src = source ?? sfxSource;
            float vol = (source == null || source == sfxSource) ? sfxVolume : hitVolume;
            src.PlayOneShot(clip, vol * volumeScale);
        }

        /// <summary>
        /// 播放按钮点击音
        /// </summary>
        public void PlayButtonClick()
        {
            PlaySfx(buttonClickSfx, 1f);
        }

        /// <summary>
        /// 暂停BGM（虚拟钟由状态门控自动冻结，无需处理）
        /// </summary>
        public void PauseBGM()
        {
            if (bgmSource.isPlaying) bgmSource.Pause();
        }

        /// <summary>
        /// 恢复BGM
        /// </summary>
        public void ResumeBGM()
        {
            bgmSource.UnPause();
        }

        /// <summary>
        /// 停止所有音频（并复位供时状态与淡出）
        /// </summary>
        public void StopAll()
        {
            if (fadeOutCoroutine != null)
            {
                StopCoroutine(fadeOutCoroutine);
                fadeOutCoroutine = null;
            }
            bgmSource.Stop();
            sfxSource.Stop();
            hitSource.Stop();
            bgmSource.volume = bgmVolume;   // 复位淡出期间被改动的音量
            audioStarted = false;
            virtualTime = 0f;
        }

        /// <summary>
        /// 应用音量设置
        /// </summary>
        public void ApplyVolumeSettings()
        {
            if (bgmSource != null) bgmSource.volume = bgmVolume;
            if (sfxSource != null) sfxSource.volume = sfxVolume;
            if (hitSource != null) hitSource.volume = hitVolume;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// 获取或自动创建 AudioSource 组件
        /// </summary>
        private AudioSource GetOrCreateAudioSource(string childName)
        {
            // 先尝试自身已有的 AudioSource
            var existing = GetComponents<AudioSource>();
            if (existing.Length > 0 && existing[0] != null)
            {
                // 第一个 AudioSource 留给 BGM，SFX/Hit 创建子对象
                if (childName == "BGM_Source") return existing[0];
            }

            // 创建子对象挂载 AudioSource
            GameObject child = new GameObject(childName);
            child.transform.SetParent(transform, false);
            AudioSource src = child.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = false;
            return src;
        }
    }
}
