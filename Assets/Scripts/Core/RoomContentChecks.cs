#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 房间内容框架的自检（内容未铺也要能验）：
    ///   · 三张空表必须通过校验（内容还没铺是正常状态）；
    ///   · "已接线效果"清单必须正好是 None / Gear / Money —— 防止有人把没实现的词标成已接线；
    ///   · 写错的行要逐条点名：未接线的效果、金额为负、缺文案、重复 id、Gear 指向不存在的器材、
    ///     内容行选项数不在 1–3、引用不存在的选项、把演奏房/商店塞进内容表、层号越界；
    ///   · 按层过滤（RowsFor）只挑当前层与"任意层"的行。
    ///
    /// 菜单与 batchmode（-executeMethod FallenAngel.Core.RoomContentChecks.Run）两用；报告写 Logs/room_content_check.txt。
    /// </summary>
    public static class RoomContentChecks
    {
        private const string Tag = "[RoomContent] ";

        [MenuItem("Tools/FallenAngel/Validate Room Content Tables")]
        public static void Run()
        {
            var lines = new List<string>();
            int pass = 0, fail = 0;

            Action<string, bool, string> check = (name, ok, detail) =>
            {
                if (ok) pass++; else fail++;
                lines.Add(string.Format("{0} {1,-30} {2}", ok ? "PASS" : "FAIL", name, detail));
            };

            try
            {
                var shipped = RoomContentCatalog.Validate();
                check("三张空表通过校验", shipped.Count == 0,
                    shipped.Count == 0
                        ? string.Format("选项 {0} / 事件 {1} / 休息 {2} / 纪念品 {3}",
                            RoomContentCatalog.Options.Count, RoomContentCatalog.Events.Count,
                            RoomContentCatalog.Rests.Count, RoomContentCatalog.Keepsakes.Count)
                        : string.Join("；", shipped.ToArray()));

                var wired = RoomContentCatalog.WiredEffects.OrderBy(k => (int)k).ToArray();
                check("已接线效果 = None / Gear / Money",
                    wired.SequenceEqual(new[] { RoomEffectKind.None, RoomEffectKind.Gear, RoomEffectKind.Money }),
                    string.Join(" / ", wired.Select(k => k.ToString()).ToArray()));

                // ---------- 写错的行要点名 ----------
                var badOptions = new List<RoomOptionRow>
                {
                    new RoomOptionRow("OP_DEBUFF", "room.op.debuff", takeKind: RoomEffectKind.Debuff),        // 未接线
                    new RoomOptionRow("OP_NEG", "room.op.neg", giveKind: RoomEffectKind.Money, giveAmount: -5), // 金额为负
                    new RoomOptionRow("OP_NOKEY", null),                                                     // 缺文案
                    new RoomOptionRow("OP_GEARMISS", "room.op.gear", giveKind: RoomEffectKind.Gear, giveId: "CG99"), // 器材不存在
                    new RoomOptionRow("OP_CG06", "room.op.gear2", giveKind: RoomEffectKind.Gear, giveId: "CG06"),
                };
                var badEvents = new List<RoomContentRow>
                {
                    new RoomContentRow("EV_DUP", ChapterRoomKind.Event, 0, "scene.ev", "text.ev",
                        new[] { "OP_CG06" }),
                    new RoomContentRow("EV_DUP", ChapterRoomKind.Event, 0, "scene.ev", "text.ev",
                        new[] { "OP_CG06" }),                                                                 // 重复 id
                    new RoomContentRow("EV_WRONGKIND", ChapterRoomKind.Performance, 0, "scene.ev", "text.ev",
                        new[] { "OP_CG06" }),                                                                 // 演奏房不该有内容行
                    new RoomContentRow("EV_NOOPT", ChapterRoomKind.Event, 0, "scene.ev", "text.ev",
                        new string[0]),                                                                       // 选项数 0
                    new RoomContentRow("EV_MANY", ChapterRoomKind.Event, 0, "scene.ev", "text.ev",
                        new[] { "OP_CG06", "OP_CG06", "OP_CG06", "OP_CG06" }),                                // 选项数 4
                    new RoomContentRow("EV_MISSINGOPT", ChapterRoomKind.Event, 0, "scene.ev", "text.ev",
                        new[] { "OP_NOPE" }),                                                                 // 引用不存在的选项
                    new RoomContentRow("EV_NOKEY", ChapterRoomKind.Event, 0, null, null, new[] { "OP_CG06" }), // 缺文案
                    new RoomContentRow("EV_BADLAYER", ChapterRoomKind.Event, 9, "scene.ev", "text.ev",
                        new[] { "OP_CG06" }),                                                                 // 层号越界
                };
                var badErrors = RoomContentCatalog.Validate(badOptions, badEvents, new List<RoomContentRow>(), new List<RoomContentRow>());
                var missing = Missing(badErrors, "还没接线", "金额不能为负", "缺 labelKey", "不存在的器材",
                    "重复", "房间类型应为", "选项数应在 1–3", "引用了不存在的选项", "缺 sceneKey", "层号越界");
                check("写错的行要被点名", missing.Count == 0,
                    missing.Count == 0 ? string.Format("抓到 {0} 条", badErrors.Count)
                        : "没点到：" + string.Join("、", missing.ToArray()));

                // 合法的合成内容：一条事件 + 一个选项，按层过滤要能挑出来
                var goodOptions = new List<RoomOptionRow>
                {
                    new RoomOptionRow("OP_OK", "room.op.ok", giveKind: RoomEffectKind.Money, giveAmount: 30),
                };
                var goodEvents = new List<RoomContentRow>
                {
                    new RoomContentRow("EV_L1", ChapterRoomKind.Event, 1, "scene.ok", "text.ok", new[] { "OP_OK" }),
                    new RoomContentRow("EV_ANY", ChapterRoomKind.Event, 0, "scene.ok", "text.ok", new[] { "OP_OK" }),
                };
                var goodErrors = RoomContentCatalog.Validate(goodOptions, goodEvents, new List<RoomContentRow>(), new List<RoomContentRow>());
                var layer1 = RoomContentCatalog.RowsFor(ChapterRoomKind.Event, 1, goodEvents);
                var layer2 = RoomContentCatalog.RowsFor(ChapterRoomKind.Event, 2, goodEvents);
                check("合法内容能过校验并按层过滤",
                    goodErrors.Count == 0 && layer1.Count == 2 && layer2.Count == 1
                    && RoomContentCatalog.RowsFor(ChapterRoomKind.Rest, 1, goodEvents).Count == 0,
                    goodErrors.Count == 0
                        ? string.Format("第 1 层 {0} 条 / 第 2 层 {1} 条", layer1.Count, layer2.Count)
                        : string.Join("；", goodErrors.ToArray()));

                string summary = string.Format("{0}PASS {1} / FAIL {2}（房间内容框架：三张空表 + 效果闸门 + 按层过滤）",
                    Tag, pass, fail);
                for (int i = 0; i < lines.Count; i++) Debug.Log(Tag + lines[i]);
                Debug.Log(summary);
                try
                {
                    Directory.CreateDirectory("Logs");
                    File.WriteAllLines("Logs/room_content_check.txt", lines.ToArray());
                    File.AppendAllText("Logs/room_content_check.txt", summary + Environment.NewLine);
                }
                catch (Exception e)
                {
                    Debug.LogWarning(Tag + "报告写入失败：" + e.Message);
                }
            }
            catch (Exception e)
            {
                fail++;
                lines.Add("FAIL " + "自检异常".PadRight(30) + e.Message);
                Debug.LogError(Tag + "自检异常：" + e);
            }

            if (Application.isBatchMode) EditorApplication.Exit(fail > 0 ? 1 : 0);
        }

        /// <summary>错误清单里没有点到名的关键词</summary>
        private static List<string> Missing(List<string> errors, params string[] keywords)
        {
            string all = errors == null ? string.Empty : string.Join("\n", errors.ToArray());
            var missing = new List<string>();
            for (int i = 0; i < keywords.Length; i++)
            {
                if (all.IndexOf(keywords[i], StringComparison.Ordinal) < 0) missing.Add(keywords[i]);
            }
            return missing;
        }
    }
}
#endif
