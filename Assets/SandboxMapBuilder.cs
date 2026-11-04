using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

[DisallowMultipleComponent]
public class SandboxMapBuilder : MonoBehaviour
{
	private const string MenuSceneName = "Start screen";

	private Material floorMaterial;
	private Material concreteMaterial;
	private Material darkSteelMaterial;
	private Material woodCrateMaterial;
	private Material tinCanMaterial;
	private Material hazardRedMaterial;
	private Material accentOrangeMaterial;
	private Material bouncyBallMaterial;
	private Material bowlingPinMaterial;
	private Material goldMaterial;

	private PhysicsMaterial bouncyPhysicsMat;
	private PhysicsMaterial slipperyPhysicsMat;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void RegisterSceneListener()
	{
		SceneManager.sceneLoaded -= OnSceneLoaded;
		SceneManager.sceneLoaded += OnSceneLoaded;
	}

	private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		if (scene.name == MenuSceneName || FindAnyObjectByType<SandboxMapBuilder>() != null)
		{
			return;
		}

		new GameObject("Sandbox Map").AddComponent<SandboxMapBuilder>();
	}

	private void Awake()
	{
		Renderer templateRenderer = FindAnyObjectByType<MeshRenderer>();
		Material templateMat = templateRenderer != null ? templateRenderer.sharedMaterial : null;

		CreateMaterials(templateMat);
		UpgradeMainPlatform();
		BuildWorkbenchStation();
		BuildFiringRangeAndCrates();
		BuildRustWorkshopBay();
		BuildPhysicsPlayground();
		BuildParkourAndVoidZone();

		VoidRespawn.AttachToAllSceneRigidbodies(-10f);
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

		floorMaterial = CreateGridMaterial(templateMat, shader);
		concreteMaterial = CreateMat(templateMat, shader, "ConcreteMat", new Color(0.28f, 0.30f, 0.32f), 0.08f, 0.25f);
		darkSteelMaterial = CreateMat(templateMat, shader, "DarkSteelMat", new Color(0.16f, 0.18f, 0.21f), 0.70f, 0.55f);
		woodCrateMaterial = CreateMat(templateMat, shader, "WoodCrateMat", new Color(0.68f, 0.47f, 0.25f), 0.05f, 0.20f);
		tinCanMaterial = CreateMat(templateMat, shader, "TinCanMat", new Color(0.76f, 0.80f, 0.84f), 0.85f, 0.78f);
		hazardRedMaterial = CreateMat(templateMat, shader, "HazardRedMat", new Color(0.84f, 0.18f, 0.15f), 0.25f, 0.50f);
		accentOrangeMaterial = CreateMat(templateMat, shader, "AccentOrangeMat", new Color(0.92f, 0.52f, 0.18f), 0.15f, 0.45f);
		bouncyBallMaterial = CreateMat(templateMat, shader, "BouncyBallMat", new Color(0.20f, 0.68f, 0.88f), 0.10f, 0.75f);
		bowlingPinMaterial = CreateMat(templateMat, shader, "BowlingPinMat", new Color(0.94f, 0.93f, 0.89f), 0.10f, 0.65f);
		goldMaterial = CreateMat(templateMat, shader, "GoldMat", new Color(0.95f, 0.76f, 0.22f), 0.90f, 0.85f);

		bouncyPhysicsMat = new PhysicsMaterial("BouncyRubber")
		{
			bounciness = 0.84f,
			bounceCombine = PhysicsMaterialCombine.Maximum,
			dynamicFriction = 0.35f,
			staticFriction = 0.35f
		};

		slipperyPhysicsMat = new PhysicsMaterial("SmoothLane")
		{
			bounciness = 0.1f,
			dynamicFriction = 0.12f,
			staticFriction = 0.12f
		};
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

	private static Material CreateGridMaterial(Material templateMat, Shader shader)
	{
		Material mat = CreateMat(templateMat, shader, "ArenaFloorGridMat", Color.white, 0.08f, 0.32f);
		int size = 128;
		Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
		{
			name = "ArenaGridTex",
			wrapMode = TextureWrapMode.Repeat,
			filterMode = FilterMode.Trilinear
		};

		Color tileA = new Color(0.20f, 0.22f, 0.23f);
		Color tileB = new Color(0.23f, 0.25f, 0.26f);
		Color lineCol = new Color(0.14f, 0.16f, 0.17f);
		Color majorLineCol = new Color(0.32f, 0.36f, 0.38f);

		Color[] pixels = new Color[size * size];
		int half = size / 2;
		for (int y = 0; y < size; y++)
		{
			for (int x = 0; x < size; x++)
			{
				bool isMajorEdge = x < 2 || y < 2;
				bool isSubEdge = Mathf.Abs(x - half) < 1 || Mathf.Abs(y - half) < 1;
				bool quadrant = (x < half) ^ (y < half);
				Color c = quadrant ? tileA : tileB;
				if (isSubEdge) c = lineCol;
				if (isMajorEdge) c = majorLineCol;
				pixels[y * size + x] = c;
			}
		}

		tex.SetPixels(pixels);
		tex.Apply(true, false);

		mat.mainTexture = tex;
		mat.mainTextureScale = new Vector2(30f, 30f);
		if (mat.HasProperty("_BaseMap"))
		{
			mat.SetTexture("_BaseMap", tex);
			mat.SetTextureScale("_BaseMap", new Vector2(30f, 30f));
		}

		return mat;
	}

	private void UpgradeMainPlatform()
	{
		GameObject plane = GameObject.Find("Plane");
		if (plane != null)
		{
			plane.transform.position = Vector3.zero;
			plane.transform.localScale = new Vector3(6f, 1f, 6f);
			Renderer r = plane.GetComponent<Renderer>();
			if (r != null && floorMaterial != null)
			{
				r.sharedMaterial = floorMaterial;
			}
		}

		// Low safety curbs around parts of the arena (leaving south-east open for the void bridge)
		CreateStaticBox("NorthCurb", new Vector3(0f, 0.35f, 29.7f), new Vector3(60f, 0.7f, 0.6f), concreteMaterial);
		CreateStaticBox("WestCurb", new Vector3(-29.7f, 0.35f, 0f), new Vector3(0.6f, 0.7f, 60f), concreteMaterial);
		CreateStaticBox("EastCurb", new Vector3(29.7f, 0.35f, 5f), new Vector3(0.6f, 0.7f, 50f), concreteMaterial);
	}

	private void BuildWorkbenchStation()
	{
		Transform station = new GameObject("WorkbenchStation").transform;
		station.SetParent(transform, false);

		// Workbench table right near player spawn (1, y, -8.73)
		Vector3 benchCenter = new Vector3(-2.2f, 0f, -5.8f);
		CreateStaticBox("WorkbenchTop", benchCenter + new Vector3(0f, 0.95f, 0f), new Vector3(3.6f, 0.12f, 1.3f), woodCrateMaterial, station);
		CreateStaticBox("LegFL", benchCenter + new Vector3(-1.6f, 0.45f, -0.5f), new Vector3(0.14f, 0.9f, 0.14f), darkSteelMaterial, station);
		CreateStaticBox("LegFR", benchCenter + new Vector3(1.6f, 0.45f, -0.5f), new Vector3(0.14f, 0.9f, 0.14f), darkSteelMaterial, station);
		CreateStaticBox("LegBL", benchCenter + new Vector3(-1.6f, 0.45f, 0.5f), new Vector3(0.14f, 0.9f, 0.14f), darkSteelMaterial, station);
		CreateStaticBox("LegBR", benchCenter + new Vector3(1.6f, 0.45f, 0.5f), new Vector3(0.14f, 0.9f, 0.14f), darkSteelMaterial, station);

		// Place existing angle grinder onto the workbench so it's easy to grab
		GameObject grinder = GameObject.Find("angle grinder");
		if (grinder != null)
		{
			grinder.transform.position = benchCenter + new Vector3(-1.0f, 1.35f, 0f);
			grinder.transform.rotation = Quaternion.Euler(0f, 25f, 90f);
			Rigidbody grb = grinder.GetComponent<Rigidbody>();
			if (grb != null)
			{
				grb.linearVelocity = Vector3.zero;
				grb.angularVelocity = Vector3.zero;
			}
		}

		// Place existing Gun onto the right side of the workbench
		Gun existingGun = FindAnyObjectByType<Gun>();
		if (existingGun != null)
		{
			existingGun.transform.position = benchCenter + new Vector3(1.0f, 1.25f, 0f);
			existingGun.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
		}

		// Portable Rusty Plate on the workbench (can be picked up AND ground with the angle grinder!)
		GameObject rustyPlate = CreateDynamicBox(
			"Rusty Metal Plate",
			benchCenter + new Vector3(-0.05f, 1.25f, 0.05f),
			new Vector3(0.55f, 0.08f, 0.45f),
			1.5f,
			darkSteelMaterial,
			station);
		rustyPlate.AddComponent<wall>();

		// Golden Trophy Cube on the workbench
		CreateDynamicBox(
			"Gold Cube",
			benchCenter + new Vector3(0.55f, 1.22f, -0.15f),
			new Vector3(0.26f, 0.26f, 0.26f),
			2.5f,
			goldMaterial,
			station);
	}

	private void BuildFiringRangeAndCrates()
	{
		Transform range = new GameObject("FiringRange").transform;
		range.SetParent(transform, false);

		// Low firing line divider
		CreateStaticBox("RangeDivider", new Vector3(1f, 0.45f, -1.2f), new Vector3(12f, 0.9f, 0.35f), concreteMaterial, range);
		CreateStaticBox("RangeDividerTrim", new Vector3(1f, 0.92f, -1.2f), new Vector3(12.1f, 0.06f, 0.4f), accentOrangeMaterial, range);

		// Left Table: Pyramid of 10 Wooden Crates (at z = 5.5)
		Vector3 crateTablePos = new Vector3(-2.8f, 0f, 5.5f);
		CreateStaticBox("CrateTable", crateTablePos + new Vector3(0f, 0.6f, 0f), new Vector3(3.4f, 1.2f, 1.4f), concreteMaterial, range);

		float crateSize = 0.44f;
		float baseTopY = 1.2f;
		for (int row = 0; row < 4; row++)
		{
			int count = 4 - row;
			float startX = -(count - 1) * (crateSize * 0.54f);
			float y = baseTopY + crateSize * 0.5f + row * (crateSize + 0.01f);
			for (int i = 0; i < count; i++)
			{
				Vector3 pos = crateTablePos + new Vector3(startX + i * (crateSize * 1.08f), y, 0f);
				CreateDynamicBox($"Wood Crate", pos, Vector3.one * crateSize, 0.8f, woodCrateMaterial, range);
			}
		}

		// Right Table: Tin Can Pyramid (at z = 5.5)
		Vector3 canTablePos = new Vector3(4.5f, 0f, 5.5f);
		CreateStaticBox("CanTable", canTablePos + new Vector3(0f, 0.6f, 0f), new Vector3(2.8f, 1.2f, 1.2f), concreteMaterial, range);

		for (int row = 0; row < 3; row++)
		{
			int count = 3 - row;
			float startX = -(count - 1) * 0.22f;
			float y = 1.2f + 0.18f + row * 0.37f;
			for (int i = 0; i < count; i++)
			{
				Vector3 pos = canTablePos + new Vector3(startX + i * 0.44f, y, 0f);
				CreateDynamicCylinder("Tin Can", pos, new Vector3(0.28f, 0.18f, 0.28f), 0.35f, tinCanMaterial, range);
			}
		}

		// Reactive Bullseye Targets at 8m, 13m, and 18m
		CreateReactiveTarget(new Vector3(0.8f, 1.6f, 8.5f), range);
		CreateReactiveTarget(new Vector3(-3.8f, 2.1f, 13.5f), range);
		CreateReactiveTarget(new Vector3(5.2f, 2.1f, 14.5f), range);
		CreateReactiveTarget(new Vector3(0.8f, 2.6f, 19.5f), range);

		// Explosive Barrels!
		CreateExplosiveBarrel(new Vector3(-0.7f, 0.65f, 5.2f), range);
		CreateExplosiveBarrel(new Vector3(2.5f, 0.65f, 5.2f), range);
		CreateExplosiveBarrel(new Vector3(0.8f, 0.65f, 12.0f), range);

		// Backstop wall behind the firing range (also rusty so you can grind or shoot it!)
		GameObject backstop = CreateStaticBox("RangeBackstopWall", new Vector3(1f, 2.2f, 23.5f), new Vector3(16f, 4.4f, 0.5f), concreteMaterial, range);
		backstop.AddComponent<wall>();
	}

	private void BuildRustWorkshopBay()
	{
		Transform workshop = new GameObject("RustWorkshopBay").transform;
		workshop.SetParent(transform, false);

		// Angled rusty panel next to the existing wall at x = 4.45, z = -9.41
		GameObject angledWall = CreateStaticBox(
			"Angled Rusty Panel",
			new Vector3(7.5f, 1.5f, -6.5f),
			new Vector3(0.35f, 3.0f, 4.2f),
			concreteMaterial,
			workshop);
		angledWall.transform.rotation = Quaternion.Euler(0f, -30f, 0f);
		angledWall.AddComponent<wall>();

		// Rusty steel workbench beam
		GameObject rustyBeam = CreateStaticBox(
			"Rusty Steel Block",
			new Vector3(6.8f, 0.75f, -12.0f),
			new Vector3(2.4f, 1.5f, 1.2f),
			concreteMaterial,
			workshop);
		rustyBeam.AddComponent<wall>();
	}

	private void BuildPhysicsPlayground()
	{
		Transform playground = new GameObject("PhysicsPlayground").transform;
		playground.SetParent(transform, false);

		// 1. Physics Seesaw at (-11, 0, -4)
		Vector3 seesawPos = new Vector3(-11.5f, 0f, -3.5f);
		CreateStaticBox("SeesawFulcrum", seesawPos + new Vector3(0f, 0.45f, 0f), new Vector3(1.6f, 0.9f, 0.5f), accentOrangeMaterial, playground);

		GameObject plank = CreateDynamicBox(
			"SeesawPlank",
			seesawPos + new Vector3(0f, 0.98f, 0f),
			new Vector3(1.4f, 0.12f, 6.2f),
			6f,
			woodCrateMaterial,
			playground);
		HingeJoint hinge = plank.AddComponent<HingeJoint>();
		hinge.anchor = Vector3.zero;
		hinge.axis = Vector3.right;
		hinge.useLimits = true;
		hinge.limits = new JointLimits { min = -24f, max = 24f };

		// Heavy weight cube & launchable crate for the seesaw
		CreateDynamicBox("Heavy Anvil Block", seesawPos + new Vector3(-2.2f, 1.2f, 2.2f), new Vector3(0.65f, 0.65f, 0.65f), 12f, darkSteelMaterial, playground);
		CreateDynamicBox("Launch Crate", seesawPos + new Vector3(0f, 1.3f, -2.2f), new Vector3(0.42f, 0.42f, 0.42f), 0.7f, accentOrangeMaterial, playground);

		// 2. Basketball Hoop & Bouncy Balls at (-16, 0, 5)
		Vector3 hoopBase = new Vector3(-16.5f, 0f, 5.5f);
		CreateStaticBox("HoopPole", hoopBase + new Vector3(0f, 2.1f, 0f), new Vector3(0.22f, 4.2f, 0.22f), darkSteelMaterial, playground);
		CreateStaticBox("HoopBackboard", hoopBase + new Vector3(0.55f, 3.6f, 0f), new Vector3(0.12f, 1.5f, 2.2f), bowlingPinMaterial, playground);

		// Rim built from 4 thin bars so balls can physically drop through the center!
		Vector3 rimCenter = hoopBase + new Vector3(1.15f, 3.15f, 0f);
		float rimRadius = 0.46f;
		CreateStaticBox("RimLeft", rimCenter + new Vector3(0f, 0f, -rimRadius), new Vector3(0.92f, 0.06f, 0.06f), accentOrangeMaterial, playground);
		CreateStaticBox("RimRight", rimCenter + new Vector3(0f, 0f, rimRadius), new Vector3(0.92f, 0.06f, 0.06f), accentOrangeMaterial, playground);
		CreateStaticBox("RimFront", rimCenter + new Vector3(rimRadius, 0f, 0f), new Vector3(0.06f, 0.06f, 0.92f), accentOrangeMaterial, playground);
		CreateStaticBox("RimBack", rimCenter + new Vector3(-rimRadius, 0f, 0f), new Vector3(0.06f, 0.06f, 0.92f), accentOrangeMaterial, playground);

		GameObject scoreLightBar = CreateStaticBox("HoopScoreIndicator", hoopBase + new Vector3(0.62f, 4.25f, 0f), new Vector3(0.08f, 0.16f, 1.4f), accentOrangeMaterial, playground);
		GameObject triggerObj = new GameObject("HoopTrigger");
		triggerObj.transform.SetParent(playground, false);
		triggerObj.transform.position = rimCenter + new Vector3(0f, -0.15f, 0f);
		BoxCollider triggerCol = triggerObj.AddComponent<BoxCollider>();
		triggerCol.isTrigger = true;
		triggerCol.size = new Vector3(0.65f, 0.25f, 0.65f);
		triggerObj.AddComponent<BasketballHoopTrigger>().Initialize(scoreLightBar.GetComponent<Renderer>());

		// Rack of Bouncy Balls
		for (int i = 0; i < 4; i++)
		{
			GameObject ball = CreateDynamicSphere(
				"Bouncy Ball",
				hoopBase + new Vector3(4.5f, 0.5f + i * 0.1f, -1.2f + i * 0.75f),
				0.42f,
				0.6f,
				i % 2 == 0 ? bouncyBallMaterial : accentOrangeMaterial,
				playground);
			Collider ballCol = ball.GetComponent<Collider>();
			if (ballCol != null)
			{
				ballCol.sharedMaterial = bouncyPhysicsMat;
			}
		}

		// 3. Bowling Lane & Pins at (-15, 0, -14)
		Vector3 laneCenter = new Vector3(-14f, 0.03f, -14.5f);
		GameObject lane = CreateStaticBox("BowlingLane", laneCenter, new Vector3(14f, 0.06f, 2.6f), woodCrateMaterial, playground);
		Collider laneCol = lane.GetComponent<Collider>();
		if (laneCol != null) laneCol.sharedMaterial = slipperyPhysicsMat;

		CreateStaticBox("LaneGutterL", laneCenter + new Vector3(0f, 0.12f, -1.4f), new Vector3(14f, 0.24f, 0.2f), darkSteelMaterial, playground);
		CreateStaticBox("LaneGutterR", laneCenter + new Vector3(0f, 0.12f, 1.4f), new Vector3(14f, 0.24f, 0.2f), darkSteelMaterial, playground);

		// 6 Bowling Pins in a triangle at the west end of the lane
		Vector3 pinOrigin = laneCenter + new Vector3(-5.2f, 0.36f, 0f);
		int pinIndex = 0;
		for (int row = 0; row < 3; row++)
		{
			for (int col = 0; col <= row; col++)
			{
				float zOffset = (col - row * 0.5f) * 0.42f;
				float xOffset = -row * 0.38f;
				pinIndex++;
				CreateDynamicCylinder(
					$"Bowling Pin",
					pinOrigin + new Vector3(xOffset, 0f, zOffset),
					new Vector3(0.18f, 0.32f, 0.18f),
					0.45f,
					bowlingPinMaterial,
					playground);
			}
		}

		// 2 Heavy Bowling Balls at the east end of the lane
		for (int i = 0; i < 2; i++)
		{
			GameObject bowlingBall = CreateDynamicSphere(
				"Bowling Ball",
				laneCenter + new Vector3(5.5f, 0.35f, -0.4f + i * 0.8f),
				0.52f,
				5.5f,
				darkSteelMaterial,
				playground);
			Collider c = bowlingBall.GetComponent<Collider>();
			if (c != null) c.sharedMaterial = slipperyPhysicsMat;
		}
	}

	private void BuildParkourAndVoidZone()
	{
		Transform parkour = new GameObject("ParkourAndVoidZone").transform;
		parkour.SetParent(transform, false);

		// 1. Crouch Test Tunnel at (12, 0, -2)
		Vector3 tunnelPos = new Vector3(13.5f, 0f, -1.5f);
		CreateStaticBox("TunnelWallL", tunnelPos + new Vector3(-1.3f, 1.1f, 0f), new Vector3(0.3f, 2.2f, 5.0f), concreteMaterial, parkour);
		CreateStaticBox("TunnelWallR", tunnelPos + new Vector3(1.3f, 1.1f, 0f), new Vector3(0.3f, 2.2f, 5.0f), concreteMaterial, parkour);
		// Ceiling bottom is at y = 1.42m (standing height is 2.0m, crouch height is 1.2m!)
		CreateStaticBox("CrouchTunnelCeiling", tunnelPos + new Vector3(0f, 1.82f, 0f), new Vector3(2.9f, 0.8f, 5.0f), accentOrangeMaterial, parkour);

		// Reward Gold Cube inside the crouch tunnel!
		CreateDynamicBox("Tunnel Gold Bar", tunnelPos + new Vector3(0f, 0.3f, 0f), new Vector3(0.35f, 0.2f, 0.55f), 2.0f, goldMaterial, parkour);

		// 2. Parkour Stepping Platforms leading to a High Overlook Deck
		Vector3[] stepPositions =
		{
			new Vector3(14f, 0.65f, 4.5f),
			new Vector3(16.5f, 1.35f, 7.0f),
			new Vector3(14.5f, 2.10f, 9.8f),
			new Vector3(17.0f, 2.85f, 12.5f),
			new Vector3(14.5f, 3.60f, 15.2f)
		};

		for (int i = 0; i < stepPositions.Length; i++)
		{
			CreateStaticBox($"JumpStep_{i + 1}", stepPositions[i], new Vector3(2.2f, 0.3f, 2.2f), i % 2 == 0 ? concreteMaterial : accentOrangeMaterial, parkour);
		}

		// High Overlook Deck at y = 4.2m
		Vector3 deckPos = new Vector3(11.0f, 4.2f, 18.0f);
		CreateStaticBox("OverlookDeck", deckPos, new Vector3(5.0f, 0.35f, 4.5f), concreteMaterial, parkour);
		CreateStaticBox("OverlookPillar1", new Vector3(9.0f, 2.1f, 16.2f), new Vector3(0.45f, 4.2f, 0.45f), darkSteelMaterial, parkour);
		CreateStaticBox("OverlookPillar2", new Vector3(13.0f, 2.1f, 19.8f), new Vector3(0.45f, 4.2f, 0.45f), darkSteelMaterial, parkour);

		CreateExplosiveBarrel(deckPos + new Vector3(-1.2f, 0.85f, -1.2f), parkour);
		CreateDynamicBox("Tower Crate 1", deckPos + new Vector3(0.8f, 0.65f, -1.2f), Vector3.one * 0.45f, 1.0f, woodCrateMaterial, parkour);
		CreateDynamicBox("Tower Crate 2", deckPos + new Vector3(0.8f, 1.15f, -1.2f), Vector3.one * 0.45f, 1.0f, woodCrateMaterial, parkour);

		// 3. Void Test Bridge extending off the South edge (z = -30 to -40)
		CreateStaticBox("VoidBridge", new Vector3(1f, -0.05f, -34.5f), new Vector3(2.4f, 0.25f, 10f), accentOrangeMaterial, parkour);
		CreateDynamicBox("Edge Test Cube", new Vector3(1f, 0.5f, -38.2f), Vector3.one * 0.55f, 1.0f, bouncyBallMaterial, parkour);
	}

	private void CreateReactiveTarget(Vector3 position, Transform parent)
	{
		GameObject stand = CreateStaticBox("TargetStand", new Vector3(position.x, position.y * 0.5f, position.z), new Vector3(0.18f, position.y, 0.18f), darkSteelMaterial, parent);
		GameObject plate = CreateStaticBox("TargetPlate", position, new Vector3(1.1f, 1.1f, 0.14f), hazardRedMaterial, stand.transform);
		CreateStaticBox("TargetInnerRing", position + new Vector3(0f, 0f, -0.08f), new Vector3(0.65f, 0.65f, 0.04f), bowlingPinMaterial, plate.transform);
		CreateStaticBox("TargetBullseye", position + new Vector3(0f, 0f, -0.10f), new Vector3(0.28f, 0.28f, 0.04f), hazardRedMaterial, plate.transform);

		plate.AddComponent<ReactiveTarget>().Initialize(plate.GetComponent<Renderer>(), hazardRedMaterial.color);
	}

	private void CreateExplosiveBarrel(Vector3 position, Transform parent)
	{
		GameObject barrel = CreateDynamicCylinder("Explosive Barrel", position, new Vector3(0.58f, 0.48f, 0.58f), 2.2f, hazardRedMaterial, parent);

		Transform topRim = GameObject.CreatePrimitive(PrimitiveType.Cylinder).transform;
		topRim.name = "BarrelBand";
		topRim.SetParent(barrel.transform, false);
		topRim.localPosition = new Vector3(0f, 0.55f, 0f);
		topRim.localScale = new Vector3(1.04f, 0.08f, 1.04f);
		Collider c1 = topRim.GetComponent<Collider>();
		if (c1 != null) Destroy(c1);
		Renderer r1 = topRim.GetComponent<Renderer>();
		if (r1 != null) r1.sharedMaterial = darkSteelMaterial;

		Transform bottomRim = GameObject.CreatePrimitive(PrimitiveType.Cylinder).transform;
		bottomRim.name = "BarrelBandBottom";
		bottomRim.SetParent(barrel.transform, false);
		bottomRim.localPosition = new Vector3(0f, -0.55f, 0f);
		bottomRim.localScale = new Vector3(1.04f, 0.08f, 1.04f);
		Collider c2 = bottomRim.GetComponent<Collider>();
		if (c2 != null) Destroy(c2);
		Renderer r2 = bottomRim.GetComponent<Renderer>();
		if (r2 != null) r2.sharedMaterial = darkSteelMaterial;

		barrel.AddComponent<ExplosiveBarrel>();
	}

	private static GameObject CreateStaticBox(string name, Vector3 position, Vector3 scale, Material mat, Transform parent = null)
	{
		GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
		obj.name = name;
		if (parent != null) obj.transform.SetParent(parent, false);
		obj.transform.position = position;
		obj.transform.localScale = scale;
		Renderer r = obj.GetComponent<Renderer>();
		if (r != null && mat != null) r.sharedMaterial = mat;
		return obj;
	}

	private static GameObject CreateDynamicBox(string name, Vector3 position, Vector3 scale, float mass, Material mat, Transform parent = null)
	{
		GameObject obj = CreateStaticBox(name, position, scale, mat, parent);
		Rigidbody rb = obj.AddComponent<Rigidbody>();
		rb.mass = mass;
		rb.interpolation = RigidbodyInterpolation.Interpolate;
		return obj;
	}

	private static GameObject CreateDynamicCylinder(string name, Vector3 position, Vector3 scale, float mass, Material mat, Transform parent = null)
	{
		GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
		obj.name = name;
		if (parent != null) obj.transform.SetParent(parent, false);
		obj.transform.position = position;
		obj.transform.localScale = scale;
		Renderer r = obj.GetComponent<Renderer>();
		if (r != null && mat != null) r.sharedMaterial = mat;
		Rigidbody rb = obj.AddComponent<Rigidbody>();
		rb.mass = mass;
		rb.interpolation = RigidbodyInterpolation.Interpolate;
		return obj;
	}

	private static GameObject CreateDynamicSphere(string name, Vector3 position, float diameter, float mass, Material mat, Transform parent = null)
	{
		GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
		obj.name = name;
		if (parent != null) obj.transform.SetParent(parent, false);
		obj.transform.position = position;
		obj.transform.localScale = Vector3.one * diameter;
		Renderer r = obj.GetComponent<Renderer>();
		if (r != null && mat != null) r.sharedMaterial = mat;
		Rigidbody rb = obj.AddComponent<Rigidbody>();
		rb.mass = mass;
		rb.interpolation = RigidbodyInterpolation.Interpolate;
		return obj;
	}
}

