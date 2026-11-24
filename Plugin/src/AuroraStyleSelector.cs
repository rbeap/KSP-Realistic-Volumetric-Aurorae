// Realistic Volumetric Aurorae (RVA). Copyright (c) 2026 HereComesTheBoom ("Realistic Volumetric Aurorae")
// SPDX-License-Identifier: CC-BY-NC-SA-4.0  (see LICENSE.txt)
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using KSP.UI.Screens;
using UnityEngine;

namespace RVA
{
    public class AuroraStyleSelector : MonoBehaviour
    {
        const string NodeName = "RVA_SOL_AURORA";
        const string LockId = "RealisticVolumetricAuroraeSol";
        const int WindowId = 0x52564153;
        const string UranusObservationTitle = "Aurora based on JWST and Hubble observations.";
        const string GenericAuroraDescription = "Same aurora as on other bodies.";
        const string SolAuroraIntro =
            "The aurorae on these bodies are not visible to the naked eye,\nas they're mostly in infrared wavelengths. You can disable them here\nif you'd like a more realistic look.";
        const string UrlumAuroraIntro =
            "Since Urlum is a Uranus analogue, you can choose if you want a\nrealistic aurora like JWST observed on Uranus, or keep the generic\naurora found on other bodies.";

        static readonly string[] SolBodies = { "Jupiter", "Saturn", "Uranus", "Neptune" };
        static readonly Dictionary<string, bool> enabledBodies =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        static bool initialized;
        static bool solDetected;
        static bool opmDetected;
        static bool setupComplete;
        static bool urlumSetupComplete;
        static bool migrateStateToNewPath;
        static bool uranusRefreshOff;

        ApplicationLauncherButton launcherButton;
        Texture2D icon;
        Rect windowRect = new Rect(0f, 0f, 520f, 420f);
        bool windowVisible;
        bool firstRun;
        bool firstRunForUrlum;
        bool positioned;
        bool initialWindowSizeSet;
        bool cameraLocked;
        string saveError;
        Coroutine uranusAppearanceRefresh;
        float contentBottom;
        float closeButtonHeight;

        public static bool SolDetected => solDetected;

