using System.Collections.Generic;
using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>语言枚举（新语言在此追加）</summary>
    public enum Language
    {
        Chinese = 0,
        English = 1,
    }

    /// <summary>字符串表条目（JsonUtility 兼容）</summary>
    [System.Serializable]
    public class StringEntry
    {
        public string key;
        public string value;
    }

    /// <summary>语言表容器（JsonUtility 兼容）</summary>
    [System.Serializable]
    public class LanguageTable
    {
        public List<StringEntry> entries = new List<StringEntry>();
    }

    /// <summary>
    /// 多语言字符串表（自研轻量方案，风格同 ChartLoader/CalibrationSettings）。
    /// 语言文件: Resources/Localization/zh-CN.json、en-US.json（entries 列表结构）。
    /// 查询顺序: 当前语言表 → 中文表（默认回退）→ key 本身（动态文本字面量）。
    /// 语言选择持久化在 PlayerPrefs（FA_Language）。
    /// </summary>
    public static class Loc
    {
        private const string LanguageKey = "FA_Language";

        /// <summary>语言切换事件（LocalizedText 订阅刷新）</summary>
        public static event System.Action OnLanguageChanged;

        public static Language CurrentLanguage { get; private set; }

        private static Dictionary<string, string> zhTable = new Dictionary<string, string>();
        private static Dictionary<string, string> enTable = new Dictionary<string, string>();

        static Loc()
        {
            LoadTables();
            CurrentLanguage = (Language)PlayerPrefs.GetInt(LanguageKey, 0);
        }

        private static void LoadTables()
        {
            zhTable = LoadTable("zh-CN");
            enTable = LoadTable("en-US");
        }

        private static Dictionary<string, string> LoadTable(string name)
        {
            Dictionary<string, string> table = new Dictionary<string, string>();
            TextAsset asset = Resources.Load<TextAsset>($"Localization/{name}");
            if (asset == null)
            {
                Debug.LogWarning($"[Loc] 找不到语言文件: Localization/{name}");
                return table;
            }
            LanguageTable data = JsonUtility.FromJson<LanguageTable>(asset.text);
            if (data?.entries == null) return table;
            foreach (StringEntry e in data.entries)
                if (!string.IsNullOrEmpty(e.key)) table[e.key] = e.value;
            return table;
        }

        /// <summary>
        /// key 是否存在于语言表（当前语言表或中文回退表）。
        /// 动态文本以字面量作 key、不在表中 → HasKey=false → LocalizedText 不刷新它。
        /// </summary>
        public static bool HasKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            if (CurrentLanguage == Language.Chinese) return zhTable.ContainsKey(key);
            return enTable.ContainsKey(key) || zhTable.ContainsKey(key);
        }

        /// <summary>取当前语言文本；缺失回退中文再回退 key 本身。支持 {0} 占位符。</summary>
        public static string T(string key, params object[] args)
        {
            string value = Resolve(key);
            if (args != null && args.Length > 0 && value.Contains("{"))
            {
                try { value = string.Format(value, args); }
                catch { /* 占位符不匹配时原样返回 */ }
            }
            return value;
        }

        private static string Resolve(string key)
        {
            if (CurrentLanguage == Language.English && enTable.TryGetValue(key, out string en))
                return en;
            if (zhTable.TryGetValue(key, out string zh))
                return zh;
            return key;
        }

        /// <summary>脚本构建的原型文本回退；已有语言表条目优先，避免修改语言资产。</summary>
        public static void AddFallback(string key, string chinese, string english)
        {
            if (!zhTable.ContainsKey(key)) zhTable[key] = chinese;
            if (!enTable.ContainsKey(key)) enTable[key] = english;
        }

        /// <summary>切换语言并持久化，触发 OnLanguageChanged 让全 UI 刷新</summary>
        public static void SetLanguage(Language lang)
        {
            if (CurrentLanguage == lang) return;
            CurrentLanguage = lang;
            PlayerPrefs.SetInt(LanguageKey, (int)lang);
            PlayerPrefs.Save();
            OnLanguageChanged?.Invoke();
            Debug.Log($"[Loc] 语言切换: {lang}");
        }
    }
}
