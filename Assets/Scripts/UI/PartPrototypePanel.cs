using FallenAngel.Core;
using UnityEngine;

namespace FallenAngel.UI
{
    /// <summary>最小原型验收工具；独立于正式 HUD，不要求重建或改动场景资产。</summary>
    public sealed class PartPrototypePanel : MonoBehaviour
    {
        private PartItem item = PartItem.None;

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F8)) StartPrototype();
            if (Input.GetKeyDown(KeyCode.F9)) item = PartEnergyRules.Next(item);
        }

        private void StartPrototype()
        {
            var starter = FindObjectOfType<GameStarter>(true);
            if (starter != null) starter.StartPartPrototype();
        }

        private void OnGUI()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;
            bool menu = gm.CurrentState == GameState.Menu && gm.Portfolio?.IsOpen != true;
            if (!menu && gm.Parts == null) return;
            Matrix4x4 previous = GUI.matrix;
            float scale = Mathf.Max(.5f, Screen.width / 540f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            GUILayout.BeginArea(new Rect(8, 8, 470, 330), GUI.skin.box);
            if (menu)
            {
                if (GUILayout.Button(Loc.T("[Prototype] Guitar / Drums (F8)"), GUILayout.Height(32))) StartPrototype();
                GUILayout.Label("原型项：A 路线记录 + B 长音 2× + C 声部道具 + D 非对称 demo");
            }
            else
            {
                var parts = gm.Parts;
                if (parts.Item != item) parts.Item = item;
                float stay = parts.SamePartSeconds(gm.SongTime);
                GUILayout.Label($"{parts.PartName} | {gm.SongTime:F2}s | Energy {parts.Energy:F1} | Hits {parts.HitCount}");
                GUILayout.Label($"D / F / J / K | Energy x{PlayRules.EnergyMultiplier:F1}");
                GUILayout.Label($"[C] {PartEnergyRules.DisplayName(item)}（F9 或按钮切换）");
                if (item == PartItem.StayRamp)
                    GUILayout.Label($"留守 {stay:F1}s → 本颗 x{PartEnergyRules.StayMultiplier(item, stay):F2}");
                else if (item == PartItem.SwitchBonus)
                    GUILayout.Label($"换手后第 {parts.NotesSinceSwitch + 1} 颗 → x{PartEnergyRules.SwitchMultiplier(item, parts.NotesSinceSwitch):F2}");
                bool playing = gm.CurrentState == GameState.Playing;
                GUI.enabled = playing;
                if (GUILayout.Button(Loc.T("Switch Guitar / Drums (Tab)"), GUILayout.Height(30))) gm.SwitchPart();
                if (GUILayout.Button(Loc.T("Cycle part item (F9)"), GUILayout.Height(26))) item = PartEnergyRules.Next(item);
                if (GUILayout.Button(Loc.T("Test modifier: Energy x2 for 5s"), GUILayout.Height(24)))
                    ModifierManager.EnsureInstance().Apply(new ModifierDef("PART_ENERGY_TEST",
                        ModifierKind.EnergyMultiplier, 2f, 5f, source: ModifierSource.Debug));
                GUI.enabled = true;
                if (gm.CurrentState == GameState.Result)
                {
                    GUILayout.Label("路线（A）：" + parts.Route.RouteLine());
                    GUILayout.Label(parts.Route.Summary());
                }
                if (gm.CurrentState == GameState.Result && GUILayout.Button(Loc.T("Retry prototype"))) gm.RetryGame();
            }
            GUILayout.EndArea();
            GUI.matrix = previous;
        }
    }
}
