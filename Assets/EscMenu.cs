using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

[DisallowMultipleComponent]
public class EscMenu : MonoBehaviour
{
	private const string MenuSceneName = "Start screen";
	private const string GameSceneName = "game";

	private static readonly Color OverlayColor = new Color(0.035f, 0.05f, 0.045f, 0.78f);
	private static readonly Color PanelColor = new Color(0.10f, 0.13f, 0.12f, 0.98f);
	private static readonly Color AccentColor = new Color(0.91f, 0.54f, 0.25f, 1f);
	private static readonly Color TextColor = new Color(0.93f, 0.92f, 0.86f, 1f);
	private static readonly Color MutedTextColor = new Color(0.66f, 0.70f, 0.66f, 1f);

	public static bool IsPaused { get; private set; }

	private GameObject menuRoot;
	private GameObject optionsPanel;
	private GameObject multiplayerPanel;
	private Text multiplayerIpLabel;
	private Text multiplayerStatusLabel;
	private Text copyFeedbackLabel;
	private Text volumeLabel;
	private Slider volumeSlider;
	private Toggle fullscreenToggle;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void RegisterSceneListener()
	{
		SceneManager.sceneLoaded -= OnSceneLoaded;
		SceneManager.sceneLoaded += OnSceneLoaded;
	}

	private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		IsPaused = false;
		Time.timeScale = 1f;

		if (scene.name == MenuSceneName || FindAnyObjectByType<EscMenu>() != null)
		{
			return;
		}