        public static bool IsManagedBody(string body)
        {
            if (string.IsNullOrEmpty(body)) return false;
            foreach (string candidate in SolBodies)
                if (string.Equals(body, candidate, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static bool HasPreset(string body) => IsManagedBody(body) && AuroraConfigs.Has(body);

        public static bool IsEnabled(string body)
        {
            if (!IsManagedBody(body) || !solDetected) return true;
            bool enabled = !enabledBodies.TryGetValue(body, out bool configured) || configured;
            return enabled && !(uranusRefreshOff
                && string.Equals(body, "Uranus", StringComparison.OrdinalIgnoreCase));
        }

        public static void Initialize()
        {
            if (initialized) return;
            initialized = true;

            foreach (string body in SolBodies) enabledBodies[body] = true;
            string gameData = Path.Combine(KSPUtil.ApplicationRootPath, "GameData");
            solDetected = Directory.Exists(Path.Combine(gameData, "Sol-Configs"))
                       && Directory.Exists(Path.Combine(gameData, "Sol-Textures"))
                       && Directory.Exists(Path.Combine(gameData, "Sol-Visuals"));
            opmDetected = Directory.Exists(Path.Combine(gameData, "OPM"));

            string path = StatePath;
            if (!File.Exists(path))
            {
                path = LegacyStatePath;
                if (!File.Exists(path)) return;
            }
            bool loadedLegacyPath = string.Equals(path, LegacyStatePath, StringComparison.OrdinalIgnoreCase);
            try
            {
                ConfigNode root = ConfigNode.Load(path);
                ConfigNode node = root != null ? root.GetNode(NodeName) : null;
                if (node == null) throw new InvalidDataException("missing " + NodeName + " node");

                if (bool.TryParse(node.GetValue("setupComplete"), out bool complete))
                    setupComplete = complete;
                if (bool.TryParse(node.GetValue("UrlumSetupComplete"), out bool urlumComplete))
                    urlumSetupComplete = urlumComplete;
                if (bool.TryParse(node.GetValue("UranusGenericMode"), out bool uranusGeneric))
                    AuroraConfigs.SetUranusGenericMode(uranusGeneric);
                else if (bool.TryParse(node.GetValue("uranusPatchMode"), out bool legacyPatches))
                    AuroraConfigs.SetUranusGenericMode(!legacyPatches);

                if (bool.TryParse(node.GetValue("UrlumGenericMode"), out bool urlumGeneric))
                {
                    if (!AuroraConfigs.SetUrlumGenericMode(urlumGeneric))
                        Debug.LogWarning("[RVA] Could not restore the saved Urlum style because the Urlum or Neidon preset is unavailable.");
                }
                else if (bool.TryParse(node.GetValue("opmGenericMode"), out bool legacyOpmGeneric)
                    && legacyOpmGeneric && !AuroraConfigs.SetUrlumGenericMode(true))
                    Debug.LogWarning("[RVA] Could not restore the saved Urlum style because the Urlum or Neidon preset is unavailable.");
                foreach (string body in SolBodies)
                    if (bool.TryParse(node.GetValue(body), out bool enabled))
                        enabledBodies[body] = enabled;
                migrateStateToNewPath = loadedLegacyPath;
            }
            catch (Exception e)
            {
                Debug.LogError($"[RVA] Could not load Sol aurora preferences from {path}: {e.GetType().Name}: {e.Message}");
            }
        }

        static string StatePath => Path.Combine(KSPUtil.ApplicationRootPath,
            "GameData/RealisticVolumetricAurorae/PluginData/AuroraStyle.cfg");

        static string LegacyStatePath => Path.Combine(KSPUtil.ApplicationRootPath,
            "GameData/RealisticVolumetricAurorae/PluginData/sol-aurora.cfg");

        void Start()
        {
            Initialize();
            if (migrateStateToNewPath)
            {
                migrateStateToNewPath = false;
                SaveState();
            }
            if (opmDetected)
            {
                if (!urlumSetupComplete)
                {
                    windowVisible = true;
                    firstRun = true;
                    firstRunForUrlum = true;
                }
            }
            else if (solDetected && !setupComplete)
            {
                windowVisible = true;
                firstRun = true;
            }
        }

        void Update()
        {
            EnsureLauncherButton();
            if (!windowVisible && cameraLocked) Unlock();
        }

        void EnsureLauncherButton()
        {
            if (ApplicationLauncher.Instance == null)
            {
                launcherButton = null;
                return;
            }
            if ((!solDetected && !opmDetected) || launcherButton != null) return;
            var scenes = ApplicationLauncher.AppScenes.FLIGHT
                       | ApplicationLauncher.AppScenes.MAPVIEW
                       | ApplicationLauncher.AppScenes.MAINMENU
                       | ApplicationLauncher.AppScenes.SPACECENTER
                       | ApplicationLauncher.AppScenes.TRACKSTATION
                       | ApplicationLauncher.AppScenes.VAB
                       | ApplicationLauncher.AppScenes.SPH;
            launcherButton = ApplicationLauncher.Instance.AddModApplication(
                OpenWindow, CloseWindow, null, null, null, null, scenes, CreateIcon());
        }

        Texture2D CreateIcon()
        {
            const int size = 32;
            icon = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                name = "RVA_AuroraStyleToolbar",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float nx = (x - 15.5f) / 14f;
                    float ny = (y - 15.5f) / 14f;
                    float radius = Mathf.Sqrt(nx * nx + ny * ny);
                    float arc = Mathf.Abs(ny - (0.32f + 0.24f * Mathf.Cos(nx * 2.8f)));
                    bool glow = radius < 0.94f && arc < 0.11f && Mathf.Abs(nx) < 0.84f;
                    pixels[y * size + x] = glow
                        ? new Color(0.35f + 0.4f * Mathf.Clamp01(ny), 0.85f, 1f, 1f)
                        : new Color(0f, 0f, 0f, 0f);
                }
            icon.SetPixels(pixels);
            icon.Apply(false, true);
            return icon;
        }

        void OpenWindow()
        {
            windowVisible = true;
        }

        void CloseWindow()
        {
            if (firstRun) FinishSetup();
            windowVisible = false;
        }

        void FinishSetup()
        {
            firstRun = false;
            if (firstRunForUrlum)
                urlumSetupComplete = true;
            else
                setupComplete = true;
            SaveState();
        }

        void OnGUI()
        {
            if (!windowVisible || (!solDetected && !opmDetected)) return;
            if (!positioned)
            {
                windowRect.x = (Screen.width - windowRect.width) * 0.5f;
                windowRect.y = (Screen.height - windowRect.height) * 0.5f;
                positioned = true;
            }
            GUI.skin = HighLogic.Skin;
            float windowHeight = windowRect.height;
            Rect drawnRect = GUI.Window(WindowId, windowRect, Draw, "Aurora style selector");
            windowRect.x = drawnRect.x;
            windowRect.y = drawnRect.y;
            windowRect.height = windowHeight;
            if (!initialWindowSizeSet && contentBottom > 0f && closeButtonHeight > 0f)
            {
                float availableHeight = Mathf.Max(1f, Screen.height - windowRect.y - 8f);
                windowRect.height = Mathf.Min(contentBottom + closeButtonHeight + 20f, availableHeight);
                initialWindowSizeSet = true;
            }
            var mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            bool over = windowRect.Contains(mouse);
            if (over && !cameraLocked)
            {
                InputLockManager.SetControlLock(ControlTypes.CAMERACONTROLS, LockId);
                cameraLocked = true;
            }
            else if (!over && cameraLocked) Unlock();
        }

        void Draw(int id)
        {
            bool showUrlumSelector = opmDetected;
            DrawCenteredText(showUrlumSelector ? UrlumAuroraIntro : SolAuroraIntro);
            GUILayout.Space(6f);

            if (solDetected && !showUrlumSelector)
            {
                foreach (string body in SolBodies)
                {
                    bool hasPreset = HasPreset(body);
                    GUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(body, GUILayout.Width(110f));
                    if (hasPreset)
                    {
                        bool enabled = enabledBodies[body];
                        bool next = GUILayout.Toggle(enabled, "Show aurora", GUILayout.Width(140f));
                        if (next != enabled) SetEnabled(body, next);
                    }
                    else GUILayout.Label("Preset not found", GUILayout.Width(140f));
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                }

                if (HasPreset("Uranus") && HasPreset("Neptune") && enabledBodies["Uranus"])
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    GUILayout.Label("Uranus aurora style", GUILayout.Width(130f));
                    int selected = AuroraConfigs.UranusGenericMode ? 1 : 0;
                    int next = GUILayout.SelectionGrid(selected,
                        new[] { "Observation-inspired", "Generic aurora" }, 2, GUILayout.Width(350f));
                    if (next != selected) SetUranusGenericMode(next == 1);
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                    GUILayout.Space(4f);
                    if (!AuroraConfigs.UranusGenericMode)
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.FlexibleSpace();
                        GUILayout.Label(UranusObservationTitle);
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();
                    }
                    else
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.FlexibleSpace();
                        GUILayout.Label(GenericAuroraDescription);
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();
                    }
                }
            }

