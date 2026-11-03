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
	[SerializeField] private LayerMask pickupLayers = ~0;

	private readonly RaycastHit[] raycastHits = new RaycastHit[16];
	private readonly List<CollisionPairState> ignoredCollisions = new List<CollisionPairState>();
	private Collider[] playerColliders;
	private Rigidbody heldBody;
	private Quaternion heldRotationOffset;
	private bool wasKinematic;
	private bool usedGravity;

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
		if (Mouse.current == null)
		{
			return;
		}

		if (heldBody != null)
		{
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

		if (Mouse.current.leftButton.wasReleasedThisFrame && heldBody != null)
		{
			Drop();
		}
	}

	private void FixedUpdate()
	{
		if (heldBody == null)
		{
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

		heldBody = body;
		wasKinematic = heldBody.isKinematic;
		usedGravity = heldBody.useGravity;
		heldRotationOffset = Quaternion.Inverse(playerCamera.transform.rotation) * heldBody.rotation;
		heldBody.linearVelocity = Vector3.zero;
		heldBody.angularVelocity = Vector3.zero;
		IgnorePlayerCollisions(heldBody);
		heldBody.useGravity = false;
		heldBody.isKinematic = true;
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

	private void Drop()
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
		}

		heldBody = null;
	}

	private Vector3 GetHoldPosition()
	{
		Transform cameraTransform = playerCamera.transform;
		return cameraTransform.position + cameraTransform.forward * holdDistance;
	}
}