public class ReactiveTarget : MonoBehaviour
{
	private Renderer plateRenderer;
	private Color baseColor;
	private Quaternion initialRotation;
	private float hitAnimTime = -10f;

	public void Initialize(Renderer renderer, Color defaultColor)
	{
		plateRenderer = renderer;
		baseColor = defaultColor;
		initialRotation = transform.localRotation;
	}

	public void OnHit()
	{
		hitAnimTime = Time.time;
	}

	private void OnCollisionEnter(Collision collision)
	{
		if (collision.relativeVelocity.magnitude > 2.5f)
		{
			OnHit();
		}
	}

	private void Update()
	{
		float elapsed = Time.time - hitAnimTime;
		if (elapsed >= 0f && elapsed < 0.85f)
		{
			float t = elapsed / 0.85f;
			float tilt = Mathf.Sin(t * Mathf.PI) * 78f;
			transform.localRotation = initialRotation * Quaternion.Euler(tilt, 0f, 0f);
			if (plateRenderer != null)
			{
				Color hitColor = Color.Lerp(new Color(0.2f, 0.95f, 0.35f), baseColor, t);
				plateRenderer.material.color = hitColor;
				if (plateRenderer.material.HasProperty("_BaseColor"))
				{
					plateRenderer.material.SetColor("_BaseColor", hitColor);
				}
			}
		}
		else
		{
			transform.localRotation = initialRotation;
		}
	}
}