            if (showUrlumSelector && HasUrlumStyleSource())
            {
                GUILayout.Space(4f);
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.Label("Urlum aurora style", GUILayout.Width(130f));
                int selected = AuroraConfigs.UrlumGenericMode ? 1 : 0;
                int next = GUILayout.SelectionGrid(selected,
                    new[] { "Realistic", "Generic aurora" }, 2, GUILayout.Width(350f));
                if (next != selected) SetUrlumGenericMode(next == 1);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.Space(4f);
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.Label(AuroraConfigs.UrlumGenericMode
                    ? GenericAuroraDescription
                    : "Aurora inspired by JWST's observations on Uranus.");
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            else if (showUrlumSelector)
            {
                GUILayout.Label("Urlum or Neidon aurora preset not found.");
            }

            if (!string.IsNullOrEmpty(saveError))
                GUILayout.Label("Could not save preferences: " + saveError);
            float naturalContentBottom = GUILayoutUtility.GetLastRect().yMax;
            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            bool closePressed = GUILayout.Button(firstRun ? "Done" : "Close", GUILayout.Width(90f));
            float closeHeight = GUILayoutUtility.GetLastRect().height;
            if (closePressed)
            {
                if (firstRun) FinishSetup();
                windowVisible = false;
                if (launcherButton != null) launcherButton.SetFalse();
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (Event.current.type == EventType.Repaint)
            {
                contentBottom = naturalContentBottom;
                closeButtonHeight = closeHeight;
            }
            GUI.DragWindow();
        }

        void DrawCenteredText(string text)
        {
            foreach (string line in text.Split('\n'))
            {
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.Label(line);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
        }

        void SetEnabled(string body, bool enabled)
        {
            enabledBodies[body] = enabled;
            saveError = null;
            SaveState();
            var flight = FindObjectOfType<RVAFlightSetup>();
            if (flight != null) flight.RefreshSolAuroraVisibility();
        }

        void SetUranusGenericMode(bool generic)
        {
            if (!enabledBodies["Uranus"]) return;
            if (!AuroraConfigs.SetUranusGenericMode(generic))
            {
                saveError = "Could not switch Uranus style: Uranus or Neptune preset is missing.";
                return;
            }

            saveError = null;
            SaveState();
            if (uranusAppearanceRefresh == null)
                uranusAppearanceRefresh = StartCoroutine(RefreshUranusByCyclingVisibility());
        }

        static bool HasUrlumStyleSource()
        {
            return AuroraConfigs.Has("Urlum") && AuroraConfigs.Has("Neidon");
        }

        static bool HasUrlumStyleSelector()
        {
            return opmDetected && HasUrlumStyleSource();
        }

        void SetUrlumGenericMode(bool generic)
        {
            if (!AuroraConfigs.SetUrlumGenericMode(generic))
            {
                saveError = "Could not switch Urlum style: Urlum or Neidon preset is missing.";
                return;
            }

            saveError = null;
            SaveState();
            foreach (var flight in FindObjectsOfType<RVAFlightSetup>())
                flight.RefreshUrlumAppearance();
            foreach (var tracking in FindObjectsOfType<RVATrackingSetup>())
                tracking.RefreshUrlumAppearance();
        }

        IEnumerator RefreshUranusByCyclingVisibility()
        {
            uranusRefreshOff = true;
            foreach (var flight in FindObjectsOfType<RVAFlightSetup>())
                flight.RefreshSolAuroraVisibility();
            foreach (var tracking in FindObjectsOfType<RVATrackingSetup>())
                tracking.RefreshUranusAppearance();

            yield return null;

            uranusRefreshOff = false;
            foreach (var flight in FindObjectsOfType<RVAFlightSetup>())
                flight.RefreshUranusAppearance();
            foreach (var tracking in FindObjectsOfType<RVATrackingSetup>())
                tracking.RefreshUranusAppearance();
            uranusAppearanceRefresh = null;
        }

        void SaveState()
        {
            try
            {
                var root = new ConfigNode();
                var node = root.AddNode(NodeName);
                node.AddValue("setupComplete", setupComplete ? "true" : "false");
                node.AddValue("UrlumSetupComplete", urlumSetupComplete ? "true" : "false");
                node.AddValue("UranusGenericMode", AuroraConfigs.UranusGenericMode ? "true" : "false");
                node.AddValue("UrlumGenericMode", AuroraConfigs.UrlumGenericMode ? "true" : "false");
                foreach (string body in SolBodies)
                    node.AddValue(body, enabledBodies[body] ? "true" : "false");
                string path = StatePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                root.Save(path);
                saveError = null;
            }
            catch (Exception e)
            {
                saveError = e.GetType().Name + ": " + e.Message;
                Debug.LogError($"[RVA] Could not save Sol aurora preferences to {StatePath}: {saveError}");
            }
        }

        void Unlock()
        {
            InputLockManager.RemoveControlLock(LockId);
            cameraLocked = false;
        }

        void OnDisable()
        {
            if (cameraLocked) Unlock();
            if (uranusAppearanceRefresh != null)
            {
                StopCoroutine(uranusAppearanceRefresh);
                uranusAppearanceRefresh = null;
                uranusRefreshOff = false;
                foreach (var flight in FindObjectsOfType<RVAFlightSetup>())
                    flight.RefreshUranusAppearance();
            }
            if (launcherButton != null)
            {
                if (ApplicationLauncher.Instance != null)
                    ApplicationLauncher.Instance.RemoveModApplication(launcherButton);
                launcherButton = null;
            }
        }

        void OnDestroy()
        {
            if (icon != null) Destroy(icon);
        }
    }
}
