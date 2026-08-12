using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
using System.IO;
#endif

namespace FallenAngel.Gameplay
{
    /// <summary>
    /// Note Prefab 自动创建器（Editor Only）
    /// 菜单: Tools > FallenAngel > Create Note Prefab
    /// </summary>
    public static class NotePrefabBuilder
    {
#if UNITY_EDITOR
        [MenuItem("Tools/FallenAngel/Create Note Prefab")]
        public static void CreateNotePrefab()
        {
            string folder = "Assets/Prefabs";
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            string path = $"{folder}/Note.prefab";

            // 根对象
            GameObject root = new GameObject("Note");
            RectTransform rootRT = root.AddComponent<RectTransform>();
            rootRT.sizeDelta = new Vector2(120, 120);

            // 音符主体图像
            GameObject noteImgGO = new GameObject("NoteImage", typeof(RectTransform), typeof(Image));
            noteImgGO.transform.SetParent(root.transform, false);
            RectTransform imgRT = (RectTransform)noteImgGO.transform;
            imgRT.anchorMin = Vector2.zero;
            imgRT.anchorMax = Vector2.one;
            imgRT.offsetMin = Vector2.zero;
            imgRT.offsetMax = Vector2.zero;
            Image noteImg = noteImgGO.GetComponent<Image>();
            noteImg.color = Color.white;

            // 长按音符身体（在NoteImage之后，渲染层级更高）
            GameObject bodyGO = new GameObject("LongNoteBody", typeof(RectTransform), typeof(Image));
            bodyGO.transform.SetParent(root.transform, false);
            RectTransform bodyRT = (RectTransform)bodyGO.transform;
            bodyRT.anchorMin = new Vector2(0.5f, 0.5f);
            bodyRT.anchorMax = new Vector2(0.5f, 0.5f);
            bodyRT.pivot = new Vector2(0.5f, 0f);
            bodyRT.anchoredPosition = Vector2.zero;
            bodyRT.sizeDelta = new Vector2(80, 300);
            bodyRT.SetAsFirstSibling(); // 在NoteImage后面（实际上应该先画身体再画头）
            Image bodyImg = bodyGO.GetComponent<Image>();
            bodyImg.color = new Color(1, 1, 1, 0.5f);

            // Note 组件
            Note note = root.AddComponent<Note>();
            // 反射设置私有字段
            var f1 = typeof(Note).GetField("noteImage",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            f1?.SetValue(note, noteImg);
            var f2 = typeof(Note).GetField("longNoteBodyImage",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            f2?.SetValue(note, bodyImg);
            var f3 = typeof(Note).GetField("bodyRect",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            f3?.SetValue(note, bodyRT);

            // 保存Prefab
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);

            Debug.Log($"[NotePrefabBuilder] Prefab 已创建: {path}");

            // 自动查找场景中的NoteSpawner并赋值
            NoteSpawner spawner = Object.FindObjectOfType<NoteSpawner>();
            if (spawner != null && prefab != null)
            {
                var f4 = typeof(NoteSpawner).GetField("notePrefab",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                f4?.SetValue(spawner, prefab.GetComponent<Note>());
                EditorUtility.SetDirty(spawner);
                Debug.Log("[NotePrefabBuilder] 已自动赋值给场景中的 NoteSpawner");
            }

            EditorUtility.DisplayDialog("FallenAngel",
                $"Note Prefab 已创建到:\n{path}\n\n如果场景中有 NoteSpawner，已自动赋值。",
                "OK");
        }
#endif
    }
}