public class ExplosiveBarrel : MonoBehaviour
{
	private const float ExplosionRadius = 7.5f;
	private const float ExplosionForce = 18f;

	private Vector3 spawnPos;
	private Quaternion spawnRot;
	private Rigidbody body;
	private Renderer[] renderers;
	private Collider[] colliders;
	private bool exploded;

	private void Awake()
	{
		spawnPos = transform.position;
		spawnRot = transform.rotation;
		body = GetComponent<Rigidbody>();
		renderers = GetComponentsInChildren<Renderer>();
		colliders = GetComponentsInChildren<Collider>();
	}

	private void OnCollisionEnter(Collision collision)
	{
		if (!exploded && collision.relativeVelocity.magnitude > 10.5f)
		{
			TriggerExplosion();
		}
	}

	public void TriggerExplosion()
	{
		if (exploded)
		{
			return;
		}

		exploded = true;
		Vector3 center = transform.position;

		SpawnExplosionVisual(center);

		Collider[] hitColliders = Physics.OverlapSphere(center, ExplosionRadius);
		for (int i = 0; i < hitColliders.Length; i++)
		{
			Collider col = hitColliders[i];
			if (col == null) continue;

			ReactiveTarget target = col.GetComponentInParent<ReactiveTarget>();
			if (target != null)
			{
				target.OnHit();
			}

			Rigidbody rb = col.attachedRigidbody;
			if (rb != null && rb != body && !rb.isKinematic)
			{
				rb.AddExplosionForce(ExplosionForce, center, ExplosionRadius, 1.4f, ForceMode.Impulse);
			}

			ExplosiveBarrel otherBarrel = col.GetComponentInParent<ExplosiveBarrel>();
			if (otherBarrel != null && otherBarrel != this && !otherBarrel.exploded)
			{
				otherBarrel.Invoke(nameof(TriggerExplosion), 0.12f);
			}
		}

		StartCoroutine(RespawnAfterDelay(4.0f));
	}

