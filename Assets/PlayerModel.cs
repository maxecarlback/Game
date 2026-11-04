using UnityEngine;

[DisallowMultipleComponent]
public class PlayerModel : MonoBehaviour
{
	private struct LimbChain
	{
		public Transform upperSegment;
		public Transform middleJoint;
		public Transform lowerSegment;
		public Transform endTransform;
		public Transform thumbPivot;
		public Transform[] fingerPivots;
	}

	[Header("Colors")]
	[SerializeField] private Color shirtColor = new Color(0.20f, 0.36f, 0.54f);
	[SerializeField] private Color skinColor = new Color(0.90f, 0.73f, 0.58f);
	[SerializeField] private Color pantsColor = new Color(0.18f, 0.21f, 0.27f);
	[SerializeField] private Color shoeColor = new Color(0.13f, 0.12f, 0.11f);

	[Header("Body Proportions")]
	[SerializeField] private float upperArmLength = 0.30f;
	[SerializeField] private float forearmLength = 0.28f;
	[SerializeField] private float armThickness = 0.085f;
	[SerializeField] private float upperLegLength = 0.42f;
	[SerializeField] private float lowerLegLength = 0.42f;
	[SerializeField] private float legThickness = 0.13f;

	private control playerControl;
	private grab grabController;
	private CharacterController characterController;
	private Camera playerCamera;

	private Transform bodyRoot;
	private Transform torsoTransform;
	private Transform pelvisTransform;

	private LimbChain leftLeg;
	private LimbChain rightLeg;
	private LimbChain leftArm;
	private LimbChain rightArm;

	private Material shirtMaterial;
	private Material skinMaterial;
	private Material pantsMaterial;
	private Material shoeMaterial;

	private float walkCycle;
	private float smoothedSpeed;
	private float holdBlend;
	private float fingerGripBlend;
	private Vector3 smoothedLeftWristLocal;
	private Vector3 smoothedRightWristLocal;
	private bool wristInitialized;

	private void Awake()
	{
		playerControl = GetComponent<control>();
		grabController = GetComponent<grab>();
		characterController = GetComponent<CharacterController>();
		playerCamera = GetComponentInChildren<Camera>();

		if (playerCamera != null && playerCamera.nearClipPlane > 0.05f)
		{
			playerCamera.nearClipPlane = 0.05f;
		}

		Renderer rootRenderer = GetComponent<Renderer>();
		Material baseMaterial = rootRenderer != null ? rootRenderer.sharedMaterial : null;
		if (rootRenderer != null)
		{
			rootRenderer.enabled = false;
		}

		CreateMaterials(baseMaterial);
		BuildBodyHierarchy();
	}

	private void OnDestroy()
	{
		if (shirtMaterial != null) Destroy(shirtMaterial);
		if (skinMaterial != null) Destroy(skinMaterial);
		if (pantsMaterial != null) Destroy(pantsMaterial);
		if (shoeMaterial != null) Destroy(shoeMaterial);
	}

	private void CreateMaterials(Material templateMaterial)
	{
		Shader shader = templateMaterial != null && templateMaterial.shader != null
			? templateMaterial.shader
			: Shader.Find("Universal Render Pipeline/Lit");

		if (shader == null)
		{
			shader = Shader.Find("Standard");
		}

		shirtMaterial = CreateColoredMaterial(templateMaterial, shader, "PlayerShirtMat", shirtColor, 0.15f);
		skinMaterial = CreateColoredMaterial(templateMaterial, shader, "PlayerSkinMat", skinColor, 0.25f);
		pantsMaterial = CreateColoredMaterial(templateMaterial, shader, "PlayerPantsMat", pantsColor, 0.12f);
		shoeMaterial = CreateColoredMaterial(templateMaterial, shader, "PlayerShoeMat", shoeColor, 0.30f);
	}

