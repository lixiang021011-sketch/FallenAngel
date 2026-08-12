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

            // Note 组件（长按身体由 Note 运行时自生成渐变图形，预制体不再包含身体节点）
            Note note = root.AddComponent<Note>();
            // 反射设置私有字段
            var f1 = typeof(Note).GetField("noteImage",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            f1?.SetValue(note, noteImg);

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