	private static void SpawnExplosionVisual(Vector3 position)
	{
		GameObject blast = GameObject.CreatePrimitive(PrimitiveType.Sphere);
		blast.name = "ExplosionBlast";
		blast.transform.position = position;
		Collider c = blast.GetComponent<Collider>();
		if (c != null) Destroy(c);

		Light light = blast.AddComponent<Light>();
		light.type = LightType.Point;
		light.color = new Color(1f, 0.6f, 0.15f);
		light.range = 12f;
		light.intensity = 6f;

		blast.AddComponent<ExplosionVisualEffect>();
	}

	private IEnumerator RespawnAfterDelay(float delay)
	{
		if (body != null)
		{
			body.linearVelocity = Vector3.zero;
			body.angularVelocity = Vector3.zero;
			body.isKinematic = true;
		}

		for (int i = 0; i < renderers.Length; i++)
		{
			if (renderers[i] != null) renderers[i].enabled = false;
		}

		for (int i = 0; i < colliders.Length; i++)
		{
			if (colliders[i] != null) colliders[i].enabled = false;
		}

		yield return new WaitForSeconds(delay);

		transform.SetPositionAndRotation(spawnPos, spawnRot);
		if (body != null)
		{
			body.position = spawnPos;
			body.rotation = spawnRot;
			body.isKinematic = false;
			body.linearVelocity = Vector3.zero;
			body.angularVelocity = Vector3.zero;
		}

		for (int i = 0; i < renderers.Length; i++)
		{
			if (renderers[i] != null) renderers[i].enabled = true;
		}

		for (int i = 0; i < colliders.Length; i++)
		{
			if (colliders[i] != null) colliders[i].enabled = true;
		}

		exploded = false;
	}
}

