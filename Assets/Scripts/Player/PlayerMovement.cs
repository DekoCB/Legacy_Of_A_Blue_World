using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// PlayerMovement — Unity New Input System
/// Incluye: caminar, correr, saltar, doble salto, agacharse (C),
///          trepar escaleras, y bajar plataformas atravesables (S).
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CapsuleCollider2D))]
public class PlayerMovement : MonoBehaviour
{
    // ================================================================
    //  CONFIGURACIÓN
    // ================================================================

    [Header("Movimiento Horizontal")]
    [SerializeField] float walkSpeed = 5f;
    [SerializeField] float runSpeed = 9f;
    [SerializeField] float acceleration = 25f;
    [SerializeField] float friction = 35f;

    [Header("Salto")]
    [SerializeField] float jumpForce = 14f;
    [SerializeField] float doubleJumpForce = 12f;
    [SerializeField] float fallGravityMult = 2.2f;
    [SerializeField] float maxFallSpeed = 20f;
    [SerializeField] float coyoteTime = 0.12f;
    [SerializeField] float jumpBufferTime = 0.10f;

    [Header("Agacharse")]
    [SerializeField] float crouchSpeedMult = 0.4f;
    [SerializeField] Vector2 crouchColliderSize = new Vector2(0.8f, 0.9f);
    [SerializeField] Vector2 crouchColliderOffset = new Vector2(0f, -0.28f);
    [SerializeField] LayerMask ceilingMask;

    [Header("Plataformas Atravesables")]
    [SerializeField] float dropDownDuration = 0.3f;  // segundos que se ignora la colisión

    [Header("Trepar")]
    [SerializeField] float climbSpeed = 4f;
    [SerializeField] LayerMask ladderMask;

    [Header("Detección de Suelo")]
    [SerializeField] Transform groundCheck;
    [SerializeField] float groundCheckRadius = 0.15f;
    [SerializeField] LayerMask groundMask;

    // ================================================================
    //  REFERENCIAS
    // ================================================================
    Rigidbody2D rb;
    Animator anim;
    CapsuleCollider2D col;

    // ── Estado ──────────────────────────────────────────────────────
    bool isGrounded;
    bool isRunning;
    bool isCrouching;
    bool isOnLadder;
    bool doubleJumpAvailable;
    bool isDropping;           // true mientras se ignora la plataforma

    float coyoteTimer;
    float jumpBufferTimer;

    Vector2 originalColliderSize;
    Vector2 originalColliderOffset;

    // ── Input ────────────────────────────────────────────────────────
    Vector2 moveInput;
    bool runHeld;
    bool crouchHeld;

    // ================================================================
    //  UNITY LIFECYCLE
    // ================================================================

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        anim = GetComponent<Animator>();
        col = GetComponent<CapsuleCollider2D>();

        originalColliderSize = col.size;
        originalColliderOffset = col.offset;