	private static Material CreateColoredMaterial(Material templateMaterial, Shader shader, string name, Color color, float smoothness)
	{
		Material mat = templateMaterial != null ? new Material(templateMaterial) : new Material(shader);
		mat.name = name;
		mat.color = color;

		if (mat.HasProperty("_BaseColor"))
		{
			mat.SetColor("_BaseColor", color);
		}

		if (mat.HasProperty("_Smoothness"))
		{
			mat.SetFloat("_Smoothness", smoothness);
		}
		else if (mat.HasProperty("_Glossiness"))
		{
			mat.SetFloat("_Glossiness", smoothness);
		}

		return mat;
	}

	private void BuildBodyHierarchy()
	{
		Transform existing = transform.Find("PlayerBody");
		if (existing != null)
		{
			Destroy(existing.gameObject);
		}

		bodyRoot = new GameObject("PlayerBody").transform;
		bodyRoot.SetParent(transform, false);

		torsoTransform = CreatePart("Torso", PrimitiveType.Capsule, bodyRoot, shirtMaterial);
		pelvisTransform = CreatePart("Pelvis", PrimitiveType.Capsule, bodyRoot, pantsMaterial);

		leftLeg = CreateLeg("Left", bodyRoot);
		rightLeg = CreateLeg("Right", bodyRoot);

		leftArm = CreateArm("Left", bodyRoot, -1f);
		rightArm = CreateArm("Right", bodyRoot, 1f);
	}

	private LimbChain CreateLeg(string sideName, Transform parent)
	{
		LimbChain leg = new LimbChain
		{
			upperSegment = CreatePart($"{sideName}UpperLeg", PrimitiveType.Capsule, parent, pantsMaterial),
			middleJoint = CreatePart($"{sideName}Knee", PrimitiveType.Sphere, parent, pantsMaterial),
			lowerSegment = CreatePart($"{sideName}LowerLeg", PrimitiveType.Capsule, parent, pantsMaterial),
			endTransform = new GameObject($"{sideName}FootRoot").transform
		};

		leg.endTransform.SetParent(parent, false);
		leg.middleJoint.localScale = Vector3.one * (legThickness * 0.96f);

		Transform boot = CreatePart($"{sideName}Foot", PrimitiveType.Cube, leg.endTransform, shoeMaterial);
		boot.localPosition = new Vector3(0f, 0.045f, 0.06f);
		boot.localScale = new Vector3(0.12f, 0.09f, 0.24f);

		Transform bootToe = CreatePart($"{sideName}FootToe", PrimitiveType.Capsule, leg.endTransform, shoeMaterial);
		bootToe.localPosition = new Vector3(0f, 0.04f, 0.16f);
		bootToe.localRotation = Quaternion.Euler(0f, 0f, 90f);
		bootToe.localScale = new Vector3(0.08f, 0.06f, 0.09f);

		return leg;
	}

