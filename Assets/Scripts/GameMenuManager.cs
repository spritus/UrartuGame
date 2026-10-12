using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Collections;
using System.Collections.Generic;

public class GameMenuManager : MonoBehaviour
{
    public static GameMenuManager Instance { get; private set; }

    /// <summary>
    /// Menünün açık olup olmadığını belirten global durum
    /// </summary>
    public static bool IsMenuOpen { get; private set; } = true;

    [Header("Müzik Ayarları")]
    public AudioClip bgMusicClip;
    private AudioSource musicSource;
    private AudioSource sfxSource;
    private bool isMusicEnabled = true;

    [Header("Bulanıklık ve Post-Processing")]
    private Volume blurVolume;
    private DepthOfField dofComponent;
    private Image backdropDimImage;

    [Header("Menü Kutusu Filigranı")]
    [Tooltip("Menü kutusunun içinde filigran olarak gösterilecek görsel (Assets/Images/BGmenu.jpg)")]
    public Texture2D menuBackgroundTexture;
    [Tooltip("Menü kutusundaki filigran saydamlık oranı (Varsayılan: 0.30 = %30)")]
    [Range(0.05f, 1f)]
    public float watermarkOpacity = 0.30f;
    private RawImage boxWatermarkImage;

    [Header("Karakter ve Kayıt")]
    private HeroController hero;
    private Vector3 initialSpawnPosition;
    private Quaternion initialSpawnRotation;
    private bool hasGameStarted = false;
    private float autoSaveTimer = 0f;

    [Header("UI Elemanları")]
    private Canvas menuCanvas;
    private GameObject menuContainer;
    private RectTransform menuContainerRect;
    private CanvasGroup menuCanvasGroup;
    private Coroutine menuAnimationCoroutine;
    private bool isAnimating = false;

    [Header("Menü Animasyonu (Üst Uzaktan - Ön Ortaya)")]
    [Tooltip("Açılma animasyonu süresi (saniye)")]
    public float openAnimDuration = 0.45f;
    [Tooltip("Kapanma animasyonu süresi (saniye)")]
    public float closeAnimDuration = 0.35f;

    [Header("Alt Paneller")]
    private GameObject mainMenuContent;
    private GameObject howToPlayContent;
    private bool isHowToPlayOpen = false;

    private List<MenuButtonData> menuButtons = new List<MenuButtonData>();
    private int selectedIndex = 0;
    private Text musicButtonText;

    private AudioClip hoverSfxClip;
    private AudioClip selectSfxClip;

    private class MenuButtonData
    {
        public int index;
        public GameObject buttonObj;
        public Image backgroundImage;
        public Outline borderOutline;
        public Text labelText;
        public string baseTitle;
        public bool isMusicButton;
        public System.Action action;
    }

    private void Awake()
    {
        if (Instance != null && Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Müzik ayarını yükle (Varsayılan: 1 = Açık)
        isMusicEnabled = PlayerPrefs.GetInt("Urartu_MusicEnabled", 1) == 1;

        // SFX ses kliplerini üret
        hoverSfxClip = CreateToneClip(580f, 0.05f);
        selectSfxClip = CreateToneClip(880f, 0.10f);

        SetupAudio();
        SetupBlurVolume();
        SetupUI();

        // Oyun ilk açıldığında menü mutlaka açık başlasın
        IsMenuOpen = true;
    }

    private void Start()
    {
        FindHeroAndSpawnPoint();

        // Müzik açıksa çalmaya başla
        if (musicSource != null && isMusicEnabled && bgMusicClip != null)
        {
            musicSource.Play();
        }

        // Başlangıçta kamerayı daima karakterin arkasına hizala (önünde değil)
        CameraFollow cam = FindAnyObjectByType<CameraFollow>();
        if (cam != null) cam.AlignBehindTarget();

        // Başlangıçta menüyü üst uzaktan ön ortaya animasyonla getir
        ShowMenu(true);
    }

    private void Update()
    {
        // ESC tuşu kontrolü (Menüyü aç/kapat) - Sadece Input System kullanarak
        bool escPressed = false;
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            escPressed = true;
        }

        if (escPressed)
        {
            if (isHowToPlayOpen)
            {
                // Nasıl Oynanır ekranındayken ESC basılırsa Ana Menüye dön
                CloseHowToPlay(true);
                return;
            }

            if (IsMenuOpen)
            {
                // Eğer oyun zaten daha önce başlatıldıysa ESC ile oyuna dönülebilir
                if (hasGameStarted)
                {
                    ResumeGame();
                }
            }
            else
            {
                // Oyundayken ESC'ye basılırsa menüyü aç ve mevcut konumu kaydet
                SaveCurrentPlayerPosition();
                ShowMenu(true);
            }
        }

        // Menü açıkken navigasyon ve seçim kontrolleri
        if (IsMenuOpen)
        {
            if (isHowToPlayOpen)
            {
                // Nasıl Oynanır ekranında Enter veya Boşluk ile Ana Menüye dön
                if (Keyboard.current != null && (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.spaceKey.wasPressedThisFrame))
                {
                    CloseHowToPlay(true);
                }
            }
            else
            {
                HandleMenuNavigation();
            }
        }
        else
        {
            // Oyun sırasında periyodik otomatik kayıt (5 saniyede bir)
            if (hasGameStarted && hero != null)
            {
                autoSaveTimer += Time.unscaledDeltaTime;
                if (autoSaveTimer >= 5f)
                {
                    autoSaveTimer = 0f;
                    SaveCurrentPlayerPosition();
                }
            }
        }
    }

    /// <summary>
    /// Klavyeden Yukarı/Aşağı yön tuşları, W/S ve Enter/Space kontrolleri (Input System)
    /// </summary>
    private void HandleMenuNavigation()
    {
        bool upPressed = false;
        bool downPressed = false;
        bool confirmPressed = false;

        if (Keyboard.current != null)
        {
            if (Keyboard.current.upArrowKey.wasPressedThisFrame || Keyboard.current.wKey.wasPressedThisFrame) upPressed = true;
            if (Keyboard.current.downArrowKey.wasPressedThisFrame || Keyboard.current.sKey.wasPressedThisFrame) downPressed = true;
            if (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame || Keyboard.current.spaceKey.wasPressedThisFrame) confirmPressed = true;
        }

        if (upPressed && menuButtons.Count > 0)
        {
            selectedIndex = (selectedIndex - 1 + menuButtons.Count) % menuButtons.Count;
            PlaySfx(hoverSfxClip);
            UpdateVisualHighlights();
        }
        else if (downPressed && menuButtons.Count > 0)
        {
            selectedIndex = (selectedIndex + 1) % menuButtons.Count;
            PlaySfx(hoverSfxClip);
            UpdateVisualHighlights();
        }

        if (confirmPressed && menuButtons.Count > 0)
        {
            PlaySfx(selectSfxClip);
            ExecuteButton(selectedIndex);
        }

        // Fare ile doğrudan hover ve sol tık takibi
        HandleMouseInput();
    }

