using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using System.Collections;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class Gun : MonoBehaviour
{
	private const string MenuSceneName = "Start screen";

	[Header("Gun Settings")]
	[SerializeField] private float fireRate = 0.18f;
	[SerializeField] private float range = 80f;
	[SerializeField] private float impactForce = 14f;
	[SerializeField] private float bulletSpeed = 65f;

	private Rigidbody gunBody;
	private Transform visualRoot;
	private Transform slideTransform;
	private Transform muzzlePoint;
	private Renderer muzzleFlashRenderer;
	private Light muzzleLight;

	private Material darkMetalMaterial;
	private Material barrelMaterial;
	private Material gripMaterial;
	private Material accentMaterial;
	private Material flashMaterial;

	private float nextFireTime;
	private float recoilAmount;
	private Vector3 currentRecoilOffset;
	private float currentRecoilPitch;

	public Vector3 RecoilPositionOffset => currentRecoilOffset;
	public Quaternion RecoilRotationOffset => Quaternion.Euler(-currentRecoilPitch, 0f, 0f);

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void RegisterSceneListener()
	{
		SceneManager.sceneLoaded -= OnSceneLoaded;
		SceneManager.sceneLoaded += OnSceneLoaded;
	}

	private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		if (scene.name == MenuSceneName || FindAnyObjectByType<Gun>() != null)
		{
			return;
		}

		SpawnDefaultGun();
	}

	public static Gun SpawnDefaultGun()
	{
		GameObject gunObj = new GameObject("Gun");
		control player = FindAnyObjectByType<control>();
		if (player != null)
		{
			Vector3 forward = Vector3.ProjectOnPlane(player.transform.forward, Vector3.up).normalized;
			if (forward.sqrMagnitude < 0.01f)
			{
				forward = Vector3.forward;
			}

			gunObj.transform.position = player.transform.position + forward * 2.2f + Vector3.down * 0.4f;
			gunObj.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
		}
		else
		{
			gunObj.transform.position = new Vector3(1f, 1.5f, -6.5f);
		}

		Rigidbody body = gunObj.AddComponent<Rigidbody>();
		body.mass = 1.2f;
		body.interpolation = RigidbodyInterpolation.Interpolate;

		BoxCollider col = gunObj.AddComponent<BoxCollider>();
		col.center = new Vector3(0f, 0.02f, 0.04f);
		col.size = new Vector3(0.14f, 0.24f, 0.46f);

		Gun gun = gunObj.AddComponent<Gun>();
		if (gunObj.GetComponent<VoidRespawn>() == null)
		{
			gunObj.AddComponent<VoidRespawn>();
		}

		return gun;
	}

	private void Awake()
	{
		gameObject.name = "Gun";
		gunBody = GetComponent<Rigidbody>();

		BoxCollider box = GetComponent<BoxCollider>();
		if (box == null)
		{
			box = gameObject.AddComponent<BoxCollider>();
			box.center = new Vector3(0f, 0.02f, 0.04f);
			box.size = new Vector3(0.14f, 0.24f, 0.46f);
		}

		Renderer existingRenderer = GetComponent<Renderer>();
		Material templateMat = existingRenderer != null ? existingRenderer.sharedMaterial : null;
		if (templateMat == null)
		{
			Renderer anyRenderer = FindAnyObjectByType<MeshRenderer>();
			if (anyRenderer != null)
			{
				templateMat = anyRenderer.sharedMaterial;
			}
		}

		if (existingRenderer != null)
		{
			existingRenderer.enabled = false;
		}

		CreateMaterials(templateMat);
		BuildGunModel();
	}

	private void OnDisable()
	{
		if (muzzleFlashRenderer != null) muzzleFlashRenderer.enabled = false;
		if (muzzleLight != null) muzzleLight.enabled = false;
		recoilAmount = 0f;
		currentRecoilOffset = Vector3.zero;
		currentRecoilPitch = 0f;
	}

	private void OnDestroy()
	{
		if (darkMetalMaterial != null) Destroy(darkMetalMaterial);
		if (barrelMaterial != null) Destroy(barrelMaterial);
		if (gripMaterial != null) Destroy(gripMaterial);
		if (accentMaterial != null) Destroy(accentMaterial);
		if (flashMaterial != null) Destroy(flashMaterial);
	}

	private void CreateMaterials(Material templateMat)
	{
		Shader shader = templateMat != null && templateMat.shader != null
			? templateMat.shader
			: Shader.Find("Universal Render Pipeline/Lit");
		if (shader == null)
		{
			shader = Shader.Find("Standard");
		}

		darkMetalMaterial = CreateMat(templateMat, shader, "GunSlideMat", new Color(0.16f, 0.18f, 0.20f), 0.75f, 0.65f);
		barrelMaterial = CreateMat(templateMat, shader, "GunBarrelMat", new Color(0.28f, 0.30f, 0.33f), 0.85f, 0.75f);
		gripMaterial = CreateMat(templateMat, shader, "GunGripMat", new Color(0.11f, 0.11f, 0.12f), 0.15f, 0.25f);
		accentMaterial = CreateMat(templateMat, shader, "GunAccentMat", new Color(0.91f, 0.54f, 0.25f), 0.4f, 0.6f);
		flashMaterial = CreateMat(templateMat, shader, "GunFlashMat", new Color(1f, 0.78f, 0.25f), 0f, 0.9f);
		if (flashMaterial.HasProperty("_EmissionColor"))
		{
			flashMaterial.EnableKeyword("_EMISSION");
			flashMaterial.SetColor("_EmissionColor", new Color(2.5f, 1.6f, 0.4f));
		}
	}

	private static Material CreateMat(Material templateMat, Shader shader, string name, Color color, float metallic, float smoothness)
	{
		Material mat = templateMat != null ? new Material(templateMat) : new Material(shader);
		mat.name = name;
		mat.color = color;
		if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
		if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
		if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
		return mat;
	}

	private void BuildGunModel()
	{
		Transform existing = transform.Find("GunVisuals");
		if (existing != null)
		{
			Destroy(existing.gameObject);
		}

		visualRoot = new GameObject("GunVisuals").transform;
		visualRoot.SetParent(transform, false);

		Transform frame = CreatePart("Frame", PrimitiveType.Cube, visualRoot, barrelMaterial);
		frame.localPosition = new Vector3(0f, 0.025f, 0.04f);
		frame.localScale = new Vector3(0.048f, 0.036f, 0.28f);

		slideTransform = CreatePart("Slide", PrimitiveType.Cube, visualRoot, darkMetalMaterial);
		slideTransform.localPosition = new Vector3(0f, 0.062f, 0.03f);
		slideTransform.localScale = new Vector3(0.054f, 0.044f, 0.30f);

		Transform barrel = CreatePart("Barrel", PrimitiveType.Cylinder, visualRoot, barrelMaterial);
		barrel.localPosition = new Vector3(0f, 0.060f, 0.21f);
		barrel.localRotation = Quaternion.Euler(90f, 0f, 0f);
		barrel.localScale = new Vector3(0.026f, 0.06f, 0.026f);

		Transform grip = CreatePart("Grip", PrimitiveType.Cube, visualRoot, gripMaterial);
		grip.localPosition = new Vector3(0f, -0.055f, -0.055f);
		grip.localRotation = Quaternion.Euler(-16f, 0f, 0f);
		grip.localScale = new Vector3(0.044f, 0.14f, 0.065f);

		Transform triggerGuard = CreatePart("TriggerGuard", PrimitiveType.Cube, visualRoot, gripMaterial);
		triggerGuard.localPosition = new Vector3(0f, -0.015f, 0.015f);
		triggerGuard.localScale = new Vector3(0.022f, 0.04f, 0.075f);

		Transform frontSight = CreatePart("FrontSight", PrimitiveType.Cube, slideTransform, accentMaterial);
		frontSight.localPosition = new Vector3(0f, 0.58f, 0.44f);
		frontSight.localScale = new Vector3(0.18f, 0.22f, 0.06f);

		Transform rearSight = CreatePart("RearSight", PrimitiveType.Cube, slideTransform, accentMaterial);
		rearSight.localPosition = new Vector3(0f, 0.58f, -0.44f);
		rearSight.localScale = new Vector3(0.36f, 0.22f, 0.06f);

		muzzlePoint = new GameObject("MuzzlePoint").transform;
		muzzlePoint.SetParent(visualRoot, false);
		muzzlePoint.localPosition = new Vector3(0f, 0.060f, 0.28f);

		Transform flashMesh = CreatePart("MuzzleFlash", PrimitiveType.Sphere, muzzlePoint, flashMaterial);
		flashMesh.localPosition = new Vector3(0f, 0f, 0.03f);
		flashMesh.localScale = new Vector3(0.07f, 0.07f, 0.14f);
		muzzleFlashRenderer = flashMesh.GetComponent<Renderer>();
		if (muzzleFlashRenderer != null)
		{
			muzzleFlashRenderer.enabled = false;
		}

		GameObject lightObj = new GameObject("MuzzleLight");
		lightObj.transform.SetParent(muzzlePoint, false);
		muzzleLight = lightObj.AddComponent<Light>();
		muzzleLight.type = LightType.Point;
		muzzleLight.color = new Color(1f, 0.72f, 0.3f);
		muzzleLight.range = 4.5f;
		muzzleLight.intensity = 3.5f;
		muzzleLight.enabled = false;
	}

	private static Transform CreatePart(string name, PrimitiveType type, Transform parent, Material mat)
	{
		GameObject obj = GameObject.CreatePrimitive(type);
		obj.name = name;
		obj.transform.SetParent(parent, false);

		Collider col = obj.GetComponent<Collider>();
		if (col != null)
		{
			Destroy(col);
		}

		Renderer renderer = obj.GetComponent<Renderer>();
		if (renderer != null && mat != null)
		{
			renderer.sharedMaterial = mat;
		}

		return obj.transform;
	}

	private void Update()
	{
		recoilAmount = Mathf.MoveTowards(recoilAmount, 0f, Time.deltaTime * 6f);
		currentRecoilOffset = Vector3.Lerp(currentRecoilOffset, new Vector3(0f, 0.02f, -0.09f) * recoilAmount, Time.deltaTime * 25f);
		currentRecoilPitch = Mathf.Lerp(currentRecoilPitch, 11f * recoilAmount, Time.deltaTime * 25f);

		if (slideTransform != null)
		{
			slideTransform.localPosition = new Vector3(0f, 0.062f, 0.03f - 0.045f * recoilAmount);
		}
	}

	public bool TryShoot(Camera aimCamera)
	{
		if (Time.time < nextFireTime)
		{
			return false;
		}

		nextFireTime = Time.time + fireRate;
		recoilAmount = 1f;

		Vector3 shootOrigin = muzzlePoint != null ? muzzlePoint.position : transform.position + transform.forward * 0.25f;
		Vector3 shootDir = aimCamera != null ? aimCamera.transform.forward : transform.forward;

		if (aimCamera != null)
		{
			RaycastHit[] hits = Physics.RaycastAll(aimCamera.transform.position, aimCamera.transform.forward, range, Physics.AllLayers, QueryTriggerInteraction.Ignore);
			RaycastHit closestHit = default;
			bool foundHit = false;

			for (int i = 0; i < hits.Length; i++)
			{
				Transform hitTransform = hits[i].transform;
				if (hitTransform == null
					|| hitTransform == transform
					|| hitTransform.IsChildOf(transform)
					|| hitTransform.GetComponentInParent<control>() != null)
				{
					continue;
				}

				if (!foundHit || hits[i].distance < closestHit.distance)
				{
					closestHit = hits[i];
					foundHit = true;
				}
			}

			if (foundHit)
			{
				shootDir = (closestHit.point - shootOrigin).normalized;
				ApplyHitEffects(closestHit, shootDir);
			}
		}

		SpawnBulletVisual(shootOrigin, shootDir);
		StartCoroutine(FlashMuzzle());
		if (MultiplayerManager.Instance != null)
		{
			MultiplayerManager.Instance.BroadcastShot(shootOrigin, shootDir);
		}
		return true;
	}

	private void ApplyHitEffects(RaycastHit hit, Vector3 shootDir)
	{
		if (hit.rigidbody != null && !hit.rigidbody.isKinematic)
		{
			hit.rigidbody.AddForceAtPosition(shootDir * impactForce, hit.point, ForceMode.Impulse);
		}

		ReactiveTarget target = hit.collider.GetComponentInParent<ReactiveTarget>();
		if (target != null)
		{
			target.OnHit();
		}

		ExplosiveBarrel barrel = hit.collider.GetComponentInParent<ExplosiveBarrel>();
		if (barrel != null)
		{
			barrel.TriggerExplosion();
		}

		wall wallSurface = hit.collider.GetComponentInParent<wall>();
		if (wallSurface != null)
		{
			wallSurface.ScrapeAtWorldPoint(hit.point, hit.normal, 0.14f);
		}
	}

	private void SpawnBulletVisual(Vector3 origin, Vector3 direction)
	{
		GameObject bullet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
		bullet.name = "BulletProjectile";
		bullet.transform.position = origin;
		bullet.transform.localScale = new Vector3(0.04f, 0.04f, 0.16f);
		bullet.transform.rotation = Quaternion.LookRotation(direction);

		Collider col = bullet.GetComponent<Collider>();
		if (col != null)
		{
			Destroy(col);
		}

		Renderer renderer = bullet.GetComponent<Renderer>();
		if (renderer != null && flashMaterial != null)
		{
			renderer.sharedMaterial = flashMaterial;
		}

		BulletVisualMover mover = bullet.AddComponent<BulletVisualMover>();
		mover.Initialize(direction * bulletSpeed, 0.6f);
	}

	private IEnumerator FlashMuzzle()
	{
		if (muzzleFlashRenderer != null) muzzleFlashRenderer.enabled = true;
		if (muzzleLight != null) muzzleLight.enabled = true;
		yield return new WaitForSeconds(0.045f);
		if (muzzleFlashRenderer != null) muzzleFlashRenderer.enabled = false;
		if (muzzleLight != null) muzzleLight.enabled = false;
	}
}

public class BulletVisualMover : MonoBehaviour
{
	private Vector3 velocity;
	private float destroyTime;

	public void Initialize(Vector3 bulletVelocity, float lifetime)
	{
		velocity = bulletVelocity;
		destroyTime = Time.time + lifetime;
	}

	private void Update()
	{
		float dt = Time.deltaTime;
		Vector3 nextPos = transform.position + velocity * dt;
		if (Physics.Linecast(transform.position, nextPos, out RaycastHit hit, Physics.AllLayers, QueryTriggerInteraction.Ignore))
		{
			Destroy(gameObject);
			return;
		}

		transform.position = nextPos;
		if (Time.time >= destroyTime)
		{
			Destroy(gameObject);
		}
	}
}
