using Modding;
using UnityEngine;
using System;
using System.Reflection;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections;
using Satchel;
using MagicUI.Core;
using UnityEngine.UI;
using Newtonsoft.Json;
using Modding.Converters;

namespace RespawnPointManager
{
    using InControl;
    public class RPMActionSet : PlayerActionSet
    {
        public PlayerAction Spawn;
        public PlayerAction Teleport;
        public PlayerAction Next;
        public PlayerAction Clear;

        public RPMActionSet()
        {
            Spawn = CreatePlayerAction("Delete Point / Create Point (tap)");
            Teleport = CreatePlayerAction("Previous Point");
            Next = CreatePlayerAction("Next Point");
            Clear = CreatePlayerAction("Clear All");

            Spawn.AddDefaultBinding(Key.Key3);
            Teleport.AddDefaultBinding(Key.Key1);
            Next.AddDefaultBinding(Key.Key2);
            Clear.AddDefaultBinding(Key.Key4);
        }
    }
    public class GlobalSettings
    {
        public bool ShowCounter = true;
        public int PositionIndex = 2;

        public bool MultiSceneMode = false;

        public bool ManualCheckpointMode = false;

        public bool IgnoreEntryCheckpoint = true;

        [JsonProperty]
        [JsonConverter(typeof(PlayerActionSetConverter))]
        public RPMActionSet Keybinds = new RPMActionSet();
    }

    public struct SpawnPoint
    {
        public Vector3 Position;
        public string SceneName;

        public SpawnPoint(Vector3 position, string sceneName)
        {
            Position = position;
            SceneName = sceneName;
        }
    }

    public class RespawnPointManager : Mod, IGlobalSettings<GlobalSettings>, ICustomMenuMod
    {
        public static RespawnPointManager Instance;
        public override string GetVersion() => "2.5.0";

        public static GlobalSettings Settings { get; set; } = new GlobalSettings();
        public void OnLoadGlobal(GlobalSettings s) => Settings = s;
        public GlobalSettings OnSaveGlobal() => Settings;

        private const float TeleportLiftHeight = 0.3f;
        private const float DuplicatePointRadius = 2f;

        private Sprite _checkpointSprite;
        public static GameObject _hudHazard;
        private Vector3 origpos;

        private List<SpawnPoint> savedSpawns = new List<SpawnPoint>();
        private int currentIndex = -1;
        private Vector3 lastHazardLocation = Vector3.zero;
        private bool isTeleporting = false;
        private float _holdTimer;

        private bool _pendingEntryCheckpoint = false;

        private bool _forceAcceptNextHazard = false;

        private float _blockEntryTimer = 0f;
        public override void Initialize()
        {
            Instance = this;
            _checkpointSprite = LoadSprite();

            On.HeroController.Awake += Awake;
            On.HeroController.Update += OnHeroUpdate;

            UnityEngine.SceneManagement.SceneManager.activeSceneChanged += (oldScene, newScene) => {
                if (!Settings.MultiSceneMode)
                {
                    savedSpawns.Clear();
                    currentIndex = -1;
                }
                else
                {
                    if (currentIndex >= savedSpawns.Count)
                        currentIndex = savedSpawns.Count - 1;
                }

                lastHazardLocation = Vector3.zero;
                _blockEntryTimer = 0.5f;
                _pendingEntryCheckpoint = true;

                UpdateHUD();
                Log($"[HazardSpawnMod] Scene {newScene.name}: {(Settings.MultiSceneMode ? "Multi-scene mode, data kept" : "Data WIPED")}. Entry lock active.");
            };

            On.DisplayItemAmount.OnEnable += (orig, self) => {
                orig(self);
                if (self.playerDataInt == "hazard_counter") UpdateHUD();
            };
        }
        public MenuScreen GetMenuScreen(MenuScreen modListMenu, ModToggleDelegates? toggleDelegates)
        {
            return ConfigurationScreen.GetScreen(modListMenu, Settings);
        }
        public bool ToggleButtonInsideMenu => true;

        public void OnTeleportModeChanged()
        {
            if (!Settings.MultiSceneMode)
            {
                string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                savedSpawns = savedSpawns.Where(p => p.SceneName == scene).ToList();
                if (currentIndex >= savedSpawns.Count) currentIndex = savedSpawns.Count - 1;
                UpdateHUD();
            }
        }

