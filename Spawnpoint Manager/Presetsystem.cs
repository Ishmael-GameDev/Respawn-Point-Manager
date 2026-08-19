using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;

namespace RespawnPointManager
{
    [Serializable]
    public class SavedPresetPoint
    {
        public string Scene;
        public float X, Y, Z;
    }

    [Serializable]
    public class SavedPreset
    {
        public List<SavedPresetPoint> Points = new List<SavedPresetPoint>();
    }

    public static class PresetManager
    {
        public static string PresetDirectory =>
            Path.Combine(Application.persistentDataPath, "RespawnPointManager", "Presets");

        public static void SaveCurrentPreset(List<SpawnPoint> points)
        {
            if (points == null || points.Count == 0)
            {
                Modding.Logger.Log("[HazardSpawnMod] Nothing to save - point list is empty.");
                return;
            }

            Directory.CreateDirectory(PresetDirectory);

            var preset = new SavedPreset();
            foreach (var p in points)
            {
                preset.Points.Add(new SavedPresetPoint
                {
                    Scene = p.SceneName,
                    X = p.Position.x,
                    Y = p.Position.y,
                    Z = p.Position.z
                });
            }

            string json = JsonConvert.SerializeObject(preset, Formatting.Indented);
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string path = Path.Combine(PresetDirectory, $"preset_{timestamp}.json");

            File.WriteAllText(path, json);

            Modding.Logger.Log($"[HazardSpawnMod] Preset saved to {path}");
        }

        public static List<SpawnPoint> LoadPresetFile(string path)
        {
            if (!File.Exists(path)) return null;

            string json = File.ReadAllText(path);
            var preset = JsonConvert.DeserializeObject<SavedPreset>(json);
            if (preset?.Points == null) return null;

            var result = new List<SpawnPoint>();
            foreach (var p in preset.Points)
            {
                result.Add(new SpawnPoint(new Vector3(p.X, p.Y, p.Z), p.Scene));
            }

            return result;
        }
    }

    public class PresetMenuUI : MonoBehaviour
    {
        private static GameObject root;
        private static Transform panel;
        private static bool opened;

        private static readonly List<GameObject> spawned = new();

        private static string currentPresetPath;

        // Цвета кнопок
        private static readonly Color ButtonColorDefault = new Color(1f, 1f, 1f, 0.06f);
        private static readonly Color ButtonColorActive = new Color(0.35f, 1f, 0.45f, 0.35f);

        public static void Toggle()
        {
            if (root == null)
                CreateUI();

            opened = !opened;
            root.SetActive(opened);

            if (opened)
                RefreshFiles();
        }

        private static void CreateUI()
        {
            root = new GameObject("PresetMenuUI");
            DontDestroyOnLoad(root);

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 9999;

            root.AddComponent<CanvasScaler>()
                .uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

            root.AddComponent<GraphicRaycaster>();

            // DIM
            var dim = new GameObject("Dim");
            dim.transform.SetParent(root.transform, false);

            var dimImg = dim.AddComponent<Image>();
            dimImg.color = new Color(0, 0, 0, 0.45f);

            var dimRt = dim.GetComponent<RectTransform>();
            dimRt.anchorMin = Vector2.zero;
            dimRt.anchorMax = Vector2.one;

            // PANEL
            var p = new GameObject("Panel");
            p.transform.SetParent(dim.transform, false);

            var prt = p.AddComponent<RectTransform>();
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(300, 360);

            var img = p.AddComponent<Image>();
            img.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);

            panel = p.transform;

            CreateText(panel, "TITLE", "PRESETS", new Vector2(0, 150), 16);
        }

        private static void RefreshFiles()
        {
            foreach (var go in spawned)
                if (go) Destroy(go);

            spawned.Clear();

            Directory.CreateDirectory(PresetManager.PresetDirectory);

            var files = Directory.GetFiles(PresetManager.PresetDirectory, "*.json");

            float y = 110f;

            foreach (var file in files)
            {
                string name = Path.GetFileNameWithoutExtension(file);
                bool isActive = string.Equals(file, currentPresetPath, StringComparison.OrdinalIgnoreCase);

                var btn = CreateButton(panel, name, new Vector2(0, y), isActive);
                spawned.Add(btn);

                btn.GetComponent<Button>().onClick.AddListener(() =>
                {
                    LoadFile(file);
                });

                y -= 32f;
            }
        }

        private static void LoadFile(string file)
        {
            var points = PresetManager.LoadPresetFile(file);
            if (points == null) return;

            RespawnPointManager.Instance.LoadPreset(points);

            currentPresetPath = file;
            RefreshFiles();

            Toggle();
        }

        // UI

        private static GameObject CreateButton(Transform parent, string text, Vector2 pos, bool isActive = false)
        {
            var obj = new GameObject(text);
            obj.transform.SetParent(parent, false);

            var rt = obj.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(260, 24);

            var img = obj.AddComponent<Image>();
            img.color = isActive ? ButtonColorActive : ButtonColorDefault;

            var btn = obj.AddComponent<Button>();
            var nav = btn.navigation;
            nav.mode = Navigation.Mode.None;
            btn.navigation = nav;

            CreateText(obj.transform, "t", text, Vector2.zero, 10);

            return obj;
        }

        private static Text CreateText(Transform parent, string name, string text, Vector2 pos, int size)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);

            var rt = obj.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(260, 20);

            var t = obj.AddComponent<Text>();
            t.text = text;
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = size;
            t.color = new Color(1f, 1f, 1f, 0.85f);
            t.alignment = TextAnchor.MiddleCenter;

            return t;
        }
    }
}