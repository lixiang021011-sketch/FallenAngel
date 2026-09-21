using System;
using System.IO;
using FallenAngel.Core;
using UnityEngine;

namespace FallenAngel.UI
{
    /// <summary>工作台三档判定（沙盒视觉）。实机五档映射见 <see cref="ArtWorkbenchBus.FromLive"/>。</summary>
    public enum ArtWorkbenchGrade
    {
        Perfect = 0,
        Great = 1,
        Miss = 2
    }

    /// <summary>工作台画面页。</summary>
    public enum ArtWorkbenchPage
    {
        Field = 0,
        SongSelect = 1,
        Result = 2
    }

    /// <summary>
    /// 美术工作台设计 token 与可保存预设。
    /// 色值从 DeepSeaTheme 拷贝一份，调工作台不会改局内主题。
    /// </summary>
    [Serializable]
    public class ArtWorkbenchPreset
    {
        public string name = "default";
        public string version = ArtWorkbenchTokens.Version;
        public int laneCount = 2;
        public float noteWidth = 168f;
        public float noteHeight = 36f;
        public float fallSpeed = 780f;
        public float hitBurstScale = 1f;
        public float hitBurstDuration = 0.28f;
        public float holdPulseSpeed = 2.4f;
        public bool bindLiveEvents;
        public Color tapFill = ArtWorkbenchTokens.TapFill;
        public Color holdFill = ArtWorkbenchTokens.HoldFill;
        public Color holdBody = ArtWorkbenchTokens.HoldBody;
        public Color laneFill = ArtWorkbenchTokens.LaneFill;
        public Color judgeLine = ArtWorkbenchTokens.JudgeLine;
        public Color perfect = ArtWorkbenchTokens.Perfect;
        public Color great = ArtWorkbenchTokens.Great;
        public Color miss = ArtWorkbenchTokens.Miss;

        public ArtWorkbenchPreset Clone()
        {
            return JsonUtility.FromJson<ArtWorkbenchPreset>(JsonUtility.ToJson(this));
        }

        public void CopyFrom(ArtWorkbenchPreset other)
        {
            if (other == null) return;
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(other), this);
        }
    }

    /// <summary>工作台 token 常量（A02）。</summary>
    public static class ArtWorkbenchTokens
    {
        public const string Version = "1.0";
        public const int LaneCount = 2;
        public const float CanvasWidth = 1080f;
        public const float CanvasHeight = 1920f;
        public const float TopBar = 88f;
        public const float BottomPanel = 420f;
        public const float JudgeFromBottom = 520f;

        public static readonly Color Background = new Color(15f / 255f, 32f / 255f, 46f / 255f, 1f);
        public static readonly Color Card = new Color(0.106f, 0.196f, 0.231f, 0.94f);
        public static readonly Color Ink = new Color(232f / 255f, 246f / 255f, 245f / 255f, 1f);
        public static readonly Color Muted = new Color(127f / 255f, 150f / 255f, 155f / 255f, 1f);
        public static readonly Color Accent = new Color(145f / 255f, 208f / 255f, 212f / 255f, 1f);
        public static readonly Color Paid = new Color(240f / 255f, 168f / 255f, 204f / 255f, 1f);
        public static readonly Color TapFill = Accent;
        public static readonly Color HoldFill = Paid;
        public static readonly Color HoldBody = new Color(Paid.r, Paid.g, Paid.b, 0.55f);
        public static readonly Color LaneFill = new Color(0.106f, 0.196f, 0.231f, 0.92f);
        public static readonly Color JudgeLine = Ink;
        public static readonly Color Perfect = Accent;
        public static readonly Color Great = new Color(0.184f, 0.498f, 0.431f, 1f);
        public static readonly Color Miss = new Color(0.690f, 0.541f, 0.525f, 1f);

        public static ArtWorkbenchPreset DefaultPreset()
        {
            return new ArtWorkbenchPreset();
        }
    }

    /// <summary>预设读写。磁盘一份，PlayerPrefs 记最后槽位，保证重启恢复。</summary>
    public static class ArtWorkbenchStore
    {
        public const string PrefsKey = "FA.ArtWorkbench.LastSlot";
        public static readonly string[] Slots = { "default", "studio-a", "studio-b", "studio-c" };

        public static string DirectoryPath
        {
            get { return Path.Combine(Application.persistentDataPath, "art-workbench"); }
        }

        public static string SlotPath(string slot)
        {
            return Path.Combine(DirectoryPath, (slot ?? "default") + ".json");
        }

        public static void Save(ArtWorkbenchPreset preset, string slot)
        {
            if (preset == null) return;
            preset.name = slot ?? "default";
            preset.version = ArtWorkbenchTokens.Version;
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(SlotPath(preset.name), JsonUtility.ToJson(preset, true));
            PlayerPrefs.SetString(PrefsKey, preset.name);
            PlayerPrefs.Save();
            Debug.Log("[ArtWorkbench] 已保存预设 " + preset.name + " -> " + SlotPath(preset.name));
        }

        public static ArtWorkbenchPreset Load(string slot)
        {
            string path = SlotPath(slot);
            if (!File.Exists(path))
            {
                Debug.Log("[ArtWorkbench] 槽位 " + slot + " 无存档，用默认 token");
                return ArtWorkbenchTokens.DefaultPreset();
            }
            try
            {
                var preset = JsonUtility.FromJson<ArtWorkbenchPreset>(File.ReadAllText(path));
                if (preset == null) return ArtWorkbenchTokens.DefaultPreset();
                PlayerPrefs.SetString(PrefsKey, slot);
                PlayerPrefs.Save();
                Debug.Log("[ArtWorkbench] 已加载预设 " + slot);
                return preset;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ArtWorkbench] 读取失败，回退默认: " + e.Message);
                return ArtWorkbenchTokens.DefaultPreset();
            }
        }

        public static ArtWorkbenchPreset LoadLastOrDefault()
        {
            string slot = PlayerPrefs.GetString(PrefsKey, "default");
            return Load(slot);
        }
    }

    /// <summary>工作台内部事件。实机事件经 Host 转进来，缺事件时由面板按钮触发。</summary>
    public sealed class ArtWorkbenchBus
    {
        public event Action<int, ArtWorkbenchGrade> OnHit;
        public event Action<int> OnHoldStart;
        public event Action<int> OnHoldEnd;
        public event Action<int> OnMiss;
        public event Action<int, int> OnHud; // combo, score
        public event Action OnSongStart;
        public event Action OnSongEnd;
        public event Action<ArtWorkbenchPage> OnPage;

        public void Hit(int lane, ArtWorkbenchGrade grade)
        {
            if (grade == ArtWorkbenchGrade.Miss) OnMiss?.Invoke(lane);
            OnHit?.Invoke(lane, grade);
        }

        public void HoldStart(int lane) { OnHoldStart?.Invoke(lane); }
        public void HoldEnd(int lane) { OnHoldEnd?.Invoke(lane); }
        public void Hud(int combo, int score) { OnHud?.Invoke(combo, score); }
        public void SongStart() { OnSongStart?.Invoke(); }
        public void SongEnd() { OnSongEnd?.Invoke(); }
        public void Page(ArtWorkbenchPage page) { OnPage?.Invoke(page); }

        public static ArtWorkbenchGrade FromLive(JudgeResultType type)
        {
            switch (type)
            {
                case Core.JudgeResultType.Perfect: return ArtWorkbenchGrade.Perfect;
                case Core.JudgeResultType.Great:
                case Core.JudgeResultType.Good: return ArtWorkbenchGrade.Great;
                default: return ArtWorkbenchGrade.Miss;
            }
        }
    }
}
