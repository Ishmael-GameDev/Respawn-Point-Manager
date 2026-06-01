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

        [JsonProperty]
        [JsonConverter(typeof(PlayerActionSetConverter))]
        public RPMActionSet Keybinds = new RPMActionSet();
    }

    public class RespawnPointManager : Mod, IGlobalSettings<GlobalSettings>, ICustomMenuMod
    {
        public static RespawnPointManager Instance;
        public override string GetVersion() => "2.4.0";

        public static GlobalSettings Settings { get; set; } = new GlobalSettings();
        public void OnLoadGlobal(GlobalSettings s) => Settings = s;
        public GlobalSettings OnSaveGlobal() => Settings;

        private Sprite _checkpointSprite;
        public static GameObject _hudHazard;
        private Vector3 origpos;

        private List<Vector3> savedSpawns = new List<Vector3>();
        private int currentIndex = -1;
        private Vector3 lastHazardLocation = Vector3.zero;
        private bool isTeleporting = false;
        private float _holdTimer;

        private float _blockEntryTimer = 0f;
        public override void Initialize()
        {
            Instance = this;
            _checkpointSprite = LoadSprite();

            On.HeroController.Awake += Awake;
            On.HeroController.Update += OnHeroUpdate;

            UnityEngine.SceneManagement.SceneManager.activeSceneChanged += (oldScene, newScene) => {
                savedSpawns.Clear();
                currentIndex = -1;
                lastHazardLocation = Vector3.zero;
                _blockEntryTimer = 1.5f;

                UpdateHUD();
                Log($"[HazardSpawnMod] Scene {newScene.name}: Data WIPED. Entry lock active.");
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
                lastHazardLocation = currentHazard;

                if (!savedSpawns.Any(p => Vector3.Distance(p, currentHazard) < 0.1f))
                {
                    savedSpawns.Add(currentHazard);
                    currentIndex = savedSpawns.Count - 1;
                    UpdateHUD();
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
        private void TryTeleportOrNavigateBack()
        {
            if (savedSpawns.Count == 0 || currentIndex < 0)
                return;

            Vector3 playerPos = HeroController.instance.transform.position;
            Vector3 currentSpawn = savedSpawns[currentIndex];

            if (Vector3.Distance(playerPos, currentSpawn) > 1f)
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
            if (savedSpawns.Count <= 0)
                return;

            int lastIndex = savedSpawns.Count - 1;

            // Если есть предыдущая точка, делаем ее активной
            if (savedSpawns.Count >= 2)
            {
                Vector3 previous = savedSpawns[lastIndex - 1];

                HeroController.instance.SetHazardRespawn(previous, false);

                lastHazardLocation = previous;
            }
            else
            {
                lastHazardLocation = Vector3.zero;
            }

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
            if (_hudHazard != null) UpdateHUD();
            Log("All points cleared.");
        }
        private void CreateSpawnAtPlayer()
        {
            Vector3 pos = HeroController.instance.transform.position;

            HeroController.instance.SetHazardRespawn(pos, false);

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

        private IEnumerator TeleportRoutine(Vector3 targetPos)
        {
            isTeleporting = true;
            Rigidbody2D rb = HeroController.instance.GetComponent<Rigidbody2D>();
            if (rb != null) rb.velocity = Vector2.zero;
            HeroController.instance.transform.position = new Vector3(targetPos.x, targetPos.y + 0.5f, targetPos.z);
            yield return new WaitForSeconds(0.1f);
            if (rb != null) rb.velocity = Vector2.zero;
            isTeleporting = false;
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

        /*public bool ToggleButtonInsideMenu => true;
        public List<IMenuMod.MenuEntry> GetMenuData(IMenuMod.MenuEntry? toggleButtonEntry)
        {
            return new List<IMenuMod.MenuEntry>
            {
                new IMenuMod.MenuEntry {
                    Name = "Show Counter",
                    Values = new[] { "On", "Off" },
                    Saver = opt => { Settings.ShowCounter = opt == 0; RedrawCounters(); },
                    Loader = () => Settings.ShowCounter ? 0 : 1
                },
                new IMenuMod.MenuEntry {
                    Name = "HUD Position",
                    Values = new[] { "Screen Edge", "Beside Geo", "Far From Geo" },
                    Saver = opt => { Settings.PositionIndex = opt; RedrawCounters(); },
                    Loader = () => Settings.PositionIndex
                }
            };
        }*/
    }
}