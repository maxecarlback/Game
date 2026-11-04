using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class control : MonoBehaviour
{
	[Header("Input")]
	[SerializeField] private InputActionAsset inputActions;
	[SerializeField] private string actionMapName = "Player";

	[Header("Movement")]
	[SerializeField] private float walkSpeed = 5f;
	[SerializeField] private float sprintSpeed = 8f;
	[SerializeField] private float crouchSpeed = 2.5f;
	[SerializeField] private float jumpHeight = 1.5f;
	[SerializeField] private float gravity = -20f;

	[Header("Crouch")]
	[SerializeField] private float crouchHeight = 1.2f;
	[SerializeField] private float crouchTransitionSpeed = 8f;

	[Header("Look")]
	[SerializeField] private Transform cameraPivot;
	[SerializeField] private float mouseSensitivity = 0.1f;
	[SerializeField] private float controllerLookSpeed = 180f;
	[SerializeField] private float maximumLookAngle = 85f;
	[SerializeField] private bool lockCursorOnStart = true;

	private CharacterController characterController;
	private InputActionMap playerActions;
	private InputAction moveAction;
	private InputAction lookAction;
	private InputAction jumpAction;
	private InputAction sprintAction;
	private InputAction crouchAction;
	private float verticalVelocity;
	private float standingHeight;
	private Vector3 standingCenter;
	private float standingCameraHeight = 0.72f;
	private Vector3 currentHorizontalVelocity;
	private float pitch;

	public Vector3 HorizontalVelocity => currentHorizontalVelocity;
	public bool IsGrounded => characterController != null && characterController.isGrounded;
	public float StandingHeight => standingHeight;

	private void Awake()
	{
		characterController = GetComponent<CharacterController>();

		CapsuleCollider capsuleCollider = GetComponent<CapsuleCollider>();
		if (capsuleCollider != null)
		{
			capsuleCollider.enabled = false;
		}

		standingHeight = characterController.height;
		standingCenter = characterController.center;

		if (cameraPivot != null)
		{
			cameraPivot.localPosition = new Vector3(0f, standingCameraHeight, 0.08f);
			pitch = cameraPivot.localEulerAngles.x;
			if (pitch > 180f)
			{
				pitch -= 360f;
			}
		}

		if (GetComponent<PlayerModel>() == null)
		{
			gameObject.AddComponent<PlayerModel>();
		}

		if (FindAnyObjectByType<EscMenu>() == null)
		{
			new GameObject("Esc Menu").AddComponent<EscMenu>();
		}

		if (FindAnyObjectByType<Gun>() == null)
		{
			Gun.SpawnDefaultGun();
		}

		if (inputActions == null)
		{
			Debug.LogError("Assign the InputSystem_Actions asset to this character controller.", this);
			return;
		}

		playerActions = inputActions.FindActionMap(actionMapName, false);
		if (playerActions == null)
		{
			Debug.LogError($"Input action map '{actionMapName}' was not found.", this);
			return;
		}

		moveAction = playerActions.FindAction("Move", false);
		lookAction = playerActions.FindAction("Look", false);
		jumpAction = playerActions.FindAction("Jump", false);
		sprintAction = playerActions.FindAction("Sprint", false);
		crouchAction = playerActions.FindAction("Crouch", false);
	}

	private void Start()
	{
		if (lockCursorOnStart)
		{
			SetCursorLocked(true);
		}
	}

	private void OnEnable()
	{
		playerActions?.Enable();
	}

	private void OnDisable()
	{
		playerActions?.Disable();
		SetCursorLocked(false);
	}

	private void Update()
	{
		if (playerActions == null)
		{
			return;
		}

		if (EscMenu.IsPaused)
		{
			currentHorizontalVelocity = Vector3.zero;
			return;
		}

		UpdateCursor();
		Look();
		Move();
		UpdateCrouch();
	}

	private void UpdateCursor()
	{
		if (lockCursorOnStart && !EscMenu.IsPaused && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
		{
			SetCursorLocked(true);
		}
	}

	private void SetCursorLocked(bool locked)
	{
		Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
		Cursor.visible = !locked;
	}

	private void Look()
	{
		if (lookAction == null || Cursor.lockState != CursorLockMode.Locked)
		{
			return;
		}

		Vector2 lookInput = lookAction.ReadValue<Vector2>();
		bool usingMouse = lookAction.activeControl?.device is Mouse;
		float lookScale = usingMouse ? mouseSensitivity : controllerLookSpeed * Time.deltaTime;

		transform.Rotate(Vector3.up, lookInput.x * lookScale, Space.World);

		if (cameraPivot == null)
		{
			return;
		}

		pitch = Mathf.Clamp(pitch - lookInput.y * lookScale, -maximumLookAngle, maximumLookAngle);
		cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
	}

	private void Move()
	{
		bool crouching = crouchAction != null && crouchAction.IsPressed();
		bool sprinting = !crouching && sprintAction != null && sprintAction.IsPressed();
		float speed = crouching ? crouchSpeed : sprinting ? sprintSpeed : walkSpeed;

		Vector2 input = moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;
		input = Vector2.ClampMagnitude(input, 1f);

		Transform reference = cameraPivot != null ? cameraPivot : Camera.main != null ? Camera.main.transform : transform;
		Vector3 forward = Vector3.ProjectOnPlane(reference.forward, Vector3.up).normalized;
		Vector3 right = Vector3.ProjectOnPlane(reference.right, Vector3.up).normalized;
		Vector3 horizontalVelocity = (forward * input.y + right * input.x) * speed;
		currentHorizontalVelocity = horizontalVelocity;

		if (characterController.isGrounded)
		{
			if (verticalVelocity < 0f)
			{
				verticalVelocity = -2f;
			}

			if (jumpAction != null && jumpAction.WasPressedThisFrame() && !crouching)
			{
				verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
			}
		}

		verticalVelocity += gravity * Time.deltaTime;
		Vector3 velocity = horizontalVelocity + Vector3.up * verticalVelocity;
		characterController.Move(velocity * Time.deltaTime);
	}

	private void UpdateCrouch()
	{
		bool wantsToCrouch = crouchAction != null && crouchAction.IsPressed();
		float targetHeight = wantsToCrouch || !CanStand() ? crouchHeight : standingHeight;
		targetHeight = Mathf.Clamp(targetHeight, characterController.radius * 2f, standingHeight);

		float height = Mathf.MoveTowards(characterController.height, targetHeight, crouchTransitionSpeed * Time.deltaTime);
		characterController.height = height;

		Vector3 center = standingCenter;
		center.y -= (standingHeight - height) * 0.5f;
		characterController.center = center;

		if (cameraPivot != null)
		{
			float crouchOffset = standingHeight - height;
			cameraPivot.localPosition = new Vector3(0f, standingCameraHeight - crouchOffset * 0.85f, 0.08f);
		}
	}

	private bool CanStand()
	{
		if (characterController.height >= standingHeight - 0.01f)
		{
			return true;
		}

		Vector3 worldCenter = transform.TransformPoint(standingCenter);
		float radius = characterController.radius * 0.95f;
		float halfSegment = Mathf.Max(0f, standingHeight * 0.5f - radius);
		Vector3 bottom = worldCenter - Vector3.up * halfSegment;
		Vector3 top = worldCenter + Vector3.up * halfSegment;
		Collider[] overlaps = Physics.OverlapCapsule(bottom, top, radius, Physics.AllLayers, QueryTriggerInteraction.Ignore);

		foreach (Collider overlap in overlaps)
		{
			if (overlap != characterController && !overlap.transform.IsChildOf(transform))
			{
				return false;
			}
		}

		return true;
	}
}
