using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 轨道/音符配色唯一来源（游戏与谱面编辑器共用）。
    ///
    /// 三套调色板（2026-09-17 音符家族设计稿 v2 定稿，形状相同、只有颜色不同）：
    ///   Teal   青绿——月白青 → 青 → 青蓝 → 深青 → 最深青
    ///   Indigo 靛青——冰白 → 天青 → 湖蓝 → 靛蓝 → 深靛（**当前选定**）
    ///   Yellow 金黄——乳白 → 浅金 → 金 → 琥珀 → 深琥珀（暖底冷强调）
    ///   Moonscraper——传统鼓谱语义（4 键红黄蓝绿 / 5 键绿红黄蓝橙），仅编辑器备选
    ///
    /// 三套都是**明度阶梯**：五轨不靠色相区分，靠明度差（实测最小相邻差 20.2/31.7/21.6，要求 ≥8）。
    ///
    /// 音符配色模式（<see cref="NoteMode"/>）：默认 <see cref="NotePaletteMode.PerNoteRandom"/>——
    /// 每个音符随机取一版，同一首歌里三版混排；<see cref="NotePaletteMode.Fixed"/> 则全部用当前选定版。
    /// </summary>
    public static class LaneColors
    {
        /// <summary>调色板编号</summary>
        public enum PaletteId
        {
            Teal = 0,
            Indigo = 1,
            Yellow = 2,
            /// <summary>传统鼓谱语义（仅编辑器备选，不计入三版随机的候选）</summary>
            Moonscraper = 3,
        }

        /// <summary>音符取色模式</summary>
        public enum NotePaletteMode
        {
            /// <summary>全部音符用 <see cref="ActivePalette"/> 一版</summary>
            Fixed = 0,
            /// <summary>每个音符随机取一版（当前用法）</summary>
            PerNoteRandom = 1,
            /// <summary>按轨道固定分配（lane % 3），同轨永远同版</summary>
            PerLane = 2,
        }

        /// <summary>当前选定调色板（轨道柱 / 判定线 / UI 用它）</summary>
        public static PaletteId ActivePalette = PaletteId.Indigo;

        /// <summary>音符取色模式</summary>
        public static NotePaletteMode NoteMode = NotePaletteMode.PerNoteRandom;

        /// <summary>随机候选（不含 Moonscraper）</summary>
        public const int RandomPaletteCount = 3;

        // ---- 三套明度阶梯（L0 最亮 → L4 最暗）----
        private static readonly Color[] TealLadder =
        {
            new Color(0.812f, 0.937f, 0.918f), // #CFEFEA
            new Color(0.659f, 0.871f, 0.855f), // #A8DEDA
            new Color(0.541f, 0.788f, 0.776f), // #8AC9C6
            new Color(0.435f, 0.690f, 0.690f), // #6FB0B0
            new Color(0.337f, 0.580f, 0.580f), // #569494
        };

        private static readonly Color[] IndigoLadder =
        {
            new Color(0.894f, 0.957f, 0.969f), // #E4F4F7
            new Color(0.663f, 0.851f, 0.910f), // #A9D9E8
            new Color(0.498f, 0.659f, 0.863f), // #7FA8DC
            new Color(0.431f, 0.498f, 0.753f), // #6E7FC0
            new Color(0.357f, 0.357f, 0.651f), // #5B5BA6
        };

        private static readonly Color[] YellowLadder =
        {
            new Color(1.000f, 0.992f, 0.949f), // #FFFDF2
            new Color(1.000f, 0.914f, 0.627f), // #FFE9A0
            new Color(1.000f, 0.820f, 0.400f), // #FFD166
            new Color(0.878f, 0.663f, 0.247f), // #E0A93F
            new Color(0.659f, 0.486f, 0.133f), // #A87C22
        };

        // Moonscraper 备选（4 键鼓谱 / 5 键吉他谱）
        private static readonly Color[] DrumColors4 =
        {
            new Color(0.82f, 0.25f, 0.22f), new Color(0.95f, 0.78f, 0.15f),
            new Color(0.25f, 0.50f, 0.95f), new Color(0.20f, 0.72f, 0.30f),
        };
        private static readonly Color[] GuitarColors5 =
        {
            new Color(0.20f, 0.72f, 0.30f), new Color(0.82f, 0.25f, 0.22f),
            new Color(0.95f, 0.78f, 0.15f), new Color(0.25f, 0.50f, 0.95f),
            new Color(0.98f, 0.55f, 0.10f),
        };

        /// <summary>唯一暖色/强调色（正向提示用：连击、奖励）——暖底版本改用冷青</summary>
        public static Color GetAccent(PaletteId id)
        {
            switch (id)
            {
                case PaletteId.Yellow: return new Color(0.435f, 0.878f, 0.839f); // #6FE0D6 暖底冷强调
                default: return new Color(0.941f, 0.659f, 0.800f);               // #F0A8CC
            }
        }

        /// <summary>按当前选定调色板取某轨道颜色</summary>
        public static Color GetLaneColor(int lane)
        {
            return GetLaneColor(lane, ActivePalette, LaneLayout.ActiveLaneCount);
        }

        /// <summary>按指定调色板取某轨道颜色（越界自动钳制）</summary>
        public static Color GetLaneColor(int lane, PaletteId id)
        {
            return GetLaneColor(lane, id, LaneLayout.ActiveLaneCount);
        }

        /// <summary>
        /// 按指定调色板与键数取色。谱面编辑器用它：编辑中的谱面键数不一定等于运行时 ActiveLaneCount。
        /// </summary>
        public static Color GetLaneColor(int lane, PaletteId id, int laneCount)
        {
            switch (id)
            {
                case PaletteId.Moonscraper:
                    Color[] ms = laneCount == 5 ? GuitarColors5 : DrumColors4;
                    return ms[Mathf.Clamp(lane, 0, ms.Length - 1)];
                case PaletteId.Teal: return TealLadder[Mathf.Clamp(lane, 0, TealLadder.Length - 1)];
                case PaletteId.Yellow: return YellowLadder[Mathf.Clamp(lane, 0, YellowLadder.Length - 1)];
                default: return IndigoLadder[Mathf.Clamp(lane, 0, IndigoLadder.Length - 1)];
            }
        }

        /// <summary>按键数取色（兼容旧签名：跟随当前选定调色板）</summary>
        public static Color GetLaneColor(int lane, int laneCount)
        {
            return GetLaneColor(lane, ActivePalette, laneCount);
        }

        private static int notePaletteSeed = 20260917;

        /// <summary>开一局时换一个种子：同一局内每首歌的混排稳定，不同局之间会变</summary>
        public static void SetNotePaletteSeed(int seed)
        {
            notePaletteSeed = seed;
        }

        /// <summary>
        /// 决定某个音符用哪一版配色（由 NoteSpawner 在生成时调用）。
        /// PerNoteRandom 用 (时间, 轨道, 种子) 的哈希取模：看起来随机，但同一局内可复现，
        /// 且音符下落过程中颜色不会变（取色只在生成时发生一次）。
        /// </summary>
        public static PaletteId PickNotePalette(int lane, float time)
        {
            switch (NoteMode)
            {
                case NotePaletteMode.Fixed:
                    return ActivePalette;
                case NotePaletteMode.PerLane:
                    return (PaletteId)(Mathf.Abs(lane) % RandomPaletteCount);
                default:
                    int h = (Mathf.RoundToInt(time * 1000f) * 73856093)
                            ^ (lane * 19349663)
                            ^ (notePaletteSeed * 83492791);
                    return (PaletteId)(Mathf.Abs(h) % RandomPaletteCount);
            }
        }

        /// <summary>当前活动轨道数（与 LaneLayout 同步）</summary>
        public static int LaneCount { get { return LaneLayout.ActiveLaneCount; } }
    }
}
