using UnityEngine;
using System.Collections.Generic;

public class wall : MonoBehaviour
{
	[SerializeField] private Renderer wallRenderer;
	[SerializeField, Range(64, 512)] private int textureSize = 256;
	[SerializeField, Range(0f, 1f)] private float rustCoverage = 0.68f;
	[SerializeField, Range(0f, 1f)] private float metallic = 0.28f;
	[SerializeField, Range(0f, 1f)] private float rustSmoothness = 0.2f;
	[SerializeField, Range(0f, 1f)] private float scrapedMetallic = 0.8f;
	[SerializeField, Range(0f, 1f)] private float scrapedSmoothness = 0.9f;
	[SerializeField, Min(0.01f)] private float scrapeRadius = 0.14f;
	[SerializeField] private int textureSeed = 17;
	[SerializeField] private Color rustColor = new Color(0.52f, 0.19f, 0.055f);
	[SerializeField] private Color bareMetalColor = new Color(0.48f, 0.5f, 0.48f);

	private readonly List<Material> runtimeMaterials = new List<Material>();
	private Texture2D rustTexture;
	private Texture2D metallicTexture;
	private Color[] rustPixels;
	private Color[] metallicPixels;

	private void Awake()
	{
		if (wallRenderer == null)
		{
			wallRenderer = GetComponent<Renderer>();
		}

		if (wallRenderer == null)
		{
			wallRenderer = GetComponentInChildren<Renderer>();
		}

		if (wallRenderer == null)
		{
			Debug.LogWarning("Assign a wall Renderer to apply the rusty texture.", this);
			return;
		}

		EnsureSolidCollider();
		rustTexture = CreateRustTexture();
		metallicTexture = CreateMetallicTexture();
		Material[] materials = wallRenderer.materials;
		foreach (Material material in materials)
		{
			if (material == null)
			{
				continue;
			}

			ApplyRust(material);
			runtimeMaterials.Add(material);
		}

		wallRenderer.materials = materials;
	}

	private void EnsureSolidCollider()
	{
		Collider[] wallColliders = GetComponentsInChildren<Collider>();
		if (wallColliders.Length == 0)
		{
			MeshFilter meshFilter = wallRenderer.GetComponent<MeshFilter>();
			if (meshFilter != null && meshFilter.sharedMesh != null)
			{
				MeshCollider meshCollider = wallRenderer.gameObject.AddComponent<MeshCollider>();
				meshCollider.sharedMesh = meshFilter.sharedMesh;
			}
			else
			{
				BoxCollider boxCollider = wallRenderer.gameObject.AddComponent<BoxCollider>();
				boxCollider.center = wallRenderer.localBounds.center;
				boxCollider.size = wallRenderer.localBounds.size;
			}

			return;
		}

		foreach (Collider wallCollider in wallColliders)
		{
			wallCollider.isTrigger = false;
		}
	}

	private Texture2D CreateRustTexture()
	{
		int size = Mathf.Clamp(textureSize, 64, 512);
		Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
		{
			name = "Wall Rust Texture",
			wrapMode = TextureWrapMode.Repeat,
			filterMode = FilterMode.Bilinear
		};
		rustPixels = new Color[size * size];
		float seed = textureSeed * 13.37f;
		Color darkMetal = new Color(0.12f, 0.135f, 0.13f);
		Color deepRust = rustColor * 0.58f;
		Color brightRust = rustColor * 1.28f;
		float rustThreshold = Mathf.Lerp(0.8f, 0.32f, rustCoverage);

		for (int y = 0; y < size; y++)
		{
			for (int x = 0; x < size; x++)
			{
				float coarse = Mathf.PerlinNoise(x * 0.025f + seed, y * 0.025f + seed);
				float medium = Mathf.PerlinNoise(x * 0.075f + seed + 29f, y * 0.075f + seed + 29f);
				float fine = Mathf.PerlinNoise(x * 0.2f + seed + 71f, y * 0.2f + seed + 71f);
				float pattern = coarse * 0.62f + medium * 0.28f + fine * 0.1f;
				float rustMask = Mathf.SmoothStep(rustThreshold - 0.12f, rustThreshold + 0.12f, pattern);
				Color rustShade = Color.Lerp(deepRust, brightRust, medium * 0.7f + fine * 0.3f);
				float pitting = Mathf.Lerp(0.72f, 1f, fine);
				rustPixels[y * size + x] = Color.Lerp(darkMetal, rustShade * pitting, rustMask);
			}
		}

		texture.SetPixels(rustPixels);
		texture.Apply();
		return texture;
	}

