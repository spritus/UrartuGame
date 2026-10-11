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

    private Animator animator;
    private CharacterController characterController;
    private bool isMoving = false;
    private float verticalVelocity = 0f;
    private bool wasGrounded = true;

    void Start()
    {
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

        // Dikey yerçekimi bileşenini ekle
        moveVelocity.y += verticalVelocity;

        // Karakteri hareket ettir
        characterController.Move(moveVelocity * Time.deltaTime);

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
}
