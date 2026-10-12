using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(CharacterController))]
public class HeroController : MonoBehaviour
{
    [Header("Movement Settings")]
    [Tooltip("Karakterin yatay hareket hızı")]
    public float moveSpeed = 15f; 
    [Tooltip("Karakterin dönüş hızı")]
    public float rotationSpeed = 720f;

    [Header("Gravity & Weight Settings (Yerçekimi ve Ağırlık)")]
    [Tooltip("Yerçekimi ivmesi (Varsayılan: 35 m/s²). Tok ve hızlı düşüş için arttırıldı.")]
    public float gravity = 35f;

    [Tooltip("Karakterin ağırlık ve tokluk hissi çarpanı (Örn: 1.5x ile efektif yerçekimi ~52 m/s² olur)")]
    public float weightMultiplier = 1.5f;

    [Tooltip("Zemindeyken yüzeye yapışma kuvveti (eğimlerden aşağı inerken uçmayı engeller)")]
    public float stickToGroundForce = 15f;

    [Tooltip("Hafif basamak veya yükseklik farklarından inerken havada uçup süzülmeyi önleyen zemin yapışma mesafesi")]
    public float groundSnapDistance = 1.5f;

    [Tooltip("Maksimum düşme hızı (Terminal hızı - m/s)")]
    public float terminalVelocity = 60f;

    [Tooltip("Havada iken yatay hareket kontrolü (0.35 = havada süzülmeyi keser, tok ve dik düşüş sağlar)")]
    public float airControl = 0.35f;

    [Header("Animation Names")]
    public string idleAnimation = "Idle"; 
    public string runAnimation = "Run"; 

    [Header("Center Boundary Settings (Merkez Sınırlandırması)")]
    [Tooltip("Hero'nun merkezden belirli bir orandan fazla uzaklaşmasını engeller")]
    public bool enableCenterLimit = true;

    [Tooltip("Merkezden izin verilen maksimum uzaklaşma yüzdesi (0.70 = %70)")]
    [Range(0.1f, 1f)]
    public float maxDistancePercentage = 0.70f;

    public enum BoundaryShape
    {
        Circle, // Dairesel sınır (Merkezden yarıçap)
        Box     // Karesel / Dikdörtgen sınır (Harita kenarlarına göre)
    }

    [Tooltip("Sınırlandırma geometrisi (Dairesel veya Karesel)")]
    public BoundaryShape boundaryShape = BoundaryShape.Circle;

    public enum CenterReferenceType
    {
        TerrainCenter,     // Sahnedeki Terrain'in merkezi (Önerilen)
        StartingPosition,  // Karakterin başladığı ilk konum
        CustomPoint        // Özel koordinat veya obje
    }

    [Tooltip("Merkez noktasının belirlenme yöntemi")]
    public CenterReferenceType centerType = CenterReferenceType.TerrainCenter;

    [Tooltip("Özel merkez obje referansı (CustomPoint seçildiğinde kullanılır)")]
    public Transform customCenterTransform;

    [Tooltip("Özel merkez koordinatı")]
    public Vector3 customCenterPosition = Vector3.zero;

    [Tooltip("Özel maksimum yarıçap (0 bırakılırsa Terrain boyutunun yarısı otomatik alınır)")]
    public float customMaxDistance = 0f;

    private Animator animator;
    private CharacterController characterController;
    private bool isMoving = false;
    private float verticalVelocity = 0f;
    private bool wasGrounded = true;
    private Vector3 startingPosition;
    private Terrain cachedTerrain;

    void Start()
    {
        startingPosition = transform.position;
        cachedTerrain = Terrain.activeTerrain;
        if (cachedTerrain == null) cachedTerrain = FindAnyObjectByType<Terrain>();
        animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInChildren<Animator>();

        characterController = GetComponent<CharacterController>();

        // Zeminle temas ve düşmemesi için kapsül ayarları
        if (characterController != null)
        {
            characterController.center = new Vector3(0, 1f, 0);
            characterController.height = 2f;
            characterController.radius = 0.4f;
            characterController.minMoveDistance = 0f;
        }
        
        if (animator != null)
        {
            // CharacterController ile çakışmaması için Root Motion kapatılır
            animator.applyRootMotion = false;
            
            if (animator.runtimeAnimatorController != null)
            {
                animator.Play(idleAnimation);
            }
        }
    }