	private LimbChain CreateArm(string sideName, Transform parent, float sideSign)
	{
		LimbChain arm = new LimbChain
		{
			upperSegment = CreatePart($"{sideName}UpperArm", PrimitiveType.Capsule, parent, shirtMaterial),
			middleJoint = CreatePart($"{sideName}Elbow", PrimitiveType.Sphere, parent, shirtMaterial),
			lowerSegment = CreatePart($"{sideName}Forearm", PrimitiveType.Capsule, parent, skinMaterial),
			endTransform = new GameObject($"{sideName}HandRoot").transform,
			fingerPivots = new Transform[3]
		};

		arm.endTransform.SetParent(parent, false);
		arm.middleJoint.localScale = Vector3.one * (armThickness * 0.96f);

		Transform palm = CreatePart($"{sideName}Palm", PrimitiveType.Cube, arm.endTransform, skinMaterial);
		palm.localPosition = new Vector3(0f, 0f, 0.045f);
		palm.localScale = new Vector3(0.038f, 0.082f, 0.09f);

		Transform thumbPivot = new GameObject($"{sideName}ThumbPivot").transform;
		thumbPivot.SetParent(arm.endTransform, false);
		thumbPivot.localPosition = new Vector3(-sideSign * 0.015f, 0.038f, 0.035f);
		arm.thumbPivot = thumbPivot;

		Transform thumbMesh = CreatePart($"{sideName}Thumb", PrimitiveType.Capsule, thumbPivot, skinMaterial);
		thumbMesh.localPosition = new Vector3(0f, 0.018f, 0.022f);
		thumbMesh.localRotation = Quaternion.Euler(50f, 0f, 0f);
		thumbMesh.localScale = new Vector3(0.024f, 0.028f, 0.024f);

		float[] fingerOffsetsY = { 0.026f, 0f, -0.026f };
		float[] fingerLengths = { 0.032f, 0.036f, 0.030f };

		for (int i = 0; i < 3; i++)
		{
			Transform fingerPivot = new GameObject($"{sideName}FingerPivot_{i}").transform;
			fingerPivot.SetParent(arm.endTransform, false);
			fingerPivot.localPosition = new Vector3(0f, fingerOffsetsY[i], 0.086f);
			arm.fingerPivots[i] = fingerPivot;

			Transform fingerMesh = CreatePart($"{sideName}Finger_{i}", PrimitiveType.Capsule, fingerPivot, skinMaterial);
			fingerMesh.localPosition = new Vector3(0f, 0f, fingerLengths[i]);
			fingerMesh.localRotation = Quaternion.Euler(90f, 0f, 0f);
			fingerMesh.localScale = new Vector3(0.021f, fingerLengths[i], 0.021f);
		}

		return arm;
	}

	private static Transform CreatePart(string name, PrimitiveType primitiveType, Transform parent, Material material)
	{
		GameObject obj = GameObject.CreatePrimitive(primitiveType);
		obj.name = name;
		obj.transform.SetParent(parent, false);

		Collider col = obj.GetComponent<Collider>();
		if (col != null)
		{
			Destroy(col);
		}

		Renderer renderer = obj.GetComponent<Renderer>();
		if (renderer != null && material != null)
		{
			renderer.sharedMaterial = material;
			renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
		}

		return obj.transform;
	}

	private void LateUpdate()
	{
		if (bodyRoot == null)
		{
			return;
		}

		if (playerCamera == null)
		{
			playerCamera = grabController != null && grabController.PlayerCamera != null
				? grabController.PlayerCamera
				: GetComponentInChildren<Camera>();
		}

		UpdateMovementCycle();
		UpdateTorsoAndLegs();
		UpdateArmsAndHands();
	}

	private void UpdateMovementCycle()
	{
		Vector3 horizontalVelocity = playerControl != null ? playerControl.HorizontalVelocity : Vector3.zero;
		float targetSpeed = horizontalVelocity.magnitude;
		smoothedSpeed = Mathf.Lerp(smoothedSpeed, targetSpeed, Time.deltaTime * 10f);

		bool grounded = playerControl == null || playerControl.IsGrounded;
		if (grounded && smoothedSpeed > 0.1f)
		{
			float cadence = Mathf.Lerp(5.5f, 11f, Mathf.InverseLerp(1.5f, 8f, smoothedSpeed));
			walkCycle += Time.deltaTime * cadence;
		}
		else
		{
			walkCycle = Mathf.MoveTowardsAngle(walkCycle * Mathf.Rad2Deg, 0f, Time.deltaTime * 220f) * Mathf.Deg2Rad;
		}
	}

