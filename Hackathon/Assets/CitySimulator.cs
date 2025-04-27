using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Linq;

public class CitySimulator : MonoBehaviour
{
    // Debug settings
    private string debugLogPath;
    private StreamWriter debugWriter;
    private bool isDebugging = true;
    
    // File paths
    public string mapDataPath = "Assets/Resources/san_jose_map_data.csv";
    public string mapData2024Path = "Assets/Resources/san_jose_map_2024.csv";
    public bool use2024Map = true;
    public string budgetDataPath = "Assets/Resources/san_jose_budget.csv";
    
    // References to UI elements
    public GameObject zonePrefab;
    public RectTransform mapContainer;
    public TextMeshProUGUI budgetText;
    public TextMeshProUGUI selectedZoneText;
    public TextMeshProUGUI instructionsText;
    public GameObject infoPanel;
    public ScrollRect mapScrollRect; // Reference to the ScrollRect component
    
    // New UI elements for impact simulation
    public GameObject impactSimulationPanel;
    public TMP_Dropdown impactTypeDropdown;
    public TMP_InputField budgetInputField;
    public TMP_InputField yearsInputField;
    public TextMeshProUGUI simulationResultsText;
    public Button simulateButton;
    public Toggle impactViewToggle; // New toggle for switching views
    
    // Double click detection
    private float doubleClickTime = 0.3f;
    private float lastClickTime;
    private CityZone lastClickedZone;
    
    // Object pooling settings
    public int poolSize = 30;
    private Queue<GameObject> zoneObjectPool;
    private Dictionary<CityZone, GameObject> activeZoneObjects;
    
    // Performance settings
    public float visibilityMargin = 100f; // Extra margin around viewport to preload zones
    public float scrollThreshold = 50f; // How far must scroll before we update visible zones
    public float updateInterval = 0.1f; // Minimum time between visibility updates
    private float lastUpdateTime = 0f;
    
    // Data structures
    private List<CityZone> zones;
    private Dictionary<string, BudgetItem> budget;
    private float totalBudget;
    private CityZone selectedZone;
    private bool isDragging;
    private Vector2 dragOffset;
    private Vector2 lastViewportPosition;
    private Rect visibleRect; // Current visible area

    void Awake()
    {
        if (isDebugging)
        {
            debugLogPath = Path.Combine(Application.persistentDataPath, "city_simulator_debug.log");
            debugWriter = new StreamWriter(debugLogPath, true);
            WriteDebugLog("=== CitySimulator Awake ===");
        }
    }

    void Start()
    {
        WriteDebugLog("CitySimulator starting...");
        
        try
        {
            // Initialize data structures
            zones = new List<CityZone>();
            budget = new Dictionary<string, BudgetItem>();
            zoneObjectPool = new Queue<GameObject>();
            activeZoneObjects = new Dictionary<CityZone, GameObject>();
            
            WriteDebugLog("Data structures initialized");
            
            // Check critical references
            if (zonePrefab == null || mapContainer == null)
            {
                WriteDebugLog("ERROR: Critical components not assigned in Inspector!");
                return;
            }

            // Validate UI components
            ValidateUIComponents();
            
            WriteDebugLog("UI components validated");

            // Create zone prefab at runtime if needed
            if (zonePrefab == null)
            {
                WriteDebugLog("Creating default zone prefab");
                CreateDefaultZonePrefab();
            }
            
            // Setup object pool
            WriteDebugLog("Initializing object pool");
            InitializeObjectPool();
            
            // Setup scroll rect reference if needed
            WriteDebugLog("Setting up scroll rect");
            SetupScrollRect();
            
            // Setup impact simulation panel
            WriteDebugLog("Setting up impact simulation panel");
            SetupImpactSimulationPanel();
            
            // Load data
            WriteDebugLog("Loading game data");
            LoadGameData();
            
            // Initial display update
            WriteDebugLog("Updating initial display");
            UpdateVisibleZones();
            UpdateBudgetDisplay();
            UpdateInstructionsDisplay();
            
            // Set initial viewport position
            lastViewportPosition = mapScrollRect != null ? 
                mapScrollRect.content.anchoredPosition : Vector2.zero;
                
            WriteDebugLog("CitySimulator initialization complete");
        }
        catch (Exception e)
        {
            WriteDebugLog($"ERROR during initialization: {e.Message}\n{e.StackTrace}");
        }
    }

    private void WriteDebugLog(string message)
    {
        if (!isDebugging) return;
        
        try
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string logMessage = $"[{timestamp}] {message}";
            Debug.Log(logMessage);
            
            if (debugWriter != null)
            {
                debugWriter.WriteLine(logMessage);
                debugWriter.Flush();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error writing to debug log: {e.Message}");
        }
    }

    private void ValidateUIComponents()
    {
        WriteDebugLog("Validating UI components");
        
        if (budgetText == null) WriteDebugLog("WARNING: Budget Text component not assigned");
        if (selectedZoneText == null) WriteDebugLog("WARNING: Selected Zone Text component not assigned");
        if (instructionsText == null) WriteDebugLog("WARNING: Instructions Text component not assigned");
        if (infoPanel == null) WriteDebugLog("WARNING: Info Panel not assigned");
        if (mapScrollRect == null) WriteDebugLog("WARNING: Map Scroll Rect not assigned");
        if (impactSimulationPanel == null) WriteDebugLog("WARNING: Impact Simulation Panel not assigned");
        if (impactTypeDropdown == null) WriteDebugLog("WARNING: Impact Type Dropdown not assigned");
        if (budgetInputField == null) WriteDebugLog("WARNING: Budget Input Field not assigned");
        if (yearsInputField == null) WriteDebugLog("WARNING: Years Input Field not assigned");
        if (simulationResultsText == null) WriteDebugLog("WARNING: Simulation Results Text not assigned");
        if (simulateButton == null) WriteDebugLog("WARNING: Simulate Button not assigned");
        if (impactViewToggle == null) WriteDebugLog("WARNING: Impact View Toggle not assigned");
    }

