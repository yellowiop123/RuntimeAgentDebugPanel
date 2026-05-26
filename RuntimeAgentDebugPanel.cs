using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

public class RuntimeAgentDebugPanel : MonoBehaviour
{
    private enum PanelTab
    {
        Inspector,
        Logs,
        Metrics,
        Commands
    }

    private struct RuntimeLog
    {
        public string message;
        public string stackTrace;
        public LogType type;
        public float time;
    }

    private class ObjectSnapshot
    {
        public GameObject gameObject;
        public string path;
        public int instanceId;
    }

    [Header("Runtime Agent Debug Panel")]
    [SerializeField] private KeyCode toggleKey = KeyCode.F1;
    [SerializeField] private bool visibleOnStart = true;
    [SerializeField] private bool dontDestroyOnLoad = true;
    [SerializeField] private int maxLogCount = 256;
    [SerializeField] private float objectRefreshInterval = 1.0f;
    [SerializeField] private bool includeInactiveObjects = true;

    private bool isVisible;
    private bool isPaused;
    private PanelTab currentTab = PanelTab.Inspector;

    private Rect windowRect = new Rect(24, 24, 980, 640);
    private Vector2 objectScroll;
    private Vector2 detailsScroll;
    private Vector2 logScroll;
    private Vector2 metricsScroll;
    private Vector2 commandScroll;

    private readonly List<ObjectSnapshot> objectSnapshots = new List<ObjectSnapshot>();
    private readonly List<RuntimeLog> logs = new List<RuntimeLog>();
    private readonly Dictionary<LogType, int> logCounters = new Dictionary<LogType, int>();

    private GameObject selectedObject;
    private string searchText = "";
    private string commandInput = "";
    private string selectedObjectPath = "";

    private float lastObjectRefreshTime;
    private float deltaTime;
    private float fps;
    private float memoryMb;
    private float timeScaleBeforePause = 1.0f;

    private GUIStyle headerStyle;
    private GUIStyle smallLabelStyle;
    private GUIStyle toolbarButtonStyle;
    private GUIStyle boxStyle;
    private GUIStyle logInfoStyle;
    private GUIStyle logWarningStyle;
    private GUIStyle logErrorStyle;

    private readonly StringBuilder stringBuilder = new StringBuilder(256);

    private void Awake()
    {
        isVisible = visibleOnStart;

        if (dontDestroyOnLoad)
        {
            DontDestroyOnLoad(gameObject);
        }

        RefreshSceneObjects(true);
    }

    private void OnEnable()
    {
        Application.logMessageReceived += HandleLogMessage;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDisable()
    {
        Application.logMessageReceived -= HandleLogMessage;
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            isVisible = !isVisible;
        }

        deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;
        fps = deltaTime > 0.0001f ? 1.0f / deltaTime : 0.0f;
        memoryMb = GC.GetTotalMemory(false) / 1024.0f / 1024.0f;

        if (Time.unscaledTime - lastObjectRefreshTime > objectRefreshInterval)
        {
            RefreshSceneObjects(false);
        }
    }

    private void OnGUI()
    {
        if (!isVisible)
        {
            DrawCollapsedHint();
            return;
        }

        InitializeStyles();
        windowRect = GUI.Window(GetInstanceID(), windowRect, DrawWindow, "Runtime Agent Debug Panel");
    }

    private void DrawCollapsedHint()
    {
        GUI.Box(new Rect(16, 16, 260, 32), "Runtime Agent Panel hidden. Press F1.");
    }

    private void DrawWindow(int windowId)
    {
        DrawTopBar();
        DrawTabs();

        GUILayout.Space(6);

        switch (currentTab)
        {
            case PanelTab.Inspector:
                DrawInspectorTab();
                break;
            case PanelTab.Logs:
                DrawLogsTab();
                break;
            case PanelTab.Metrics:
                DrawMetricsTab();
                break;
            case PanelTab.Commands:
                DrawCommandsTab();
                break;
        }

        GUI.DragWindow(new Rect(0, 0, windowRect.width, 24));
    }

