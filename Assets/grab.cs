using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public class grab : MonoBehaviour
{
	private struct CollisionPairState
	{
		public Collider playerCollider;
		public Collider objectCollider;
		public bool wasIgnored;
	}

	[SerializeField] private Camera playerCamera;
	[SerializeField] private float pickupRange = 3f;
	[SerializeField] private float holdDistance = 2f;
	[SerializeField] private float minimumHoldDistance = 1.25f;
	[SerializeField] private float maximumHoldDistance = 4f;
	[SerializeField, Min(0f)] private float obstacleCheckRadius = 0.1f;
	[SerializeField] private float throwSpeedMultiplier = 1f;
	[SerializeField] private float maximumThrowSpeed = 8f;
	[SerializeField] private float upwardThrowSpeed = 0.35f;
	[SerializeField] private LayerMask pickupLayers = ~0;

	private readonly RaycastHit[] raycastHits = new RaycastHit[16];
	private readonly RaycastHit[] obstacleHits = new RaycastHit[16];
	private readonly List<CollisionPairState> ignoredCollisions = new List<CollisionPairState>();
	private Collider[] playerColliders;
	private Rigidbody heldBody;
	private Gun heldGun;
	private Quaternion heldRotationOffset;
	private bool wasKinematic;
	private bool usedGravity;
	private bool pickedUpThisFrame;
	private Vector3 lastHoldPosition;
	private Vector3 heldVelocity;
	private float lastGrabAttemptTime = -10f;

	public bool IsHoldingItem => heldBody != null;
	public bool IsHoldingGun => heldGun != null;
	public Rigidbody HeldBody => heldBody;
	public Camera PlayerCamera => playerCamera;
	public float LastGrabAttemptTime => lastGrabAttemptTime;

	private void Awake()
	{
		playerColliders = GetComponentsInChildren<Collider>();

		if (playerCamera == null)
		{
			playerCamera = Camera.main;
		}
	}

	private void Update()
	{
		if (EscMenu.IsPaused || Mouse.current == null)
		{
			return;
		}

		if (heldBody != null)
		{
			UpdateHeldVelocity();

			float scroll = Mouse.current.scroll.ReadValue().y;
			if (Mathf.Abs(scroll) > 0.01f)
			{
				holdDistance = Mathf.Clamp(holdDistance + scroll * 0.0025f, minimumHoldDistance, maximumHoldDistance);
			}
		}

		if (Mouse.current.leftButton.wasPressedThisFrame && heldBody == null)
		{
			TryPickup();
		}

		if (heldBody != null)
		{
			if (heldGun != null)
			{
				if (!pickedUpThisFrame && Mouse.current.leftButton.wasPressedThisFrame)
				{
					heldGun.TryShoot(playerCamera);
				}
				else if (Mouse.current.rightButton.wasPressedThisFrame
					|| (Keyboard.current != null && Keyboard.current.gKey.wasPressedThisFrame))
				{
					Drop(true);
				}
			}
			else if (Mouse.current.leftButton.wasReleasedThisFrame)
			{
				Drop(true);
			}
		}

		pickedUpThisFrame = false;
	}

	private void FixedUpdate()
	{
		if (heldBody == null)
		{
			return;
		}

		if (heldGun != null && playerCamera != null)
		{
			Vector3 targetPos = GetGunHoldPosition() + playerCamera.transform.TransformVector(heldGun.RecoilPositionOffset);
			Quaternion targetRot = playerCamera.transform.rotation * heldGun.RecoilRotationOffset;
			heldBody.MovePosition(targetPos);
			heldBody.MoveRotation(targetRot);
			return;
		}

		heldBody.MovePosition(GetHoldPosition());
		heldBody.MoveRotation(playerCamera.transform.rotation * heldRotationOffset);
	}

	private void OnDisable()
	{
		Drop();
	}

	private void TryPickup()
	{
		lastGrabAttemptTime = Time.time;

		if (playerCamera == null)
		{
			Debug.LogWarning("Assign a player camera to the grab component.", this);
			return;
		}

		int hitCount = Physics.RaycastNonAlloc(
			playerCamera.transform.position,
			playerCamera.transform.forward,
			raycastHits,
			pickupRange,
			pickupLayers,
			QueryTriggerInteraction.Ignore);

		RaycastHit closestHit = default;
		bool foundHit = false;

		for (int i = 0; i < hitCount; i++)
		{
			Transform hitTransform = raycastHits[i].transform;
			if (hitTransform == transform || hitTransform.IsChildOf(transform))
			{
				continue;
			}

			if (!foundHit || raycastHits[i].distance < closestHit.distance)
			{
				closestHit = raycastHits[i];
				foundHit = true;
			}
		}

		if (!foundHit)
		{
			return;
		}

		Rigidbody body = closestHit.rigidbody;
		if (body == null || body.transform == transform || body.transform.IsChildOf(transform))
		{
			return;
		}

		BeginHolding(body);
	}

	public bool TryHoldItem(Rigidbody body)
	{
		if (body == null || heldBody != null || playerCamera == null)
		{
			return false;
		}

		BeginHolding(body);
		return true;
	}

	public GameObject TakeHeldItem()
	{
		if (heldBody == null)
		{
			return null;
		}

		Rigidbody body = heldBody;
		RestorePlayerCollisions();
		body.isKinematic = wasKinematic;
		body.useGravity = usedGravity;
		body.linearVelocity = Vector3.zero;
		body.angularVelocity = Vector3.zero;
		heldBody = null;
		heldGun = null;
		return body.gameObject;
	}

	private void BeginHolding(Rigidbody body)
	{
		heldBody = body;
		heldGun = body.GetComponent<Gun>();
		pickedUpThisFrame = true;
		wasKinematic = heldBody.isKinematic;
		usedGravity = heldBody.useGravity;
		heldRotationOffset = heldGun != null
			? Quaternion.identity
			: Quaternion.Inverse(playerCamera.transform.rotation) * heldBody.rotation;
		lastHoldPosition = heldGun != null ? GetGunHoldPosition() : GetHoldPosition();
		heldVelocity = Vector3.zero;
		heldBody.linearVelocity = Vector3.zero;
		heldBody.angularVelocity = Vector3.zero;
		IgnorePlayerCollisions(heldBody);
		heldBody.useGravity = false;
		heldBody.isKinematic = true;
	}

	private void UpdateHeldVelocity()
	{
		if (Time.deltaTime <= 0f)
		{
			return;
		}

		Vector3 currentHoldPosition = GetHoldPosition();
		heldVelocity = Vector3.ClampMagnitude(
			(currentHoldPosition - lastHoldPosition) / Time.deltaTime,
			maximumThrowSpeed);
		lastHoldPosition = currentHoldPosition;
	}

	private void IgnorePlayerCollisions(Rigidbody body)
	{
		Collider[] objectColliders = body.GetComponentsInChildren<Collider>();
		foreach (Collider playerCollider in playerColliders)
		{
			if (playerCollider == null)
			{
				continue;
			}

			foreach (Collider objectCollider in objectColliders)
			{
				if (objectCollider == null || objectCollider == playerCollider)
				{
					continue;
				}

				bool wasIgnored = Physics.GetIgnoreCollision(playerCollider, objectCollider);
				ignoredCollisions.Add(new CollisionPairState
				{
					playerCollider = playerCollider,
					objectCollider = objectCollider,
					wasIgnored = wasIgnored
				});
				Physics.IgnoreCollision(playerCollider, objectCollider, true);
			}
		}
	}

	private void RestorePlayerCollisions()
	{
		foreach (CollisionPairState pair in ignoredCollisions)
		{
			if (pair.playerCollider != null && pair.objectCollider != null)
			{
				Physics.IgnoreCollision(pair.playerCollider, pair.objectCollider, pair.wasIgnored);
			}
		}

		ignoredCollisions.Clear();
	}

	private void Drop(bool throwItem = false)
	{
		if (heldBody == null)
		{
			RestorePlayerCollisions();
			return;
		}

		RestorePlayerCollisions();
		heldBody.isKinematic = wasKinematic;
		heldBody.useGravity = usedGravity;
		if (!wasKinematic)
		{
			heldBody.linearVelocity = Vector3.zero;
			heldBody.angularVelocity = Vector3.zero;
			if (throwItem && playerCamera != null)
			{
				Vector3 throwVelocity = Vector3.ClampMagnitude(heldVelocity * throwSpeedMultiplier, maximumThrowSpeed)
					+ Vector3.up * upwardThrowSpeed;
				heldBody.AddForce(throwVelocity, ForceMode.VelocityChange);
			}
		}

		heldBody = null;
		heldGun = null;
	}

	private Vector3 GetGunHoldPosition()
	{
		Transform cameraTransform = playerCamera.transform;
		Vector3 origin = cameraTransform.position;
		Vector3 desiredPosition = origin
			+ cameraTransform.forward * 0.68f
			+ cameraTransform.right * 0.14f
			- cameraTransform.up * 0.16f;
		return ResolveObstacleSafePosition(origin, desiredPosition);
	}

	private Vector3 GetHoldPosition()
	{
		Transform cameraTransform = playerCamera.transform;
		Vector3 origin = cameraTransform.position;
		Vector3 desiredPosition = origin + cameraTransform.forward * holdDistance;
		return ResolveObstacleSafePosition(origin, desiredPosition);
	}

	private Vector3 ResolveObstacleSafePosition(Vector3 origin, Vector3 desiredPosition)
	{
		Vector3 direction = desiredPosition - origin;
		float distance = direction.magnitude;
		if (distance <= 0f || obstacleCheckRadius <= 0f)
		{
			return desiredPosition;
		}

		direction /= distance;
		int hitCount = Physics.SphereCastNonAlloc(
			origin,
			obstacleCheckRadius,
			direction,
			obstacleHits,
			distance,
			Physics.AllLayers,
			QueryTriggerInteraction.Ignore);
		float safeDistance = distance;

		for (int i = 0; i < hitCount; i++)
		{
			Transform hitTransform = obstacleHits[i].transform;
			if (hitTransform == null
				|| hitTransform == transform
				|| hitTransform.IsChildOf(transform)
				|| hitTransform == heldBody.transform
				|| hitTransform.IsChildOf(heldBody.transform))
			{
				continue;
			}

			safeDistance = Mathf.Min(safeDistance, obstacleHits[i].distance);
		}

		return origin + direction * Mathf.Max(0f, safeDistance - 0.02f);
	}
}