	private void UpdateTorsoAndLegs()
	{
		float currentHeight = characterController != null ? characterController.height : 2f;
		float standingHeight = playerControl != null && playerControl.StandingHeight > 0.1f ? playerControl.StandingHeight : 2f;
		Vector3 center = characterController != null ? characterController.center : Vector3.zero;

		float bottomY = center.y - currentHeight * 0.5f;
		float crouchRatio = playerControl != null
			? playerControl.CrouchRatio
			: Mathf.InverseLerp(standingHeight, 1.15f, currentHeight);

		Transform camTransform = playerCamera != null ? playerCamera.transform : transform;
		Vector3 camLocalPos = transform.InverseTransformPoint(camTransform.position);

		float shoulderY = Mathf.Min(camLocalPos.y - 0.18f, bottomY + currentHeight * 0.72f);
		float hipY = Mathf.Lerp(bottomY + 0.84f, bottomY + 0.42f, crouchRatio);
		float bobOffset = Mathf.Sin(walkCycle * 2f) * 0.018f * Mathf.Clamp01(smoothedSpeed / 5f);
		hipY += bobOffset;

		Vector3 hipCenterLocal = new Vector3(0f, hipY, -0.10f - crouchRatio * 0.12f);
		Vector3 shoulderCenterLocal = new Vector3(0f, shoulderY + bobOffset * 0.5f, -0.08f);

		Vector3 hipWorld = transform.TransformPoint(hipCenterLocal);
		Vector3 shoulderWorld = transform.TransformPoint(shoulderCenterLocal);

		PositionCapsuleBetween(torsoTransform, hipWorld, shoulderWorld, 0.28f, transform.forward);
		torsoTransform.localScale = new Vector3(0.32f, torsoTransform.localScale.y, 0.20f);

		pelvisTransform.position = hipWorld;
		pelvisTransform.rotation = transform.rotation * Quaternion.Euler(0f, 0f, 90f);
		pelvisTransform.localScale = new Vector3(0.18f, 0.19f, 0.18f);

		UpdateSingleLeg(leftLeg, -0.145f, 0f, hipCenterLocal, bottomY, crouchRatio);
		UpdateSingleLeg(rightLeg, 0.145f, Mathf.PI, hipCenterLocal, bottomY, crouchRatio);
	}

	private void UpdateSingleLeg(LimbChain leg, float hipOffsetX, float phaseOffset, Vector3 hipCenterLocal, float bottomY, float crouchRatio)
	{
		Vector3 hipLocal = hipCenterLocal + new Vector3(hipOffsetX, -0.03f, 0.02f);
		Vector3 hipWorld = transform.TransformPoint(hipLocal);

		float moveFactor = Mathf.Clamp01(smoothedSpeed / 5f);
		bool grounded = playerControl == null || playerControl.IsGrounded;

		float phase = walkCycle + phaseOffset;
		float strideZ = 0f;
		float liftY = 0f;
		float footPitch = 0f;

		if (grounded)
		{
			float strideLength = Mathf.Lerp(0.24f, 0.38f, Mathf.InverseLerp(2f, 8f, smoothedSpeed)) * moveFactor;
			strideZ = Mathf.Sin(phase) * strideLength;
			liftY = Mathf.Max(0f, Mathf.Cos(phase)) * 0.11f * moveFactor;
			footPitch = -Mathf.Sin(phase) * 22f * moveFactor;
		}
		else
		{
			strideZ = hipOffsetX < 0f ? 0.10f : -0.08f;
			liftY = hipOffsetX < 0f ? 0.14f : 0.08f;
			footPitch = 16f;
		}

		Vector3 ankleLocal = new Vector3(hipOffsetX, bottomY + 0.08f + liftY, -0.04f + strideZ);
		Vector3 ankleWorld = transform.TransformPoint(ankleLocal);

		Vector3 kneeHint = (transform.forward + Vector3.up * (0.15f + crouchRatio * 0.25f) + transform.right * (hipOffsetX * 0.15f)).normalized;
		Vector3 kneeWorld = SolveTwoBoneIK(hipWorld, ankleWorld, upperLegLength, lowerLegLength, kneeHint, out Vector3 clampedAnkleWorld);

		PositionCapsuleBetween(leg.upperSegment, hipWorld, kneeWorld, legThickness, transform.forward);
		leg.middleJoint.position = kneeWorld;
		leg.middleJoint.rotation = transform.rotation;
		PositionCapsuleBetween(leg.lowerSegment, kneeWorld, clampedAnkleWorld, legThickness * 0.88f, transform.forward);

		leg.endTransform.position = new Vector3(clampedAnkleWorld.x, transform.TransformPoint(new Vector3(0f, bottomY + liftY, 0f)).y, clampedAnkleWorld.z);
		leg.endTransform.rotation = transform.rotation * Quaternion.Euler(footPitch, 0f, 0f);
	}

