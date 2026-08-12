using UnityEngine;
using FallenAngel.Data;
using FallenAngel.Core;

namespace FallenAngel.Audio
{
    /// <summary>
    /// 音频管理器 - 负责背景音乐和音效播放
    /// 与GameManager同步歌曲时间
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
        [Range(0f, 1f)] public float sfxVolume = 0.7f;
        [Range(0f, 1f)] public float hitVolume = 0.6f;

        /// <summary>BGM是否正在播放</summary>
        public bool IsPlaying => bgmSource.isPlaying;

        /// <summary>当前歌曲播放进度（秒）</summary>
        public float CurrentTime => bgmSource.time;

        private bool isInitialized;

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
            ApplyVolumeSettings();
            isInitialized = true;
        }

        /// <summary>
        /// 加载并播放谱面对应的背景音乐
        /// </summary>
        public void LoadAndPlayBGM(ChartData chart, float startTime = 0f)
        {
            if (chart == null || string.IsNullOrEmpty(chart.metadata.audioFileName))
            {
                Debug.LogWarning("[AudioManager] 谱面未配置音频文件，使用无音频模式");
                return;
            }

            AudioClip clip = Resources.Load<AudioClip>($"Audio/{chart.metadata.audioFileName}");
            if (clip == null)
            {
                Debug.LogWarning($"[AudioManager] 找不到音频文件: Audio/{chart.metadata.audioFileName}，使用无音频模式");
                return;
            }

            PlayBGM(clip, startTime);
        }

        /// <summary>
        /// 播放指定AudioClip作为BGM
        /// </summary>
        public void PlayBGM(AudioClip clip, float startTime = 0f)
        {
            if (!isInitialized) return;

            bgmSource.Stop();
            bgmSource.clip = clip;
            bgmSource.time = Mathf.Max(0f, startTime);
            bgmSource.Play();
        }

        /// <summary>
        /// 同步BGM时间到GameManager
        /// </summary>
        private void Update()
        {
            if (FallenAngel.Core.GameManager.Instance != null &&
                FallenAngel.Core.GameManager.Instance.CurrentState == FallenAngel.Core.GameState.Playing)
            {
                if (bgmSource.isPlaying)
                {
                    FallenAngel.Core.GameManager.Instance.SetSongTime(bgmSource.time);
                }
            }
        }

        /// <summary>
        /// 播放判定音效
        /// </summary>
        public void PlayHitSfx(JudgeResultType result)
        {
            AudioClip clip = result switch
            {
                JudgeResultType.Perfect => perfectHitSfx,
                JudgeResultType.Great => greatHitSfx,
                JudgeResultType.Good => goodHitSfx,
                _ => missSfx
            };
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
        /// 暂停BGM
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
        /// 停止所有音频
        /// </summary>
        public void StopAll()
        {
            bgmSource.Stop();
            sfxSource.Stop();
            hitSource.Stop();
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