        private void OnHeroUpdate(On.HeroController.orig_Update orig, HeroController self)
        {
            orig(self);
            if (PlayerData.instance == null) return;

            if (_blockEntryTimer > 0)
            {
                _blockEntryTimer -= Time.deltaTime;
                return;
            }

            Vector3 currentHazard = PlayerData.instance.hazardRespawnLocation;

            if (currentHazard != Vector3.zero && currentHazard != lastHazardLocation)
            {
                string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

                bool suppressEntryCheckpoint = Settings.ManualCheckpointMode || Settings.IgnoreEntryCheckpoint;
                bool isEntryCheckpoint = _pendingEntryCheckpoint;
                _pendingEntryCheckpoint = false;

                if (_forceAcceptNextHazard)
                {
                    _forceAcceptNextHazard = false;
                    lastHazardLocation = currentHazard;

                    if (!savedSpawns.Any(p => p.SceneName == scene && Vector3.Distance(p.Position, currentHazard) < DuplicatePointRadius))
                    {
                        AddSpawnPoint(new SpawnPoint(currentHazard, scene));
                    }
                }
                else if (isEntryCheckpoint && suppressEntryCheckpoint)
                {
                    lastHazardLocation = currentHazard;
                }
                else if (Settings.ManualCheckpointMode)
                {
                    HeroController.instance.SetHazardRespawn(lastHazardLocation, false);
                }
                else
                {
                    lastHazardLocation = currentHazard;

                    if (!savedSpawns.Any(p => p.SceneName == scene && Vector3.Distance(p.Position, currentHazard) < DuplicatePointRadius))
                    {
                        AddSpawnPoint(new SpawnPoint(currentHazard, scene));
                    }
                }
            }

            // Управление
            if (!isTeleporting)
            {
                if (Settings.Keybinds.Teleport.WasPressed)
                {
                    TryTeleportOrNavigateBack();
                }

                if (Settings.Keybinds.Next.WasPressed)
                {
                    Navigate(1);
                }

                if (Settings.Keybinds.Spawn.WasPressed)
                {
                    _holdTimer = 0f;
                }

                if (Settings.Keybinds.Spawn.IsPressed)
                {
                    _holdTimer += Time.deltaTime;

                    if (_holdTimer >= 0.7f)
                    {
                        DeleteLast();
                        _holdTimer = -999f;
                    }
                }

                if (Settings.Keybinds.Spawn.WasReleased)
                {
                    if (_holdTimer < 0.7f && _holdTimer > -800f)
                    {
                        CreateSpawnAtPlayer();
                    }

                    _holdTimer = 0f;
                }

                if (Settings.Keybinds.Clear.WasPressed)
                {
                    _holdTimer = 0f;
                }

                if (Settings.Keybinds.Clear.IsPressed)
                {
                    _holdTimer += Time.deltaTime;

                    if (_holdTimer >= 0.7f)
                    {
                        ClearAllData();
                        _holdTimer = -999f;
                    }
                }

                if (Settings.Keybinds.Clear.WasReleased)
                {
                    _holdTimer = 0f;
                }
            }
        }
        private void AddSpawnPoint(SpawnPoint point)
        {
            bool wasAtEnd = currentIndex == savedSpawns.Count - 1;

            savedSpawns.Add(point);

            if (wasAtEnd)
            {
                currentIndex = savedSpawns.Count - 1;
            }

            UpdateHUD();
        }

        private void ForceSaveHazardAtTeleport(SpawnPoint target)
        {
            HeroController.instance.SetHazardRespawn(target.Position, false);
            lastHazardLocation = target.Position;
            _pendingEntryCheckpoint = false;
            _forceAcceptNextHazard = false;
        }

