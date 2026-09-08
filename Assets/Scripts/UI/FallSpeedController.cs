using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FallenAngel.Core;

namespace FallenAngel.UI
{
    /// <summary>
    /// 下落速度调节控制器：[−]/[＋] 调节 GameManager.speedMultiplier，
    /// PlayerPrefs 持久化（下次开局保持）。速度实时生效（ActualFallTime 每帧读取）。
    /// 挂载位置：Canvas 根（始终激活——此前挂 HUDPanel 时 Menu 状态 GamePanel 失活会断设置页按钮监听）。
    /// 双组绑定：暂停面板组（pauseRoot 内）+ 选项设置页组（SceneBuilder 注入），同源同步。
    /// </summary>
    public class FallSpeedController : MonoBehaviour
    {
        private const string PrefsKey = "FallSpeedMultiplier";
        private const float MinSpeed = 0.5f;
        private const float MaxSpeed = 2.0f;
        private const float Step = 0.1f;

        [SerializeField] private GameObject pauseRoot; // PauseRoot（SceneBuilder 注入）
        [Header("设置页按钮组（SceneBuilder 注入）")]
        [SerializeField] private Button settingsMinusButton;
        [SerializeField] private Button settingsPlusButton;
        [SerializeField] private TextMeshProUGUI settingsSpeedText; // 动态文本

        private Button minusButton;
        private Button plusButton;
        private TextMeshProUGUI speedText;
        private float speed = 1f;

        private void Awake()
        {
            speed = Mathf.Clamp(PlayerPrefs.GetFloat(PrefsKey, 1f), MinSpeed, MaxSpeed);
        }

        private void Start()
        {
            FindReferences();
            BindButtons();
            ApplySpeed();
            UpdateText();
        }

        private void FindReferences()
        {
            if (pauseRoot == null) return;
            Transform root = pauseRoot.transform;
            Transform minusT = root.Find("FallSpeedMinusButton");
            if (minusT != null) minusButton = minusT.GetComponent<Button>();
            Transform plusT = root.Find("FallSpeedPlusButton");
            if (plusT != null) plusButton = plusT.GetComponent<Button>();
            Transform txtT = root.Find("FallSpeedText");
            if (txtT != null) speedText = txtT.GetComponent<TextMeshProUGUI>();
        }

        private void BindButtons()
        {
            if (minusButton != null)
            {
                minusButton.onClick.RemoveAllListeners();
                minusButton.onClick.AddListener(() => Adjust(-Step));
            }
            if (plusButton != null)
            {
                plusButton.onClick.RemoveAllListeners();
                plusButton.onClick.AddListener(() => Adjust(Step));
            }
            if (settingsMinusButton != null)
            {
                settingsMinusButton.onClick.RemoveAllListeners();
                settingsMinusButton.onClick.AddListener(() => Adjust(-Step));
            }
            if (settingsPlusButton != null)
            {
                settingsPlusButton.onClick.RemoveAllListeners();
                settingsPlusButton.onClick.AddListener(() => Adjust(Step));
            }
        }

        private void Adjust(float delta)
        {
            speed = Mathf.Clamp(speed + delta, MinSpeed, MaxSpeed);
            PlayerPrefs.SetFloat(PrefsKey, speed);
            ApplySpeed();
            UpdateText();
        }

        private void ApplySpeed()
        {
            if (GameManager.Instance != null)
                GameManager.Instance.speedMultiplier = speed;
        }

        private void UpdateText()
        {
            string text = speed.ToString("0.0") + "x";
            if (speedText != null) speedText.text = text;
            if (settingsSpeedText != null) settingsSpeedText.text = text;
        }
    }
}