	private void UpdateArmsAndHands()
	{
		Transform cam = playerCamera != null ? playerCamera.transform : transform;
		Rigidbody heldBody = grabController != null ? grabController.HeldBody : null;
		bool isHolding = heldBody != null;

		float timeSinceGrabClick = grabController != null ? Time.time - grabController.LastGrabAttemptTime : 10f;
		float reachPulse = (!isHolding && timeSinceGrabClick >= 0f && timeSinceGrabClick < 0.30f)
			? Mathf.Sin((timeSinceGrabClick / 0.30f) * Mathf.PI)
			: 0f;

		float targetHoldBlend = isHolding ? 1f : reachPulse * 0.85f;
		holdBlend = Mathf.MoveTowards(holdBlend, targetHoldBlend, Time.deltaTime * 8f);

		float targetGrip = isHolding ? 1f : reachPulse;
		fingerGripBlend = Mathf.MoveTowards(fingerGripBlend, targetGrip, Time.deltaTime * 12f);

		Vector3 leftShoulderWorld = cam.TransformPoint(new Vector3(-0.24f, -0.23f, -0.04f));
		Vector3 rightShoulderWorld = cam.TransformPoint(new Vector3(0.24f, -0.23f, -0.04f));

		float moveFactor = Mathf.Clamp01(smoothedSpeed / 5f);
		float swingL = -Mathf.Sin(walkCycle) * 0.055f * moveFactor;
		float swingR = -Mathf.Sin(walkCycle + Mathf.PI) * 0.055f * moveFactor;
		float idleBob = Mathf.Sin(Time.time * 2.2f) * 0.008f;

		Vector3 restLeftLocal = new Vector3(-0.26f, -0.24f + idleBob + Mathf.Abs(swingL) * 0.25f, 0.40f + swingL);
		Vector3 restRightLocal = new Vector3(0.26f, -0.24f + idleBob + Mathf.Abs(swingR) * 0.25f, 0.40f + swingR);

		Vector3 grabLeftLocal = new Vector3(-0.13f, -0.10f, 0.64f);
		Vector3 grabRightLocal = new Vector3(0.13f, -0.10f, 0.64f);

		if (isHolding)
		{
			ComputeHeldObjectGripLocal(cam, heldBody, out grabLeftLocal, out grabRightLocal);
		}

		Vector3 desiredLeftLocal = Vector3.Lerp(restLeftLocal, grabLeftLocal, holdBlend);
		Vector3 desiredRightLocal = Vector3.Lerp(restRightLocal, grabRightLocal, holdBlend);

		if (!wristInitialized)
		{
			smoothedLeftWristLocal = desiredLeftLocal;
			smoothedRightWristLocal = desiredRightLocal;
			wristInitialized = true;
		}
		else
		{
			float followSpeed = isHolding ? 22f : 14f;
			smoothedLeftWristLocal = Vector3.Lerp(smoothedLeftWristLocal, desiredLeftLocal, Time.deltaTime * followSpeed);
			smoothedRightWristLocal = Vector3.Lerp(smoothedRightWristLocal, desiredRightLocal, Time.deltaTime * followSpeed);
		}

		Vector3 leftWristWorld = cam.TransformPoint(smoothedLeftWristLocal);
		Vector3 rightWristWorld = cam.TransformPoint(smoothedRightWristLocal);

		UpdateSingleArm(leftArm, leftShoulderWorld, leftWristWorld, cam, -1f);
		UpdateSingleArm(rightArm, rightShoulderWorld, rightWristWorld, cam, 1f);
	}