    private void DrawTopBar()
    {
        GUILayout.BeginHorizontal(boxStyle, GUILayout.Height(48));

        GUILayout.Label("Agent Runtime Monitor", headerStyle, GUILayout.Width(220));
        GUILayout.Label("FPS: " + fps.ToString("F1"), smallLabelStyle, GUILayout.Width(90));
        GUILayout.Label("Memory: " + memoryMb.ToString("F1") + " MB", smallLabelStyle, GUILayout.Width(150));
        GUILayout.Label("Scene: " + SceneManager.GetActiveScene().name, smallLabelStyle, GUILayout.Width(180));
        GUILayout.Label("Objects: " + objectSnapshots.Count, smallLabelStyle, GUILayout.Width(110));
        GUILayout.Label("Time: " + Time.time.ToString("F2"), smallLabelStyle, GUILayout.Width(110));

        GUILayout.FlexibleSpace();

        if (GUILayout.Button(isPaused ? "Resume" : "Pause", toolbarButtonStyle, GUILayout.Width(86)))
        {
            TogglePause();
        }

        if (GUILayout.Button("Refresh", toolbarButtonStyle, GUILayout.Width(86)))
        {
            RefreshSceneObjects(true);
        }

        if (GUILayout.Button("Hide", toolbarButtonStyle, GUILayout.Width(70)))
        {
            isVisible = false;
        }

        GUILayout.EndHorizontal();
    }

    private void DrawTabs()
    {
        GUILayout.BeginHorizontal();

        DrawTabButton(PanelTab.Inspector, "Inspector");
        DrawTabButton(PanelTab.Logs, "Logs");
        DrawTabButton(PanelTab.Metrics, "Metrics");
        DrawTabButton(PanelTab.Commands, "Commands");

        GUILayout.FlexibleSpace();

        GUILayout.EndHorizontal();
    }

    private void DrawTabButton(PanelTab tab, string label)
    {
        Color oldColor = GUI.color;

        if (currentTab == tab)
        {
            GUI.color = new Color(0.55f, 0.85f, 1.0f, 1.0f);
        }

        if (GUILayout.Button(label, toolbarButtonStyle, GUILayout.Width(120), GUILayout.Height(28)))
        {
            currentTab = tab;
        }

        GUI.color = oldColor;
    }

    private void DrawInspectorTab()
    {
        GUILayout.BeginHorizontal();

        DrawObjectListPanel();
        DrawObjectDetailsPanel();

        GUILayout.EndHorizontal();
    }