		new GameObject("Esc Menu").AddComponent<EscMenu>();
	}

	private void Awake()
	{
		IsPaused = false;
		BuildInterface();
		SetPaused(false);
	}

	private void OnDestroy()
	{
		if (MultiplayerManager.Instance != null)
		{
			MultiplayerManager.Instance.OnStatusChanged -= RefreshMultiplayerLabels;
		}

		if (IsPaused)
		{
			IsPaused = false;
			Time.timeScale = 1f;
		}
	}

	private void Update()
	{
		if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
		{
			if (IsPaused && optionsPanel != null && optionsPanel.activeSelf)
			{
				optionsPanel.SetActive(false);
			}
			else if (IsPaused && multiplayerPanel != null && multiplayerPanel.activeSelf)
			{
				multiplayerPanel.SetActive(false);
			}
			else
			{
				SetPaused(!IsPaused);
			}
		}
	}

	public void SetPaused(bool paused)
	{
		IsPaused = paused;
		bool inMultiplayer = MultiplayerManager.Instance != null
			&& (MultiplayerManager.Instance.IsHosting || MultiplayerManager.Instance.IsClient);
		Time.timeScale = (paused && !inMultiplayer) ? 0f : 1f;

		if (menuRoot != null)
		{
			menuRoot.SetActive(paused);
		}

		if (!paused)
		{
			if (optionsPanel != null) optionsPanel.SetActive(false);
			if (multiplayerPanel != null) multiplayerPanel.SetActive(false);
		}

		if (paused)
		{
			if (volumeSlider != null)
			{
				volumeSlider.SetValueWithoutNotify(AudioListener.volume);
			}

			if (volumeLabel != null)
			{
				volumeLabel.text = "VOLUME  " + Mathf.RoundToInt(AudioListener.volume * 100f) + "%";
			}

			if (fullscreenToggle != null)
			{
				fullscreenToggle.SetIsOnWithoutNotify(Screen.fullScreen);
			}

			Cursor.lockState = CursorLockMode.None;
			Cursor.visible = true;
		}
		else
		{
			Cursor.lockState = CursorLockMode.Locked;
			Cursor.visible = false;
		}
	}

	private void BuildInterface()
	{
		Canvas canvas = new GameObject("Esc Menu Canvas").AddComponent<Canvas>();
		canvas.transform.SetParent(transform, false);
		canvas.renderMode = RenderMode.ScreenSpaceOverlay;
		canvas.sortingOrder = 20;

		CanvasScaler scaler = canvas.gameObject.AddComponent<CanvasScaler>();
		scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
		scaler.referenceResolution = new Vector2(1920f, 1080f);
		scaler.matchWidthOrHeight = 0.5f;
		canvas.gameObject.AddComponent<GraphicRaycaster>();

		menuRoot = new GameObject("Menu Root", typeof(RectTransform));
		menuRoot.transform.SetParent(canvas.transform, false);
		SetRect(menuRoot.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

		Image overlay = CreateImage(menuRoot.transform, "Overlay", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, OverlayColor);
		overlay.raycastTarget = true;

		GameObject mainPanel = new GameObject("Main Panel", typeof(RectTransform), typeof(Image));
		mainPanel.transform.SetParent(menuRoot.transform, false);
		SetRect(mainPanel.GetComponent<RectTransform>(), new Vector2(0.08f, 0.16f), new Vector2(0.46f, 0.84f), Vector2.zero, Vector2.zero);
		mainPanel.GetComponent<Image>().color = PanelColor;

		Image accent = CreateImage(mainPanel.transform, "Accent", new Vector2(0f, 0.08f), new Vector2(0.014f, 0.92f), Vector2.zero, Vector2.zero, AccentColor);
		accent.raycastTarget = false;

		CreateText(mainPanel.transform, "Kicker", "FIELD / 01", 16, AccentColor, TextAnchor.MiddleLeft,
			new Vector2(0.10f, 0.82f), new Vector2(0.90f, 0.89f), Vector2.zero, Vector2.zero, FontStyle.Bold);
		CreateText(mainPanel.transform, "Title", "PAUSED", 54, TextColor, TextAnchor.MiddleLeft,
			new Vector2(0.10f, 0.68f), new Vector2(0.90f, 0.82f), Vector2.zero, Vector2.zero, FontStyle.Bold);
		CreateText(mainPanel.transform, "Subtitle", "PRESS ESC TO RESUME", 16, MutedTextColor, TextAnchor.MiddleLeft,
			new Vector2(0.10f, 0.61f), new Vector2(0.90f, 0.67f), Vector2.zero, Vector2.zero, FontStyle.Normal);

		CreateButton(mainPanel.transform, "Resume", "RESUME", new Vector2(0.10f, 0.49f), new Vector2(0.90f, 0.57f), true, () => SetPaused(false));
		CreateButton(mainPanel.transform, "Multiplayer", "OPEN TO MULTIPLAYER", new Vector2(0.10f, 0.39f), new Vector2(0.90f, 0.47f), false, OpenToMultiplayer);
		CreateButton(mainPanel.transform, "Options", "OPTIONS", new Vector2(0.10f, 0.29f), new Vector2(0.90f, 0.37f), false, ToggleOptions);
		CreateButton(mainPanel.transform, "MainMenu", "MAIN MENU", new Vector2(0.10f, 0.19f), new Vector2(0.90f, 0.27f), false, LoadMainMenu);
		CreateButton(mainPanel.transform, "Quit", "QUIT", new Vector2(0.10f, 0.09f), new Vector2(0.90f, 0.17f), false, Quit);

		BuildOptionsPanel(menuRoot.transform);
		BuildMultiplayerPanel(menuRoot.transform);
		EnsureEventSystem();

		MultiplayerManager mgr = MultiplayerManager.EnsureInstance();
		mgr.OnStatusChanged -= RefreshMultiplayerLabels;
		mgr.OnStatusChanged += RefreshMultiplayerLabels;
	}

	private void BuildOptionsPanel(Transform parent)
	{
		optionsPanel = new GameObject("Options Panel", typeof(RectTransform), typeof(Image));
		optionsPanel.transform.SetParent(parent, false);
		SetRect(optionsPanel.GetComponent<RectTransform>(), new Vector2(0.50f, 0.22f), new Vector2(0.86f, 0.78f), Vector2.zero, Vector2.zero);
		optionsPanel.GetComponent<Image>().color = PanelColor;

		CreateText(optionsPanel.transform, "Options Title", "OPTIONS", 30, TextColor, TextAnchor.MiddleLeft,
			new Vector2(0.08f, 0.78f), new Vector2(0.92f, 0.92f), Vector2.zero, Vector2.zero, FontStyle.Bold);
		volumeLabel = CreateText(optionsPanel.transform, "Volume Label", "VOLUME  100%", 18, TextColor, TextAnchor.MiddleLeft,
			new Vector2(0.08f, 0.60f), new Vector2(0.92f, 0.70f), Vector2.zero, Vector2.zero, FontStyle.Normal);
		CreateVolumeSlider(optionsPanel.transform);
		CreateFullscreenToggle(optionsPanel.transform);
		CreateButton(optionsPanel.transform, "Back", "BACK", new Vector2(0.08f, 0.10f), new Vector2(0.92f, 0.20f), false, ToggleOptions);
		optionsPanel.SetActive(false);
	}

	private void CreateVolumeSlider(Transform parent)
	{
		volumeSlider = new GameObject("Volume Slider", typeof(RectTransform), typeof(Image), typeof(Slider)).GetComponent<Slider>();
		volumeSlider.transform.SetParent(parent, false);
		SetRect(volumeSlider.GetComponent<RectTransform>(), new Vector2(0.08f, 0.48f), new Vector2(0.92f, 0.54f), Vector2.zero, Vector2.zero);
		volumeSlider.GetComponent<Image>().color = new Color(0.23f, 0.28f, 0.25f, 1f);
		volumeSlider.minValue = 0f;
		volumeSlider.maxValue = 1f;
		volumeSlider.value = AudioListener.volume;

		Image fill = CreateImage(volumeSlider.transform, "Fill", Vector2.zero, Vector2.one, new Vector2(2f, 2f), new Vector2(-4f, -4f), AccentColor);
		Image handle = CreateImage(volumeSlider.transform, "Handle", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(18f, 18f), Vector2.zero, TextColor);
		volumeSlider.fillRect = fill.rectTransform;
		volumeSlider.handleRect = handle.rectTransform;
		volumeSlider.targetGraphic = handle;
		volumeSlider.onValueChanged.AddListener(value =>
		{
			AudioListener.volume = value;
			volumeLabel.text = "VOLUME  " + Mathf.RoundToInt(value * 100f) + "%";
		});
		volumeLabel.text = "VOLUME  " + Mathf.RoundToInt(volumeSlider.value * 100f) + "%";
	}

	private void CreateFullscreenToggle(Transform parent)
	{
		fullscreenToggle = new GameObject("Fullscreen Toggle", typeof(RectTransform), typeof(Toggle)).GetComponent<Toggle>();
		fullscreenToggle.transform.SetParent(parent, false);
		SetRect(fullscreenToggle.GetComponent<RectTransform>(), new Vector2(0.08f, 0.30f), new Vector2(0.92f, 0.40f), Vector2.zero, Vector2.zero);

		Image box = CreateImage(fullscreenToggle.transform, "Checkbox", new Vector2(0f, 0.17f), new Vector2(0f, 0.83f), new Vector2(30f, 30f), Vector2.zero, new Color(0.23f, 0.28f, 0.25f, 1f));
		Image check = CreateImage(box.transform, "Check", new Vector2(0.2f, 0.2f), new Vector2(0.8f, 0.8f), Vector2.zero, Vector2.zero, AccentColor);
		fullscreenToggle.targetGraphic = box;
		fullscreenToggle.graphic = check;
		fullscreenToggle.isOn = Screen.fullScreen;
		fullscreenToggle.onValueChanged.AddListener(value => Screen.fullScreen = value);
		CreateText(fullscreenToggle.transform, "Fullscreen Label", "FULLSCREEN", 18, TextColor, TextAnchor.MiddleLeft,
			new Vector2(0.14f, 0f), new Vector2(0.92f, 1f), Vector2.zero, Vector2.zero, FontStyle.Normal);
	}

	private static void CreateButton(Transform parent, string name, string label, Vector2 anchorMin, Vector2 anchorMax, bool primary, UnityEngine.Events.UnityAction action)
	{
		Button button = new GameObject(name + " Button", typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<Button>();
		button.transform.SetParent(parent, false);
		SetRect(button.GetComponent<RectTransform>(), anchorMin, anchorMax, Vector2.zero, Vector2.zero);

		Image image = button.GetComponent<Image>();
		image.color = primary ? AccentColor : new Color(0.14f, 0.18f, 0.16f, 1f);
		button.targetGraphic = image;
		ColorBlock colors = button.colors;
		colors.normalColor = image.color;
		colors.highlightedColor = primary ? new Color(1f, 0.66f, 0.34f) : new Color(0.23f, 0.29f, 0.26f);
		colors.pressedColor = new Color(0.72f, 0.39f, 0.19f);
		colors.selectedColor = colors.highlightedColor;
		button.colors = colors;
		button.onClick.AddListener(action);

		CreateText(button.transform, "Label", label, 19, primary ? OverlayColor : TextColor, TextAnchor.MiddleLeft,
			new Vector2(0.08f, 0f), new Vector2(0.92f, 1f), Vector2.zero, Vector2.zero, FontStyle.Bold);
	}

	private static Text CreateText(Transform parent, string name, string value, int fontSize, Color color, TextAnchor alignment,
		Vector2 anchorMin, Vector2 anchorMax, Vector2 size, Vector2 position, FontStyle style)
	{
		Text text = new GameObject(name, typeof(RectTransform), typeof(Text)).GetComponent<Text>();
		text.transform.SetParent(parent, false);
		SetRect(text.rectTransform, anchorMin, anchorMax, size, position);
		text.text = value;
		text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
		text.fontSize = fontSize;
		text.fontStyle = style;
		text.color = color;
		text.alignment = alignment;
		text.horizontalOverflow = HorizontalWrapMode.Wrap;
		text.verticalOverflow = VerticalWrapMode.Truncate;
		text.raycastTarget = false;
		return text;
	}

	private static Image CreateImage(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 size, Vector2 position, Color color)
	{
		Image image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
		image.transform.SetParent(parent, false);
		SetRect(image.rectTransform, anchorMin, anchorMax, size, position);
		image.color = color;
		return image;
	}

	private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 size, Vector2 position)
	{
		rect.anchorMin = anchorMin;
		rect.anchorMax = anchorMax;
		rect.sizeDelta = size;
		rect.anchoredPosition = position;
	}

	private static void EnsureEventSystem()
	{
		if (EventSystem.current != null)
		{
			return;
		}

		EventSystem eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule)).GetComponent<EventSystem>();
		eventSystem.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
	}

	private void ToggleOptions()
	{
		if (multiplayerPanel != null)
		{
			multiplayerPanel.SetActive(false);
		}

		if (optionsPanel != null)
		{
			optionsPanel.SetActive(!optionsPanel.activeSelf);
		}
	}

	private void BuildMultiplayerPanel(Transform parent)
	{
		multiplayerPanel = new GameObject("Multiplayer Panel", typeof(RectTransform), typeof(Image));
		multiplayerPanel.transform.SetParent(parent, false);
		SetRect(multiplayerPanel.GetComponent<RectTransform>(), new Vector2(0.50f, 0.18f), new Vector2(0.90f, 0.82f), Vector2.zero, Vector2.zero);
		multiplayerPanel.GetComponent<Image>().color = PanelColor;

		CreateText(multiplayerPanel.transform, "MP Title", "MULTIPLAYER HOST", 28, TextColor, TextAnchor.MiddleLeft,
			new Vector2(0.08f, 0.82f), new Vector2(0.92f, 0.93f), Vector2.zero, Vector2.zero, FontStyle.Bold);

		CreateText(multiplayerPanel.transform, "MP Subtitle", "SHARE THIS IP:PORT WITH OTHER PLAYERS:", 15, MutedTextColor, TextAnchor.MiddleLeft,
			new Vector2(0.08f, 0.72f), new Vector2(0.92f, 0.80f), Vector2.zero, Vector2.zero, FontStyle.Bold);

		Image ipBox = CreateImage(multiplayerPanel.transform, "IP Box", new Vector2(0.08f, 0.56f), new Vector2(0.92f, 0.71f), Vector2.zero, Vector2.zero, new Color(0.06f, 0.08f, 0.075f, 1f));
		ipBox.raycastTarget = false;

		multiplayerIpLabel = CreateText(multiplayerPanel.transform, "IP Value", "OPENING...", 26, AccentColor, TextAnchor.MiddleCenter,
			new Vector2(0.10f, 0.56f), new Vector2(0.90f, 0.71f), Vector2.zero, Vector2.zero, FontStyle.Bold);

		multiplayerStatusLabel = CreateText(multiplayerPanel.transform, "MP Status", "Initializing NAT hole-punching & UPnP...", 15, TextColor, TextAnchor.UpperLeft,
			new Vector2(0.08f, 0.36f), new Vector2(0.92f, 0.53f), Vector2.zero, Vector2.zero, FontStyle.Normal);

		copyFeedbackLabel = CreateText(multiplayerPanel.transform, "Copy Feedback", "", 14, AccentColor, TextAnchor.MiddleLeft,
			new Vector2(0.08f, 0.23f), new Vector2(0.92f, 0.30f), Vector2.zero, Vector2.zero, FontStyle.Bold);

		CreateButton(multiplayerPanel.transform, "CopyIP", "COPY IP:PORT", new Vector2(0.08f, 0.24f), new Vector2(0.54f, 0.34f), true, CopyMultiplayerAddress);
		CreateButton(multiplayerPanel.transform, "CloseMP", "BACK", new Vector2(0.58f, 0.24f), new Vector2(0.92f, 0.34f), false, () => multiplayerPanel.SetActive(false));

		CreateText(multiplayerPanel.transform, "Port Note", "No router port forwarding required (UPnP + STUN Hole-Punching)", 13, MutedTextColor, TextAnchor.MiddleLeft,
			new Vector2(0.08f, 0.08f), new Vector2(0.92f, 0.18f), Vector2.zero, Vector2.zero, FontStyle.Normal);

		multiplayerPanel.SetActive(false);
	}

	private void OpenToMultiplayer()
	{
		if (optionsPanel != null)
		{
			optionsPanel.SetActive(false);
		}

		if (multiplayerPanel != null)
		{
			multiplayerPanel.SetActive(true);
		}

		Time.timeScale = 1f;
		MultiplayerManager mgr = MultiplayerManager.EnsureInstance();
		if (!mgr.IsHosting)
		{
			mgr.StartHosting();
		}

		RefreshMultiplayerLabels();
	}

	private void CopyMultiplayerAddress()
	{
		MultiplayerManager mgr = MultiplayerManager.Instance;
		if (mgr != null && !string.IsNullOrEmpty(mgr.DisplayAddress))
		{
			GUIUtility.systemCopyBuffer = mgr.DisplayAddress;
			if (copyFeedbackLabel != null)
			{
				copyFeedbackLabel.text = "COPIED: " + mgr.DisplayAddress;
			}
		}
	}

	private void RefreshMultiplayerLabels()
	{
		MultiplayerManager mgr = MultiplayerManager.Instance;
		if (mgr == null)
		{
			return;
		}

		if (multiplayerIpLabel != null)
		{
			multiplayerIpLabel.text = !string.IsNullOrEmpty(mgr.DisplayAddress) ? mgr.DisplayAddress : "OPENING...";
		}

		if (multiplayerStatusLabel != null)
		{
			string lanInfo = !string.IsNullOrEmpty(mgr.LocalIpPort) ? $"LAN IP:Port: {mgr.LocalIpPort}\n" : "";
			string pubInfo = !string.IsNullOrEmpty(mgr.PublicIpPort) ? $"Public IP:Port: {mgr.PublicIpPort}\n" : "";
			multiplayerStatusLabel.text = $"{pubInfo}{lanInfo}Players Connected: {mgr.ConnectedPeerCount + 1}";
		}
	}

	private void LoadMainMenu()
	{
		SetPaused(false);
		SceneManager.LoadScene(MenuSceneName);
	}

	private static void Quit()
	{
		Time.timeScale = 1f;
#if UNITY_EDITOR
		UnityEditor.EditorApplication.isPlaying = false;
#else
		Application.Quit();
#endif
	}
}