    private void SetupScrollRect()
    {
        WriteDebugLog("Setting up ScrollRect...");
        
        if (mapScrollRect == null)
        {
            WriteDebugLog("No ScrollRect found, attempting to create one...");
            
            try
            {
                // Try to find a ScrollRect in children
                mapScrollRect = GetComponentInChildren<ScrollRect>();
                
                if (mapScrollRect == null)
                {
                    WriteDebugLog("No ScrollRect found in children, creating default...");
                    
                    // Create a new UI Panel with ScrollRect
                    GameObject panel = new GameObject("MapPanel");
                    panel.transform.SetParent(transform, false);
                    
                    // Add RectTransform
                    RectTransform panelRect = panel.AddComponent<RectTransform>();
                    panelRect.anchorMin = Vector2.zero;
                    panelRect.anchorMax = Vector2.one;
                    panelRect.offsetMin = Vector2.zero;
                    panelRect.offsetMax = Vector2.zero;
                    
                    // Add Image component
                    Image panelImage = panel.AddComponent<Image>();
                    panelImage.color = new Color(0, 0, 0, 0);
                    
                    // Add ScrollRect
                    mapScrollRect = panel.AddComponent<ScrollRect>();
                    
                    // Create content object
                    GameObject content = new GameObject("Content");
                    content.transform.SetParent(panel.transform, false);
                    
                    // Setup content RectTransform
                    RectTransform contentRect = content.AddComponent<RectTransform>();
                    contentRect.anchorMin = Vector2.zero;
                    contentRect.anchorMax = Vector2.one;
                    contentRect.offsetMin = Vector2.zero;
                    contentRect.offsetMax = Vector2.zero;
                    
                    // Configure ScrollRect
                    mapScrollRect.content = contentRect;
                    mapScrollRect.horizontal = true;
                    mapScrollRect.vertical = true;
                    mapScrollRect.movementType = ScrollRect.MovementType.Elastic;
                    mapScrollRect.elasticity = 0.1f;
                    mapScrollRect.inertia = true;
                    mapScrollRect.decelerationRate = 0.135f;
                    mapScrollRect.scrollSensitivity = 1f;
                    
                    // Set mapContainer to content
                    mapContainer = contentRect;
                    
                    WriteDebugLog("Default ScrollRect created successfully");
                }
                else
                {
                    WriteDebugLog("Found existing ScrollRect in children");
                }
            }
            catch (Exception e)
            {
                WriteDebugLog($"ERROR creating ScrollRect: {e.Message}\n{e.StackTrace}");
                // Disable scrolling functionality but allow the rest to work
                mapScrollRect = null;
            }
        }
        else
        {
            WriteDebugLog("ScrollRect already assigned");
        }
        
        if (mapScrollRect == null)
        {
            WriteDebugLog("WARNING: No ScrollRect available. Scrolling functionality will be disabled.");
        }
    }

    private void SetupImpactSimulationPanel()
    {
        if (impactSimulationPanel == null)
        {
            WriteDebugLog("Creating impact simulation panel");
            
            // Create panel
            impactSimulationPanel = new GameObject("ImpactSimulationPanel");
            impactSimulationPanel.transform.SetParent(transform, false);
            
            // Add RectTransform
            RectTransform panelRect = impactSimulationPanel.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.7f, 0.5f);
            panelRect.anchorMax = new Vector2(0.95f, 0.9f);
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            
            // Add Image component
            Image panelImage = impactSimulationPanel.AddComponent<Image>();
            panelImage.color = new Color(0.2f, 0.2f, 0.2f, 0.9f);
            
            // Create title
            GameObject titleObj = new GameObject("Title");
            titleObj.transform.SetParent(impactSimulationPanel.transform, false);
            RectTransform titleRect = titleObj.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0, 0.9f);
            titleRect.anchorMax = new Vector2(1, 1);
            titleRect.offsetMin = new Vector2(10, 0);
            titleRect.offsetMax = new Vector2(-10, -10);
            
            TextMeshProUGUI titleText = titleObj.AddComponent<TextMeshProUGUI>();
            titleText.text = "Impact Simulation";
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.fontSize = 24;
            titleText.color = Color.white;
            
            // Create impact view toggle
            GameObject toggleObj = new GameObject("ImpactViewToggle");
            toggleObj.transform.SetParent(impactSimulationPanel.transform, false);
            RectTransform toggleRect = toggleObj.AddComponent<RectTransform>();
            toggleRect.anchorMin = new Vector2(0.1f, 0.8f);
            toggleRect.anchorMax = new Vector2(0.9f, 0.85f);
            toggleRect.offsetMin = Vector2.zero;
            toggleRect.offsetMax = Vector2.zero;
            
            impactViewToggle = toggleObj.AddComponent<Toggle>();
            Image toggleImage = toggleObj.AddComponent<Image>();
            toggleImage.color = new Color(0.3f, 0.3f, 0.3f);
            
            // Create toggle label
            GameObject labelObj = new GameObject("Label");
            labelObj.transform.SetParent(toggleObj.transform, false);
            RectTransform labelRect = labelObj.AddComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(30, 0);
            labelRect.offsetMax = Vector2.zero;
            
            TextMeshProUGUI labelText = labelObj.AddComponent<TextMeshProUGUI>();
            labelText.text = "Show City-Wide Top Impacts";
            labelText.alignment = TextAlignmentOptions.Left;
            labelText.fontSize = 14;
            labelText.color = Color.white;
            
            // Create toggle checkmark
            GameObject checkmarkObj = new GameObject("Checkmark");
            checkmarkObj.transform.SetParent(toggleObj.transform, false);
            RectTransform checkmarkRect = checkmarkObj.AddComponent<RectTransform>();
            checkmarkRect.anchorMin = new Vector2(0, 0);
            checkmarkRect.anchorMax = new Vector2(0, 1);
            checkmarkRect.sizeDelta = new Vector2(20, 0);
            checkmarkRect.offsetMin = new Vector2(5, 0);
            checkmarkRect.offsetMax = new Vector2(25, 0);
            
            Image checkmarkImage = checkmarkObj.AddComponent<Image>();
            checkmarkImage.color = new Color(0.2f, 0.6f, 1f);
            
            // Setup toggle
            impactViewToggle.targetGraphic = toggleImage;
            impactViewToggle.graphic = checkmarkImage;
            impactViewToggle.isOn = false;
            impactViewToggle.onValueChanged.AddListener(OnImpactViewToggleChanged);
            
            // Create dropdown
            GameObject dropdownObj = new GameObject("ImpactTypeDropdown");
            dropdownObj.transform.SetParent(impactSimulationPanel.transform, false);
            RectTransform dropdownRect = dropdownObj.AddComponent<RectTransform>();
            dropdownRect.anchorMin = new Vector2(0.1f, 0.7f);
            dropdownRect.anchorMax = new Vector2(0.9f, 0.75f);
            dropdownRect.offsetMin = Vector2.zero;
            dropdownRect.offsetMax = Vector2.zero;
            
            impactTypeDropdown = dropdownObj.AddComponent<TMP_Dropdown>();
            impactTypeDropdown.template = CreateDropdownTemplate(dropdownObj);
            
            // Create budget input
            GameObject budgetObj = new GameObject("BudgetInput");
            budgetObj.transform.SetParent(impactSimulationPanel.transform, false);
            RectTransform budgetRect = budgetObj.AddComponent<RectTransform>();
            budgetRect.anchorMin = new Vector2(0.1f, 0.5f);
            budgetRect.anchorMax = new Vector2(0.9f, 0.6f);
            budgetRect.offsetMin = Vector2.zero;
            budgetRect.offsetMax = Vector2.zero;
            