    private void DrawObjectListPanel()
    {
        GUILayout.BeginVertical(boxStyle, GUILayout.Width(350), GUILayout.ExpandHeight(true));

        GUILayout.Label("Scene Object Index", headerStyle);

        GUILayout.BeginHorizontal();
        GUILayout.Label("Search", GUILayout.Width(56));
        searchText = GUILayout.TextField(searchText);
        if (GUILayout.Button("X", GUILayout.Width(28)))
        {
            searchText = "";
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(4);

        objectScroll = GUILayout.BeginScrollView(objectScroll);

        for (int i = 0; i < objectSnapshots.Count; i++)
        {
            ObjectSnapshot snapshot = objectSnapshots[i];

            if (snapshot.gameObject == null)
            {
                continue;
            }

            if (!PassesSearchFilter(snapshot))
            {
                continue;
            }

            Color oldColor = GUI.color;

            if (snapshot.gameObject == selectedObject)
            {
                GUI.color = new Color(0.45f, 0.75f, 1.0f, 1.0f);
            }
            else if (!snapshot.gameObject.activeInHierarchy)
            {
                GUI.color = new Color(0.65f, 0.65f, 0.65f, 1.0f);
            }

            string label = snapshot.gameObject.activeInHierarchy ? snapshot.path : "[Inactive] " + snapshot.path;

            if (GUILayout.Button(label, GUILayout.Height(24)))
            {
                SelectObject(snapshot.gameObject, snapshot.path);
            }

            GUI.color = oldColor;
        }

        GUILayout.EndScrollView();
        GUILayout.EndVertical();
    }

    private void DrawObjectDetailsPanel()
    {
        GUILayout.BeginVertical(boxStyle, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

        GUILayout.Label("Runtime Object Inspector", headerStyle);

        if (selectedObject == null)
        {
            GUILayout.Space(16);
            GUILayout.Label("Select a GameObject from the scene index.");
            GUILayout.EndVertical();
            return;
        }

        detailsScroll = GUILayout.BeginScrollView(detailsScroll);

        DrawSelectedObjectHeader();
        GUILayout.Space(8);
        DrawTransformEditor();
        GUILayout.Space(8);
        DrawComponentSummary();
        GUILayout.Space(8);
        DrawQuickActions();

        GUILayout.EndScrollView();
        GUILayout.EndVertical();
    }

    private void DrawSelectedObjectHeader()
    {
        GUILayout.BeginVertical(boxStyle);

        GUILayout.Label("Selected Object", headerStyle);
        GUILayout.Label("Name: " + selectedObject.name);
        GUILayout.Label("Path: " + selectedObjectPath);
        GUILayout.Label("Instance ID: " + selectedObject.GetInstanceID());
        GUILayout.Label("Tag: " + selectedObject.tag);
        GUILayout.Label("Layer: " + LayerMask.LayerToName(selectedObject.layer) + " (" + selectedObject.layer + ")");
        GUILayout.Label("Active Self: " + selectedObject.activeSelf);
        GUILayout.Label("Active In Hierarchy: " + selectedObject.activeInHierarchy);

        GUILayout.EndVertical();
    }

    private void DrawTransformEditor()
    {
        Transform target = selectedObject.transform;

        GUILayout.BeginVertical(boxStyle);

        GUILayout.Label("Transform Runtime Editor", headerStyle);

        Vector3 position = DrawVector3Field("Position", target.position);
        Vector3 rotation = DrawVector3Field("Rotation", target.eulerAngles);
        Vector3 scale = DrawVector3Field("Scale", target.localScale);

        if (GUI.changed)
        {
            target.position = position;
            target.eulerAngles = rotation;
            target.localScale = scale;
        }

        GUILayout.EndVertical();
    }

    private Vector3 DrawVector3Field(string label, Vector3 value)
    {
        GUILayout.BeginHorizontal();

        GUILayout.Label(label, GUILayout.Width(80));

        GUILayout.Label("X", GUILayout.Width(14));
        value.x = DrawFloatField(value.x, GUILayout.Width(82));

        GUILayout.Label("Y", GUILayout.Width(14));
        value.y = DrawFloatField(value.y, GUILayout.Width(82));

        GUILayout.Label("Z", GUILayout.Width(14));
        value.z = DrawFloatField(value.z, GUILayout.Width(82));

        GUILayout.EndHorizontal();

        return value;
    }

    private float DrawFloatField(float value, params GUILayoutOption[] options)
    {
        string text = GUILayout.TextField(value.ToString("F3"), options);

        float result;
        if (float.TryParse(text, out result))
        {
            return result;
        }

        return value;
    }

    private void DrawComponentSummary()
    {
        GUILayout.BeginVertical(boxStyle);

        GUILayout.Label("Component Stack", headerStyle);

        Component[] components = selectedObject.GetComponents<Component>();
        for (int i = 0; i < components.Length; i++)
        {
            Component component = components[i];

            if (component == null)
            {
                GUILayout.Label(i + ". Missing Script");
            }
            else
            {
                GUILayout.Label(i + ". " + component.GetType().Name);
            }
        }

        GUILayout.EndVertical();
    }

    private void DrawQuickActions()
    {
        GUILayout.BeginVertical(boxStyle);

        GUILayout.Label("Quick Runtime Actions", headerStyle);

        GUILayout.BeginHorizontal();

        if (GUILayout.Button("Reset Transform", GUILayout.Height(30)))
        {
            UndoLikeResetTransform(selectedObject.transform);
        }

        if (GUILayout.Button("Teleport To Origin", GUILayout.Height(30)))
        {
            selectedObject.transform.position = Vector3.zero;
        }

        if (GUILayout.Button(selectedObject.activeSelf ? "Set Inactive" : "Set Active", GUILayout.Height(30)))
        {
            selectedObject.SetActive(!selectedObject.activeSelf);
            RefreshSceneObjects(true);
        }

        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();

        if (GUILayout.Button("Copy Object Report To Console", GUILayout.Height(30)))
        {
            Debug.Log(BuildObjectReport(selectedObject));
        }

        if (GUILayout.Button("Focus Main Camera On Object", GUILayout.Height(30)))
        {
            FocusMainCameraOnSelectedObject();
        }

        GUILayout.EndHorizontal();

        GUILayout.EndVertical();
    }

    private void DrawLogsTab()
    {
        GUILayout.BeginVertical(boxStyle);

        GUILayout.BeginHorizontal();

        GUILayout.Label("Runtime Log Stream", headerStyle, GUILayout.Width(220));

        GUILayout.Label("Info: " + GetLogCount(LogType.Log), GUILayout.Width(90));
        GUILayout.Label("Warning: " + GetLogCount(LogType.Warning), GUILayout.Width(110));
        GUILayout.Label("Error: " + GetLogCount(LogType.Error), GUILayout.Width(100));
        GUILayout.Label("Exception: " + GetLogCount(LogType.Exception), GUILayout.Width(120));

        GUILayout.FlexibleSpace();

        if (GUILayout.Button("Clear Logs", toolbarButtonStyle, GUILayout.Width(100)))
        {
            logs.Clear();
            logCounters.Clear();
        }

        GUILayout.EndHorizontal();

        logScroll = GUILayout.BeginScrollView(logScroll);

        for (int i = logs.Count - 1; i >= 0; i--)
        {
            RuntimeLog entry = logs[i];
            GUIStyle style = GetLogStyle(entry.type);

            GUILayout.BeginVertical(boxStyle);
            GUILayout.Label("[" + entry.time.ToString("F2") + "] [" + entry.type + "] " + entry.message, style);

            if (!string.IsNullOrEmpty(entry.stackTrace) && entry.type != LogType.Log)
            {
                GUILayout.TextArea(entry.stackTrace, GUILayout.MinHeight(42));
            }

            GUILayout.EndVertical();
        }

        GUILayout.EndScrollView();

        GUILayout.EndVertical();
    }

    private void DrawMetricsTab()
    {
        GUILayout.BeginVertical(boxStyle);

        GUILayout.Label("Runtime Metrics Dashboard", headerStyle);

        metricsScroll = GUILayout.BeginScrollView(metricsScroll);

        DrawMetricLine("Application", Application.productName);
        DrawMetricLine("Unity Version", Application.unityVersion);
        DrawMetricLine("Platform", Application.platform.ToString());
        DrawMetricLine("System Language", Application.systemLanguage.ToString());
        DrawMetricLine("Target Frame Rate", Application.targetFrameRate.ToString());
        DrawMetricLine("VSync Count", QualitySettings.vSyncCount.ToString());
        DrawMetricLine("Quality Level", QualitySettings.names[QualitySettings.GetQualityLevel()]);
        DrawMetricLine("Screen", Screen.width + " x " + Screen.height);
        DrawMetricLine("DPI", Screen.dpi.ToString("F1"));
        DrawMetricLine("Scene", SceneManager.GetActiveScene().name);
        DrawMetricLine("Loaded Scene Count", SceneManager.sceneCount.ToString());
        DrawMetricLine("Time Scale", Time.timeScale.ToString("F3"));
        DrawMetricLine("Unscaled Time", Time.unscaledTime.ToString("F3"));
        DrawMetricLine("Realtime Since Startup", Time.realtimeSinceStartup.ToString("F3"));
        DrawMetricLine("FPS", fps.ToString("F1"));
        DrawMetricLine("Managed Memory", memoryMb.ToString("F2") + " MB");
        DrawMetricLine("Total Indexed Objects", objectSnapshots.Count.ToString());
        DrawMetricLine("Log Count", logs.Count.ToString());

        GUILayout.Space(10);
        GUILayout.Label("Scene Composition", headerStyle);
        DrawSceneComposition();

        GUILayout.EndScrollView();
        GUILayout.EndVertical();
    }

    private void DrawMetricLine(string key, string value)
    {
        GUILayout.BeginHorizontal(boxStyle);
        GUILayout.Label(key, GUILayout.Width(220));
        GUILayout.Label(value);
        GUILayout.EndHorizontal();
    }

    private void DrawSceneComposition()
    {
        int activeCount = 0;
        int inactiveCount = 0;
        int cameraCount = 0;
        int lightCount = 0;
        int rendererCount = 0;
        int colliderCount = 0;
        int rigidbodyCount = 0;

        for (int i = 0; i < objectSnapshots.Count; i++)
        {
            GameObject go = objectSnapshots[i].gameObject;

            if (go == null)
            {
                continue;
            }

            if (go.activeInHierarchy)
            {
                activeCount++;
            }
            else
            {
                inactiveCount++;
            }

            if (go.GetComponent<Camera>() != null)
            {
                cameraCount++;
            }

            if (go.GetComponent<Light>() != null)
            {
                lightCount++;
            }

            if (go.GetComponent<Renderer>() != null)
            {
                rendererCount++;
            }

            if (go.GetComponent<Collider>() != null)
            {
                colliderCount++;
            }

            if (go.GetComponent<Rigidbody>() != null)
            {
                rigidbodyCount++;
            }
        }

        DrawMetricLine("Active Objects", activeCount.ToString());
        DrawMetricLine("Inactive Objects", inactiveCount.ToString());
        DrawMetricLine("Cameras", cameraCount.ToString());
        DrawMetricLine("Lights", lightCount.ToString());
        DrawMetricLine("Renderers", rendererCount.ToString());
        DrawMetricLine("Colliders", colliderCount.ToString());
        DrawMetricLine("Rigidbodies", rigidbodyCount.ToString());
    }

    private void DrawCommandsTab()
    {
        GUILayout.BeginVertical(boxStyle);

        GUILayout.Label("Agent Command Console", headerStyle);
        GUILayout.Label("Supported commands: help, stats, scene, select <name>, log <message>, timescale <value>, screenshot");

        commandScroll = GUILayout.BeginScrollView(commandScroll);

        GUILayout.BeginHorizontal();
        commandInput = GUILayout.TextField(commandInput, GUILayout.Height(28));

        if (GUILayout.Button("Run", toolbarButtonStyle, GUILayout.Width(80), GUILayout.Height(28)))
        {
            ExecuteCommand(commandInput);
            commandInput = "";
        }

        GUILayout.EndHorizontal();

        GUILayout.Space(8);

        GUILayout.BeginHorizontal();

        if (GUILayout.Button("help", GUILayout.Height(30)))
        {
            ExecuteCommand("help");
        }

        if (GUILayout.Button("stats", GUILayout.Height(30)))
        {
            ExecuteCommand("stats");
        }

        if (GUILayout.Button("scene", GUILayout.Height(30)))
        {
            ExecuteCommand("scene");
        }

        if (GUILayout.Button("screenshot", GUILayout.Height(30)))
        {
            ExecuteCommand("screenshot");
        }

        GUILayout.EndHorizontal();

        GUILayout.Space(12);

        GUILayout.Label("This panel is designed as a runtime production-debug bridge.");
        GUILayout.Label("It can inspect scene state without stopping Play Mode.");
        GUILayout.Label("Use it for level validation, gameplay tuning, and prototype diagnostics.");

        GUILayout.EndScrollView();
        GUILayout.EndVertical();
    }

    private void ExecuteCommand(string rawCommand)
    {
        if (string.IsNullOrWhiteSpace(rawCommand))
        {
            return;
        }

        string command = rawCommand.Trim();
        string[] parts = command.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
        string keyword = parts[0].ToLowerInvariant();
        string arg = parts.Length > 1 ? parts[1] : "";

        switch (keyword)
        {
            case "help":
                Debug.Log("RuntimeAgentDebugPanel commands: help, stats, scene, select <name>, log <message>, timescale <value>, screenshot");
                break;

            case "stats":
                Debug.Log("Runtime stats: fps=" + fps.ToString("F1") + ", memory=" + memoryMb.ToString("F2") + "MB, objects=" + objectSnapshots.Count + ", logs=" + logs.Count);
                break;

            case "scene":
                Debug.Log("Active scene: " + SceneManager.GetActiveScene().name + ", loaded scenes=" + SceneManager.sceneCount);
                break;

            case "select":
                SelectObjectByName(arg);
                break;

            case "log":
                Debug.Log("[AgentCommand] " + arg);
                break;

            case "timescale":
                ExecuteTimeScaleCommand(arg);
                break;

            case "screenshot":
                CaptureScreenshot();
                break;

            default:
                Debug.LogWarning("Unknown command: " + command);
                break;
        }
    }

    private void ExecuteTimeScaleCommand(string arg)
    {
        float value;
        if (float.TryParse(arg, out value))
        {
            Time.timeScale = Mathf.Clamp(value, 0.0f, 8.0f);
            Debug.Log("Time.timeScale set to " + Time.timeScale.ToString("F3"));
        }
        else
        {
            Debug.LogWarning("Invalid timescale value: " + arg);
        }
    }

    private void CaptureScreenshot()
    {
        string fileName = "RuntimeAgentDebugPanel_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
        ScreenCapture.CaptureScreenshot(fileName);
        Debug.Log("Screenshot requested: " + fileName);
    }

    private void RefreshSceneObjects(bool force)
    {
        if (!force && Time.unscaledTime - lastObjectRefreshTime < objectRefreshInterval)
        {
            return;
        }

        lastObjectRefreshTime = Time.unscaledTime;
        objectSnapshots.Clear();

        GameObject[] allObjects = Resources.FindObjectsOfTypeAll<GameObject>();

        for (int i = 0; i < allObjects.Length; i++)
        {
            GameObject go = allObjects[i];

            if (go == null)
            {
                continue;
            }

            if (!IsSceneObject(go))
            {
                continue;
            }

            if (!includeInactiveObjects && !go.activeInHierarchy)
            {
                continue;
            }

            ObjectSnapshot snapshot = new ObjectSnapshot
            {
                gameObject = go,
                instanceId = go.GetInstanceID(),
                path = BuildHierarchyPath(go.transform)
            };

            objectSnapshots.Add(snapshot);
        }

        objectSnapshots.Sort((a, b) => string.Compare(a.path, b.path, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsSceneObject(GameObject go)
    {
        if (!go.scene.IsValid())
        {
            return false;
        }

        if (go.hideFlags == HideFlags.NotEditable || go.hideFlags == HideFlags.HideAndDontSave)
        {
            return false;
        }

        return true;
    }

    private bool PassesSearchFilter(ObjectSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return true;
        }

        string keyword = searchText.Trim();

        if (snapshot.path.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        if (snapshot.gameObject.name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return false;
    }

    private void SelectObject(GameObject go, string path)
    {
        selectedObject = go;
        selectedObjectPath = path;
    }

    private void SelectObjectByName(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
        {
            Debug.LogWarning("select command requires an object name.");
            return;
        }

        for (int i = 0; i < objectSnapshots.Count; i++)
        {
            ObjectSnapshot snapshot = objectSnapshots[i];

            if (snapshot.gameObject == null)
            {
                continue;
            }

            if (snapshot.gameObject.name.IndexOf(objectName, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                SelectObject(snapshot.gameObject, snapshot.path);
                currentTab = PanelTab.Inspector;
                Debug.Log("Selected object: " + snapshot.path);
                return;
            }
        }

        Debug.LogWarning("Object not found: " + objectName);
    }

    private string BuildHierarchyPath(Transform target)
    {
        stringBuilder.Length = 0;

        Transform current = target;
        while (current != null)
        {
            if (stringBuilder.Length == 0)
            {
                stringBuilder.Insert(0, current.name);
            }
            else
            {
                stringBuilder.Insert(0, current.name + "/");
            }

            current = current.parent;
        }

        return stringBuilder.ToString();
    }

    private void UndoLikeResetTransform(Transform target)
    {
        target.localPosition = Vector3.zero;
        target.localRotation = Quaternion.identity;
        target.localScale = Vector3.one;
    }

    private void FocusMainCameraOnSelectedObject()
    {
        if (selectedObject == null)
        {
            return;
        }

        Camera camera = Camera.main;
        if (camera == null)
        {
            Debug.LogWarning("Main Camera not found.");
            return;
        }

        Vector3 targetPosition = selectedObject.transform.position;
        Vector3 direction = (camera.transform.position - targetPosition).normalized;

        if (direction.sqrMagnitude < 0.01f)
        {
            direction = new Vector3(0, 1, -1).normalized;
        }

        camera.transform.position = targetPosition + direction * 8.0f;
        camera.transform.LookAt(targetPosition);

        Debug.Log("Main Camera focused on: " + selectedObject.name);
    }

    private string BuildObjectReport(GameObject go)
    {
        if (go == null)
        {
            return "No object selected.";
        }

        stringBuilder.Length = 0;
        stringBuilder.AppendLine("===== Runtime Object Report =====");
        stringBuilder.AppendLine("Name: " + go.name);
        stringBuilder.AppendLine("Path: " + selectedObjectPath);
        stringBuilder.AppendLine("Instance ID: " + go.GetInstanceID());
        stringBuilder.AppendLine("Tag: " + go.tag);
        stringBuilder.AppendLine("Layer: " + LayerMask.LayerToName(go.layer) + " (" + go.layer + ")");
        stringBuilder.AppendLine("Active Self: " + go.activeSelf);
        stringBuilder.AppendLine("Active In Hierarchy: " + go.activeInHierarchy);
        stringBuilder.AppendLine("Position: " + go.transform.position);
        stringBuilder.AppendLine("Rotation: " + go.transform.eulerAngles);
        stringBuilder.AppendLine("Scale: " + go.transform.localScale);
        stringBuilder.AppendLine("Components:");

        Component[] components = go.GetComponents<Component>();
        for (int i = 0; i < components.Length; i++)
        {
            stringBuilder.AppendLine("- " + (components[i] == null ? "Missing Script" : components[i].GetType().FullName));
        }

        return stringBuilder.ToString();
    }

    private void TogglePause()
    {
        if (!isPaused)
        {
            timeScaleBeforePause = Time.timeScale <= 0.0f ? 1.0f : Time.timeScale;
            Time.timeScale = 0.0f;
            isPaused = true;
        }
        else
        {
            Time.timeScale = timeScaleBeforePause;
            isPaused = false;
        }
    }

    private void HandleLogMessage(string condition, string stackTrace, LogType type)
    {
        RuntimeLog entry = new RuntimeLog
        {
            message = condition,
            stackTrace = stackTrace,
            type = type,
            time = Time.realtimeSinceStartup
        };

        logs.Add(entry);

        if (!logCounters.ContainsKey(type))
        {
            logCounters[type] = 0;
        }

        logCounters[type]++;

        while (logs.Count > maxLogCount)
        {
            logs.RemoveAt(0);
        }
    }

    private int GetLogCount(LogType type)
    {
        int count;
        return logCounters.TryGetValue(type, out count) ? count : 0;
    }

    private GUIStyle GetLogStyle(LogType type)
    {
        switch (type)
        {
            case LogType.Warning:
                return logWarningStyle;
            case LogType.Error:
            case LogType.Assert:
            case LogType.Exception:
                return logErrorStyle;
            default:
                return logInfoStyle;
        }
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        selectedObject = null;
        selectedObjectPath = "";
        RefreshSceneObjects(true);
        Debug.Log("RuntimeAgentDebugPanel indexed loaded scene: " + scene.name);
    }

    private void InitializeStyles()
    {
        if (headerStyle != null)
        {
            return;
        }

        headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.8f, 0.95f, 1.0f, 1.0f) }
        };

        smallLabelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            normal = { textColor = new Color(0.86f, 0.90f, 0.94f, 1.0f) }
        };

        toolbarButtonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold
        };

        boxStyle = new GUIStyle(GUI.skin.box)
        {
            padding = new RectOffset(8, 8, 8, 8)
        };

        logInfoStyle = new GUIStyle(GUI.skin.label)
        {
            wordWrap = true,
            normal = { textColor = new Color(0.85f, 0.95f, 1.0f, 1.0f) }
        };

        logWarningStyle = new GUIStyle(GUI.skin.label)
        {
            wordWrap = true,
            normal = { textColor = new Color(1.0f, 0.82f, 0.25f, 1.0f) }
        };

        logErrorStyle = new GUIStyle(GUI.skin.label)
        {
            wordWrap = true,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1.0f, 0.35f, 0.35f, 1.0f) }
        };
    }
}