    private void HandleMouseInput()
    {
        if (isHowToPlayOpen) return;
        if (Mouse.current == null) return;

        Vector2 mousePos = Mouse.current.position.ReadValue();
        bool leftClicked = Mouse.current.leftButton.wasPressedThisFrame;

        for (int i = 0; i < menuButtons.Count; i++)
        {
            RectTransform rect = menuButtons[i].buttonObj.GetComponent<RectTransform>();
            if (rect != null && RectTransformUtility.RectangleContainsScreenPoint(rect, mousePos, null))
            {
                if (selectedIndex != i)
                {
                    selectedIndex = i;
                    PlaySfx(hoverSfxClip);
                    UpdateVisualHighlights();
                }

                if (leftClicked)
                {
                    PlaySfx(selectSfxClip);
                    ExecuteButton(i);
                }
                break;
            }
        }
    }

    /// <summary>
    /// Fare ile bir seçeneğin üzerine gelindiğinde çalışır
    /// </summary>
    public void OnPointerEnterItem(int index)
    {
        if (isHowToPlayOpen) return;
        if (selectedIndex != index)
        {
            selectedIndex = index;
            PlaySfx(hoverSfxClip);
            UpdateVisualHighlights();
        }
    }

    /// <summary>
    /// Fare ile bir seçeneğe tıklandığında çalışır
    /// </summary>
    public void OnPointerClickItem(int index)
    {
        if (isHowToPlayOpen) return;
        selectedIndex = index;
        PlaySfx(selectSfxClip);
        ExecuteButton(index);
    }

    private void ExecuteButton(int index)
    {
        if (index >= 0 && index < menuButtons.Count)
        {
            menuButtons[index].action?.Invoke();
        }
    }

    // --- MENÜ İŞLEVLERİ ---

    /// <summary>
    /// 1. Seçenek: Yeni Oyun (Sıfırdan başlatır)
    /// </summary>
    private void OnClickNewGame()
    {
        hasGameStarted = true;

        if (hero != null)
        {
            TeleportHero(initialSpawnPosition, initialSpawnRotation);
            SavePlayerPosition(initialSpawnPosition, initialSpawnRotation);
        }

        CameraFollow cam = FindAnyObjectByType<CameraFollow>();
        if (cam != null) cam.AlignBehindTarget();

        ShowMenu(false);
    }

    /// <summary>
    /// 2. Seçenek: Son Yerden Devam Et (Kaydedilmiş koordinattan başlatır)
    /// </summary>
    private void OnClickContinue()
    {
        hasGameStarted = true;

        if (PlayerPrefs.HasKey("Urartu_Save_X"))
        {
            float x = PlayerPrefs.GetFloat("Urartu_Save_X");
            float y = PlayerPrefs.GetFloat("Urartu_Save_Y");
            float z = PlayerPrefs.GetFloat("Urartu_Save_Z");
            float rotY = PlayerPrefs.GetFloat("Urartu_Save_RotY", 0f);

            Vector3 savedPos = new Vector3(x, y, z);
            Quaternion savedRot = Quaternion.Euler(0f, rotY, 0f);

            TeleportHero(savedPos, savedRot);
        }
        else
        {
            // Kayıt yoksa başlangıç konumuyla devam et
            TeleportHero(initialSpawnPosition, initialSpawnRotation);
            SavePlayerPosition(initialSpawnPosition, initialSpawnRotation);
        }

        CameraFollow cam = FindAnyObjectByType<CameraFollow>();
        if (cam != null) cam.AlignBehindTarget();

        ShowMenu(false);
    }

    /// <summary>
    /// <summary>
    /// 3. Seçenek: Nasıl Oynanır panelini aç
    /// </summary>
    private void OnClickHowToPlay()
    {
        OpenHowToPlay();
    }

    public void OpenHowToPlay()
    {
        isHowToPlayOpen = true;
        if (mainMenuContent != null) mainMenuContent.SetActive(false);
        if (howToPlayContent != null) howToPlayContent.SetActive(true);
        PlaySfx(selectSfxClip);
    }

    public void CloseHowToPlay(bool playSound = true)
    {
        isHowToPlayOpen = false;
        if (howToPlayContent != null) howToPlayContent.SetActive(false);
        if (mainMenuContent != null) mainMenuContent.SetActive(true);
        if (playSound) PlaySfx(selectSfxClip);
        UpdateVisualHighlights();
    }

    public void PlayHoverSfx() => PlaySfx(hoverSfxClip);
    public void PlaySelectSfx() => PlaySfx(selectSfxClip);

    /// <summary>
    /// 4. Seçenek: Müzik Aç / Kapat
    /// </summary>
    private void OnClickToggleMusic()
    {
        isMusicEnabled = !isMusicEnabled;
        PlayerPrefs.SetInt("Urartu_MusicEnabled", isMusicEnabled ? 1 : 0);
        PlayerPrefs.Save();

        if (musicSource != null)
        {
            if (isMusicEnabled)
            {
                if (!musicSource.isPlaying) musicSource.Play();
            }
            else
            {
                musicSource.Stop();
            }
        }

        UpdateMusicButtonText();
    }

    /// <summary>
    /// 5. Seçenek: Çıkış
    /// </summary>
    private void OnClickExit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    /// <summary>
    /// ESC ile oyuna geri dönüş
    /// </summary>
    private void ResumeGame()
    {
        ShowMenu(false);
    }