	private void ComputeHeldObjectGripLocal(Transform cam, Rigidbody heldBody, out Vector3 leftLocal, out Vector3 rightLocal)
	{
		if (grabController != null && grabController.IsHoldingGun)
		{
			Vector3 leftGripWorld = heldBody.transform.TransformPoint(new Vector3(-0.042f, -0.04f, -0.08f));
			Vector3 rightGripWorld = heldBody.transform.TransformPoint(new Vector3(0.032f, -0.055f, -0.11f));
			leftLocal = cam.InverseTransformPoint(leftGripWorld);
			rightLocal = cam.InverseTransformPoint(rightGripWorld);
			return;
		}

		Collider[] colliders = heldBody.GetComponentsInChildren<Collider>();
		Bounds worldBounds = new Bounds(heldBody.worldCenterOfMass, Vector3.one * 0.25f);
		bool hasBounds = false;

		for (int i = 0; i < colliders.Length; i++)
		{
			Collider col = colliders[i];
			if (col == null || !col.enabled)
			{
				continue;
			}

			if (!hasBounds)
			{
				worldBounds = col.bounds;
				hasBounds = true;
			}
			else
			{
				worldBounds.Encapsulate(col.bounds);
			}
		}

		Vector3 centerLocal = cam.InverseTransformPoint(hasBounds ? worldBounds.center : heldBody.position);
		Vector3 extents = worldBounds.extents;

		float rightExtent = Mathf.Abs(Vector3.Dot(cam.right, Vector3.right)) * extents.x
			+ Mathf.Abs(Vector3.Dot(cam.right, Vector3.up)) * extents.y
			+ Mathf.Abs(Vector3.Dot(cam.right, Vector3.forward)) * extents.z;

		float forwardExtent = Mathf.Abs(Vector3.Dot(cam.forward, Vector3.right)) * extents.x
			+ Mathf.Abs(Vector3.Dot(cam.forward, Vector3.up)) * extents.y
			+ Mathf.Abs(Vector3.Dot(cam.forward, Vector3.forward)) * extents.z;

		float gripHalfWidth = Mathf.Clamp(rightExtent * 0.85f, 0.09f, 0.42f);
		float frontOffset = Mathf.Clamp(forwardExtent * 0.45f, 0.04f, 0.28f);
		float gripZ = Mathf.Max(0.38f, centerLocal.z - frontOffset);
		float gripY = centerLocal.y - 0.03f;

		leftLocal = new Vector3(centerLocal.x - gripHalfWidth, gripY, gripZ);
		rightLocal = new Vector3(centerLocal.x + gripHalfWidth, gripY, gripZ);
	}

	private void UpdateSingleArm(LimbChain arm, Vector3 shoulderWorld, Vector3 wristWorld, Transform cam, float sideSign)
	{
		float distanceToWrist = Vector3.Distance(shoulderWorld, wristWorld);
		float baseTotalLength = upperArmLength + forearmLength;
		float stretchScale = distanceToWrist > baseTotalLength * 0.92f
			? (distanceToWrist / (baseTotalLength * 0.92f))
			: 1f;
		stretchScale = Mathf.Clamp(stretchScale, 1f, 3.2f);

		float currentUpperLength = upperArmLength * stretchScale;
		float currentForearmLength = forearmLength * stretchScale;

		Vector3 elbowHint = (cam.right * (sideSign * 0.65f) - cam.up * 0.75f - cam.forward * 0.20f).normalized;
		Vector3 elbowWorld = SolveTwoBoneIK(
			shoulderWorld,
			wristWorld,
			currentUpperLength,
			currentForearmLength,
			elbowHint,
			out Vector3 clampedWristWorld);

		PositionCapsuleBetween(arm.upperSegment, shoulderWorld, elbowWorld, armThickness, cam.up);
		arm.middleJoint.position = elbowWorld;
		arm.middleJoint.rotation = cam.rotation;
		PositionCapsuleBetween(arm.lowerSegment, elbowWorld, clampedWristWorld, armThickness * 0.88f, cam.up);

		Vector3 forearmDir = (clampedWristWorld - elbowWorld).normalized;
		if (forearmDir.sqrMagnitude < 0.001f)
		{
			forearmDir = cam.forward;
		}

		Vector3 palmInward = (-cam.right * sideSign + cam.forward * (0.35f * holdBlend)).normalized;
		Vector3 handUp = Vector3.Cross(forearmDir, palmInward).normalized;
		if (handUp.sqrMagnitude < 0.001f)
		{
			handUp = cam.up;
		}

		if (Vector3.Dot(handUp, cam.up) < 0f)
		{
			handUp = -handUp;
		}

		arm.endTransform.position = clampedWristWorld;
		arm.endTransform.rotation = Quaternion.LookRotation(forearmDir, handUp);

		float fingerCurlAngle = Mathf.Lerp(14f, 68f, fingerGripBlend);
		if (arm.fingerPivots != null)
		{
			for (int i = 0; i < arm.fingerPivots.Length; i++)
			{
				if (arm.fingerPivots[i] != null)
				{
					arm.fingerPivots[i].localRotation = Quaternion.Euler(0f, -sideSign * (fingerCurlAngle + i * 4f), 0f);
				}
			}
		}

		if (arm.thumbPivot != null)
		{
			float thumbCurl = Mathf.Lerp(10f, 48f, fingerGripBlend);
			arm.thumbPivot.localRotation = Quaternion.Euler(0f, -sideSign * thumbCurl, -15f);
		}
	}

