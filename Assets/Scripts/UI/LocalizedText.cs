using UnityEngine;
using TMPro;
using FallenAngel.Core;

namespace FallenAngel.UI
{
    /// <summary>
    /// 本地化文本：Start/激活时与语言切换时从 Loc 刷新。
    /// 动态文本（key 即字面量、不在语言表中，Loc.HasKey=false）不会被刷新，
    /// 保证分数/倒计时等运行时赋值不被语言切换覆盖。
    /// </summary>
    public class LocalizedText : MonoBehaviour
    {
        [SerializeField] private string key;

        private TextMeshProUGUI tmp;

        /// <summary>设置 key 并立即刷新（SceneBuilder 构建时调用）</summary>
        public void SetKey(string newKey)
        {
            key = newKey;
            Refresh();
        }

        private void Awake()
        {
            tmp = GetComponent<TextMeshProUGUI>();
        }

        private void OnEnable()
        {
            Loc.OnLanguageChanged -= Refresh;
            Loc.OnLanguageChanged += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            Loc.OnLanguageChanged -= Refresh;
        }

        private void Refresh()
        {
            if (tmp == null || string.IsNullOrEmpty(key)) return;
            if (!Loc.HasKey(key)) return; // 动态文本不刷新
            tmp.text = Loc.T(key);
        }
    }
}
