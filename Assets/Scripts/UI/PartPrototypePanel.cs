using FallenAngel.Core;
using UnityEngine;

namespace FallenAngel.UI
{
    /// <summary>最小原型验收工具；独立于正式 HUD，不要求重建或改动场景资产。</summary>
    public sealed class PartPrototypePanel : MonoBehaviour
    {
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F8)) StartPrototype();
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
            GUILayout.BeginArea(new Rect(8, 8, 350, 142), GUI.skin.box);
            if (menu)
            {
                if (GUILayout.Button(Loc.T("[Prototype] Guitar / Drums (F8)"), GUILayout.Height(32))) StartPrototype();
            }
            else
            {
                var parts = gm.Parts;
                GUILayout.Label($"{parts.PartName} | {gm.SongTime:F2}s | Energy {parts.Energy:F1} | Hits {parts.HitCount}");
                GUILayout.Label($"D / F / J / K | Energy x{PlayRules.EnergyMultiplier:F1}");
                bool playing = gm.CurrentState == GameState.Playing;
                GUI.enabled = playing;
                if (GUILayout.Button(Loc.T("Switch Guitar / Drums (Tab)"), GUILayout.Height(30))) gm.SwitchPart();
                if (GUILayout.Button(Loc.T("Test item: Energy x2 for 5s"), GUILayout.Height(26)))
                    ModifierManager.EnsureInstance().Apply(new ModifierDef("PART_ENERGY_TEST",
                        ModifierKind.EnergyMultiplier, 2f, 5f, source: ModifierSource.Debug));
                GUI.enabled = true;
                if (gm.CurrentState == GameState.Result && GUILayout.Button(Loc.T("Retry prototype"))) gm.RetryGame();
            }
            GUILayout.EndArea();
            GUI.matrix = previous;
        }
    }
}
