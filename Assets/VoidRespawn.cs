using UnityEngine;

[DisallowMultipleComponent]
public class VoidRespawn : MonoBehaviour
{
	[SerializeField] private float voidThresholdY = -10f;
	[SerializeField] private float respawnHeightOffset = 0.25f;

	private Rigidbody body;
	private Vector3 spawnPosition;
	private Quaternion spawnRotation;
	private bool initialized;

	public static void AttachToAllSceneRigidbodies(float thresholdY = -10f)
	{
		Rigidbody[] bodies = FindObjectsByType<Rigidbody>(FindObjectsInactive.Include, FindObjectsSortMode.None);
		for (int i = 0; i < bodies.Length; i++)
		{
			Rigidbody rb = bodies[i];
			if (rb == null || rb.GetComponent<control>() != null)
			{
				continue;
			}

			VoidRespawn respawn = rb.GetComponent<VoidRespawn>();
			if (respawn == null)
			{
				respawn = rb.gameObject.AddComponent<VoidRespawn>();
			}

			respawn.voidThresholdY = thresholdY;
			respawn.InitializeSpawnPoint();
		}
	}

	private void Awake()
	{
		InitializeSpawnPoint();
	}

	public void InitializeSpawnPoint()
	{
		if (initialized)
		{
			return;
		}

		body = GetComponent<Rigidbody>();
		spawnPosition = ResolveSafePlatformPosition(transform.position) + Vector3.up * respawnHeightOffset;
		spawnRotation = transform.rotation;
		initialized = true;
	}

	private void FixedUpdate()
	{
		if (!initialized)
		{
			InitializeSpawnPoint();
		}

		if (transform.position.y < voidThresholdY)
		{
			Respawn();
		}
	}

	public void Respawn()
	{
		if (body != null)
		{
			if (!body.isKinematic)
			{
				body.linearVelocity = Vector3.zero;
				body.angularVelocity = Vector3.zero;
			}

			body.position = spawnPosition;
			body.rotation = spawnRotation;
			body.WakeUp();
		}

		transform.SetPositionAndRotation(spawnPosition, spawnRotation);
	}

	public static Vector3 ResolveSafePlatformPosition(Vector3 initialPosition)
	{
		GameObject platform = GameObject.Find("Plane");
		if (platform == null)
		{
			return new Vector3(initialPosition.x, Mathf.Max(initialPosition.y, 1.5f), initialPosition.z);
		}

		Renderer platformRenderer = platform.GetComponent<Renderer>();
		if (platformRenderer != null)
		{
			Bounds b = platformRenderer.bounds;
			float margin = 1.0f;
			float minX = b.min.x + margin;
			float maxX = b.max.x - margin;
			float minZ = b.min.z + margin;
			float maxZ = b.max.z - margin;

			float safeX = minX < maxX ? Mathf.Clamp(initialPosition.x, minX, maxX) : b.center.x;
			float safeZ = minZ < maxZ ? Mathf.Clamp(initialPosition.z, minZ, maxZ) : b.center.z;
			float safeY = Mathf.Max(initialPosition.y, b.max.y + 1.2f);
			return new Vector3(safeX, safeY, safeZ);
		}

		Vector3 pos = platform.transform.position;
		return new Vector3(initialPosition.x, Mathf.Max(initialPosition.y, pos.y + 1.5f), initialPosition.z);
	}
}

