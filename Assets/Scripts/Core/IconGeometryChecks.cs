#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using FallenAngel.UI;
using UnityEditor;
using UnityEngine;

namespace FallenAngel.Core
{
    /// <summary>
    /// 图标几何自检：按游戏里真实的宿主尺寸逐个形状重建网格，
    /// 检查「墨迹包围盒中心是否等于矩形中心」（错位）与「是否溢出矩形」（跑出按钮）。
    /// 只读，不改场景与资源；菜单与 batchmode（-executeMethod FallenAngel.Core.IconGeometryChecks.Run）两用。
    /// </summary>
    public static class IconGeometryChecks
    {
        private sealed class Case
        {
            public DeepSeaGraphic.Shape Shape;
            public float W, H;
            public string Where;
            public Case(DeepSeaGraphic.Shape shape, float w, float h, string where) { Shape = shape; W = w; H = h; Where = where; }
        }

        private static readonly List<Case> Cases = new List<Case>
        {
            new Case(DeepSeaGraphic.Shape.Close, 170, 80, "面板关闭按钮(SceneBuilder 170x80)"),
            new Case(DeepSeaGraphic.Shape.Close, 96, 96, "商店/装备关闭按钮"),
            new Case(DeepSeaGraphic.Shape.Close, 80, 70, "地图抽屉关闭按钮"),
            new Case(DeepSeaGraphic.Shape.Pause, 100, 80, "暂停按钮"),
            new Case(DeepSeaGraphic.Shape.Beacon, 82, 82, "地图房间·起点"),
            new Case(DeepSeaGraphic.Shape.Battle, 82, 82, "地图房间·战斗"),
            new Case(DeepSeaGraphic.Shape.Shop, 82, 82, "地图房间·商店"),
            new Case(DeepSeaGraphic.Shape.Empty, 82, 82, "地图房间·空房"),
            new Case(DeepSeaGraphic.Shape.Final, 82, 82, "地图房间·终点"),
            new Case(DeepSeaGraphic.Shape.Battle, 45, 55, "RoomIcon 默认尺寸"),
            new Case(DeepSeaGraphic.Shape.Equipment, 90, 80, "商店格装备符号"),
            new Case(DeepSeaGraphic.Shape.Equipment, 55, 80, "装备格符号"),
            new Case(DeepSeaGraphic.Shape.Coin, 30, 30, "现金胶囊硬币"),
            new Case(DeepSeaGraphic.Shape.Growth, 40, 40, "积分行图标"),
            new Case(DeepSeaGraphic.Shape.Position, 34, 25, "地图当前位置标记"),
            new Case(DeepSeaGraphic.Shape.Check, 22, 26, "地图已通过角标"),
            new Case(DeepSeaGraphic.Shape.Lock, 22, 26, "地图锁定角标"),
            new Case(DeepSeaGraphic.Shape.Talent, 36, 36, "地图天赋入口"),
            new Case(DeepSeaGraphic.Shape.Info, 36, 36, "按钮图标·信息"),
            new Case(DeepSeaGraphic.Shape.Refresh, 36, 36, "按钮图标·刷新"),
            new Case(DeepSeaGraphic.Shape.Back, 36, 36, "按钮图标·返回"),
            new Case(DeepSeaGraphic.Shape.Play, 36, 36, "按钮图标·播放"),
            new Case(DeepSeaGraphic.Shape.Chevron, 36, 36, "按钮图标·指示"),
            new Case(DeepSeaGraphic.Shape.Reward, 34, 34, "天赋语义·奖励"),
            new Case(DeepSeaGraphic.Shape.Amplify, 34, 34, "天赋语义·增幅"),
            new Case(DeepSeaGraphic.Shape.Floor, 34, 34, "天赋语义·保底"),
            new Case(DeepSeaGraphic.Shape.Upgrade, 34, 34, "天赋语义·升级"),
        };

        [MenuItem("Tools/FallenAngel/Validate Icon Geometry")]
        public static void Run()
        {
            var lines = new List<string>();
            int pass = 0, fail = 0;
            foreach (var c in Cases)
            {
                var go = new GameObject("IconCheck", typeof(RectTransform), typeof(CanvasRenderer), typeof(DeepSeaGraphic));
                var rt = (RectTransform)go.transform;
                rt.sizeDelta = new Vector2(c.W, c.H);
                var graphic = go.GetComponent<DeepSeaGraphic>();
                graphic.shape = c.Shape;
                Rect bounds = graphic.GetMeshBounds();
                Rect rect = rt.rect;
                float offset = bounds.width > 0f || bounds.height > 0f ? Vector2.Distance(bounds.center, rect.center) : float.NaN;
                bool centered = !float.IsNaN(offset) && offset <= 1.0f;
                bool inside = bounds.xMin >= rect.xMin - 1f && bounds.xMax <= rect.xMax + 1f
                              && bounds.yMin >= rect.yMin - 1f && bounds.yMax <= rect.yMax + 1f;
                bool ok = centered && inside;
                if (ok) pass++; else fail++;
                lines.Add(string.Format("{0} {1,-11} {2,6:0}x{3,-5:0} 中心偏移={4,6:0.00}px 墨迹={5:0.#}x{6:0.#} {7}",
                    ok ? "PASS" : "FAIL", c.Shape, c.W, c.H, offset, bounds.width, bounds.height, c.Where));
                UnityEngine.Object.DestroyImmediate(go);
            }
            string summary = string.Format("图标几何自检：{0} 项通过 / {1} 项异常（判定：中心偏移 ≤1px 且不溢出矩形）", pass, fail);
            foreach (var line in lines) Debug.Log("[IconGeometry] " + line);
            Debug.Log("[IconGeometry] " + summary);
            try
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllLines("Logs/icon_geometry_check.txt", lines.ToArray());
                File.AppendAllText("Logs/icon_geometry_check.txt", summary + Environment.NewLine);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[IconGeometry] 报告写入失败：" + e.Message);
            }
        }
    }
}
#endif