    private void ShowMenu(bool show, bool animated = true)
    {
        IsMenuOpen = show;

        if (isHowToPlayOpen)
        {
            CloseHowToPlay(false);
        }

        if (show)
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            if (mainMenuContent != null) mainMenuContent.SetActive(true);
            if (howToPlayContent != null) howToPlayContent.SetActive(false);
            UpdateMusicButtonText();
            UpdateVisualHighlights();
        }
        else
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        if (animated && gameObject.activeInHierarchy)
        {
            if (menuAnimationCoroutine != null)
            {
                StopCoroutine(menuAnimationCoroutine);
            }
            menuAnimationCoroutine = StartCoroutine(AnimateMenuCoroutine(show));
        }
        else
        {
            // Anlık geçiş
            if (menuContainer != null)
            {
                menuContainer.SetActive(show);
                if (menuContainerRect != null)
                {
                    menuContainerRect.anchoredPosition = show ? Vector2.zero : new Vector2(0f, 950f);
                    menuContainerRect.localScale = show ? Vector3.one : new Vector3(0.15f, 0.15f, 1f);
                }
                if (menuCanvasGroup != null)
                {
                    menuCanvasGroup.alpha = show ? 1f : 0f;
                    menuCanvasGroup.blocksRaycasts = show;
                    menuCanvasGroup.interactable = show;
                }
            }

            if (backdropDimImage != null)
            {
                backdropDimImage.gameObject.SetActive(show);
                Color c = backdropDimImage.color;
                c.a = show ? 0.52f : 0f;
                backdropDimImage.color = c;
            }

            if (blurVolume != null)
            {
                blurVolume.weight = show ? 1f : 0f;
            }
        }
    }

    /// <summary>
    /// Menüyü üst uzaktan ön ortaya ve tersine doğru animasyonla hareket ettiren Coroutine
    /// </summary>
    private IEnumerator AnimateMenuCoroutine(bool open)
    {
        isAnimating = true;
        float duration = open ? openAnimDuration : closeAnimDuration;
        float elapsed = 0f;

        Vector2 startPos = menuContainerRect != null ? menuContainerRect.anchoredPosition : (open ? new Vector2(0f, 950f) : Vector2.zero);
        Vector3 startScale = menuContainerRect != null ? menuContainerRect.localScale : (open ? new Vector3(0.15f, 0.15f, 1f) : Vector3.one);
        float startAlpha = menuCanvasGroup != null ? menuCanvasGroup.alpha : (open ? 0f : 1f);
        float startDimAlpha = backdropDimImage != null ? backdropDimImage.color.a : (open ? 0f : 0.52f);
        float startBlur = blurVolume != null ? blurVolume.weight : (open ? 0f : 1f);

        Vector2 targetPos = open ? Vector2.zero : new Vector2(0f, 950f);
        Vector3 targetScale = open ? Vector3.one : new Vector3(0.15f, 0.15f, 1f);
        float targetAlpha = open ? 1f : 0f;
        float targetDimAlpha = open ? 0.52f : 0f;
        float targetBlur = open ? 1f : 0f;

        if (open)
        {
            if (menuContainer != null) menuContainer.SetActive(true);
            if (backdropDimImage != null) backdropDimImage.gameObject.SetActive(true);
            if (menuCanvasGroup != null)
            {
                menuCanvasGroup.blocksRaycasts = true;
                menuCanvasGroup.interactable = true;
            }
        }
        else
        {
            if (menuCanvasGroup != null)
            {
                menuCanvasGroup.blocksRaycasts = false;
                menuCanvasGroup.interactable = false;
            }
        }

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // Açılırken EaseOut (yumuşakça hedefe yerleşir), kapanırken EaseIn (hızlanarak gökyüzüne uzaklaşır)
            float ease = open ? (1f - Mathf.Pow(1f - t, 3)) : (t * t * t);

            if (menuContainerRect != null)
            {
                menuContainerRect.anchoredPosition = Vector2.LerpUnclamped(startPos, targetPos, ease);
                menuContainerRect.localScale = Vector3.LerpUnclamped(startScale, targetScale, ease);
            }

            if (menuCanvasGroup != null)
            {
                menuCanvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, ease);
            }

            if (backdropDimImage != null)
            {
                Color c = backdropDimImage.color;
                c.a = Mathf.Lerp(startDimAlpha, targetDimAlpha, ease);
                backdropDimImage.color = c;
            }

            if (blurVolume != null)
            {
                blurVolume.weight = Mathf.Lerp(startBlur, targetBlur, ease);
            }

            yield return null;
        }

        // Bitiş değerlerini sabitle
        if (menuContainerRect != null)
        {
            menuContainerRect.anchoredPosition = targetPos;
            menuContainerRect.localScale = targetScale;
        }

        if (menuCanvasGroup != null)
        {
            menuCanvasGroup.alpha = targetAlpha;
        }

        if (backdropDimImage != null)
        {
            Color c = backdropDimImage.color;
            c.a = targetDimAlpha;
            backdropDimImage.color = c;
        }

        if (blurVolume != null)
        {
            blurVolume.weight = targetBlur;
        }

        if (!open)
        {
            if (menuContainer != null) menuContainer.SetActive(false);
            if (backdropDimImage != null) backdropDimImage.gameObject.SetActive(false);
        }

        isAnimating = false;
        menuAnimationCoroutine = null;
    }

    private void UpdateMusicButtonText()
    {
        if (musicButtonText != null)
        {
            string stateStr = isMusicEnabled ? "<color=#55FF88>AÇIK</color>" : "<color=#FF6666>KAPALI</color>";
            musicButtonText.text = $"Müzik: {stateStr}";
        }
    }

    private void UpdateVisualHighlights()
    {
        for (int i = 0; i < menuButtons.Count; i++)
        {
            var btn = menuButtons[i];
            bool isSelected = (i == selectedIndex);

            if (isSelected)
            {
                // Seçili buton vurgusu (%50 opacity sarı tonu)
                btn.backgroundImage.color = new Color(0.85f, 0.70f, 0.20f, 0.20f);
                if (btn.borderOutline != null)
                {
                    btn.borderOutline.enabled = true;
                    btn.borderOutline.effectColor = new Color(0.90f, 0.75f, 0.25f, 0.50f); // %50 opacity
                    btn.borderOutline.effectDistance = new Vector2(2f, -2f);
                }
                btn.labelText.color = new Color(0.95f, 0.90f, 0.75f, 0.95f);
                btn.labelText.fontSize = 22;
                btn.labelText.fontStyle = FontStyle.Bold;

                // Ok göstergesi ekle
                if (btn.isMusicButton)
                {
                    string stateStr = isMusicEnabled ? "<color=#55FF88>AÇIK</color>" : "<color=#FF6666>KAPALI</color>";
                    btn.labelText.text = $"▶  Müzik: {stateStr}  ◀";
                }
                else
                {
                    btn.labelText.text = $"▶  {btn.baseTitle}  ◀";
                }

                btn.buttonObj.transform.localScale = new Vector3(1.05f, 1.05f, 1f);
            }
            else
            {
                // Normal buton (Sakin, koyu şeffaf)
                btn.backgroundImage.color = new Color(0.12f, 0.15f, 0.22f, 0.65f);
                if (btn.borderOutline != null)
                {
                    btn.borderOutline.enabled = true;
                    btn.borderOutline.effectColor = new Color(0.35f, 0.40f, 0.50f, 0.40f);
                    btn.borderOutline.effectDistance = new Vector2(1f, -1f);
                }
                btn.labelText.color = new Color(0.88f, 0.90f, 0.95f, 0.85f);
                btn.labelText.fontSize = 20;
                btn.labelText.fontStyle = FontStyle.Normal;

                if (btn.isMusicButton)
                {
                    string stateStr = isMusicEnabled ? "<color=#55FF88>AÇIK</color>" : "<color=#FF6666>KAPALI</color>";
                    btn.labelText.text = $"Müzik: {stateStr}";
                }
                else
                {
                    btn.labelText.text = btn.baseTitle;
                }

                btn.buttonObj.transform.localScale = Vector3.one;
            }
        }
    }

    // --- SES SİSTEMİ ---

    private void SetupAudio()
    {
        // 1. Müzik Kaynağı
        musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.loop = true;
        musicSource.playOnAwake = false;
        musicSource.volume = 0.55f;
        musicSource.spatialBlend = 0f; // 2D Ses

        // Müziği Assets/Sounds veya Resources'dan otomatik bul
        if (bgMusicClip == null)
        {
            bgMusicClip = Resources.Load<AudioClip>("BGMusic");
        }

#if UNITY_EDITOR
        if (bgMusicClip == null)
        {
            bgMusicClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Sounds/BGMusic.mp3");
        }
#endif

        if (bgMusicClip != null)
        {
            musicSource.clip = bgMusicClip;
        }

        // 2. Menü Tıklama Efektleri
        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.loop = false;
        sfxSource.playOnAwake = false;
        sfxSource.volume = 0.4f;
        sfxSource.spatialBlend = 0f;
    }

    private void PlaySfx(AudioClip clip)
    {
        if (sfxSource != null && clip != null)
        {
            sfxSource.PlayOneShot(clip);
        }
    }

    private AudioClip CreateToneClip(float freq, float duration)
    {
        int sampleRate = 44100;
        int count = Mathf.Max(1, (int)(sampleRate * duration));
        float[] data = new float[count];
        for (int i = 0; i < count; i++)
        {
            float t = (float)i / sampleRate;
            float envelope = 1f - ((float)i / count);
            data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * envelope * 0.18f;
        }
        AudioClip clip = AudioClip.Create("Tone_" + freq, count, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // --- BULANIKLIK SİSTEMİ (URP POST-PROCESSING) ---

    private void SetupBlurVolume()
    {
        Transform oldVol = transform.Find("Menu_BlurVolume");
        if (oldVol != null) Destroy(oldVol.gameObject);

        GameObject volObj = new GameObject("Menu_BlurVolume");
        volObj.transform.SetParent(transform);

        blurVolume = volObj.AddComponent<Volume>();
        blurVolume.isGlobal = true;
        blurVolume.priority = 100f;
        blurVolume.weight = 0f;

        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "RuntimeMenuBlurProfile";

        dofComponent = profile.Add<DepthOfField>(true);
        dofComponent.mode.overrideState = true;
        dofComponent.mode.value = DepthOfFieldMode.Gaussian;
        dofComponent.gaussianStart.overrideState = true;
        dofComponent.gaussianStart.value = 0f;
        dofComponent.gaussianEnd.overrideState = true;
        dofComponent.gaussianEnd.value = 5f;
        dofComponent.gaussianMaxRadius.overrideState = true;
        dofComponent.gaussianMaxRadius.value = 1.8f;

        blurVolume.profile = profile;
    }

    // --- UI TASARIMI VE OLUŞTURMA ---

    private void SetupUI()
    {
        // EventSystem Kontrolü
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            GameObject esObj = new GameObject("EventSystem");
            esObj.AddComponent<EventSystem>();
            esObj.AddComponent<InputSystemUIInputModule>();
        }

        // Font bul
        Font menuFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (menuFont == null) menuFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (menuFont == null) menuFont = Font.CreateDynamicFontFromOSFont("Segoe UI", 18);
        if (menuFont == null) menuFont = Font.CreateDynamicFontFromOSFont("Arial", 18);

        // Varsa eski canvas'ı temizle
        Transform oldCanvas = transform.Find("Urartu_MenuCanvas");
        if (oldCanvas != null) Destroy(oldCanvas.gameObject);

        // 1. Canvas
        GameObject canvasObj = new GameObject("Urartu_MenuCanvas");
        canvasObj.transform.SetParent(transform);
        menuCanvas = canvasObj.AddComponent<Canvas>();
        menuCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        menuCanvas.sortingOrder = 999;

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObj.AddComponent<GraphicRaycaster>();

        // 2. Tam Ekran Karartma & Bulanıklık Katmanı (Backdrop)
        // NOT: Arka ekranda BGMenu görseli YOKTUR. Arka ekran doğrudan 3D oyun dünyasının bulanık görüntüsüdür.
        GameObject backdropObj = new GameObject("BackdropDim");
        backdropObj.transform.SetParent(canvasObj.transform, false);
        backdropDimImage = backdropObj.AddComponent<Image>();
        backdropDimImage.color = new Color(0.03f, 0.04f, 0.07f, 0f); // Başlangıçta saydam, animasyonla kararır
        RectTransform bdRect = backdropObj.GetComponent<RectTransform>();
        bdRect.anchorMin = Vector2.zero;
        bdRect.anchorMax = Vector2.one;
        bdRect.sizeDelta = Vector2.zero;

        // 3. Menü Ana Paneli (Center Container)
        menuContainer = new GameObject("MenuContainer");
        menuContainer.transform.SetParent(canvasObj.transform, false);
        menuContainerRect = menuContainer.AddComponent<RectTransform>();
        menuContainerRect.anchorMin = new Vector2(0.5f, 0.5f);
        menuContainerRect.anchorMax = new Vector2(0.5f, 0.5f);
        menuContainerRect.pivot = new Vector2(0.5f, 0.5f);
        menuContainerRect.sizeDelta = new Vector2(620f, 680f);

        // CanvasGroup (Opaklık ve etkileşim geçişleri için)
        menuCanvasGroup = menuContainer.AddComponent<CanvasGroup>();
        menuCanvasGroup.alpha = 0f;
        menuCanvasGroup.blocksRaycasts = false;
        menuCanvasGroup.interactable = false;

        // Başlangıç animasyon pozisyonu: Üst uzaktan (Y=950f, Scale=0.15f)
        menuContainerRect.anchoredPosition = new Vector2(0f, 950f);
        menuContainerRect.localScale = new Vector3(0.15f, 0.15f, 1f);

        // Panel arka plan kartı (Koyu taban)
        Image panelBg = menuContainer.AddComponent<Image>();
        panelBg.color = new Color(0.07f, 0.09f, 0.13f, 0.88f);

        // Kutu içi taşmaları önleyen maskeleme
        menuContainer.AddComponent<RectMask2D>();

        // 3.5. Menü Kutusunun Arka Planı (BGMenu.jpg)
        LoadWatermarkTexture();
        if (menuBackgroundTexture != null)
        {
            GameObject wmObj = new GameObject("MenuBoxBackground");
            wmObj.transform.SetParent(menuContainer.transform, false);
            boxWatermarkImage = wmObj.AddComponent<RawImage>();
            boxWatermarkImage.texture = menuBackgroundTexture;
            boxWatermarkImage.color = new Color(1f, 1f, 1f, watermarkOpacity);
            boxWatermarkImage.raycastTarget = false;

            RectTransform wmRect = wmObj.GetComponent<RectTransform>();
            wmRect.anchorMin = Vector2.zero;
            wmRect.anchorMax = Vector2.one;
            wmRect.sizeDelta = Vector2.zero;

            AspectRatioFitter arf = wmObj.AddComponent<AspectRatioFitter>();
            arf.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            if (menuBackgroundTexture.height > 0)
            {
                arf.aspectRatio = (float)menuBackgroundTexture.width / menuBackgroundTexture.height;
            }
            else
            {
                arf.aspectRatio = 16f / 9f;
            }
        }

        Outline panelOutline = menuContainer.AddComponent<Outline>();
        panelOutline.effectColor = new Color(0.85f, 0.70f, 0.30f, 0.45f);
        panelOutline.effectDistance = new Vector2(2f, -2f);

        Shadow panelShadow = menuContainer.AddComponent<Shadow>();
        panelShadow.effectColor = new Color(0f, 0f, 0f, 0.75f);
        panelShadow.effectDistance = new Vector2(5f, -5f);

        // ==========================================
        // 4. ANA MENÜ İÇERİK GRUBU (MainMenuContent)
        // ==========================================
        mainMenuContent = new GameObject("MainMenuContent");
        mainMenuContent.transform.SetParent(menuContainer.transform, false);
        RectTransform mmcRect = mainMenuContent.AddComponent<RectTransform>();
        mmcRect.anchorMin = Vector2.zero;
        mmcRect.anchorMax = Vector2.one;
        mmcRect.sizeDelta = Vector2.zero;

        // Başlık (Title)
        GameObject titleObj = new GameObject("GameTitle");
        titleObj.transform.SetParent(mainMenuContent.transform, false);
        RectTransform titleRect = titleObj.AddComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.5f, 1f);
        titleRect.anchorMax = new Vector2(0.5f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -38f);
        titleRect.sizeDelta = new Vector2(500f, 55f);

        Text titleText = titleObj.AddComponent<Text>();
        titleText.font = menuFont;
        titleText.text = "U R A R T U";
        titleText.fontSize = 44;
        titleText.fontStyle = FontStyle.Bold;
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.color = new Color(0.95f, 0.80f, 0.35f, 1f); // Altın Sarısı Başlık

        Outline titleOutline = titleObj.AddComponent<Outline>();
        titleOutline.effectColor = new Color(0f, 0f, 0f, 0.8f);
        titleOutline.effectDistance = new Vector2(2f, -2f);

        // Alt Başlık (Subtitle - Slogan)
        GameObject subObj = new GameObject("Subtitle");
        subObj.transform.SetParent(mainMenuContent.transform, false);
        RectTransform subRect = subObj.AddComponent<RectTransform>();
        subRect.anchorMin = new Vector2(0.5f, 1f);
        subRect.anchorMax = new Vector2(0.5f, 1f);
        subRect.pivot = new Vector2(0.5f, 1f);
        subRect.anchoredPosition = new Vector2(0f, -92f);
        subRect.sizeDelta = new Vector2(400f, 25f);

        Text subText = subObj.AddComponent<Text>();
        subText.font = menuFont;
        subText.text = "—  G İ Z E M L İ   U Y G A R L I K  —";
        subText.fontSize = 13;
        subText.alignment = TextAnchor.MiddleCenter;
        subText.color = new Color(0.70f, 0.72f, 0.80f, 0.85f);

        // İnce Ayrım Çizgisi
        GameObject divObj = new GameObject("Divider");
        divObj.transform.SetParent(mainMenuContent.transform, false);
        RectTransform divRect = divObj.AddComponent<RectTransform>();
        divRect.anchorMin = new Vector2(0.5f, 1f);
        divRect.anchorMax = new Vector2(0.5f, 1f);
        divRect.pivot = new Vector2(0.5f, 1f);
        divRect.anchoredPosition = new Vector2(0f, -125f);
        divRect.sizeDelta = new Vector2(460f, 2f);

        Image divImg = divObj.AddComponent<Image>();
        divImg.color = new Color(0.85f, 0.70f, 0.30f, 0.35f);

        // 5. Menü Butonları Grubu (5 Seçenek)
        string[] titles = new string[]
        {
            "Yeni Oyun",
            "Son Yerden Devam Et",
            "Nasıl Oynanır",
            "Müzik: Açık",
            "Çıkış"
        };

        System.Action[] actions = new System.Action[]
        {
            OnClickNewGame,
            OnClickContinue,
            OnClickHowToPlay,
            OnClickToggleMusic,
            OnClickExit
        };

        float startY = -148f;
        float btnHeight = 56f;
        float spacing = 13f;

        for (int i = 0; i < titles.Length; i++)
        {
            int index = i;
            GameObject btnObj = new GameObject("Btn_" + index);
            btnObj.transform.SetParent(mainMenuContent.transform, false);

            RectTransform bRect = btnObj.AddComponent<RectTransform>();
            bRect.anchorMin = new Vector2(0.5f, 1f);
            bRect.anchorMax = new Vector2(0.5f, 1f);
            bRect.pivot = new Vector2(0.5f, 1f);
            bRect.anchoredPosition = new Vector2(0f, startY - (i * (btnHeight + spacing)));
            bRect.sizeDelta = new Vector2(460f, btnHeight);

            Image bImg = btnObj.AddComponent<Image>();
            bImg.color = new Color(0.12f, 0.15f, 0.22f, 0.65f);

            Outline bOutline = btnObj.AddComponent<Outline>();
            bOutline.effectColor = new Color(0.35f, 0.40f, 0.50f, 0.4f);
            bOutline.effectDistance = new Vector2(1f, -1f);

            // Buton Yazısı
            GameObject txtObj = new GameObject("Label");
            txtObj.transform.SetParent(btnObj.transform, false);
            RectTransform tRect = txtObj.AddComponent<RectTransform>();
            tRect.anchorMin = Vector2.zero;
            tRect.anchorMax = Vector2.one;
            tRect.sizeDelta = Vector2.zero;

            Text lText = txtObj.AddComponent<Text>();
            lText.font = menuFont;
            lText.text = titles[i];
            lText.fontSize = 20;
            lText.alignment = TextAnchor.MiddleCenter;
            lText.color = new Color(0.88f, 0.90f, 0.95f, 0.85f);

            // Fare ile üzerine gelme ve tıklama algılayıcısı
            MenuPointerHandler ptrHandler = btnObj.AddComponent<MenuPointerHandler>();
            ptrHandler.Init(index, this);

            MenuButtonData bData = new MenuButtonData
            {
                index = index,
                buttonObj = btnObj,
                backgroundImage = bImg,
                borderOutline = bOutline,
                labelText = lText,
                baseTitle = titles[i],
                isMusicButton = (index == 3),
                action = actions[i]
            };

            if (index == 3)
            {
                musicButtonText = lText;
            }

            menuButtons.Add(bData);
        }

        // Alt Bilgi / Yönlendirme Metni
        GameObject footerObj = new GameObject("FooterHint");
        footerObj.transform.SetParent(mainMenuContent.transform, false);
        RectTransform fRect = footerObj.AddComponent<RectTransform>();
        fRect.anchorMin = new Vector2(0.5f, 0f);
        fRect.anchorMax = new Vector2(0.5f, 0f);
        fRect.pivot = new Vector2(0.5f, 0f);
        fRect.anchoredPosition = new Vector2(0f, 22f);
        fRect.sizeDelta = new Vector2(540f, 30f);

        Text fText = footerObj.AddComponent<Text>();
        fText.font = menuFont;
        fText.text = "▲ / ▼ Yön Tuşları veya Fare ile Seç  •  [Enter] / [Tıkla] Seç  •  [ESC] Menü";
        fText.fontSize = 12;
        fText.alignment = TextAnchor.MiddleCenter;
        fText.color = new Color(0.60f, 0.65f, 0.75f, 0.70f);

        // ==========================================
        // 5. NASIL OYNANIR PANELİ (HowToPlayContent)
        // ==========================================
        howToPlayContent = new GameObject("HowToPlayContent");
        howToPlayContent.transform.SetParent(menuContainer.transform, false);
        RectTransform htpcRect = howToPlayContent.AddComponent<RectTransform>();
        htpcRect.anchorMin = Vector2.zero;
        htpcRect.anchorMax = Vector2.one;
        htpcRect.sizeDelta = Vector2.zero;

        // Başlık
        GameObject htpTitleObj = new GameObject("HTPTitle");
        htpTitleObj.transform.SetParent(howToPlayContent.transform, false);
        RectTransform htptRect = htpTitleObj.AddComponent<RectTransform>();
        htptRect.anchorMin = new Vector2(0.5f, 1f);
        htptRect.anchorMax = new Vector2(0.5f, 1f);
        htptRect.pivot = new Vector2(0.5f, 1f);
        htptRect.anchoredPosition = new Vector2(0f, -32f);
        htptRect.sizeDelta = new Vector2(520f, 42f);

        Text htptText = htpTitleObj.AddComponent<Text>();
        htptText.font = menuFont;
        htptText.text = "N A S I L   O Y N A N I R";
        htptText.fontSize = 32;
        htptText.fontStyle = FontStyle.Bold;
        htptText.alignment = TextAnchor.MiddleCenter;
        htptText.color = new Color(0.95f, 0.80f, 0.35f, 1f);

        Outline htptOutline = htpTitleObj.AddComponent<Outline>();
        htptOutline.effectColor = new Color(0f, 0f, 0f, 0.8f);
        htptOutline.effectDistance = new Vector2(2f, -2f);

        // İnce Ayrım Çizgisi
        GameObject htpDivObj = new GameObject("HTPDivider");
        htpDivObj.transform.SetParent(howToPlayContent.transform, false);
        RectTransform htpdivRect = htpDivObj.AddComponent<RectTransform>();
        htpdivRect.anchorMin = new Vector2(0.5f, 1f);
        htpdivRect.anchorMax = new Vector2(0.5f, 1f);
        htpdivRect.pivot = new Vector2(0.5f, 1f);
        htpdivRect.anchoredPosition = new Vector2(0f, -76f);
        htpdivRect.sizeDelta = new Vector2(480f, 2f);

        Image htpdivImg = htpDivObj.AddComponent<Image>();
        htpdivImg.color = new Color(0.85f, 0.70f, 0.30f, 0.35f);

        // KONTROLLER KARTI
        GameObject ctrlCard = new GameObject("ControlsCard");
        ctrlCard.transform.SetParent(howToPlayContent.transform, false);
        RectTransform ccRect = ctrlCard.AddComponent<RectTransform>();
        ccRect.anchorMin = new Vector2(0.5f, 1f);
        ccRect.anchorMax = new Vector2(0.5f, 1f);
        ccRect.pivot = new Vector2(0.5f, 1f);
        ccRect.anchoredPosition = new Vector2(0f, -88f);
        ccRect.sizeDelta = new Vector2(530f, 178f);

        Image ccImg = ctrlCard.AddComponent<Image>();
        ccImg.color = new Color(0.08f, 0.11f, 0.17f, 0.75f);

        Outline ccOutline = ctrlCard.AddComponent<Outline>();
        ccOutline.effectColor = new Color(0.85f, 0.70f, 0.30f, 0.35f);
        ccOutline.effectDistance = new Vector2(1f, -1f);

        GameObject ctrlTxtObj = new GameObject("ControlsText");
        ctrlTxtObj.transform.SetParent(ctrlCard.transform, false);
        RectTransform ctRect = ctrlTxtObj.AddComponent<RectTransform>();
        ctRect.anchorMin = Vector2.zero;
        ctRect.anchorMax = Vector2.one;
        ctRect.offsetMin = new Vector2(20f, 10f);
        ctRect.offsetMax = new Vector2(-20f, -12f);

        Text ctText = ctrlTxtObj.AddComponent<Text>();
        ctText.font = menuFont;
        ctText.supportRichText = true;
        ctText.fontSize = 15;
        ctText.lineSpacing = 1.3f;
        ctText.alignment = TextAnchor.UpperLeft;
        ctText.color = new Color(0.92f, 0.94f, 0.98f, 0.95f);
        ctText.text =
            "<color=#F5D259><b>🎮  KONTROLLER VE HAREKET</b></color>\n" +
            "• <b>▲ ▼ ◄ ► Yön Tuşları:</b> Karakter Hareketi (Yürüme / Koşma)\n" +
            "• <b>A / D Tuşları:</b> Kamerayı 360° Serbest Döndürme\n" +
            "• <b>W / S Tuşları:</b> Kamerayı Yakınlaştırma / Uzaklaştırma\n" +
            "• <b>ESC Tuşu:</b> Menüyü Aç / Oyuna Dön";

        // GİZEMLİ UYGARLIK LORE KARTI
        GameObject loreCard = new GameObject("LoreCard");
        loreCard.transform.SetParent(howToPlayContent.transform, false);
        RectTransform lcRect = loreCard.AddComponent<RectTransform>();
        lcRect.anchorMin = new Vector2(0.5f, 1f);
        lcRect.anchorMax = new Vector2(0.5f, 1f);
        lcRect.pivot = new Vector2(0.5f, 1f);
        lcRect.anchoredPosition = new Vector2(0f, -276f);
        lcRect.sizeDelta = new Vector2(530f, 175f);

        Image lcImg = loreCard.AddComponent<Image>();
        lcImg.color = new Color(0.08f, 0.11f, 0.17f, 0.75f);

        Outline lcOutline = loreCard.AddComponent<Outline>();
        lcOutline.effectColor = new Color(0.85f, 0.70f, 0.30f, 0.35f);
        lcOutline.effectDistance = new Vector2(1f, -1f);

        GameObject loreTxtObj = new GameObject("LoreText");
        loreTxtObj.transform.SetParent(loreCard.transform, false);
        RectTransform ltRect = loreTxtObj.AddComponent<RectTransform>();
        ltRect.anchorMin = Vector2.zero;
        ltRect.anchorMax = Vector2.one;
        ltRect.offsetMin = new Vector2(20f, 10f);
        ltRect.offsetMax = new Vector2(-20f, -12f);

        Text ltText = loreTxtObj.AddComponent<Text>();
        ltText.font = menuFont;
        ltText.supportRichText = true;
        ltText.fontSize = 14;
        ltText.lineSpacing = 1.35f;
        ltText.alignment = TextAnchor.UpperLeft;
        ltText.color = new Color(0.88f, 0.90f, 0.95f, 0.92f);
        ltText.text =
            "<color=#F5D259><b>📜  GİZEMLİ UYGARLIK</b></color>\n" +
            "<i>\"Binlerce yıllık derin bir uykudan uyanan kadim taşlar yeniden fısıldamaya başladı...\"</i>\n\n" +
            "<i>\"Urartu'nun sisler altındaki kaleleri ve tapınakları, unutulmuş kralların sırlarıyla cesur bir kaşifi bekliyor. Her adımında kadim çağların izini sür.\"</i>";

        // VURUCU ÇAĞRI VE SLOGAN
        GameObject ctaObj = new GameObject("CtaSlogan");
        ctaObj.transform.SetParent(howToPlayContent.transform, false);
        RectTransform ctaRect = ctaObj.AddComponent<RectTransform>();
        ctaRect.anchorMin = new Vector2(0.5f, 1f);
        ctaRect.anchorMax = new Vector2(0.5f, 1f);
        ctaRect.pivot = new Vector2(0.5f, 1f);
        ctaRect.anchoredPosition = new Vector2(0f, -462f);
        ctaRect.sizeDelta = new Vector2(530f, 38f);

        Text ctaText = ctaObj.AddComponent<Text>();
        ctaText.font = menuFont;
        ctaText.text = "✨  Hadi gizemleri keşfedelim!  ✨";
        ctaText.fontSize = 21;
        ctaText.fontStyle = FontStyle.Bold;
        ctaText.alignment = TextAnchor.MiddleCenter;
        ctaText.color = new Color(1f, 0.86f, 0.35f, 1f);

        Outline ctaOutline = ctaObj.AddComponent<Outline>();
        ctaOutline.effectColor = new Color(0f, 0f, 0f, 0.8f);
        ctaOutline.effectDistance = new Vector2(2f, -2f);

        // GERİ DÖN BUTONU
        GameObject backBtnObj = new GameObject("BackBtn");
        backBtnObj.transform.SetParent(howToPlayContent.transform, false);
        RectTransform bbRect = backBtnObj.AddComponent<RectTransform>();
        bbRect.anchorMin = new Vector2(0.5f, 1f);
        bbRect.anchorMax = new Vector2(0.5f, 1f);
        bbRect.pivot = new Vector2(0.5f, 1f);
        bbRect.anchoredPosition = new Vector2(0f, -512f);
        bbRect.sizeDelta = new Vector2(360f, 52f);

        Image bbImg = backBtnObj.AddComponent<Image>();
        bbImg.color = new Color(0.15f, 0.20f, 0.30f, 0.85f);

        Outline bbOutline = backBtnObj.AddComponent<Outline>();
        bbOutline.effectColor = new Color(0.90f, 0.75f, 0.25f, 0.50f);
        bbOutline.effectDistance = new Vector2(1.5f, -1.5f);

        Button backButtonComp = backBtnObj.AddComponent<Button>();
        backButtonComp.onClick.AddListener(() => CloseHowToPlay(true));

        GameObject bbTxtObj = new GameObject("Label");
        bbTxtObj.transform.SetParent(backBtnObj.transform, false);
        RectTransform bbtRect = bbTxtObj.AddComponent<RectTransform>();
        bbtRect.anchorMin = Vector2.zero;
        bbtRect.anchorMax = Vector2.one;
        bbtRect.sizeDelta = Vector2.zero;

        Text bbtText = bbTxtObj.AddComponent<Text>();
        bbtText.font = menuFont;
        bbtText.text = "◀   A N A   M E N Ü Y E   D Ö N";
        bbtText.fontSize = 18;
        bbtText.fontStyle = FontStyle.Bold;
        bbtText.alignment = TextAnchor.MiddleCenter;
        bbtText.color = new Color(0.95f, 0.92f, 0.80f, 1f);

        MenuPointerHandler bbPtr = backBtnObj.AddComponent<MenuPointerHandler>();
        bbPtr.InitCustom(this, () => CloseHowToPlay(true));

        // Nasıl Oynanır Alt Bilgi
        GameObject htpHintObj = new GameObject("HTPHint");
        htpHintObj.transform.SetParent(howToPlayContent.transform, false);
        RectTransform hhRect = htpHintObj.AddComponent<RectTransform>();
        hhRect.anchorMin = new Vector2(0.5f, 0f);
        hhRect.anchorMax = new Vector2(0.5f, 0f);
        hhRect.pivot = new Vector2(0.5f, 0f);
        hhRect.anchoredPosition = new Vector2(0f, 22f);
        hhRect.sizeDelta = new Vector2(500f, 25f);

        Text hhText = htpHintObj.AddComponent<Text>();
        hhText.font = menuFont;
        hhText.text = "[ESC] veya [Enter] ile Ana Menüye Dön";
        hhText.fontSize = 12;
        hhText.alignment = TextAnchor.MiddleCenter;
        hhText.color = new Color(0.60f, 0.65f, 0.75f, 0.70f);

        // Başlangıçta Nasıl Oynanır kapalıdır
        howToPlayContent.SetActive(false);
        mainMenuContent.SetActive(true);
    }

    // --- OYUNCU KAYIT VE YER BULMA ---

    private void FindHeroAndSpawnPoint()
    {
        hero = FindAnyObjectByType<HeroController>();
        if (hero != null)
        {
            initialSpawnPosition = hero.transform.position;
            initialSpawnRotation = hero.transform.rotation;
        }
    }

    private void TeleportHero(Vector3 targetPos, Quaternion targetRot)
    {
        if (hero == null) hero = FindAnyObjectByType<HeroController>();
        if (hero != null)
        {
            CharacterController cc = hero.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;

            hero.transform.position = targetPos;
            hero.transform.rotation = targetRot;

            if (cc != null) cc.enabled = true;
        }
    }

    private void SaveCurrentPlayerPosition()
    {
        if (hero == null) hero = FindAnyObjectByType<HeroController>();
        if (hero != null)
        {
            SavePlayerPosition(hero.transform.position, hero.transform.rotation);
        }
    }

    private void SavePlayerPosition(Vector3 pos, Quaternion rot)
    {
        PlayerPrefs.SetFloat("Urartu_Save_X", pos.x);
        PlayerPrefs.SetFloat("Urartu_Save_Y", pos.y);
        PlayerPrefs.SetFloat("Urartu_Save_Z", pos.z);
        PlayerPrefs.SetFloat("Urartu_Save_RotY", rot.eulerAngles.y);
        PlayerPrefs.Save();
    }

    private void LoadWatermarkTexture()
    {
        if (menuBackgroundTexture != null) return;

        // 1. Resources üzerinden yükle
        menuBackgroundTexture = Resources.Load<Texture2D>("BGMenu");
        if (menuBackgroundTexture == null) menuBackgroundTexture = Resources.Load<Texture2D>("BGmenu");

#if UNITY_EDITOR
        // 2. Editör AssetDatabase üzerinden yükle
        if (menuBackgroundTexture == null)
        {
            menuBackgroundTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Images/BGMenu.jpg");
        }
        if (menuBackgroundTexture == null)
        {
            menuBackgroundTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Images/BGmenu.jpg");
        }
#endif

        // 3. Doğrudan dosya olarak oku ve yükle
        if (menuBackgroundTexture == null)
        {
            string p1 = System.IO.Path.Combine(Application.dataPath, "Images/BGMenu.jpg");
            string p2 = System.IO.Path.Combine(Application.dataPath, "Images/BGmenu.jpg");
            string finalPath = System.IO.File.Exists(p1) ? p1 : (System.IO.File.Exists(p2) ? p2 : null);

            if (finalPath != null)
            {
                byte[] bytes = System.IO.File.ReadAllBytes(finalPath);
                Texture2D tex = new Texture2D(2, 2);
                if (tex.LoadImage(bytes))
                {
                    menuBackgroundTexture = tex;
                }
            }
        }
    }
}

/// <summary>
/// Menü butonları için fare üzerine gelme (Hover) ve tıklama (Click) yakalayıcı bileşeni
/// </summary>
public class MenuPointerHandler : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
{
    private int itemIndex;
    private GameMenuManager menuManager;
    private System.Action customAction;

    public void Init(int index, GameMenuManager manager)
    {
        this.itemIndex = index;
        this.menuManager = manager;
        this.customAction = null;
    }

    public void InitCustom(GameMenuManager manager, System.Action action)
    {
        this.itemIndex = -1;
        this.menuManager = manager;
        this.customAction = action;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (customAction != null)
        {
            if (menuManager != null) menuManager.PlayHoverSfx();
            return;
        }
        if (menuManager != null)
        {
            menuManager.OnPointerEnterItem(itemIndex);
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (customAction != null)
        {
            customAction.Invoke();
            return;
        }
        if (menuManager != null)
        {
            menuManager.OnPointerClickItem(itemIndex);
        }
    }
}