    void Update()
    {
        // SADECE YÖN TUŞLARI (Up, Down, Left, Right Arrow) kahramanı hareket ettirir.
        // WASD tuşları kameraya ayrılmıştır, kahramanı HAREKET ETTİRMEZ!
        float horizontal = 0f;
        float vertical = 0f;

        if (Keyboard.current != null)
        {
            if (Keyboard.current.upArrowKey.isPressed) vertical += 1f;
            if (Keyboard.current.downArrowKey.isPressed) vertical -= 1f;
            if (Keyboard.current.rightArrowKey.isPressed) horizontal += 1f;
            if (Keyboard.current.leftArrowKey.isPressed) horizontal -= 1f;
        }

        // Kamera açısına göre yön belirle (Kamera döndükçe yukarı tuşu her zaman kameranın baktığı yöne gider)
        Vector3 movementInput = Vector3.zero;
        if (Camera.main != null)
        {
            Vector3 camFwd = Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized;
            Vector3 camRight = Vector3.ProjectOnPlane(Camera.main.transform.right, Vector3.up).normalized;
            movementInput = (camFwd * vertical + camRight * horizontal).normalized;
        }
        else
        {
            movementInput = new Vector3(horizontal, 0f, vertical).normalized;
        }

        bool movingNow = movementInput.magnitude >= 0.1f;

        // Animator parametresini senkronize et
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            animator.SetBool("IsRunning", movingNow);
        }

        // Karakterin dönüşü
        if (movingNow)
        {
            Quaternion targetRotation = Quaternion.LookRotation(movementInput);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);