        private void TryTeleportOrNavigateBack()
        {
            if (savedSpawns.Count == 0 || currentIndex < 0)
                return;

            SpawnPoint currentSpawn = savedSpawns[currentIndex];
            string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

            if (currentSpawn.SceneName != currentScene)
            {
                GameManager.instance.StartCoroutine(TeleportRoutine(currentSpawn));
                return;
            }

            Vector3 playerPos = HeroController.instance.transform.position;

            if (Vector3.Distance(playerPos, currentSpawn.Position) > DuplicatePointRadius)
            {
                GameManager.instance.StartCoroutine(TeleportRoutine(currentSpawn));
            }
            else
            {
                Navigate(-1);
            }
        }
        private void DeleteLast()
        {
            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            int lastIndex = savedSpawns.FindLastIndex(p => p.SceneName == scene);

            if (lastIndex < 0)
                return;

            int prevIndexInScene = -1;
            for (int i = lastIndex - 1; i >= 0; i--)
            {
                if (savedSpawns[i].SceneName == scene)
                {
                    prevIndexInScene = i;
                    break;
                }
            }

            if (prevIndexInScene >= 0)
            {
                Vector3 previous = savedSpawns[prevIndexInScene].Position;

                HeroController.instance.SetHazardRespawn(previous, false);

                lastHazardLocation = previous;
            }
            else
            {
                lastHazardLocation = Vector3.zero;

                bool suppressEntryCheckpoint = Settings.ManualCheckpointMode || Settings.IgnoreEntryCheckpoint;
                if (suppressEntryCheckpoint && HeroController.instance != null)
                {
                    HeroController.instance.SetHazardRespawn(Vector3.zero, false);
                }
            }

            _pendingEntryCheckpoint = false;
            _forceAcceptNextHazard = false;

            savedSpawns.RemoveAt(lastIndex);

            if (currentIndex >= savedSpawns.Count)
            {
                currentIndex = savedSpawns.Count - 1;
            }

            UpdateHUD();
        }
        private void ClearAllData()
        {
            savedSpawns.Clear();
            currentIndex = -1;
            lastHazardLocation = Vector3.zero;
            _pendingEntryCheckpoint = false;
            _forceAcceptNextHazard = false;

            bool suppressEntryCheckpoint = Settings.ManualCheckpointMode || Settings.IgnoreEntryCheckpoint;
            if (suppressEntryCheckpoint && HeroController.instance != null)
            {
                HeroController.instance.SetHazardRespawn(Vector3.zero, false);
            }

            if (_hudHazard != null) UpdateHUD();
            Log("All points cleared.");
        }
        private void CreateSpawnAtPlayer()
        {
            Vector3 pos = HeroController.instance.transform.position;

            HeroController.instance.SetHazardRespawn(pos, false);

            _forceAcceptNextHazard = true;
            lastHazardLocation = Vector3.zero;

            Log("Spawn created at " + pos);
        }
        private void Awake(On.HeroController.orig_Awake orig, HeroController self)
        {
            orig(self);
            var hudCanvas = GameObject.Find("_GameCameras").FindGameObjectInChildren("HudCamera").FindGameObjectInChildren("Hud Canvas");
            var prefab = GameManager.instance.inventoryFSM.gameObject.FindGameObjectInChildren("Geo");
            origpos = prefab.transform.position;
            DrawHud(prefab, hudCanvas);
        }

        public void UpdateHUD()
        {
            if (_hudHazard != null)
            {
                int displayCount = savedSpawns.Count;
                int displayIndex = (currentIndex == -1) ? 0 : currentIndex + 1;
                _hudHazard.GetComponent<DisplayItemAmount>().textObject.text = $"{displayIndex} / {displayCount}";
            }
        }

        private Vector2 GetPositionOption()
        {
            return Settings.PositionIndex switch
            {
                0 => new Vector2(-5.0f, 10.4f),  // Screen Edge
                1 => new Vector2(2.2f, 11.3f),   // Beside Geo
                2 => new Vector2(5.0f, 11.3f),   // Far From Geo
                _ => new Vector2(2.2f, 11.3f)
            };
        }

        public void RedrawCounters()
        {
            if (_hudHazard == null) return;
            _hudHazard.SetActive(Settings.ShowCounter);
            var pos = GetPositionOption();
            _hudHazard.transform.position = origpos + new Vector3(pos.x, pos.y);
            UpdateHUD();
        }

        private void Navigate(int direction)
        {
            if (savedSpawns.Count == 0) return;
            int nextIndex = currentIndex + direction;
            if (nextIndex >= 0 && nextIndex < savedSpawns.Count)
            {
                currentIndex = nextIndex;
                UpdateHUD();
                GameManager.instance.StartCoroutine(TeleportRoutine(savedSpawns[currentIndex]));
            }
        }