            budgetInputField = budgetObj.AddComponent<TMP_InputField>();
            budgetInputField.textComponent = CreateInputText(budgetObj);
            budgetInputField.placeholder = CreateInputPlaceholder(budgetObj, "Enter budget amount");
            
            // Create years input
            GameObject yearsObj = new GameObject("YearsInput");
            yearsObj.transform.SetParent(impactSimulationPanel.transform, false);
            RectTransform yearsRect = yearsObj.AddComponent<RectTransform>();
            yearsRect.anchorMin = new Vector2(0.1f, 0.3f);
            yearsRect.anchorMax = new Vector2(0.9f, 0.4f);
            yearsRect.offsetMin = Vector2.zero;
            yearsRect.offsetMax = Vector2.zero;
            
            yearsInputField = yearsObj.AddComponent<TMP_InputField>();
            yearsInputField.textComponent = CreateInputText(yearsObj);
            yearsInputField.placeholder = CreateInputPlaceholder(yearsObj, "Enter number of years");
            
            // Create simulate button
            GameObject buttonObj = new GameObject("SimulateButton");
            buttonObj.transform.SetParent(impactSimulationPanel.transform, false);
            RectTransform buttonRect = buttonObj.AddComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.3f, 0.15f);
            buttonRect.anchorMax = new Vector2(0.7f, 0.25f);
            buttonRect.offsetMin = Vector2.zero;
            buttonRect.offsetMax = Vector2.zero;
            
            simulateButton = buttonObj.AddComponent<Button>();
            Image buttonImage = buttonObj.AddComponent<Image>();
            buttonImage.color = new Color(0.2f, 0.6f, 1f);
            
            TextMeshProUGUI buttonText = CreateInputText(buttonObj);
            buttonText.text = "Simulate";
            buttonText.alignment = TextAlignmentOptions.Center;
            
            // Create results text
            GameObject resultsObj = new GameObject("ResultsText");
            resultsObj.transform.SetParent(impactSimulationPanel.transform, false);
            RectTransform resultsRect = resultsObj.AddComponent<RectTransform>();
            resultsRect.anchorMin = new Vector2(0.1f, 0.05f);
            resultsRect.anchorMax = new Vector2(0.9f, 0.15f);
            resultsRect.offsetMin = Vector2.zero;
            resultsRect.offsetMax = Vector2.zero;
            
            simulationResultsText = resultsObj.AddComponent<TextMeshProUGUI>();
            simulationResultsText.alignment = TextAlignmentOptions.Center;
            simulationResultsText.fontSize = 14;
            simulationResultsText.color = Color.white;
            
            // Add button listener
            simulateButton.onClick.AddListener(OnSimulateButtonClick);
            
            // Initially hide the panel
            impactSimulationPanel.SetActive(false);
        }
    }

    private RectTransform CreateDropdownTemplate(GameObject parent)
    {
        GameObject template = new GameObject("Template");
        template.transform.SetParent(parent.transform, false);
        RectTransform templateRect = template.AddComponent<RectTransform>();
        templateRect.anchorMin = new Vector2(0, 0);
        templateRect.anchorMax = new Vector2(1, 1);
        templateRect.offsetMin = Vector2.zero;
        templateRect.offsetMax = Vector2.zero;
        
        Image templateImage = template.AddComponent<Image>();
        templateImage.color = new Color(0.2f, 0.2f, 0.2f);
        
        return templateRect;
    }

    private TextMeshProUGUI CreateInputText(GameObject parent)
    {
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(parent.transform, false);
        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10, 5);
        textRect.offsetMax = new Vector2(-10, -5);
        
        TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
        text.alignment = TextAlignmentOptions.Left;
        text.fontSize = 14;
        text.color = Color.white;
        
        return text;
    }

    private TextMeshProUGUI CreateInputPlaceholder(GameObject parent, string placeholderText)
    {
        GameObject placeholderObj = new GameObject("Placeholder");
        placeholderObj.transform.SetParent(parent.transform, false);
        RectTransform placeholderRect = placeholderObj.AddComponent<RectTransform>();
        placeholderRect.anchorMin = Vector2.zero;
        placeholderRect.anchorMax = Vector2.one;
        placeholderRect.offsetMin = new Vector2(10, 5);
        placeholderRect.offsetMax = new Vector2(-10, -5);
        
        TextMeshProUGUI placeholder = placeholderObj.AddComponent<TextMeshProUGUI>();
        placeholder.text = placeholderText;
        placeholder.alignment = TextAlignmentOptions.Left;
        placeholder.fontSize = 14;
        placeholder.color = new Color(0.7f, 0.7f, 0.7f);
        
        return placeholder;
    }

    private void OnSimulateButtonClick()
    {
        if (selectedZone == null) return;
        
        try
        {
            // Get selected impact type
            string impactType = impactTypeDropdown.options[impactTypeDropdown.value].text;
            
            // Get budget amount
            if (!float.TryParse(budgetInputField.text, out float budgetAmount))
            {
                simulationResultsText.text = "Please enter a valid budget amount";
                return;
            }
            
            // Get number of years
            if (!int.TryParse(yearsInputField.text, out int years))
            {
                simulationResultsText.text = "Please enter a valid number of years";
                return;
            }
            
            // Calculate impact over time
            float baseImpact = selectedZone.impacts.ContainsKey(impactType) ? 
                selectedZone.impacts[impactType] : 0f;
            
            float yearlyImpact = baseImpact * (budgetAmount / selectedZone.cost);
            float totalImpact = yearlyImpact * years;
            
            // Format results
            string results = $"Simulation Results:\n\n";
            results += $"Base Impact: {baseImpact:F1}\n";
            results += $"Yearly Impact: {yearlyImpact:F1}\n";
            results += $"Total Impact over {years} years: {totalImpact:F1}\n\n";
            
            if (totalImpact > 0)
            {
                results += "This investment will have a positive effect on " + impactType;
            }
            else if (totalImpact < 0)
            {
                results += "This investment will have a negative effect on " + impactType;
            }
            else
            {
                results += "This investment will have no significant effect on " + impactType;
            }
            
            simulationResultsText.text = results;
        }
        catch (Exception e)
        {
            WriteDebugLog($"ERROR in OnSimulateButtonClick: {e.Message}");
            simulationResultsText.text = "Error calculating simulation results";
        }
    }

    private void LoadGameData()
    {
        WriteDebugLog("Loading game data...");
        
        // Determine which map data to use
        string selectedMapPath = use2024Map ? mapData2024Path : mapDataPath;
        
        // Load map data (districts/areas)
        WriteDebugLog($"Loading map data from: {selectedMapPath}");
        if (!LoadMapData(selectedMapPath))
        {
            WriteDebugLog("ERROR: Failed to load map data");
        }
        
        // Load budget data
        WriteDebugLog($"Loading budget data from: {budgetDataPath}");
        if (!LoadBudgetData(budgetDataPath))
        {
            WriteDebugLog("ERROR: Failed to load budget data");
        }
    }

    private void InitializeObjectPool()
    {
        WriteDebugLog("Initializing object pool...");
        
        try
        {
            if (zonePrefab == null)
            {
                WriteDebugLog("ERROR: Zone prefab is null, creating default...");
                CreateDefaultZonePrefab();
            }
            
            WriteDebugLog($"Initializing object pool with {poolSize} zone objects");
            
            // Clear existing pool if any
            while (zoneObjectPool.Count > 0)
            {
                GameObject obj = zoneObjectPool.Dequeue();
                if (obj != null)
                {
                    Destroy(obj);
                }
            }
            
            // Initialize new pool
            for (int i = 0; i < poolSize; i++)
            {
                try
                {
                    WriteDebugLog($"Creating pool object {i + 1}/{poolSize}");
                    GameObject zoneObj = Instantiate(zonePrefab, mapContainer);
                    if (zoneObj == null)
                    {
                        WriteDebugLog($"ERROR: Failed to create pool object {i + 1}");
                        continue;
                    }
                    
                    zoneObj.SetActive(false);
                    PresetupZoneEvents(zoneObj);
                    zoneObjectPool.Enqueue(zoneObj);
                }
                catch (Exception e)
                {
                    WriteDebugLog($"ERROR creating pool object {i + 1}: {e.Message}");
                }
            }
            
            WriteDebugLog($"Object pool initialization complete. Created {zoneObjectPool.Count} objects");
        }
        catch (Exception e)
        {
            WriteDebugLog($"ERROR in InitializeObjectPool: {e.Message}\n{e.StackTrace}");
        }
    }
    
    private void PresetupZoneEvents(GameObject zoneObj)
    {
        WriteDebugLog("Setting up zone events...");
        
        try
        {
            if (zoneObj == null)
            {
                WriteDebugLog("ERROR: Zone object is null");
                return;
            }
            
            EventTrigger trigger = zoneObj.GetComponent<EventTrigger>();
            if (trigger == null)
            {
                WriteDebugLog("Adding EventTrigger component");
                trigger = zoneObj.AddComponent<EventTrigger>();
            }
            else
            {
                WriteDebugLog("Clearing existing EventTrigger");
                trigger.triggers.Clear();
            }

            // Setup the event entries
            EventTrigger.Entry clickEntry = new EventTrigger.Entry();
            clickEntry.eventID = EventTriggerType.PointerDown;
            trigger.triggers.Add(clickEntry);

            EventTrigger.Entry dragEntry = new EventTrigger.Entry();
            dragEntry.eventID = EventTriggerType.Drag;
            trigger.triggers.Add(dragEntry);

            EventTrigger.Entry endDragEntry = new EventTrigger.Entry();
            endDragEntry.eventID = EventTriggerType.EndDrag;
            trigger.triggers.Add(endDragEntry);
            
            WriteDebugLog("Zone events setup complete");
        }
        catch (Exception e)
        {
            WriteDebugLog($"ERROR in PresetupZoneEvents: {e.Message}\n{e.StackTrace}");
        }
    }

    private void CreateDefaultZonePrefab()
    {
        WriteDebugLog("Creating default zone prefab...");
        
        try
        {
            zonePrefab = new GameObject("ZonePrefab");
            
            // Add required components
            RectTransform rectTransform = zonePrefab.AddComponent<RectTransform>();
            rectTransform.sizeDelta = new Vector2(100, 100); // Larger size for more detail
            
            // Add Image component for the base
            Image baseImage = zonePrefab.AddComponent<Image>();
            baseImage.color = new Color(0.2f, 0.2f, 0.2f, 0.8f); // Dark gray base
            
            // Create buildings container
            GameObject buildingsContainer = new GameObject("Buildings");
            buildingsContainer.transform.SetParent(zonePrefab.transform, false);
            RectTransform buildingsRect = buildingsContainer.AddComponent<RectTransform>();
            buildingsRect.anchorMin = Vector2.zero;
            buildingsRect.anchorMax = Vector2.one;
            buildingsRect.offsetMin = new Vector2(5, 5); // Padding
            buildingsRect.offsetMax = new Vector2(-5, -5);
            
            // Add buildings
            AddBuilding(buildingsContainer, new Vector2(0.2f, 0.2f), new Vector2(0.3f, 0.4f), new Color(0.4f, 0.4f, 0.4f)); // Tall building
            AddBuilding(buildingsContainer, new Vector2(0.6f, 0.3f), new Vector2(0.25f, 0.3f), new Color(0.5f, 0.5f, 0.5f)); // Medium building
            AddBuilding(buildingsContainer, new Vector2(0.3f, 0.7f), new Vector2(0.2f, 0.2f), new Color(0.6f, 0.6f, 0.6f)); // Small building
            
            // Add roads
            AddRoad(buildingsContainer, new Vector2(0.5f, 0.1f), new Vector2(0.8f, 0.05f), new Color(0.3f, 0.3f, 0.3f)); // Horizontal road
            AddRoad(buildingsContainer, new Vector2(0.1f, 0.5f), new Vector2(0.05f, 0.8f), new Color(0.3f, 0.3f, 0.3f)); // Vertical road
            
            // Add text for the name
            GameObject textObj = new GameObject("NameText");
            textObj.transform.SetParent(zonePrefab.transform, false);
            
            RectTransform textRectTransform = textObj.AddComponent<RectTransform>();
            textRectTransform.anchorMin = Vector2.zero;
            textRectTransform.anchorMax = Vector2.one;
            textRectTransform.offsetMin = Vector2.zero;
            textRectTransform.offsetMax = Vector2.zero;
            
            TextMeshProUGUI nameText = textObj.AddComponent<TextMeshProUGUI>();
            nameText.alignment = TextAlignmentOptions.Center;
            nameText.fontSize = 12;
            nameText.color = Color.white;
            nameText.fontStyle = FontStyles.Bold;
            
            // Add EventTrigger component
            zonePrefab.AddComponent<EventTrigger>();
            
            // Make it a prefab instance by setting inactive
            zonePrefab.SetActive(false);
            
            WriteDebugLog("Default zone prefab created successfully");
        }
        catch (Exception e)
        {
            WriteDebugLog($"ERROR in CreateDefaultZonePrefab: {e.Message}\n{e.StackTrace}");
            throw;
        }
    }
    
    private void AddBuilding(GameObject parent, Vector2 position, Vector2 size, Color color)
    {
        GameObject building = new GameObject("Building");
        building.transform.SetParent(parent.transform, false);
        
        RectTransform rectTransform = building.AddComponent<RectTransform>();
        rectTransform.anchorMin = position;
        rectTransform.anchorMax = position + size;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        
        Image image = building.AddComponent<Image>();
        image.color = color;
        
        // Add windows
        int windowRows = Mathf.FloorToInt(size.y * 10);
        int windowCols = Mathf.FloorToInt(size.x * 10);
        
        for (int row = 0; row < windowRows; row++)
        {
            for (int col = 0; col < windowCols; col++)
            {
                if (UnityEngine.Random.value > 0.3f) // 70% chance of window
                {
                    AddWindow(building, new Vector2(
                        (col + 0.5f) / windowCols,
                        (row + 0.5f) / windowRows
                    ));
                }
            }
        }
    }
    
    private void AddWindow(GameObject building, Vector2 position)
    {
        GameObject window = new GameObject("Window");
        window.transform.SetParent(building.transform, false);
        
        RectTransform rectTransform = window.AddComponent<RectTransform>();
        rectTransform.anchorMin = position - new Vector2(0.02f, 0.02f);
        rectTransform.anchorMax = position + new Vector2(0.02f, 0.02f);
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        
        Image image = window.AddComponent<Image>();
        image.color = new Color(0.8f, 0.8f, 0.9f, 0.8f); // Light blue window
    }
    
    private void AddRoad(GameObject parent, Vector2 position, Vector2 size, Color color)
    {
        GameObject road = new GameObject("Road");
        road.transform.SetParent(parent.transform, false);
        
        RectTransform rectTransform = road.AddComponent<RectTransform>();
        rectTransform.anchorMin = position;
        rectTransform.anchorMax = position + size;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        
        Image image = road.AddComponent<Image>();
        image.color = color;
        
        // Add road markings
        if (size.x > size.y) // Horizontal road
        {
            for (float x = 0.1f; x < 0.9f; x += 0.2f)
            {
                AddRoadMarking(road, new Vector2(x, 0.5f), new Vector2(0.1f, 0.02f));
            }
        }
        else // Vertical road
        {
            for (float y = 0.1f; y < 0.9f; y += 0.2f)
            {
                AddRoadMarking(road, new Vector2(0.5f, y), new Vector2(0.02f, 0.1f));
            }
        }
    }
    
    private void AddRoadMarking(GameObject road, Vector2 position, Vector2 size)
    {
        GameObject marking = new GameObject("RoadMarking");
        marking.transform.SetParent(road.transform, false);
        
        RectTransform rectTransform = marking.AddComponent<RectTransform>();
        rectTransform.anchorMin = position - size/2;
        rectTransform.anchorMax = position + size/2;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        
        Image image = marking.AddComponent<Image>();
        image.color = Color.yellow;
    }

    // Load city map data from CSV
    private bool LoadMapData(string filename)
    {
        try
        {
            // Clear any existing zones
            zones.Clear();
            
            // Check if file exists
            TextAsset csvAsset = Resources.Load<TextAsset>(Path.GetFileNameWithoutExtension(filename));
            if (csvAsset == null)
            {
                // Try to load directly from file system if not in Resources
                if (!File.Exists(filename))
                {
                    WriteDebugLog("WARNING: Map data file not found: " + filename + ". Using default data.");
                    CreateDefaultMapData();
                    return true;
                }
                
                string[] lines = File.ReadAllLines(filename);
                ParseCsvLines(lines);
            }
            else
            {
                // File exists in Resources, parse it
                string[] lines = csvAsset.text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                ParseCsvLines(lines);
            }
            
            WriteDebugLog($"Successfully loaded {zones.Count} map zones");
            return true;
        }
        catch (Exception e)
        {
            WriteDebugLog("ERROR: Error loading map data: " + e.Message);
            WriteDebugLog("Using default map data instead.");
            CreateDefaultMapData();
            return true;
        }
    }
    
    private void ParseCsvLines(string[] lines)
    {
        // Skip header line
        for (int i = 1; i < lines.Length; i++)
        {
            string[] tokens = lines[i].Split(',');

            if (tokens.Length >= 6)
            {
                CityZone zone = new CityZone();
                zone.name = tokens[0].Trim();
                zone.position = new Vector2(float.Parse(tokens[1]), float.Parse(tokens[2]));
                zone.type = tokens[3].Trim();
                zone.cost = float.Parse(tokens[4]);

                // Parse impacts from remaining tokens
                for (int j = 5; j < tokens.Length; j += 2)
                {
                    if (j + 1 < tokens.Length && !string.IsNullOrEmpty(tokens[j]) && !string.IsNullOrEmpty(tokens[j + 1]))
                    {
                        zone.impacts[tokens[j].Trim()] = float.Parse(tokens[j + 1]);
                    }
                }

                zones.Add(zone);
            }
        }
    }

    // Create default map data for testing
    private void CreateDefaultMapData()
    {
        zones.Clear();
        
        // Add some sample zones
        CityZone downtown = new CityZone();
        downtown.name = "Downtown";
        downtown.position = new Vector2(0, 0);
        downtown.type = "Commercial";
        downtown.cost = 150;
        downtown.impacts["Economy"] = 5.0f;
        downtown.impacts["Environment"] = -2.0f;
        zones.Add(downtown);
        
        // Add 3 more sample zones
        CityZone residential = new CityZone();
        residential.name = "Residential District";
        residential.position = new Vector2(100, 0);
        residential.type = "Residential";
        residential.cost = 100;
        residential.impacts["Housing"] = 4.0f;
        residential.impacts["Traffic"] = 3.0f;
        zones.Add(residential);
        
        CityZone park = new CityZone();
        park.name = "Central Park";
        park.position = new Vector2(50, 50);
        park.type = "Park";
        park.cost = 50;
        park.impacts["Environment"] = 5.0f;
        park.impacts["Recreation"] = 4.0f;
        zones.Add(park);
        
        CityZone industrial = new CityZone();
        industrial.name = "Industrial Area";
        industrial.position = new Vector2(-100, -50);
        industrial.type = "Industrial";
        industrial.cost = 120;
        industrial.impacts["Economy"] = 4.0f;
        industrial.impacts["Environment"] = -3.0f;
        industrial.impacts["Jobs"] = 5.0f;
        zones.Add(industrial);
        
        WriteDebugLog("Created default map with " + zones.Count + " zones");
    }

    // Load budget data from CSV
    private bool LoadBudgetData(string filename)
    {
        try
        {
            // Clear any existing budget
            budget.Clear();
            totalBudget = 0;
            
            // Try to load from Resources first
            TextAsset csvAsset = Resources.Load<TextAsset>(Path.GetFileNameWithoutExtension(filename));
            if (csvAsset == null)
            {
                // Try to load directly from file system if not in Resources
                if (!File.Exists(filename))
                {
                    WriteDebugLog("WARNING: Budget data file not found: " + filename + ". Using default data.");
                    CreateDefaultBudgetData();
                    return true;
                }
                
                string[] lines = File.ReadAllLines(filename);
                ParseBudgetCsvLines(lines);
            }
            else
            {
                // File exists in Resources, parse it
                string[] lines = csvAsset.text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                ParseBudgetCsvLines(lines);
            }
            
            WriteDebugLog($"Successfully loaded budget data with {budget.Count} items");
            return true;
        }
        catch (Exception e)
        {
            WriteDebugLog("ERROR: Error loading budget data: " + e.Message);
            WriteDebugLog("Using default budget data instead.");
            CreateDefaultBudgetData();
            return true;
        }
    }
    
    private void ParseBudgetCsvLines(string[] lines)
    {
        // Skip header line
        for (int i = 1; i < lines.Length; i++)
        {
            string[] tokens = lines[i].Split(',');

            if (tokens.Length >= 4)
            {
                BudgetItem item = new BudgetItem();
                item.name = tokens[0].Trim();
                item.allocated = float.Parse(tokens[1]);
                item.spent = float.Parse(tokens[2]);
                item.category = tokens[3].Trim();

                budget[item.name] = item;
                totalBudget += item.allocated;
            }
        }
    }

    // Create default budget data for testing
    private void CreateDefaultBudgetData()
    {
        budget.Clear();
        totalBudget = 0;
        
        // Add sample budget items
        AddBudgetItem("Public Safety", 500, 450, "Safety");
        AddBudgetItem("Parks & Recreation", 200, 180, "Recreation");
        AddBudgetItem("Infrastructure", 400, 350, "Infrastructure");
        AddBudgetItem("Public Transportation", 300, 290, "Transportation");
        AddBudgetItem("Education", 450, 430, "Education");
        AddBudgetItem("Healthcare", 350, 320, "Healthcare");
    }

    private void AddBudgetItem(string name, float allocated, float spent, string category)
    {
        BudgetItem item = new BudgetItem();
        item.name = name;
        item.allocated = allocated;
        item.spent = spent;
        item.category = category;
        
        budget[item.name] = item;
        totalBudget += item.allocated;
    }

    // Calculate visible area and update zone objects
    private void UpdateVisibleZones()
    {
        if (mapContainer == null || zones == null || zones.Count == 0)
        {
            WriteDebugLog("WARNING: Cannot update visible zones - missing required components");
            return;
        }

        try
        {
            // Calculate viewport bounds
            CalculateVisibleRect();
            
            // Keep track of zones to show and hide
            HashSet<CityZone> visibleZones = new HashSet<CityZone>();
            List<CityZone> zonesToHide = new List<CityZone>();

            // First pass: identify visible zones
            foreach (CityZone zone in zones)
            {
                bool isVisible = IsZoneVisible(zone);
                if (isVisible)
                {
                    visibleZones.Add(zone);
                }
            }

            // Second pass: handle zone objects
            foreach (var kvp in activeZoneObjects)
            {
                if (!visibleZones.Contains(kvp.Key))
                {
                    zonesToHide.Add(kvp.Key);
                }
            }

            // Hide zones that are no longer visible
            foreach (var zone in zonesToHide)
            {
                HideZone(zone);
            }

            // Show newly visible zones
            foreach (CityZone zone in visibleZones)
            {
                if (!activeZoneObjects.ContainsKey(zone))
                {
                    ShowZone(zone);
                }
            }
        }
        catch (Exception e)
        {
            WriteDebugLog($"ERROR in UpdateVisibleZones: {e.Message}\n{e.StackTrace}");
        }
    }
    
    private void CalculateVisibleRect()
    {
        if (mapScrollRect != null && mapScrollRect.viewport != null)
        {
            // Get viewport corners
            Vector3[] viewportCorners = new Vector3[4];
            mapScrollRect.viewport.GetWorldCorners(viewportCorners);
            
            // Convert to rect in world space
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            
            for (int i = 0; i < 4; i++)
            {
                minX = Mathf.Min(minX, viewportCorners[i].x);
                maxX = Mathf.Max(maxX, viewportCorners[i].x);
                minY = Mathf.Min(minY, viewportCorners[i].y);
                maxY = Mathf.Max(maxY, viewportCorners[i].y);
            }
            
            // Add margins
            minX -= visibilityMargin;
            maxX += visibilityMargin;
            minY -= visibilityMargin;
            maxY += visibilityMargin;
            
            // Store as rect
            visibleRect = new Rect(minX, minY, maxX - minX, maxY - minY);
        }
        else
        {
            // Fallback - very large rectangle
            visibleRect = new Rect(-5000, -5000, 10000, 10000);
        }
    }
    
    private bool IsZoneVisible(CityZone zone)
    {
        // Get world position of zone
        Vector3 worldPos = mapContainer.TransformPoint(new Vector3(zone.position.x, zone.position.y, 0));
        
        // Check if in visible rect
        return visibleRect.Contains(new Vector2(worldPos.x, worldPos.y));
    }

    // Show a zone object
    private void ShowZone(CityZone zone)
    {
        // Get object from pool
        GameObject zoneObj = GetZoneFromPool();
        
        // Configure for this zone
        ConfigureZoneObject(zoneObj, zone);
        
        // Track it
        activeZoneObjects[zone] = zoneObj;
    }
    
    // Hide a zone object
    private void HideZone(CityZone zone)
    {
        if (activeZoneObjects.TryGetValue(zone, out GameObject zoneObj))
        {
            // Return to pool
            ReturnZoneToPool(zoneObj);
            
            // Remove tracking
            activeZoneObjects.Remove(zone);
        }
    }
    
    // Get a zone from the object pool
    private GameObject GetZoneFromPool()
    {
        GameObject zoneObj;
        
        if (zoneObjectPool.Count > 0)
        {
            zoneObj = zoneObjectPool.Dequeue();
        }
        else
        {
            // Create new object if pool is empty
            WriteDebugLog("WARNING: Object pool depleted, creating new zone object");
            zoneObj = Instantiate(zonePrefab, mapContainer);
            PresetupZoneEvents(zoneObj);
        }
        
        return zoneObj;
    }
    
    // Return a zone to the object pool
    private void ReturnZoneToPool(GameObject zoneObj)
    {
        // Reset and deactivate
        zoneObj.SetActive(false);
        
        // Add back to pool
        zoneObjectPool.Enqueue(zoneObj);
    }

    // Configure a zone object for a specific zone
    private void ConfigureZoneObject(GameObject zoneObj, CityZone zone)
    {
        zoneObj.SetActive(true);
        
        // Set position
        RectTransform rectTransform = zoneObj.GetComponent<RectTransform>();
        rectTransform.anchoredPosition = zone.position;
        rectTransform.sizeDelta = new Vector2(50, 50);

        // Set color based on zone type
        Image image = zoneObj.GetComponent<Image>();
        switch (zone.type.ToLower())
        {
            case "residential":
                image.color = new Color(0, 0.5f, 1f, 0.7f); // Blue
                break;
            case "commercial":
                image.color = new Color(1f, 0.5f, 0, 0.7f); // Orange
                break;
            case "industrial":
                image.color = new Color(0.5f, 0.5f, 0.5f, 0.7f); // Gray
                break;
            case "park":
            case "open space":
                image.color = new Color(0, 1f, 0, 0.7f); // Green
                break;
            case "cultural":
                image.color = new Color(1f, 0, 1f, 0.7f); // Magenta
                break;
            case "transportation":
                image.color = new Color(1f, 1f, 0, 0.7f); // Yellow
                break;
            case "education":
                image.color = new Color(0, 1f, 1f, 0.7f); // Cyan
                break;
            case "entertainment":
                image.color = new Color(1f, 0, 0, 0.7f); // Red
                break;
            default:
                image.color = new Color(0.8f, 0.8f, 0.8f, 0.7f); // Light gray
                break;
        }

        // Highlight if selected
        if (zone == selectedZone)
        {
            image.color = new Color(image.color.r, image.color.g, image.color.b, 0.9f);
            zoneObj.transform.SetAsLastSibling();
        }

        // Set name
        TextMeshProUGUI nameText = zoneObj.GetComponentInChildren<TextMeshProUGUI>();
        if (nameText != null)
        {
            nameText.text = zone.name;
        }

        // Connect event callbacks
        SetupZoneCallbacks(zoneObj, zone);
    }
    
    // Setup zone callbacks
    private void SetupZoneCallbacks(GameObject zoneObj, CityZone zone)
    {
        EventTrigger trigger = zoneObj.GetComponent<EventTrigger>();
        if (trigger != null && trigger.triggers.Count >= 3)
        {
            // Click event
            trigger.triggers[0].callback = new EventTrigger.TriggerEvent();
            trigger.triggers[0].callback.AddListener((data) => { OnZoneClick(zone, (PointerEventData)data); });

            // Drag event
            trigger.triggers[1].callback = new EventTrigger.TriggerEvent();
            trigger.triggers[1].callback.AddListener((data) => { OnZoneDrag(zone, (PointerEventData)data); });

            // End drag event
            trigger.triggers[2].callback = new EventTrigger.TriggerEvent();
            trigger.triggers[2].callback.AddListener((data) => { OnZoneEndDrag(zone); });
        }
    }

    // Handle zone click
    public void OnZoneClick(CityZone zone, PointerEventData eventData)
    {
        if (zone == null) return;
        
        float currentTime = Time.time;
        bool isDoubleClick = (currentTime - lastClickTime) < doubleClickTime && zone == lastClickedZone;
        
        // Update click tracking
        lastClickTime = currentTime;
        lastClickedZone = zone;
        
        selectedZone = zone;
        isDragging = true;

        try
        {
            // Calculate drag offset
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                mapContainer,
                eventData.position,
                eventData.pressEventCamera,
                out Vector2 localPoint);

            dragOffset = zone.position - localPoint;

            // Update UI
            UpdateSelectedZoneDisplay();

            // Highlight selected zone and reset others
            foreach (var kvp in activeZoneObjects)
            {
                if (kvp.Value == null) continue;
                
                Image image = kvp.Value.GetComponent<Image>();
                if (image == null) continue;
                
                if (kvp.Key == selectedZone)
                {
                    image.color = new Color(image.color.r, image.color.g, image.color.b, 0.9f);
                    kvp.Value.transform.SetAsLastSibling(); // Bring to front
                }
                else
                {
                    image.color = new Color(image.color.r, image.color.g, image.color.b, 0.7f);
                }
            }
            
            // Show/hide impact simulation panel based on double click
            if (impactSimulationPanel != null)
            {
                impactSimulationPanel.SetActive(isDoubleClick);
            }
        }
        catch (Exception e)
        {
            WriteDebugLog($"ERROR in OnZoneClick: {e.Message}");
            isDragging = false;
        }
    }

    // Handle zone drag
    public void OnZoneDrag(CityZone zone, PointerEventData eventData)
    {
        if (isDragging && zone == selectedZone)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                mapContainer,
                eventData.position,
                eventData.pressEventCamera,
                out Vector2 localPoint);

            zone.position = localPoint + dragOffset;
            
            // Update the position of the GameObject
            if (activeZoneObjects.TryGetValue(zone, out GameObject zoneObj))
            {
                RectTransform rectTransform = zoneObj.GetComponent<RectTransform>();
                rectTransform.anchoredPosition = zone.position;
            }
        }
    }

    // Handle end of drag
    public void OnZoneEndDrag(CityZone zone)
    {
        if (zone == selectedZone)
        {
            isDragging = false;
            // Calculate budget impact
            CalculateBudgetImpact();
        }
    }

    // Calculate budget impact of changes (simplified)
    private void CalculateBudgetImpact()
    {
        // This would be more complex in a real app
        UpdateBudgetDisplay();
    }

    // Update budget display
    private void UpdateBudgetDisplay()
    {
        if (budgetText != null)
        {
            string text = "San Jose City Budget\n\n";
            text += "Total: $" + totalBudget.ToString("N0") + " million\n\n";

            // Group by category
            Dictionary<string, float> categoryTotals = new Dictionary<string, float>();
            foreach (var item in budget.Values)
            {
                if (!categoryTotals.ContainsKey(item.category))
                {
                    categoryTotals[item.category] = 0;
                }
                categoryTotals[item.category] += item.allocated;
            }

            // Display categories
            foreach (var category in categoryTotals)
            {
                text += category.Key + ": $" + category.Value.ToString("N0") + " million\n";
            }

            budgetText.text = text;
        }
    }

    // Update selected zone display
    private void UpdateSelectedZoneDisplay()
    {
        if (selectedZoneText != null && selectedZone != null)
        {
            string text = "Selected: " + selectedZone.name + "\n";
            text += "Type: " + selectedZone.type + "\n";
            text += "Cost: $" + selectedZone.cost.ToString("N0") + " million\n\n";

            text += "Impacts:\n";
            foreach (var impact in selectedZone.impacts)
            {
                text += impact.Key + ": " + impact.Value.ToString("N1") + "\n";
            }

            selectedZoneText.text = text;
            infoPanel.SetActive(true);
            
            // Update impact simulation panel if it's visible
            if (impactSimulationPanel != null && impactSimulationPanel.activeSelf)
            {
                // Update impact type dropdown based on toggle state
                if (impactTypeDropdown != null)
                {
                    if (impactViewToggle != null && impactViewToggle.isOn)
                    {
                        UpdateImpactTypeDropdown();
                    }
                    else
                    {
                        impactTypeDropdown.ClearOptions();
                        List<string> impactTypes = new List<string>(selectedZone.impacts.Keys);
                        impactTypeDropdown.AddOptions(impactTypes);
                    }
                }
                
                // Reset inputs
                if (budgetInputField != null) budgetInputField.text = "";
                if (yearsInputField != null) yearsInputField.text = "5";
                if (simulationResultsText != null) simulationResultsText.text = "";
            }
        }
        else if (selectedZoneText != null)
        {
            selectedZoneText.text = "No zone selected";
            infoPanel.SetActive(false);
            
            // Hide impact simulation panel
            if (impactSimulationPanel != null)
            {
                impactSimulationPanel.SetActive(false);
            }
        }
    }

    private void UpdateImpactTypeDropdown()
    {
        if (impactTypeDropdown == null) return;

        // Get the top 3 impacts from the 2024 map data
        Dictionary<string, float> impactScores = new Dictionary<string, float>();
        
        // Read the 2024 map data
        TextAsset csvAsset = Resources.Load<TextAsset>(Path.GetFileNameWithoutExtension(mapData2024Path));
        if (csvAsset != null)
        {
            string[] lines = csvAsset.text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            
            // Skip header line
            for (int i = 1; i < lines.Length; i++)
            {
                string[] tokens = lines[i].Split(',');
                if (tokens.Length >= 10) // Ensure we have enough tokens for impacts
                {
                    // Add Impact1
                    if (!string.IsNullOrEmpty(tokens[5]) && !string.IsNullOrEmpty(tokens[6]))
                    {
                        string impact = tokens[5].Trim();
                        float value = float.Parse(tokens[6]);
                        if (!impactScores.ContainsKey(impact))
                            impactScores[impact] = 0;
                        impactScores[impact] += value;
                    }
                    
                    // Add Impact2
                    if (!string.IsNullOrEmpty(tokens[7]) && !string.IsNullOrEmpty(tokens[8]))
                    {
                        string impact = tokens[7].Trim();
                        float value = float.Parse(tokens[8]);
                        if (!impactScores.ContainsKey(impact))
                            impactScores[impact] = 0;
                        impactScores[impact] += value;
                    }
                    
                    // Add Impact3
                    if (!string.IsNullOrEmpty(tokens[9]) && !string.IsNullOrEmpty(tokens[10]))
                    {
                        string impact = tokens[9].Trim();
                        float value = float.Parse(tokens[10]);
                        if (!impactScores.ContainsKey(impact))
                            impactScores[impact] = 0;
                        impactScores[impact] += value;
                    }
                }
            }
        }

        // Sort impacts by score and get top 3
        var topImpacts = impactScores.OrderByDescending(x => x.Value)
                                   .Take(3)
                                   .Select(x => x.Key)
                                   .ToList();

        // Update dropdown
        impactTypeDropdown.ClearOptions();
        impactTypeDropdown.AddOptions(topImpacts);
    }

    // Update instructions display
    private void UpdateInstructionsDisplay()
    {
        if (instructionsText != null)
        {
            instructionsText.text = "Instructions:\n" +
                "- Click to select a zone\n" +
                "- Double-click to open impact simulation\n" +
                "- Drag to move zones\n" +
                "- See budget impact in panel\n" +
                "- ESC to save and quit";
        }
    }

    // Save state to CSV
    public void SaveState()
    {
        try
        {
            // Create temporary lists
            List<string> lines = new List<string>();
            
            // Header
            lines.Add("Name,X,Y,Type,Cost,Impact1,Value1,Impact2,Value2,Impact3,Value3");
            
            // Zone data
            foreach (CityZone zone in zones)
            {
                string line = zone.name + "," + 
                           zone.position.x + "," + 
                           zone.position.y + "," + 
                           zone.type + "," + 
                           zone.cost;
                           
                // Add impacts (up to 3)
                int impactCount = 0;
                foreach (var impact in zone.impacts)
                {
                    line += "," + impact.Key + "," + impact.Value;
                    impactCount++;
                    if (impactCount >= 3) break;
                }
                
                // Pad with empty impacts if needed
                for (int i = impactCount; i < 3; i++)
                {
                    line += ",,";
                }
                
                lines.Add(line);
            }
            
            // Write to file
            File.WriteAllLines(use2024Map ? mapData2024Path : mapDataPath, lines);
            
            WriteDebugLog("State saved to " + (use2024Map ? mapData2024Path : mapDataPath));
        }
        catch (Exception e)
        {
            WriteDebugLog("ERROR: Error saving state: " + e.Message);
        }
    }

    void Update()
    {
        try
        {
            // Check for ESC key to save and quit
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                WriteDebugLog("ESC pressed - saving and quitting");
                SaveState();
                #if UNITY_EDITOR
                EditorApplication.isPlaying = false;
                #else
                Application.Quit();
                #endif
            }
            
            // Check if we need to update visible zones (when scrolling/panning)
            if (mapScrollRect != null && mapScrollRect.content != null)
            {
                Vector2 currentPos = mapScrollRect.content.anchoredPosition;
                if (Vector2.Distance(currentPos, lastViewportPosition) > scrollThreshold)
                {
                    // Only update if enough time has passed since last update
                    if (Time.time - lastUpdateTime >= updateInterval)
                    {
                        WriteDebugLog("Updating visible zones due to scroll");
                        lastViewportPosition = currentPos;
                        UpdateVisibleZones();
                        lastUpdateTime = Time.time;
                    }
                }
            }
        }
        catch (Exception e)
        {
            WriteDebugLog($"ERROR in Update: {e.Message}\n{e.StackTrace}");
        }
    }

    private void OnDestroy()
    {
        WriteDebugLog("Cleaning up CitySimulator");
        
        try
        {
            // Clean up event listeners
            foreach (var kvp in activeZoneObjects)
            {
                if (kvp.Value != null)
                {
                    EventTrigger trigger = kvp.Value.GetComponent<EventTrigger>();
                    if (trigger != null)
                    {
                        trigger.triggers.Clear();
                    }
                }
            }
            
            // Clear object pool
            while (zoneObjectPool.Count > 0)
            {
                GameObject obj = zoneObjectPool.Dequeue();
                if (obj != null)
                {
                    Destroy(obj);
                }
            }
            
            // Close debug writer
            if (debugWriter != null)
            {
                debugWriter.Close();
                debugWriter = null;
            }
            
            WriteDebugLog("CitySimulator cleanup complete");
        }
        catch (Exception e)
        {
            WriteDebugLog($"ERROR during cleanup: {e.Message}");
        }
    }

    private void OnImpactViewToggleChanged(bool isCityWide)
    {
        if (selectedZone == null) return;
        
        if (isCityWide)
        {
            UpdateImpactTypeDropdown();
        }
        else
        {
            // Show zone-specific impacts
            if (impactTypeDropdown != null)
            {
                impactTypeDropdown.ClearOptions();
                List<string> impactTypes = new List<string>(selectedZone.impacts.Keys);
                impactTypeDropdown.AddOptions(impactTypes);
            }
        }
    }
}

// Structure to represent a city zone
[System.Serializable]
public class CityZone
{
    public string name;
    public Vector2 position;
    public string type;
    public float cost;
    public Dictionary<string, float> impacts = new Dictionary<string, float>();
}

// Budget item representation
[System.Serializable]
public class BudgetItem
{
    public string name;
    public float allocated;
    public float spent;
    public string category;
} 