	private static Vector3 SolveTwoBoneIK(
		Vector3 rootPos,
		Vector3 targetPos,
		float lengthA,
		float lengthB,
		Vector3 bendHint,
		out Vector3 clampedTargetPos)
	{
		Vector3 toTarget = targetPos - rootPos;
		float distance = toTarget.magnitude;
		float maxReach = Mathf.Max(0.01f, lengthA + lengthB - 0.004f);
		float minReach = Mathf.Abs(lengthA - lengthB) + 0.01f;

		if (distance < 0.0001f)
		{
			toTarget = Vector3.forward;
			distance = minReach;
		}

		Vector3 dir = toTarget / distance;
		float clampedDist = Mathf.Clamp(distance, minReach, maxReach);
		clampedTargetPos = rootPos + dir * clampedDist;

		float cosAngle = Mathf.Clamp(
			(lengthA * lengthA + clampedDist * clampedDist - lengthB * lengthB) / (2f * lengthA * clampedDist),
			-1f,
			1f);
		float alongDist = lengthA * cosAngle;
		float perpDist = lengthA * Mathf.Sqrt(Mathf.Max(0f, 1f - cosAngle * cosAngle));

		Vector3 bendDir = bendHint - dir * Vector3.Dot(bendHint, dir);
		if (bendDir.sqrMagnitude < 0.0001f)
		{
			bendDir = Vector3.Cross(dir, Vector3.up);
			if (bendDir.sqrMagnitude < 0.0001f)
			{
				bendDir = Vector3.Cross(dir, Vector3.right);
			}
		}

		bendDir.Normalize();
		return rootPos + dir * alongDist + bendDir * perpDist;
	}

	private static void PositionCapsuleBetween(Transform part, Vector3 start, Vector3 end, float thickness, Vector3 forwardHint)
	{
		Vector3 delta = end - start;
		float length = delta.magnitude;
		if (length < 0.001f)
		{
			part.position = start;
			return;
		}

		Vector3 upDir = delta / length;
		Vector3 forward = forwardHint - upDir * Vector3.Dot(forwardHint, upDir);
		if (forward.sqrMagnitude < 0.001f)
		{
			forward = Vector3.Cross(upDir, Vector3.right);
			if (forward.sqrMagnitude < 0.001f)
			{
				forward = Vector3.Cross(upDir, Vector3.forward);
			}
		}

		part.position = (start + end) * 0.5f;
		part.rotation = Quaternion.LookRotation(forward.normalized, upDir);
		part.localScale = new Vector3(thickness, length * 0.5f + thickness * 0.30f, thickness);
	}
}