        private IEnumerator TeleportRoutine(SpawnPoint target)
        {
            isTeleporting = true;

            string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

            if (target.SceneName != currentScene)
            {
                if (!SceneCanBeLoaded(target.SceneName))
                {
                    Log($"[HazardSpawnMod] Teleport aborted: scene '{target.SceneName}' cannot be loaded.");
                    isTeleporting = false;
                    yield break;
                }

                yield return CrossSceneTeleportRoutine(target);
            }
            else
            {
                Rigidbody2D rb = HeroController.instance.GetComponent<Rigidbody2D>();
                if (rb != null) rb.velocity = Vector2.zero;
                HeroController.instance.transform.position = new Vector3(target.Position.x, target.Position.y + TeleportLiftHeight, target.Position.z);
                yield return new WaitForSeconds(0.1f);
                if (rb != null) rb.velocity = Vector2.zero;

                ForceSaveHazardAtTeleport(target);
            }

            isTeleporting = false;
        }

        private bool SceneCanBeLoaded(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return false;
            try
            {
                return Application.CanStreamedLevelBeLoaded(sceneName);
            }
            catch (Exception e)
            {
                Log($"[HazardSpawnMod] Scene check failed for '{sceneName}': {e.Message}");
                return false;
            }
        }

        private IEnumerator CrossSceneTeleportRoutine(SpawnPoint target)
        {
            _blockEntryTimer = 0.5f;

            HeroController.instance.StopAnimationControl();
            Rigidbody2D rb = HeroController.instance.GetComponent<Rigidbody2D>();
            if (rb != null) rb.velocity = Vector2.zero;
            HeroController.instance.RegainControl();

            GameManager.instance.BeginSceneTransition(new GameManager.SceneLoadInfo
            {
                SceneName = target.SceneName,
                EntryGateName = "dreamGate",
                EntryDelay = 0f,
                WaitForSceneTransitionCameraFade = false,
                Visualization = GameManager.SceneLoadVisualizations.Default,
                AlwaysUnloadUnusedAssets = true
            });

            yield return new WaitWhile(() => GameManager.instance.IsInSceneTransition);
            yield return null; // даём сцене осесть кадр перед тем как трогать позицию

            if (rb != null) rb.velocity = Vector2.zero;
            HeroController.instance.transform.position = new Vector3(target.Position.x, target.Position.y + TeleportLiftHeight, target.Position.z);
            if (rb != null) rb.velocity = Vector2.zero;

            HeroController.instance.RegainControl();
            if (GameManager.instance.cameraCtrl != null) GameManager.instance.cameraCtrl.FadeSceneIn();

            ForceSaveHazardAtTeleport(target);
            UpdateHUD();
        }

        private GameObject CreateStatObject(string name, string text, GameObject prefab, Transform parent, Sprite sprite, Vector3 offset)
        {
            var go = UnityEngine.Object.Instantiate(prefab, parent, true);
            go.transform.position = origpos + offset;
            var renderer = go.GetComponent<SpriteRenderer>();
            if (sprite != null) renderer.sprite = sprite;

            var geoAmount = go.FindGameObjectInChildren("Geo Amount");
            if (geoAmount != null) geoAmount.transform.localPosition -= new Vector3(0.3f, 0, 0);

            var component = go.GetComponent<DisplayItemAmount>();
            component.playerDataInt = name;
            component.textObject.text = text;
            component.textObject.fontSize = 4;
            go.SetActive(true);
            return go;
        }

        private void DrawHud(GameObject prefab, GameObject hudCanvas)
        {
            var pos = GetPositionOption();
            _hudHazard = CreateStatObject("hazard_counter", "0 / 0", prefab, hudCanvas.transform, _checkpointSprite, new Vector3(pos.x, pos.y));
            _hudHazard.SetActive(Settings.ShowCounter);
        }

        private Sprite LoadSprite()
        {
            var resource = Assembly.GetExecutingAssembly().GetManifestResourceNames().FirstOrDefault(x => x.EndsWith("checkpoint.png"));
            if (string.IsNullOrEmpty(resource)) return null;
            using (Stream res = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                byte[] buffer = new byte[res.Length];
                res.Read(buffer, 0, buffer.Length);
                var tex = new Texture2D(1, 1);
                tex.LoadImage(buffer, true);
                return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 150f);
            }
        }
    }
}