	private Texture2D CreateMetallicTexture()
	{
		int size = Mathf.Clamp(textureSize, 64, 512);
		Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
		{
			name = "Wall Rust Smoothness Map",
			wrapMode = TextureWrapMode.Repeat,
			filterMode = FilterMode.Bilinear
		};
		metallicPixels = new Color[size * size];
		Color rustySurface = new Color(metallic, metallic, metallic, rustSmoothness);
		for (int i = 0; i < metallicPixels.Length; i++)
		{
			metallicPixels[i] = rustySurface;
		}

		texture.SetPixels(metallicPixels);
		texture.Apply();
		return texture;
	}

	public void ScrapeAtWorldPoint(Vector3 worldPoint, Vector3 worldNormal, float radius)
	{
		if (rustTexture == null || wallRenderer == null)
		{
			return;
		}

		Vector2 uv = GetWallUv(worldPoint, worldNormal);
		MeshFilter meshFilter = wallRenderer.GetComponent<MeshFilter>();
		Bounds localBounds = meshFilter != null && meshFilter.sharedMesh != null
			? meshFilter.sharedMesh.bounds
			: new Bounds(Vector3.zero, Vector3.one);
		Vector3 localNormal = wallRenderer.transform.InverseTransformDirection(worldNormal).normalized;
		Vector3 scale = wallRenderer.transform.lossyScale;
		float width;
		float height;

		if (Mathf.Abs(localNormal.x) >= Mathf.Abs(localNormal.y) && Mathf.Abs(localNormal.x) >= Mathf.Abs(localNormal.z))
		{
			width = localBounds.size.z * Mathf.Abs(scale.z);
			height = localBounds.size.y * Mathf.Abs(scale.y);
		}
		else if (Mathf.Abs(localNormal.y) >= Mathf.Abs(localNormal.z))
		{
			width = localBounds.size.x * Mathf.Abs(scale.x);
			height = localBounds.size.z * Mathf.Abs(scale.z);
		}
		else
		{
			width = localBounds.size.x * Mathf.Abs(scale.x);
			height = localBounds.size.y * Mathf.Abs(scale.y);
		}

		float uvRadius = radius / Mathf.Max(0.01f, Mathf.Min(width, height));
		ScrapeAtUV(uv, uvRadius);
	}

	private Vector2 GetWallUv(Vector3 worldPoint, Vector3 worldNormal)
	{
		Collider[] wallColliders = GetComponentsInChildren<Collider>();
		Vector3 normal = worldNormal.sqrMagnitude > 0.001f ? worldNormal.normalized : wallRenderer.transform.forward;
		foreach (Collider wallCollider in wallColliders)
		{
			if (!(wallCollider is MeshCollider meshCollider))
			{
				continue;
			}

			RaycastHit hit;
			if (meshCollider.Raycast(new Ray(worldPoint + normal * 0.05f, -normal), out hit, 0.1f)
				|| meshCollider.Raycast(new Ray(worldPoint - normal * 0.05f, normal), out hit, 0.1f))
			{
				return hit.textureCoord;
			}
		}

		Vector3 localPoint = wallRenderer.transform.InverseTransformPoint(worldPoint);
		Vector3 localNormal = wallRenderer.transform.InverseTransformDirection(normal).normalized;
		MeshFilter meshFilter = wallRenderer.GetComponent<MeshFilter>();
		Bounds bounds = meshFilter != null && meshFilter.sharedMesh != null
			? meshFilter.sharedMesh.bounds
			: new Bounds(Vector3.zero, Vector3.one);

		if (Mathf.Abs(localNormal.x) >= Mathf.Abs(localNormal.y) && Mathf.Abs(localNormal.x) >= Mathf.Abs(localNormal.z))
		{
			return new Vector2(
				Mathf.InverseLerp(bounds.min.z, bounds.max.z, localPoint.z),
				Mathf.InverseLerp(bounds.min.y, bounds.max.y, localPoint.y));
		}

		if (Mathf.Abs(localNormal.y) >= Mathf.Abs(localNormal.z))
		{
			return new Vector2(
				Mathf.InverseLerp(bounds.min.x, bounds.max.x, localPoint.x),
				Mathf.InverseLerp(bounds.min.z, bounds.max.z, localPoint.z));
		}

		return new Vector2(
			Mathf.InverseLerp(bounds.min.x, bounds.max.x, localPoint.x),
			Mathf.InverseLerp(bounds.min.y, bounds.max.y, localPoint.y));
	}

