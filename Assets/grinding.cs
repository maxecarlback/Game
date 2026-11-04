using UnityEngine;

public class grinding : MonoBehaviour
{
	[SerializeField] private Transform scrapePoint;
	[SerializeField, Min(0.01f)] private float scrapeRadius = 0.14f;
	[SerializeField, Min(0.01f)] private float contactRadius = 0.12f;
	[SerializeField, Min(0.01f)] private float scrapeInterval = 0.04f;

	private readonly Collider[] nearbyColliders = new Collider[16];
	private Collider[] grinderColliders;
	private float nextScrapeTime;

	private void Awake()
	{
		grinderColliders = GetComponentsInChildren<Collider>();
	}

	private void FixedUpdate()
	{
		if (Time.time < nextScrapeTime || grinderColliders == null)
		{
			return;
		}

		if (scrapePoint != null)
		{
			if (TryScrapeNear(scrapePoint.position, null, contactRadius))
			{
				return;
			}
		}
		else
		{
			foreach (Collider grinderCollider in grinderColliders)
			{
				if (grinderCollider == null || !grinderCollider.enabled)
				{
					continue;
				}

				float probeRadius = grinderCollider.bounds.extents.magnitude + contactRadius;
				if (TryScrapeNear(grinderCollider.bounds.center, grinderCollider, probeRadius))
				{
					return;
				}
			}
		}
	}

	private bool TryScrapeNear(Vector3 probePosition, Collider grinderCollider, float probeRadius)
	{
		int colliderCount = Physics.OverlapSphereNonAlloc(
			probePosition,
			probeRadius,
			nearbyColliders,
			Physics.AllLayers,
			QueryTriggerInteraction.Collide);

		for (int i = 0; i < colliderCount; i++)
		{
			Collider nearbyCollider = nearbyColliders[i];
			if (nearbyCollider == null || nearbyCollider.transform == transform || nearbyCollider.transform.IsChildOf(transform))
			{
				continue;
			}

			Vector3 point;
			Vector3 normal;
			if (grinderCollider != null)
			{
				Vector3 grinderPoint = grinderCollider.ClosestPoint(nearbyCollider.bounds.center);
				point = nearbyCollider.ClosestPoint(grinderPoint);
				if (Vector3.Distance(grinderPoint, point) > contactRadius)
				{
					continue;
				}

				normal = grinderPoint - point;
			}
			else
			{
				point = nearbyCollider.ClosestPoint(probePosition);
				if (Vector3.Distance(probePosition, point) > probeRadius)
				{
					continue;
				}

				normal = probePosition - point;
			}

			wall wallSurface = GetOrCreateWallSurface(nearbyCollider);
			if (wallSurface == null)
			{
				continue;
			}

			if (normal.sqrMagnitude < 0.001f)
			{
				normal = -transform.forward;
			}

			Scrape(wallSurface, point, normal.normalized);
			return true;
		}

		return false;
	}

	private wall GetOrCreateWallSurface(Collider surfaceCollider)
	{
		if (surfaceCollider == null || surfaceCollider is CharacterController || surfaceCollider.GetComponentInParent<control>() != null)
		{
			return null;
		}

		return surfaceCollider.GetComponentInParent<wall>();
	}

	private void OnCollisionStay(Collision collision)
	{
		wall wallSurface = GetOrCreateWallSurface(collision.collider);
		if (wallSurface == null || Time.time < nextScrapeTime || collision.contactCount == 0)
		{
			return;
		}

		ContactPoint contact = collision.GetContact(0);
		Scrape(wallSurface, contact.point, contact.normal);
	}

	private void OnTriggerStay(Collider other)
	{
		wall wallSurface = GetOrCreateWallSurface(other);
		if (wallSurface == null || Time.time < nextScrapeTime)
		{
			return;
		}

		Vector3 point = other.ClosestPoint(transform.position);
		Vector3 normal = transform.position - point;
		if (normal.sqrMagnitude < 0.001f)
		{
			normal = -transform.forward;
		}

		Scrape(wallSurface, point, normal.normalized);
	}

	private void Scrape(wall wallSurface, Vector3 point, Vector3 normal)
	{
		if (wallSurface == null || Time.time < nextScrapeTime)
		{
			return;
		}

		wallSurface.ScrapeAtWorldPoint(point, normal, scrapeRadius);
		nextScrapeTime = Time.time + scrapeInterval;
	}
}