        InputSystem.settings.backgroundBehavior =
        InputSettings.BackgroundBehavior.IgnoreFocus;
    }

    void Update()
    {
        UpdateTimers();
        HandleJumpInput();
        HandleCrouch();
        HandleDropDown();   // ← nuevo
        UpdateAnimations();
    }

    void FixedUpdate()
    {
        CheckGround();

        if (isOnLadder)
            HandleClimbing();
        else
        {
            ApplyGravity();
            HandleHorizontalMovement();
        }

        ClampFallSpeed();
    }

    // ================================================================
    //  CALLBACKS DEL INPUT SYSTEM
    //  Player Input component debe estar en modo "Send Messages".
    //  Recuerda asignar la tecla C a la acción "Crouch" en el asset.
    // ================================================================

    void OnMove(InputValue value)
    {
        moveInput = value.Get<Vector2>();
    }

    void OnJump(InputValue value)
    {
        if (value.isPressed)
            jumpBufferTimer = jumpBufferTime;
        else if (rb.linearVelocity.y > 0f)
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.y * 0.45f);
    }

    void OnRun(InputValue value)
    {
        runHeld = value.isPressed;
    }

    void OnCrouch(InputValue value)
    {
        crouchHeld = value.isPressed;
    }

    // ================================================================
    //  DETECCIÓN DE SUELO
    // ================================================================

    void CheckGround()
    {
        isGrounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundMask);

        if (isGrounded)
        {
            coyoteTimer = coyoteTime;
            doubleJumpAvailable = true;
        }
    }

    // ================================================================
    //  TIMERS
    // ================================================================

    void UpdateTimers()
    {
        coyoteTimer = Mathf.Max(coyoteTimer - Time.deltaTime, 0f);
        jumpBufferTimer = Mathf.Max(jumpBufferTimer - Time.deltaTime, 0f);
    }

    // ================================================================
    //  SALTO Y DOBLE SALTO
    // ================================================================

    void HandleJumpInput()
    {
        if (jumpBufferTimer <= 0f) return;

        if (coyoteTimer > 0f)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
            coyoteTimer = 0f;
            jumpBufferTimer = 0f;
        }
        else if (doubleJumpAvailable && !isOnLadder)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, doubleJumpForce);
            doubleJumpAvailable = false;
            jumpBufferTimer = 0f;
        }
    }

    // ================================================================
    //  GRAVEDAD VARIABLE
    // ================================================================

    void ApplyGravity()
    {
        if (isGrounded) return;

        bool falling = rb.linearVelocity.y < 0f;
        bool jumpReleased = rb.linearVelocity.y > 0f && jumpBufferTimer <= 0f;

        if (falling || jumpReleased)
            rb.linearVelocity += Vector2.up * (Physics2D.gravity.y * (fallGravityMult - 1f) * Time.fixedDeltaTime);
    }

    void ClampFallSpeed()
    {
        if (rb.linearVelocity.y < -maxFallSpeed)
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, -maxFallSpeed);
    }

    // ================================================================
    //  MOVIMIENTO HORIZONTAL
    // ================================================================

    void HandleHorizontalMovement()
    {
        isRunning = runHeld && !isCrouching;

        float targetSpeed;
        if (isCrouching) targetSpeed = walkSpeed * crouchSpeedMult;
        else if (isRunning) targetSpeed = runSpeed;
        else targetSpeed = walkSpeed;

        targetSpeed *= moveInput.x;

        // Elige la tasa según si está acelerando o frenando
        float rate = (Mathf.Abs(rb.linearVelocity.x) > Mathf.Abs(targetSpeed))
            ? friction       // frenando (también cubre soltar Shift)
            : acceleration;

        float newX = Mathf.MoveTowards(rb.linearVelocity.x, targetSpeed, rate * Time.fixedDeltaTime);
        rb.linearVelocity = new Vector2(newX, rb.linearVelocity.y);

        if (moveInput.x != 0f)
            transform.localScale = new Vector3(Mathf.Sign(moveInput.x), 1f, 1f);
    }

    // ================================================================
    //  AGACHARSE  (tecla C)
    // ================================================================

    void HandleCrouch()
    {
        bool wantsCrouch = crouchHeld && isGrounded;

        if (wantsCrouch && !isCrouching)
            SetCrouch(true);
        else if (!wantsCrouch && isCrouching && !HasCeilingAbove())
            SetCrouch(false);
    }

    void SetCrouch(bool crouching)
    {
        isCrouching = crouching;
        col.size = crouching ? crouchColliderSize : originalColliderSize;
        col.offset = crouching ? crouchColliderOffset : originalColliderOffset;
    }

    bool HasCeilingAbove()
    {
        Vector2 headPos = (Vector2)transform.position + Vector2.up * originalColliderSize.y * 0.5f;
        return Physics2D.OverlapCircle(headPos, groundCheckRadius, ceilingMask);
    }

    // ================================================================
    //  BAJAR PLATAFORMAS  (tecla S — moveInput.y < 0)
    //
    //  Requiere en las plataformas atravesables:
    //  · Layer: "OneWayPlatform"
    //  · Componente: Platform Effector 2D  (Use One Way: ON)
    //  · Collider 2D con "Used By Effector": ON
    // ================================================================

    void HandleDropDown()
    {
        // Condición: S presionada + en suelo + no está ya bajando
        if (moveInput.y < -0.5f && isGrounded && !isDropping)
            StartCoroutine(DropRoutine());
    }

    IEnumerator DropRoutine()
    {
        isDropping = true;

        int playerLayer = gameObject.layer;
        int platformLayer = LayerMask.NameToLayer("OneWayPlatform");

        // Ignora la colisión temporalmente para que el personaje caiga
        Physics2D.IgnoreLayerCollision(playerLayer, platformLayer, true);

        yield return new WaitForSeconds(dropDownDuration);

        Physics2D.IgnoreLayerCollision(playerLayer, platformLayer, false);

        isDropping = false;
    }

    // ================================================================
    //  TREPAR ESCALERAS
    // ================================================================

    void HandleClimbing()
    {
        rb.gravityScale = 0f;
        rb.linearVelocity = new Vector2(moveInput.x * walkSpeed * 0.5f, moveInput.y * climbSpeed);

        if (jumpBufferTimer > 0f)
        {
            isOnLadder = false;
            rb.gravityScale = 1f;
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce * 0.8f);
            doubleJumpAvailable = true;
            jumpBufferTimer = 0f;
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (((1 << other.gameObject.layer) & ladderMask) != 0)
        {
            isOnLadder = true;
            rb.gravityScale = 0f;
        }
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (((1 << other.gameObject.layer) & ladderMask) != 0)
        {
            isOnLadder = false;
            rb.gravityScale = 1f;
        }
    }

    // ================================================================
    //  ANIMACIONES
    // ================================================================

    void UpdateAnimations()
    {
        if (anim == null) return;

        anim.SetBool("isGrounded", isGrounded);
        anim.SetBool("isCrouching", isCrouching);
        anim.SetBool("isRunning", isRunning && Mathf.Abs(moveInput.x) > 0.01f);
        anim.SetBool("isOnLadder", isOnLadder);
        anim.SetBool("isDropping", isDropping);
        anim.SetFloat("speedX", Mathf.Abs(rb.linearVelocity.x));
        anim.SetFloat("velocityY", rb.linearVelocity.y);
    }

    // ================================================================
    //  GIZMOS
    // ================================================================
#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (groundCheck != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
        }
        if (col != null)
        {
            Gizmos.color = Color.yellow;
            Vector2 headPos = (Vector2)transform.position + Vector2.up * col.size.y * 0.5f;
            Gizmos.DrawWireSphere(headPos, groundCheckRadius);
        }
    }
#endif
}

// ================================================================
//  RESUMEN DE TECLAS
// ================================================================
//
//  Acción      Tecla        Tipo en Input Actions Asset
//  ─────────── ──────────── ───────────────────────────
//  Move        WASD/flechas Value / Vector2
//  Jump        Espacio      Button
//  Run         Left Shift   Button
//  Crouch      C            Button          ← cambiar a C
//  Drop Down   S (↓)        (moveInput.y, no necesita acción separada)
//
//  Layers necesarias:
//  ─────────────────────────────────────────
//  · Ground          → suelo sólido
//  · OneWayPlatform  → plataformas atravesables
//  · Ladder          → escaleras (Trigger)
//  · Ceiling         → techos (puede ser la misma que Ground)
//
// ================================================================