public class ExplosionVisualEffect : MonoBehaviour
{
	private float startTime;
	private Renderer meshRenderer;
	private Light blastLight;

	private void Awake()
	{
		startTime = Time.time;
		meshRenderer = GetComponent<Renderer>();
		blastLight = GetComponent<Light>();

		if (meshRenderer != null)
		{
			Shader s = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
			Material mat = new Material(s);
			Color col = new Color(1f, 0.55f, 0.1f);
			mat.color = col;
			if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
			meshRenderer.material = mat;
		}
	}

	private void Update()
	{
		float t = (Time.time - startTime) / 0.28f;
		if (t >= 1f)
		{
			Destroy(gameObject);
			return;
		}

		float scale = Mathf.Lerp(0.4f, 5.2f, Mathf.Sin(t * Mathf.PI * 0.5f));
		transform.localScale = Vector3.one * scale;
		if (blastLight != null)
		{
			blastLight.intensity = Mathf.Lerp(6f, 0f, t);
		}
	}
}

public class BasketballHoopTrigger : MonoBehaviour
{
	private Renderer indicatorRenderer;
	private Color baseColor = new Color(0.92f, 0.52f, 0.18f);
	private float scoreTime = -10f;

	public void Initialize(Renderer indicator)
	{
		indicatorRenderer = indicator;
	}

	private void OnTriggerEnter(Collider other)
	{
		if (other.attachedRigidbody != null && other.GetComponentInParent<control>() == null)
		{
			scoreTime = Time.time;
		}
	}

	private void Update()
	{
		if (indicatorRenderer == null)
		{
			return;
		}

		float elapsed = Time.time - scoreTime;
		if (elapsed >= 0f && elapsed < 1.2f)
		{
			Color c = Color.Lerp(new Color(0.15f, 0.95f, 0.35f), baseColor, elapsed / 1.2f);
			indicatorRenderer.material.color = c;
			if (indicatorRenderer.material.HasProperty("_BaseColor"))
			{
				indicatorRenderer.material.SetColor("_BaseColor", c);
			}
		}
	}
}