            if (!isMoving)
            {
                isMoving = true;
                if (animator != null && animator.runtimeAnimatorController != null) 
                {
                    animator.Play(runAnimation);
                }
            }
        }
        else
        {
            if (isMoving)
            {
                isMoving = false;
                if (animator != null && animator.runtimeAnimatorController != null) 
                {
                    animator.Play(idleAnimation);
                }
            }
        }

        // --- FİZİK, YERÇEKİMİ VE AĞIRLIK SİSTEMİ ---
        bool isGrounded = characterController.isGrounded;

        // 1. ZEMİN YAPIŞMA (GROUND SNAP):
        // Karakter hafif bir eğim, çukur veya basamaktan aşağı inerken
        // havada uçup paraşüt gibi süzülmesini engeller, anında zemine oturtur.
        if (!isGrounded && wasGrounded && verticalVelocity <= 0f)
        {
            if (CheckGroundBelow(groundSnapDistance, out RaycastHit snapHit))
            {
                float dropDist = snapHit.distance - 0.5f;
                if (dropDist > 0.001f)
                {
                    characterController.Move(Vector3.down * dropDist);
                    isGrounded = characterController.isGrounded;
                    if (isGrounded)
                    {
                        verticalVelocity = -stickToGroundForce;
                    }
                }
            }
        }

        // 2. DİKEY HIZ VE YERÇEKİMİ HESAPLAMASI:
        if (isGrounded)
        {
            // Zemindeyken yüzeye yapışma kuvveti (küçük tümseklerde havalanmayı önler)
            verticalVelocity = -stickToGroundForce;
        }
        else
        {
            // Havada / Düşerken: Arttırılmış yüksek yerçekimi ve tok insan ağırlığı ivmesi
            float effectiveGravity = gravity * weightMultiplier;
            verticalVelocity -= effectiveGravity * Time.deltaTime;

            // Terminal hızı (maksimum serbest düşüş hızı) sınırı
            if (verticalVelocity < -terminalVelocity)
            {
                verticalVelocity = -terminalVelocity;
            }
        }

        // 3. YATAY HAREKET VE EĞİM UYUMU:
        Vector3 moveVelocity = Vector3.zero;

        if (movingNow)
        {
            moveVelocity = movementInput * moveSpeed;

            if (isGrounded)
            {
                // Yokuş aşağı inerken yüzeyi takip et (havalanmayı önler)
                if (CheckGroundBelow(0.8f, out RaycastHit slopeHit))
                {
                    float slopeAngle = Vector3.Angle(Vector3.up, slopeHit.normal);
                    if (slopeAngle > 0.1f && slopeAngle <= characterController.slopeLimit)
                    {
                        Vector3 projected = Vector3.ProjectOnPlane(moveVelocity, slopeHit.normal);
                        if (projected.y < 0f)
                        {
                            moveVelocity = projected;
                        }
                    }
                }
            }
            else
            {
                // Havada süzülmeyi kesmek için hava kontrolünü düşür (ileri uçmayı önler, direkt düşürür)
                moveVelocity *= airControl;
            }
        }

        // 4. MERKEZDEN UZAKLAŞMA SINIRLAMASI (%70 Sınırı - Hareketi sınırda yumuşakça teğet kaydır):
        if (enableCenterLimit)
        {
            Vector3 center = GetCenterPoint();
            Vector2 halfExtents = GetMaxHalfExtents();

            if (boundaryShape == BoundaryShape.Circle)
            {
                float maxRadius = Mathf.Min(halfExtents.x, halfExtents.y);
                Vector3 diff = transform.position - center;
                diff.y = 0f;
                float currentDist = diff.magnitude;

                if (currentDist > 0.001f)
                {
                    Vector3 outwardDir = diff / currentDist;

                    // Eğer karakter sınıra ulaştıysa veya dışındaysa:
                    if (currentDist >= maxRadius - 0.05f)
                    {
                        // Dışarıya (merkezden uzağa) olan hız bileşenini kaldır (teğet kaymaya izin ver)
                        float outwardSpeed = Vector3.Dot(moveVelocity, outwardDir);
                        if (outwardSpeed > 0f)
                        {
                            moveVelocity.x -= outwardDir.x * outwardSpeed;
                            moveVelocity.z -= outwardDir.z * outwardSpeed;
                        }
                    }
                }
            }
            else // Box
            {
                float minX = center.x - halfExtents.x;
                float maxX = center.x + halfExtents.x;
                float minZ = center.z - halfExtents.y;
                float maxZ = center.z + halfExtents.y;

                if (transform.position.x >= maxX - 0.05f && moveVelocity.x > 0f) moveVelocity.x = 0f;
                if (transform.position.x <= minX + 0.05f && moveVelocity.x < 0f) moveVelocity.x = 0f;
                if (transform.position.z >= maxZ - 0.05f && moveVelocity.z > 0f) moveVelocity.z = 0f;
                if (transform.position.z <= minZ + 0.05f && moveVelocity.z < 0f) moveVelocity.z = 0f;
            }
        }

        // Dikey yerçekimi bileşenini ekle
        moveVelocity.y += verticalVelocity;

        // Karakteri hareket ettir
        characterController.Move(moveVelocity * Time.deltaTime);

        // Hareket sonrası pozisyonu mutlak sınırda tut (kesin güvenlik kilidi)
        if (enableCenterLimit)
        {
            Vector3 center = GetCenterPoint();
            Vector2 halfExtents = GetMaxHalfExtents();

            if (boundaryShape == BoundaryShape.Circle)
            {
                float maxRadius = Mathf.Min(halfExtents.x, halfExtents.y);
                Vector3 finalDiff = transform.position - center;
                finalDiff.y = 0f;

                if (finalDiff.magnitude > maxRadius)
                {
                    Vector3 clamped = center + finalDiff.normalized * maxRadius;
                    clamped.y = transform.position.y;
                    characterController.enabled = false;
                    transform.position = clamped;
                    characterController.enabled = true;
                }
            }
            else // Box
            {
                float clampedX = Mathf.Clamp(transform.position.x, center.x - halfExtents.x, center.x + halfExtents.x);
                float clampedZ = Mathf.Clamp(transform.position.z, center.z - halfExtents.y, center.z + halfExtents.y);

                if (Mathf.Abs(clampedX - transform.position.x) > 0.001f || Mathf.Abs(clampedZ - transform.position.z) > 0.001f)
                {
                    characterController.enabled = false;
                    transform.position = new Vector3(clampedX, transform.position.y, clampedZ);
                    characterController.enabled = true;
                }
            }
        }

        // Bir sonraki kare için zemin bilgisini sakla
        wasGrounded = characterController.isGrounded;
    }

    /// <summary>
    /// Karakterin ayaklarının hemen altındaki zemini kontrol eder (kendi collider'ını hariç tutar)
    /// </summary>
    private bool CheckGroundBelow(float maxDistance, out RaycastHit closestHit)
    {
        Vector3 origin = transform.position + Vector3.up * 0.5f;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, maxDistance + 0.5f, ~0, QueryTriggerInteraction.Ignore);
        
        float minDistance = float.MaxValue;
        closestHit = default;
        bool found = false;

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit h = hits[i];
            if (h.collider != null && h.collider.gameObject != gameObject && !h.collider.transform.IsChildOf(transform))
            {
                float distFromFeet = h.distance - 0.5f;
                if (distFromFeet >= -0.05f && distFromFeet < minDistance)
                {
                    minDistance = distFromFeet;
                    closestHit = h;
                    found = true;
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Sınırlandırmanın referans merkez noktasını döner
    /// </summary>
    public Vector3 GetCenterPoint()
    {
        if (centerType == CenterReferenceType.StartingPosition)
        {
            return startingPosition != Vector3.zero ? startingPosition : transform.position;
        }
        else if (centerType == CenterReferenceType.CustomPoint)
        {
            if (customCenterTransform != null) return customCenterTransform.position;
            return customCenterPosition;
        }
        else // TerrainCenter
        {
            if (cachedTerrain == null)
            {
                cachedTerrain = Terrain.activeTerrain;
                if (cachedTerrain == null) cachedTerrain = FindAnyObjectByType<Terrain>();
            }

            if (cachedTerrain != null && cachedTerrain.terrainData != null)
            {
                Vector3 tPos = cachedTerrain.transform.position;
                Vector3 tSize = cachedTerrain.terrainData.size;
                return new Vector3(tPos.x + tSize.x * 0.5f, transform.position.y, tPos.z + tSize.z * 0.5f);
            }

            return startingPosition != Vector3.zero ? startingPosition : transform.position;
        }
    }

    /// <summary>
    /// Merkezden izin verilen maksimum yarım eksen mesafelerini döner
    /// </summary>
    public Vector2 GetMaxHalfExtents()
    {
        if (customMaxDistance > 0f)
        {
            return new Vector2(customMaxDistance, customMaxDistance) * maxDistancePercentage;
        }

        if (cachedTerrain == null)
        {
            cachedTerrain = Terrain.activeTerrain;
            if (cachedTerrain == null) cachedTerrain = FindAnyObjectByType<Terrain>();
        }

        if (cachedTerrain != null && cachedTerrain.terrainData != null)
        {
            Vector3 tSize = cachedTerrain.terrainData.size;
            return new Vector2(tSize.x * 0.5f, tSize.z * 0.5f) * maxDistancePercentage;
        }

        return new Vector2(500f, 500f) * maxDistancePercentage;
    }

    /// <summary>
    /// Editörde sınır alanını görselleştirir
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (!enableCenterLimit) return;

        Vector3 center = Application.isPlaying ? GetCenterPoint() : (Terrain.activeTerrain != null ? Terrain.activeTerrain.transform.position + new Vector3(Terrain.activeTerrain.terrainData.size.x * 0.5f, transform.position.y, Terrain.activeTerrain.terrainData.size.z * 0.5f) : transform.position);
        center.y = transform.position.y;

        Vector2 halfExtents = Application.isPlaying ? GetMaxHalfExtents() : (Terrain.activeTerrain != null ? new Vector2(Terrain.activeTerrain.terrainData.size.x * 0.5f, Terrain.activeTerrain.terrainData.size.z * 0.5f) * maxDistancePercentage : new Vector2(500f, 500f) * maxDistancePercentage);

        Gizmos.color = new Color(0f, 0.85f, 1f, 0.9f);
        Gizmos.DrawWireSphere(center, 2f);

        if (boundaryShape == BoundaryShape.Circle)
        {
            float radius = Mathf.Min(halfExtents.x, halfExtents.y);
            int segments = 64;
            Vector3 prevPoint = center + new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float angle = (i * 360f / segments) * Mathf.Deg2Rad;
                Vector3 nextPoint = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                Gizmos.DrawLine(prevPoint, nextPoint);
                prevPoint = nextPoint;
            }
        }
        else
        {
            Vector3 size = new Vector3(halfExtents.x * 2f, 2f, halfExtents.y * 2f);
            Gizmos.DrawWireCube(center, size);
        }
    }
}