	private void ScrapeAtUV(Vector2 uv, float radiusUv)
	{
		int size = rustTexture.width;
		int centerX = Mathf.Clamp(Mathf.RoundToInt(uv.x * (size - 1)), 0, size - 1);
		int centerY = Mathf.Clamp(Mathf.RoundToInt(uv.y * (size - 1)), 0, size - 1);
		int pixelRadius = Mathf.Clamp(Mathf.CeilToInt(radiusUv * size), 1, size / 3);
		int minX = Mathf.Max(0, centerX - pixelRadius);
		int minY = Mathf.Max(0, centerY - pixelRadius);
		int patchWidth = Mathf.Min(size - 1, centerX + pixelRadius) - minX + 1;
		int patchHeight = Mathf.Min(size - 1, centerY + pixelRadius) - minY + 1;
		Color[] rustPatch = new Color[patchWidth * patchHeight];
		Color[] metallicPatch = new Color[patchWidth * patchHeight];

		for (int y = 0; y < patchHeight; y++)
		{
			for (int x = 0; x < patchWidth; x++)
			{
				int pixelX = minX + x;
				int pixelY = minY + y;
				int pixelIndex = pixelY * size + pixelX;
				float distance = Vector2.Distance(new Vector2(pixelX, pixelY), new Vector2(centerX, centerY));
				if (distance <= pixelRadius)
				{
					float blend = 1f - Mathf.SmoothStep(pixelRadius * 0.72f, pixelRadius, distance);
					rustPixels[pixelIndex] = Color.Lerp(rustPixels[pixelIndex], bareMetalColor, blend);
					metallicPixels[pixelIndex] = Color.Lerp(
						metallicPixels[pixelIndex],
						new Color(scrapedMetallic, scrapedMetallic, scrapedMetallic, scrapedSmoothness),
						blend);
				}

				int patchIndex = y * patchWidth + x;
				rustPatch[patchIndex] = rustPixels[pixelIndex];
				metallicPatch[patchIndex] = metallicPixels[pixelIndex];
			}
		}

		rustTexture.SetPixels(minX, minY, patchWidth, patchHeight, rustPatch);
		metallicTexture.SetPixels(minX, minY, patchWidth, patchHeight, metallicPatch);
		rustTexture.Apply(false, false);
		metallicTexture.Apply(false, false);
	}

	private void ApplyRust(Material material)
	{
		if (material.HasProperty("_BaseMap"))
		{
			material.SetTexture("_BaseMap", rustTexture);
		}
		else if (material.HasProperty("_MainTex"))
		{
			material.SetTexture("_MainTex", rustTexture);
		}

		if (material.HasProperty("_MetallicGlossMap"))
		{
			material.SetTexture("_MetallicGlossMap", metallicTexture);
			material.EnableKeyword("_METALLICSPECGLOSSMAP");
			material.EnableKeyword("_METALLICGLOSSMAP");
		}

		if (material.HasProperty("_BaseColor"))
		{
			material.SetColor("_BaseColor", Color.white);
		}
		else if (material.HasProperty("_Color"))
		{
			material.SetColor("_Color", Color.white);
		}

		if (material.HasProperty("_Metallic"))
		{
			material.SetFloat("_Metallic", material.HasProperty("_MetallicGlossMap") ? 1f : metallic);
		}

		if (material.HasProperty("_Smoothness"))
		{
			material.SetFloat("_Smoothness", material.HasProperty("_MetallicGlossMap") ? 1f : rustSmoothness);
		}
		else if (material.HasProperty("_Glossiness"))
		{
			material.SetFloat("_Glossiness", material.HasProperty("_MetallicGlossMap") ? 1f : rustSmoothness);
		}
	}

	private void OnDestroy()
	{
		if (rustTexture != null)
		{
			Destroy(rustTexture);
		}

		if (metallicTexture != null)
		{
			Destroy(metallicTexture);
		}

		foreach (Material material in runtimeMaterials)
		{
			if (material != null)
			{
				Destroy(material);
			}
		}
	}
}
