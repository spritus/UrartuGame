using UnityEngine;
using UnityEngine.InputSystem;

public class CameraFollow : MonoBehaviour
{
    [Header("Target Settings")]
    public Transform target;
    [Tooltip("Kameranın odaklanacağı hedef yükseklik (karakterin göğüs/baş hizası)")]
    public float targetHeight = 2.5f;

    [Header("Distance & Zoom Settings (W / S Tuşları)")]
    [Tooltip("Varsayılan kamera mesafesi (öncekinden daha uzak)")]
    public float defaultDistance = 24f;
    [Tooltip("Minimum yaklaşma mesafesi (W tuşu yakınlaşma sınırı)")]
    public float minDistance = 14f;
    [Tooltip("Maksimum uzaklaşma mesafesi (S tuşu uzaklaşma sınırı)")]
    public float maxDistance = 34f;
    [Tooltip("W ve S ile yakınlaşma/uzaklaşma hızı")]
    public float zoomSpeed = 15f;

    [Header("Angle & Orbit Settings (A / D Tuşları)")]
    [Tooltip("Yukarıdan bakış açısı (daha yukarıdan bakması için yükseltildi)")]
    public float pitchAngle = 38f;
    [Tooltip("A ve D tuşları ile yatay açı dönüş hızı")]
    public float yawSpeed = 75f;
    [Tooltip("A ve D tuşlarının dönüş açısını sınırla (false: 360 derece serbest tam dönüş)")]
    public bool clampYaw = false;
    [Tooltip("Sola maksimum açı sınırı (clampYaw açıkken geçerlidir)")]
    public float minYawAngle = -70f;
    [Tooltip("Sağa maksimum açı sınırı (clampYaw açıkken geçerlidir)")]
    public float maxYawAngle = 70f;

    [Header("Smoothing")]
    [Tooltip("Kamera hareket ve geçiş yumuşaklığı")]
    public float smoothSpeed = 8f;

    // Geriye dönük uyumluluk için
    [HideInInspector]
    public Vector3 offset = new Vector3(0f, 14f, -22f);

    private float currentDistance;
    private float targetDistance;
    private float currentYaw = 0f;
    private float targetYaw = 0f;

    void Start()
    {
        currentDistance = defaultDistance;
        targetDistance = defaultDistance;
        currentYaw = 0f;
        targetYaw = 0f;

        if (target == null)
        {
            HeroController hero = FindAnyObjectByType<HeroController>();
            if (hero != null)
            {
                target = hero.transform;
            }
            else
            {
                Debug.LogWarning("Kamera hedefi bulamadı! Lütfen sahnede HeroController scriptinin bir objeye eklendiğinden emin olun.");
            }
        }
    }

    void Update()
    {
        // WASD TUŞLARI İLE KAMERA KONTROLÜ
        if (Keyboard.current != null)
        {
            // W / S: Yakınlaşma ve Uzaklaşma (Sınırlı)
            if (Keyboard.current.wKey.isPressed)
            {
                targetDistance -= zoomSpeed * Time.deltaTime;
            }
            if (Keyboard.current.sKey.isPressed)
            {
                targetDistance += zoomSpeed * Time.deltaTime;
            }
            targetDistance = Mathf.Clamp(targetDistance, minDistance, maxDistance);

            // A / D: 360 Derece Serbest Orbit Kamera Dönüşü (A ve D ile tam dönüş kısıtlaması kaldırıldı)
            if (Keyboard.current.aKey.isPressed)
            {
                targetYaw -= yawSpeed * Time.deltaTime;
            }
            if (Keyboard.current.dKey.isPressed)
            {
                targetYaw += yawSpeed * Time.deltaTime;
            }

            // İsteğe bağlı sınırlandırma (varsayılan olarak false: kısıtlama yok, tam döner)
            if (clampYaw)
            {
                targetYaw = Mathf.Clamp(targetYaw, minYawAngle, maxYawAngle);
            }
            else
            {
                // Sürekli dönüşte float değerlerinin aşırı büyümesini önlemek için periyodik dengeleme
                if (Mathf.Abs(targetYaw) >= 3600f)
                {
                    targetYaw %= 360f;
                    currentYaw %= 360f;
                }
            }
        }

        // Değerleri yumuşak bir şekilde hedefe doğru yaklaştır
        currentDistance = Mathf.Lerp(currentDistance, targetDistance, smoothSpeed * Time.deltaTime);
        currentYaw = Mathf.Lerp(currentYaw, targetYaw, smoothSpeed * Time.deltaTime);
    }

    void LateUpdate()
    {
        if (target == null)
        {
            HeroController hero = FindAnyObjectByType<HeroController>();
            if (hero != null) target = hero.transform;
            else return;
        }

        Vector3 targetFocusPoint = target.position + Vector3.up * targetHeight;

        // Yaw (sağ/sol) ve Pitch (yukarı/aşağı) rotasyonu
        Quaternion rotation = Quaternion.Euler(pitchAngle, currentYaw, 0f);

        // İstenen kamera konumu: hedefin arkasında, belirlenen mesafede ve açıda
        Vector3 desiredPosition = targetFocusPoint + rotation * new Vector3(0f, 0f, -currentDistance);

        // Kameranın yumuşakça pozisyona gitmesi
        transform.position = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed * Time.deltaTime);

        // Kameranın hedefe doğru bakması
        transform.LookAt(targetFocusPoint);
    }
}
