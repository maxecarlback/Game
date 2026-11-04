using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

public class StartScreenMenu : MonoBehaviour
{
	private const string MenuSceneName = "Start screen";
	private const string GameSceneName = "game";

	private static readonly Color BackgroundColor = new Color(0.055f, 0.075f, 0.07f, 1f);
	private static readonly Color PanelColor = new Color(0.10f, 0.13f, 0.12f, 0.98f);
	private static readonly Color AccentColor = new Color(0.91f, 0.54f, 0.25f, 1f);
	private static readonly Color TextColor = new Color(0.93f, 0.92f, 0.86f, 1f);

	private Text volumeLabel;
	private GameObject optionsPanel;
	private InputField joinAddressInput;
	private Text joinStatusText;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void RegisterSceneListener()
	{
		SceneManager.sceneLoaded -= OnSceneLoaded;
		SceneManager.sceneLoaded += OnSceneLoaded;
	}

	private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		if (scene.name != MenuSceneName || FindAnyObjectByType<StartScreenMenu>() != null)
		{
			return;
		}

		new GameObject("Start Screen Menu").AddComponent<StartScreenMenu>();
	}

	private void Start()
	{
		Cursor.lockState = CursorLockMode.None;
		Cursor.visible = true;
		BuildInterface();
	}

	private void BuildInterface()
	{
		Canvas canvas = new GameObject("Start Screen Canvas").AddComponent<Canvas>();
		canvas.renderMode = RenderMode.ScreenSpaceOverlay;
		canvas.sortingOrder = 10;
		CanvasScaler scaler = canvas.gameObject.AddComponent<CanvasScaler>();
		scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
		scaler.referenceResolution = new Vector2(1920f, 1080f);
		scaler.matchWidthOrHeight = 0.5f;
		canvas.gameObject.AddComponent<GraphicRaycaster>();

		Image background = CreateImage(canvas.transform, "Background", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, BackgroundColor);
		background.raycastTarget = false;
		Image accent = CreateImage(canvas.transform, "Accent", new Vector2(0.075f, 0.18f), new Vector2(0.08f, 0.82f), Vector2.zero, Vector2.zero, AccentColor);
		accent.raycastTarget = false;

		CreateText(canvas.transform, "Kicker", "FIELD / 01", 18, AccentColor, TextAnchor.MiddleLeft,
			new Vector2(0.12f, 0.78f), new Vector2(0.55f, 0.84f), Vector2.zero, Vector2.zero, FontStyle.Bold);
		CreateText(canvas.transform, "Title", "THE UNKNOWN", 72, TextColor, TextAnchor.MiddleLeft,
			new Vector2(0.115f, 0.63f), new Vector2(0.68f, 0.77f), Vector2.zero, Vector2.zero, FontStyle.Bold);
		CreateText(canvas.transform, "Subtitle", "A FIRST-PERSON ADVENTURE", 20, new Color(0.66f, 0.70f, 0.66f), TextAnchor.MiddleLeft,
			new Vector2(0.12f, 0.57f), new Vector2(0.60f, 0.63f), Vector2.zero, Vector2.zero, FontStyle.Normal);

		CreateButton(canvas.transform, "Play", "PLAY", new Vector2(0.12f, 0.42f), true, Play);
		CreateButton(canvas.transform, "Options", "OPTIONS", new Vector2(0.12f, 0.33f), false, ToggleOptions);
		CreateButton(canvas.transform, "Quit", "QUIT", new Vector2(0.12f, 0.24f), false, Quit);
		CreateText(canvas.transform, "Footer", "READY WHEN YOU ARE", 14, new Color(0.48f, 0.54f, 0.50f), TextAnchor.MiddleLeft,
			new Vector2(0.12f, 0.08f), new Vector2(0.48f, 0.12f), Vector2.zero, Vector2.zero, FontStyle.Normal);

		BuildMultiplayerJoinBox(canvas.transform);
		BuildOptionsPanel(canvas.transform);
		EnsureEventSystem();
	}

	private void BuildMultiplayerJoinBox(Transform parent)
	{
		GameObject joinPanel = new GameObject("Join Multiplayer Panel", typeof(RectTransform), typeof(Image));
		joinPanel.transform.SetParent(parent, false);
		SetRect(joinPanel.GetComponent<RectTransform>(), new Vector2(0.52f, 0.24f), new Vector2(0.90f, 0.58f), Vector2.zero, Vector2.zero);
		joinPanel.GetComponent<Image>().color = PanelColor;

		Image accent = CreateImage(joinPanel.transform, "JoinAccent", new Vector2(0f, 0.08f), new Vector2(0.012f, 0.92f), Vector2.zero, Vector2.zero, AccentColor);
		accent.raycastTarget = false;

		CreateText(joinPanel.transform, "Join Title", "JOIN MULTIPLAYER", 26, TextColor, TextAnchor.MiddleLeft,
			new Vector2(0.07f, 0.74f), new Vector2(0.93f, 0.90f), Vector2.zero, Vector2.zero, FontStyle.Bold);

		CreateText(joinPanel.transform, "Join Subtitle", "ENTER HOST IP:PORT", 14, new Color(0.66f, 0.70f, 0.66f), TextAnchor.MiddleLeft,
			new Vector2(0.07f, 0.60f), new Vector2(0.93f, 0.72f), Vector2.zero, Vector2.zero, FontStyle.Bold);

		GameObject inputObj = new GameObject("IP InputField", typeof(RectTransform), typeof(Image), typeof(InputField));
		inputObj.transform.SetParent(joinPanel.transform, false);
		SetRect(inputObj.GetComponent<RectTransform>(), new Vector2(0.07f, 0.38f), new Vector2(0.72f, 0.56f), Vector2.zero, Vector2.zero);
		Image inputBg = inputObj.GetComponent<Image>();
		inputBg.color = new Color(0.06f, 0.08f, 0.075f, 1f);

		Text placeholder = CreateText(inputObj.transform, "Placeholder", "e.g. 127.0.0.1:7777", 18, new Color(0.45f, 0.50f, 0.47f, 0.85f), TextAnchor.MiddleLeft,
			new Vector2(0.05f, 0.08f), new Vector2(0.95f, 0.92f), Vector2.zero, Vector2.zero, FontStyle.Italic);

		Text inputText = CreateText(inputObj.transform, "Text", "", 19, TextColor, TextAnchor.MiddleLeft,
			new Vector2(0.05f, 0.08f), new Vector2(0.95f, 0.92f), Vector2.zero, Vector2.zero, FontStyle.Bold);
		inputText.supportRichText = false;

		joinAddressInput = inputObj.GetComponent<InputField>();
		joinAddressInput.targetGraphic = inputBg;
		joinAddressInput.textComponent = inputText;
		joinAddressInput.placeholder = placeholder;
		joinAddressInput.lineType = InputField.LineType.SingleLine;
		joinAddressInput.text = "127.0.0.1:7777";

		CreateJoinPanelButton(joinPanel.transform, "Paste", "PASTE", new Vector2(0.74f, 0.38f), new Vector2(0.93f, 0.56f), false, PasteClipboardAddress);
		CreateJoinPanelButton(joinPanel.transform, "Join", "JOIN GAME", new Vector2(0.07f, 0.14f), new Vector2(0.93f, 0.31f), true, JoinMultiplayerGame);

		joinStatusText = CreateText(joinPanel.transform, "JoinStatus", "", 13, AccentColor, TextAnchor.MiddleLeft,
			new Vector2(0.07f, 0.02f), new Vector2(0.93f, 0.12f), Vector2.zero, Vector2.zero, FontStyle.Normal);
	}

	private void CreateJoinPanelButton(Transform parent, string name, string label, Vector2 anchorMin, Vector2 anchorMax, bool primary, UnityEngine.Events.UnityAction action)
	{
		Button button = new GameObject(name + " Button", typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<Button>();
		button.transform.SetParent(parent, false);
		SetRect(button.GetComponent<RectTransform>(), anchorMin, anchorMax, Vector2.zero, Vector2.zero);

		Image image = button.GetComponent<Image>();
		image.color = primary ? AccentColor : new Color(0.16f, 0.21f, 0.19f, 1f);
		button.targetGraphic = image;
		ColorBlock colors = button.colors;
		colors.normalColor = image.color;
		colors.highlightedColor = primary ? new Color(1f, 0.66f, 0.34f) : new Color(0.23f, 0.29f, 0.26f);
		colors.pressedColor = new Color(0.72f, 0.39f, 0.19f);
		colors.selectedColor = colors.highlightedColor;
		button.colors = colors;
		button.onClick.AddListener(action);

		CreateText(button.transform, "Label", label, 17, primary ? BackgroundColor : TextColor, TextAnchor.MiddleCenter,
			new Vector2(0.04f, 0f), new Vector2(0.96f, 1f), Vector2.zero, Vector2.zero, FontStyle.Bold);
	}

	private void PasteClipboardAddress()
	{
		if (joinAddressInput != null && !string.IsNullOrWhiteSpace(GUIUtility.systemCopyBuffer))
		{
			joinAddressInput.text = GUIUtility.systemCopyBuffer.Trim();
		}
	}

	private void JoinMultiplayerGame()
	{
		string address = joinAddressInput != null ? joinAddressInput.text.Trim() : "";
		if (!MultiplayerManager.TryParseAddress(address, out _, out _))
		{
			if (joinStatusText != null)
			{
				joinStatusText.text = "Enter a valid IP:Port (e.g. 192.168.1.10:7777)";
			}
			return;
		}

		MultiplayerManager.JoinFromMenu(address);
	}

	private void BuildOptionsPanel(Transform parent)
	{
		optionsPanel = new GameObject("Options Panel", typeof(RectTransform), typeof(Image));
		optionsPanel.transform.SetParent(parent, false);
		SetRect(optionsPanel.GetComponent<RectTransform>(), new Vector2(0.57f, 0.22f), new Vector2(0.90f, 0.76f), Vector2.zero, Vector2.zero);
		optionsPanel.GetComponent<Image>().color = PanelColor;

		CreateText(optionsPanel.transform, "Options Title", "OPTIONS", 30, TextColor, TextAnchor.MiddleLeft,
			new Vector2(0.08f, 0.78f), new Vector2(0.92f, 0.92f), Vector2.zero, Vector2.zero, FontStyle.Bold);
		volumeLabel = CreateText(optionsPanel.transform, "Volume Label", "VOLUME  100%", 18, TextColor, TextAnchor.MiddleLeft,
			new Vector2(0.08f, 0.60f), new Vector2(0.92f, 0.70f), Vector2.zero, Vector2.zero, FontStyle.Normal);
		CreateVolumeSlider(optionsPanel.transform);
		CreateFullscreenToggle(optionsPanel.transform);
		CreateButton(optionsPanel.transform, "Back", "BACK", new Vector2(0.08f, 0.10f), false, ToggleOptions);
		optionsPanel.SetActive(false);
	}

	private void CreateVolumeSlider(Transform parent)
	{
		Slider slider = new GameObject("Volume Slider", typeof(RectTransform), typeof(Image), typeof(Slider)).GetComponent<Slider>();
		slider.transform.SetParent(parent, false);
		SetRect(slider.GetComponent<RectTransform>(), new Vector2(0.08f, 0.48f), new Vector2(0.92f, 0.54f), Vector2.zero, Vector2.zero);
		slider.GetComponent<Image>().color = new Color(0.23f, 0.28f, 0.25f, 1f);
		slider.minValue = 0f;
		slider.maxValue = 1f;
		slider.value = AudioListener.volume;

		Image fill = CreateImage(slider.transform, "Fill", Vector2.zero, Vector2.one, new Vector2(2f, 2f), new Vector2(-4f, -4f), AccentColor);
		Image handle = CreateImage(slider.transform, "Handle", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(18f, 18f), Vector2.zero, TextColor);
		slider.fillRect = fill.rectTransform;
		slider.handleRect = handle.rectTransform;
		slider.targetGraphic = handle;
		slider.onValueChanged.AddListener(value =>
		{
			AudioListener.volume = value;
			volumeLabel.text = "VOLUME  " + Mathf.RoundToInt(value * 100f) + "%";
		});
		volumeLabel.text = "VOLUME  " + Mathf.RoundToInt(slider.value * 100f) + "%";
	}

	private void CreateFullscreenToggle(Transform parent)
	{
		Toggle toggle = new GameObject("Fullscreen Toggle", typeof(RectTransform), typeof(Toggle)).GetComponent<Toggle>();
		toggle.transform.SetParent(parent, false);
		SetRect(toggle.GetComponent<RectTransform>(), new Vector2(0.08f, 0.30f), new Vector2(0.92f, 0.40f), Vector2.zero, Vector2.zero);

		Image box = CreateImage(toggle.transform, "Checkbox", new Vector2(0f, 0.17f), new Vector2(0f, 0.83f), new Vector2(30f, 30f), Vector2.zero, new Color(0.23f, 0.28f, 0.25f, 1f));
		Image check = CreateImage(box.transform, "Check", new Vector2(0.2f, 0.2f), new Vector2(0.8f, 0.8f), Vector2.zero, Vector2.zero, AccentColor);
		toggle.targetGraphic = box;
		toggle.graphic = check;
		toggle.isOn = Screen.fullScreen;
		toggle.onValueChanged.AddListener(value => Screen.fullScreen = value);
		CreateText(toggle.transform, "Fullscreen Label", "FULLSCREEN", 18, TextColor, TextAnchor.MiddleLeft,
			new Vector2(0.14f, 0f), new Vector2(0.92f, 1f), Vector2.zero, Vector2.zero, FontStyle.Normal);
	}

	private void CreateButton(Transform parent, string name, string label, Vector2 anchor, bool primary, UnityEngine.Events.UnityAction action)
	{
		Button button = new GameObject(name + " Button", typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<Button>();
		button.transform.SetParent(parent, false);
		SetRect(button.GetComponent<RectTransform>(), anchor, anchor + new Vector2(parent == optionsPanel?.transform ? 0.84f : 0.34f, 0.065f), Vector2.zero, Vector2.zero);

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

		CreateText(button.transform, "Label", label, 19, primary ? BackgroundColor : TextColor, TextAnchor.MiddleLeft,
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

	private static void Play()
	{
		SceneManager.LoadScene(GameSceneName);
	}

	private void ToggleOptions()
	{
		optionsPanel.SetActive(!optionsPanel.activeSelf);
	}

	private static void Quit()
	{
#if UNITY_EDITOR
		UnityEditor.EditorApplication.isPlaying = false;
#else
		Application.Quit();
#endif
	}
}