using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class MultiplayerManager : MonoBehaviour
{
	private class PeerState
	{
		public string peerId;
		public IPEndPoint udpEndpoint;
		public float lastSeenTime;
		public Vector3 position;
		public float yaw;
		public float pitch;
		public float crouchRatio;
		public float speed;
		public bool isGrounded;
		public int heldObjectId = -1;
		public bool isHoldingGun;
		public RemotePlayerAvatar avatar;
	}

	private struct OutgoingRelayMessage
	{
		public string topic;
		public string payload;
	}

	public static MultiplayerManager Instance { get; private set; }

	private static string pendingJoinAddress;

	public bool IsHosting { get; private set; }
	public bool IsClient { get; private set; }
	public bool IsConnected => IsHosting || (IsClient && peers.Count > 0);
	public int ConnectedPeerCount => peers.Count;

	public string LocalIpPort { get; private set; } = "";
	public string PublicIpPort { get; private set; } = "";
	public string DisplayAddress => !string.IsNullOrEmpty(PublicIpPort) ? PublicIpPort : LocalIpPort;
	public string StatusMessage { get; private set; } = "Not connected";

	public event Action OnStatusChanged;

	private string localPeerId;
	private UdpClient udpClient;
	private int boundPort;
	private volatile IPEndPoint discoveredStunEndpoint;
	private CancellationTokenSource netCts;
	private readonly ConcurrentQueue<(IPEndPoint ep, string msg)> incomingPackets = new ConcurrentQueue<(IPEndPoint, string)>();
	private readonly ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();
	private readonly Dictionary<string, PeerState> peers = new Dictionary<string, PeerState>();

	private readonly List<Rigidbody> syncedBodies = new List<Rigidbody>();
	private readonly Dictionary<Rigidbody, int> bodyToId = new Dictionary<Rigidbody, int>();
	private readonly Dictionary<int, float> objectAuthorityUntil = new Dictionary<int, float>();

	private IPEndPoint targetHostEndpoint;
	private string activeRelayTopic;
	private bool relayPollRunning;
	private float nextStateSendTime;
	private float nextHelloTime;
	private float nextRelaySendTime;
	private int nextObjectSyncCursor;

	private control localPlayer;
	private grab localGrab;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void AutoInit()
	{
		SceneManager.sceneLoaded -= OnSceneLoaded;
		SceneManager.sceneLoaded += OnSceneLoaded;
	}

	public static MultiplayerManager EnsureInstance()
	{
		if (Instance != null)
		{
			return Instance;
		}

		GameObject go = new GameObject("MultiplayerManager");
		DontDestroyOnLoad(go);
		Instance = go.AddComponent<MultiplayerManager>();
		return Instance;
	}

	public static void JoinFromMenu(string addressInput)
	{
		EnsureInstance();
		pendingJoinAddress = addressInput != null ? addressInput.Trim() : "";
		SceneManager.LoadScene("game");
	}

	private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		if (scene.name == "Start screen")
		{
			if (Instance != null)
			{
				Instance.StopNetworking();
			}
			return;
		}

		if (scene.name == "game")
		{
			MultiplayerManager mgr = EnsureInstance();
			mgr.StartCoroutine(mgr.InitializeSceneObjectsRoutine());
		}
	}

	private void Awake()
	{
		if (Instance != null && Instance != this)
		{
			Destroy(gameObject);
			return;
		}

		Instance = this;
		DontDestroyOnLoad(gameObject);
		Application.runInBackground = true;
		localPeerId = Guid.NewGuid().ToString("N").Substring(0, 8);
	}

	private void OnDestroy()
	{
		StopNetworking();
		if (Instance == this)
		{
			Instance = null;
		}
	}

	private void OnApplicationQuit()
	{
		StopNetworking();
	}

	private IEnumerator InitializeSceneObjectsRoutine()
	{
		yield return null;
		RefreshSceneReferences();

		if (!string.IsNullOrEmpty(pendingJoinAddress))
		{
			string addr = pendingJoinAddress;
			pendingJoinAddress = null;
			StartClient(addr);
		}
	}

	public void RefreshSceneReferences()
	{
		localPlayer = FindAnyObjectByType<control>();
		localGrab = localPlayer != null ? localPlayer.GetComponent<grab>() : null;

		syncedBodies.Clear();
		bodyToId.Clear();

		Rigidbody[] allBodies = FindObjectsByType<Rigidbody>(FindObjectsInactive.Include, FindObjectsSortMode.None);
		Array.Sort(allBodies, (a, b) =>
		{
			int cmp = string.CompareOrdinal(a.name, b.name);
			if (cmp != 0) return cmp;
			return a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex());
		});

		for (int i = 0; i < allBodies.Length; i++)
		{
			Rigidbody rb = allBodies[i];
			if (rb == null || rb.GetComponent<control>() != null)
			{
				continue;
			}

			int id = syncedBodies.Count;
			syncedBodies.Add(rb);
			bodyToId[rb] = id;
		}
	}

	public void StartHosting()
	{
		if (IsHosting)
		{
			return;
		}

		StopNetworking();
		RefreshSceneReferences();

		Application.runInBackground = true;
		netCts = new CancellationTokenSource();

		boundPort = BindUdpSocket(7777);
		string localIp = GetLocalLanIPv4();
		LocalIpPort = $"{localIp}:{boundPort}";
		PublicIpPort = "";
		IsHosting = true;
		IsClient = false;

		SetStatus($"Opening multiplayer on {LocalIpPort} (mapping NAT/STUN...)");
		StartReceiveLoop(netCts.Token);

		// Subscribe to LAN + Local relay topic immediately
		StartRelayListener(GetRelayTopic(LocalIpPort));

		// Discover STUN public IP:Port and attempt automatic UPnP port mapping in background
		CancellationToken token = netCts.Token;
		Task.Run(async () =>
		{
			IPEndPoint stunEp = await QueryStunPublicEndpointAsync(udpClient);
			bool upnpMapped = await TryMapUpnpPortAsync(boundPort, token);

			mainThreadActions.Enqueue(() =>
			{
				if (!IsHosting) return;

				if (stunEp != null)
				{
					PublicIpPort = $"{stunEp.Address}:{stunEp.Port}";
					StartRelayListener(GetRelayTopic(PublicIpPort));
				}

				string natMode = upnpMapped ? "UPnP + Hole-Punch Ready" : "NAT Hole-Punch + Relay Ready";
				string shown = !string.IsNullOrEmpty(PublicIpPort)
					? $"{PublicIpPort}  (LAN: {LocalIpPort})"
					: LocalIpPort;
				SetStatus($"OPEN ON: {shown}\n({natMode} - No port forwarding needed)");
			});
		}, token);
	}

	public void StartClient(string addressInput)
	{
		StopNetworking();
		RefreshSceneReferences();

		if (!TryParseAddress(addressInput, out string hostStr, out int port))
		{
			SetStatus("Invalid IP:Port format (use e.g. 127.0.0.1:7777)");
			return;
		}

		Application.runInBackground = true;
		netCts = new CancellationTokenSource();

		boundPort = BindUdpSocket(0);
		string localIp = GetLocalLanIPv4();
		LocalIpPort = $"{localIp}:{boundPort}";
		IsClient = true;
		IsHosting = false;

		IPAddress resolvedIp = ResolveHostAddress(hostStr);
		if (resolvedIp != null)
		{
			targetHostEndpoint = new IPEndPoint(resolvedIp, port);
		}

		string normalizedTarget = $"{hostStr}:{port}";
		SetStatus($"Connecting to {normalizedTarget}...");

		StartReceiveLoop(netCts.Token);
		StartRelayListener(GetRelayTopic(normalizedTarget));

		// Also discover our own STUN public endpoint and send hole-punch announcement via signaling channel
		CancellationToken token = netCts.Token;
		Task.Run(async () =>
		{
			IPEndPoint myStun = await QueryStunPublicEndpointAsync(udpClient);
			mainThreadActions.Enqueue(() =>
			{
				if (!IsClient) return;
				string myPublic = myStun != null ? $"{myStun.Address}:{myStun.Port}" : LocalIpPort;
				SendRelayPacket(activeRelayTopic, $"PUNCH|{localPeerId}|{myPublic}|{LocalIpPort}");
			});
		}, token);
	}

	public void StopNetworking()
	{
		IsHosting = false;
		IsClient = false;
		activeRelayTopic = null;
		relayPollRunning = false;

		if (netCts != null)
		{
			try { netCts.Cancel(); } catch { }
			netCts.Dispose();
			netCts = null;
		}

		if (udpClient != null)
		{
			try { udpClient.Close(); } catch { }
			udpClient = null;
		}

		foreach (KeyValuePair<string, PeerState> kvp in peers)
		{
			if (kvp.Value.avatar != null)
			{
				Destroy(kvp.Value.avatar.gameObject);
			}
		}

		peers.Clear();
		LocalIpPort = "";
		PublicIpPort = "";
		SetStatus("Not connected");
	}

	private int BindUdpSocket(int preferredPort)
	{
		for (int attempt = 0; attempt < 20; attempt++)
		{
			int tryPort = preferredPort == 0 ? 0 : preferredPort + attempt;
			try
			{
				UdpClient client = new UdpClient(new IPEndPoint(IPAddress.Any, tryPort));
				client.EnableBroadcast = true;
				// Ignore ICMP Port Unreachable resets on Windows UDP sockets
				try
				{
					const int SIO_UDP_CONNRESET = -1744830452;
					client.Client.IOControl((IOControlCode)SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null);
				}
				catch { }

				udpClient = client;
				return ((IPEndPoint)client.Client.LocalEndPoint).Port;
			}
			catch
			{
				if (preferredPort == 0) break;
			}
		}

		udpClient = new UdpClient(0);
		return ((IPEndPoint)udpClient.Client.LocalEndPoint).Port;
	}

	private void StartReceiveLoop(CancellationToken token)
	{
		UdpClient client = udpClient;
		if (client == null) return;

		Task.Run(async () =>
		{
			while (!token.IsCancellationRequested)
			{
				try
				{
					UdpReceiveResult result = await client.ReceiveAsync();
					byte[] data = result.Buffer;
					if (data == null || data.Length < 4)
					{
						continue;
					}

					// Parse binary RFC 5389 STUN Binding Response directly on the game UDP socket
					if (data[0] == 0x00 || data[0] == 0x01)
					{
						IPEndPoint parsedStun = ParseStunResponse(data);
						if (parsedStun != null)
						{
							discoveredStunEndpoint = parsedStun;
						}
						continue;
					}

					string text = Encoding.UTF8.GetString(data);
					if (text.StartsWith("AGY|", StringComparison.Ordinal))
					{
						incomingPackets.Enqueue((result.RemoteEndPoint, text.Substring(4)));
					}
				}
				catch (ObjectDisposedException)
				{
					break;
				}
				catch
				{
					await Task.Delay(10, token);
				}
			}
		}, token);
	}

	private void Update()
	{
		while (mainThreadActions.TryDequeue(out Action action))
		{
			try { action?.Invoke(); } catch { }
		}

		while (incomingPackets.TryDequeue(out (IPEndPoint ep, string msg) packet))
		{
			ProcessPacket(packet.ep, packet.msg);
		}

		if (!IsHosting && !IsClient)
		{
			return;
		}

		if (localPlayer == null)
		{
			RefreshSceneReferences();
		}

		float now = Time.unscaledTime;

		if (IsClient && now >= nextHelloTime)
		{
			nextHelloTime = now + 0.8f;
			if (targetHostEndpoint != null)
			{
				SendUdp(targetHostEndpoint, $"HELLO|{localPeerId}");
				// Also try localhost on target port if testing on the same machine
				SendUdp(new IPEndPoint(IPAddress.Loopback, targetHostEndpoint.Port), $"HELLO|{localPeerId}");
			}
		}

		if (now >= nextStateSendTime)
		{
			nextStateSendTime = now + 0.04f; // 25 Hz state sync
			BroadcastLocalState();
		}

		UpdateRemoteAvatars();
	}

	private void BroadcastLocalState()
	{
		if (localPlayer == null)
		{
			return;
		}

		Vector3 pos = localPlayer.transform.position;
		float yaw = localPlayer.transform.eulerAngles.y;
		Camera cam = localGrab != null && localGrab.PlayerCamera != null ? localGrab.PlayerCamera : Camera.main;
		float pitch = cam != null ? cam.transform.eulerAngles.x : 0f;
		if (pitch > 180f) pitch -= 360f;

		float crouch = localPlayer.CrouchRatio;
		float speed = localPlayer.HorizontalVelocity.magnitude;
		int grounded = localPlayer.IsGrounded ? 1 : 0;

		int heldId = -1;
		int holdingGun = 0;
		if (localGrab != null && localGrab.HeldBody != null)
		{
			if (bodyToId.TryGetValue(localGrab.HeldBody, out int id))
			{
				heldId = id;
				objectAuthorityUntil[id] = Time.unscaledTime + 1.0f;
			}

			holdingGun = localGrab.IsHoldingGun ? 1 : 0;
		}

		string stateMsg = string.Format(
			CultureInfo.InvariantCulture,
			"STATE|{0}|{1:F3},{2:F3},{3:F3}|{4:F1}|{5:F1}|{6:F2}|{7:F2}|{8}|{9}|{10}",
			localPeerId,
			pos.x, pos.y, pos.z,
			yaw, pitch, crouch, speed, grounded, heldId, holdingGun);

		SendToAllPeers(stateMsg);

		// If holding an object, sync that object every tick
		if (heldId >= 0 && heldId < syncedBodies.Count)
		{
			Rigidbody heldRb = syncedBodies[heldId];
			if (heldRb != null)
			{
				SendObjectTransform(heldId, heldRb, true);
			}
		}

		// Host also trickles dynamic physics objects so stacks/balls stay in sync
		if (IsHosting && syncedBodies.Count > 0)
		{
			for (int k = 0; k < 3; k++)
			{
				nextObjectSyncCursor = (nextObjectSyncCursor + 1) % syncedBodies.Count;
				int id = nextObjectSyncCursor;
				if (id == heldId) continue;
				if (objectAuthorityUntil.TryGetValue(id, out float until) && Time.unscaledTime < until) continue;

				Rigidbody rb = syncedBodies[id];
				if (rb != null && rb.gameObject.activeInHierarchy && !rb.IsSleeping())
				{
					SendObjectTransform(id, rb, false);
				}
			}
		}
	}

	private void SendObjectTransform(int id, Rigidbody rb, bool isHeld)
	{
		Vector3 p = rb.position;
		Quaternion r = rb.rotation;
		Vector3 v = rb.isKinematic ? Vector3.zero : rb.linearVelocity;
		string objMsg = string.Format(
			CultureInfo.InvariantCulture,
			"OBJ|{0}|{1}|{2:F3},{3:F3},{4:F3}|{5:F3},{6:F3},{7:F3},{8:F3}|{9:F2},{10:F2},{11:F2}|{12}",
			localPeerId,
			id,
			p.x, p.y, p.z,
			r.x, r.y, r.z, r.w,
			v.x, v.y, v.z,
			isHeld ? 1 : 0);

		SendToAllPeers(objMsg);
	}

	public void BroadcastShot(Vector3 origin, Vector3 direction)
	{
		if (!IsHosting && !IsClient)
		{
			return;
		}

		string shotMsg = string.Format(
			CultureInfo.InvariantCulture,
			"SHOT|{0}|{1:F3},{2:F3},{3:F3}|{4:F3},{5:F3},{6:F3}",
			localPeerId,
			origin.x, origin.y, origin.z,
			direction.x, direction.y, direction.z);

		SendToAllPeers(shotMsg, alsoRelayImmediate: true);
	}

	private void SendToAllPeers(string message, bool alsoRelayImmediate = false)
	{
		if (IsHosting)
		{
			foreach (KeyValuePair<string, PeerState> kvp in peers)
			{
				if (kvp.Value.udpEndpoint != null)
				{
					SendUdp(kvp.Value.udpEndpoint, message);
				}
			}
		}
		else if (IsClient && targetHostEndpoint != null)
		{
			SendUdp(targetHostEndpoint, message);
		}

		// Fallback relay for strict NAT peers
		if (!string.IsNullOrEmpty(activeRelayTopic))
		{
			float now = Time.unscaledTime;
			if (alsoRelayImmediate || now >= nextRelaySendTime)
			{
				nextRelaySendTime = now + 0.22f;
				SendRelayPacket(activeRelayTopic, message);
			}
		}
	}

	private void SendUdp(IPEndPoint endpoint, string payload)
	{
		if (udpClient == null || endpoint == null)
		{
			return;
		}

		try
		{
			byte[] bytes = Encoding.UTF8.GetBytes("AGY|" + payload);
			udpClient.SendAsync(bytes, bytes.Length, endpoint);
		}
		catch { }
	}

	private void ProcessPacket(IPEndPoint senderEp, string message)
	{
		if (string.IsNullOrEmpty(message))
		{
			return;
		}

		string[] parts = message.Split('|');
		if (parts.Length < 2)
		{
			return;
		}

		string cmd = parts[0];
		string senderId = parts[1];
		if (senderId == localPeerId)
		{
			return;
		}

		PeerState peer = GetOrCreatePeer(senderId, senderEp);

		if (cmd == "HELLO")
		{
			SendUdp(senderEp, $"WELCOME|{localPeerId}");
			return;
		}

		if (cmd == "WELCOME")
		{
			if (IsClient && senderEp != null)
			{
				targetHostEndpoint = senderEp;
			}
			return;
		}

		if (cmd == "PUNCH" && parts.Length >= 4)
		{
			// Received NAT hole-punch request via signaling channel!
			if (TryParseEndpoint(parts[2], out IPEndPoint pubEp))
			{
				peer.udpEndpoint = pubEp;
				for (int i = 0; i < 4; i++)
				{
					SendUdp(pubEp, $"WELCOME|{localPeerId}");
				}
			}

			if (TryParseEndpoint(parts[3], out IPEndPoint lanEp))
			{
				SendUdp(lanEp, $"WELCOME|{localPeerId}");
			}
			return;
		}

		if (cmd == "STATE" && parts.Length >= 10)
		{
			peer.position = ParseVec3(parts[2]);
			peer.yaw = ParseFloat(parts[3]);
			peer.pitch = ParseFloat(parts[4]);
			peer.crouchRatio = ParseFloat(parts[5]);
			peer.speed = ParseFloat(parts[6]);
			peer.isGrounded = parts[7] == "1";
			peer.heldObjectId = (int)ParseFloat(parts[8]);
			peer.isHoldingGun = parts[9] == "1";

			// If Hosting, relay this peer's state to any other connected clients
			if (IsHosting)
			{
				foreach (KeyValuePair<string, PeerState> kvp in peers)
				{
					if (kvp.Key != senderId && kvp.Value.udpEndpoint != null)
					{
						SendUdp(kvp.Value.udpEndpoint, message);
					}
				}
			}
			return;
		}

		if (cmd == "OBJ" && parts.Length >= 6)
		{
			int objId = (int)ParseFloat(parts[2]);
			if (objId >= 0 && objId < syncedBodies.Count)
			{
				Rigidbody rb = syncedBodies[objId];
				if (rb != null && (localGrab == null || localGrab.HeldBody != rb))
				{
					Vector3 pos = ParseVec3(parts[3]);
					Quaternion rot = ParseQuat(parts[4]);
					Vector3 vel = ParseVec3(parts[5]);
					bool remoteHeld = parts.Length >= 7 && parts[6] == "1";

					if (remoteHeld)
					{
						objectAuthorityUntil[objId] = Time.unscaledTime + 0.5f;
					}

					if (!rb.gameObject.activeSelf)
					{
						rb.gameObject.SetActive(true);
					}

					rb.position = Vector3.Lerp(rb.position, pos, 0.65f);
					rb.rotation = Quaternion.Slerp(rb.rotation, rot, 0.65f);
					if (!rb.isKinematic)
					{
						rb.linearVelocity = vel;
					}
				}
			}
			return;
		}

		if (cmd == "SHOT" && parts.Length >= 4)
		{
			Vector3 origin = ParseVec3(parts[2]);
			Vector3 dir = ParseVec3(parts[3]);
			SpawnRemoteShotEffect(origin, dir);
		}
	}

	private PeerState GetOrCreatePeer(string peerId, IPEndPoint senderEp)
	{
		if (!peers.TryGetValue(peerId, out PeerState peer))
		{
			peer = new PeerState
			{
				peerId = peerId,
				udpEndpoint = senderEp,
				position = new Vector3(1f, 2f, -7.5f),
				lastSeenTime = Time.unscaledTime
			};
			peers[peerId] = peer;

			GameObject avatarObj = new GameObject($"RemotePlayer_{peerId}");
			peer.avatar = avatarObj.AddComponent<RemotePlayerAvatar>();
			peer.avatar.Initialize(peerId);

			if (IsHosting)
			{
				string shown = DisplayAddress;
				SetStatus($"OPEN ON: {shown}\n({peers.Count} player(s) connected)");
			}
			else if (IsClient)
			{
				SetStatus($"Connected to host ({peers.Count} remote player(s))");
			}
		}

		if (senderEp != null)
		{
			peer.udpEndpoint = senderEp;
			if (IsClient)
			{
				targetHostEndpoint = senderEp;
			}
		}

		peer.lastSeenTime = Time.unscaledTime;
		return peer;
	}

	private void UpdateRemoteAvatars()
	{
		foreach (KeyValuePair<string, PeerState> kvp in peers)
		{
			PeerState peer = kvp.Value;
			if (peer.avatar == null)
			{
				continue;
			}

			Rigidbody heldBody = (peer.heldObjectId >= 0 && peer.heldObjectId < syncedBodies.Count)
				? syncedBodies[peer.heldObjectId]
				: null;

			peer.avatar.ApplyNetworkState(
				peer.position,
				peer.yaw,
				peer.pitch,
				peer.crouchRatio,
				peer.speed,
				peer.isGrounded,
				heldBody,
				peer.isHoldingGun);
		}
	}

	private void SpawnRemoteShotEffect(Vector3 origin, Vector3 dir)
	{
		if (dir.sqrMagnitude < 0.001f) dir = Vector3.forward;
		dir.Normalize();

		if (Physics.Raycast(origin, dir, out RaycastHit hit, 80f, Physics.AllLayers, QueryTriggerInteraction.Ignore))
		{
			if (hit.rigidbody != null && !hit.rigidbody.isKinematic)
			{
				hit.rigidbody.AddForceAtPosition(dir * 14f, hit.point, ForceMode.Impulse);
			}

			ReactiveTarget target = hit.collider.GetComponentInParent<ReactiveTarget>();
			if (target != null) target.OnHit();

			ExplosiveBarrel barrel = hit.collider.GetComponentInParent<ExplosiveBarrel>();
			if (barrel != null) barrel.TriggerExplosion();

			wall wallSurface = hit.collider.GetComponentInParent<wall>();
			if (wallSurface != null) wallSurface.ScrapeAtWorldPoint(hit.point, hit.normal, 0.14f);
		}

		GameObject bullet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
		bullet.name = "RemoteBulletProjectile";
		bullet.transform.position = origin;
		bullet.transform.localScale = new Vector3(0.04f, 0.04f, 0.16f);
		bullet.transform.rotation = Quaternion.LookRotation(dir);
		Collider col = bullet.GetComponent<Collider>();
		if (col != null) Destroy(col);
		bullet.AddComponent<BulletVisualMover>().Initialize(dir * 65f, 0.6f);
	}

	// =========================================================================
	// Zero-Port-Forwarding Helpers: STUN + UPnP IGD + Public Signaling/Relay
	// =========================================================================

	private static string GetRelayTopic(string addressPort)
	{
		string clean = addressPort.Trim().ToLowerInvariant().Replace(".", "_").Replace(":", "_");
		return "agy_mp_room_" + clean;
	}

	private void StartRelayListener(string topic)
	{
		activeRelayTopic = topic;
		if (!relayPollRunning)
		{
			relayPollRunning = true;
			StartCoroutine(PollRelayRoutine());
		}
	}

	private IEnumerator PollRelayRoutine()
	{
		string lastId = "";
		while (relayPollRunning && (IsHosting || IsClient))
		{
			if (!string.IsNullOrEmpty(activeRelayTopic))
			{
				string url = $"https://ntfy.sh/{activeRelayTopic}/raw?poll=1&since=10s";
				using (UnityWebRequest req = UnityWebRequest.Get(url))
				{
					req.timeout = 4;
					yield return req.SendWebRequest();
					if (req.result == UnityWebRequest.Result.Success && !string.IsNullOrEmpty(req.downloadHandler.text))
					{
						string[] lines = req.downloadHandler.text.Split('\n');
						for (int i = 0; i < lines.Length; i++)
						{
							string line = lines[i].Trim();
							if (string.IsNullOrEmpty(line) || line == lastId) continue;
							lastId = line;
							incomingPackets.Enqueue(( null, line ));
						}
					}
				}
			}

			yield return new WaitForSecondsRealtime(1.1f);
		}

		relayPollRunning = false;
	}

	private void SendRelayPacket(string topic, string message)
	{
		if (string.IsNullOrEmpty(topic) || string.IsNullOrEmpty(message))
		{
			return;
		}

		StartCoroutine(PostRelayPacketRoutine(topic, message));
	}

	private static IEnumerator PostRelayPacketRoutine(string topic, string message)
	{
		byte[] body = Encoding.UTF8.GetBytes(message);
		using (UnityWebRequest req = new UnityWebRequest($"https://ntfy.sh/{topic}", "POST"))
		{
			req.uploadHandler = new UploadHandlerRaw(body);
			req.downloadHandler = new DownloadHandlerBuffer();
			req.timeout = 4;
			yield return req.SendWebRequest();
		}
	}

	private async Task<IPEndPoint> QueryStunPublicEndpointAsync(UdpClient socket)
	{
		if (socket == null) return null;
		discoveredStunEndpoint = null;

		string[] stunHosts =
		{
			"stun.l.google.com:19302",
			"stun1.l.google.com:19302",
			"stun.cloudflare.com:3478"
		};

		// Build 20-byte RFC 5389 STUN Binding Request
		byte[] request = new byte[20];
		request[0] = 0x00;
		request[1] = 0x01; // Binding Request
		request[2] = 0x00;
		request[3] = 0x00; // Message Length = 0
		request[4] = 0x21;
		request[5] = 0x12;
		request[6] = 0xA4;
		request[7] = 0x42; // Magic Cookie 0x2112A442
		System.Random rng = new System.Random();
		for (int i = 8; i < 20; i++)
		{
			request[i] = (byte)rng.Next(0, 256);
		}

		foreach (string stunHost in stunHosts)
		{
			try
			{
				string[] hp = stunHost.Split(':');
				IPAddress[] ips = await Dns.GetHostAddressesAsync(hp[0]);
				IPAddress ipv4 = null;
				foreach (IPAddress ip in ips)
				{
					if (ip.AddressFamily == AddressFamily.InterNetwork)
					{
						ipv4 = ip;
						break;
					}
				}

				if (ipv4 == null) continue;
				IPEndPoint stunEp = new IPEndPoint(ipv4, int.Parse(hp[1]));

				await socket.SendAsync(request, request.Length, stunEp);
				for (int wait = 0; wait < 12; wait++)
				{
					await Task.Delay(80);
					if (discoveredStunEndpoint != null)
					{
						return discoveredStunEndpoint;
					}
				}
			}
			catch { }
		}

		return discoveredStunEndpoint;
	}

	private static IPEndPoint ParseStunResponse(byte[] buf)
	{
		if (buf == null || buf.Length < 20 || buf[0] != 0x01 || buf[1] != 0x01)
		{
			return null;
		}

		int idx = 20;
		while (idx + 4 <= buf.Length)
		{
			int attrType = (buf[idx] << 8) | buf[idx + 1];
			int attrLen = (buf[idx + 2] << 8) | buf[idx + 3];
			idx += 4;
			if (idx + attrLen > buf.Length) break;

			// XOR-MAPPED-ADDRESS (0x0020) or MAPPED-ADDRESS (0x0001)
			if ((attrType == 0x0020 || attrType == 0x0001) && attrLen >= 8 && buf[idx + 1] == 0x01)
			{
				bool isXor = attrType == 0x0020;
				int port = (buf[idx + 2] << 8) | buf[idx + 3];
				if (isXor) port ^= 0x2112;

				byte[] ipBytes = new byte[4];
				byte[] cookie = { 0x21, 0x12, 0xA4, 0x42 };
				for (int i = 0; i < 4; i++)
				{
					ipBytes[i] = isXor ? (byte)(buf[idx + 4 + i] ^ cookie[i]) : buf[idx + 4 + i];
				}

				return new IPEndPoint(new IPAddress(ipBytes), port);
			}

			int pad = (4 - (attrLen % 4)) % 4;
			idx += attrLen + pad;
		}

		return null;
	}

	private static async Task<bool> TryMapUpnpPortAsync(int port, CancellationToken token)
	{
		try
		{
			using (UdpClient ssdp = new UdpClient(0))
			{
				IPEndPoint multicastEp = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
				string search =
					"M-SEARCH * HTTP/1.1\r\n" +
					"HOST: 239.255.255.250:1900\r\n" +
					"MAN: \"ssdp:discover\"\r\n" +
					"MX: 1\r\n" +
					"ST: urn:schemas-upnp-org:device:InternetGatewayDevice:1\r\n\r\n";
				byte[] bytes = Encoding.ASCII.GetBytes(search);
				await ssdp.SendAsync(bytes, bytes.Length, multicastEp);

				Task<UdpReceiveResult> recvTask = ssdp.ReceiveAsync();
				if (await Task.WhenAny(recvTask, Task.Delay(1200, token)) != recvTask)
				{
					return false;
				}

				string resp = Encoding.ASCII.GetString(recvTask.Result.Buffer);
				string location = ExtractHeader(resp, "LOCATION:");
				if (string.IsNullOrEmpty(location))
				{
					return false;
				}

				using (WebClient wc = new WebClient())
				{
					string xml = await wc.DownloadStringTaskAsync(location);
					string serviceType = xml.Contains("urn:schemas-upnp-org:service:WANIPConnection:1")
						? "urn:schemas-upnp-org:service:WANIPConnection:1"
						: "urn:schemas-upnp-org:service:WANPPPConnection:1";

					int svcIdx = xml.IndexOf(serviceType, StringComparison.OrdinalIgnoreCase);
					if (svcIdx < 0) return false;

					int ctrlStart = xml.IndexOf("<controlURL>", svcIdx, StringComparison.OrdinalIgnoreCase);
					int ctrlEnd = xml.IndexOf("</controlURL>", svcIdx, StringComparison.OrdinalIgnoreCase);
					if (ctrlStart < 0 || ctrlEnd < 0) return false;

					string controlPath = xml.Substring(ctrlStart + 12, ctrlEnd - (ctrlStart + 12)).Trim();
					Uri baseUri = new Uri(location);
					Uri controlUri = new Uri(baseUri, controlPath);

					string localIp = GetLocalLanIPv4();
					string soapBody =
						"<?xml version=\"1.0\"?>" +
						"<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
						"<s:Body>" +
						$"<u:AddPortMapping xmlns:u=\"{serviceType}\">" +
						"<NewRemoteHost></NewRemoteHost>" +
						$"<NewExternalPort>{port}</NewExternalPort>" +
						"<NewProtocol>UDP</NewProtocol>" +
						$"<NewInternalPort>{port}</NewInternalPort>" +
						$"<NewInternalClient>{localIp}</NewInternalClient>" +
						"<NewEnabled>1</NewEnabled>" +
						"<NewPortMappingDescription>UnityGameMP</NewPortMappingDescription>" +
						"<NewLeaseDuration>0</NewLeaseDuration>" +
						"</u:AddPortMapping>" +
						"</s:Body></s:Envelope>";

					wc.Headers.Set("Content-Type", "text/xml; charset=\"utf-8\"");
					wc.Headers.Set("SOAPAction", $"\"{serviceType}#AddPortMapping\"");
					await wc.UploadStringTaskAsync(controlUri, "POST", soapBody);
					return true;
				}
			}
		}
		catch
		{
			return false;
		}
	}

	private static string ExtractHeader(string raw, string headerName)
	{
		string[] lines = raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
		foreach (string line in lines)
		{
			if (line.StartsWith(headerName, StringComparison.OrdinalIgnoreCase))
			{
				return line.Substring(headerName.Length).Trim();
			}
		}
		return null;
	}

	public static string GetLocalLanIPv4()
	{
		try
		{
			using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
			{
				socket.Connect("8.8.8.8", 65530);
				if (socket.LocalEndPoint is IPEndPoint ep)
				{
					return ep.Address.ToString();
				}
			}
		}
		catch { }

		try
		{
			foreach (IPAddress ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
			{
				if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
				{
					return ip.ToString();
				}
			}
		}
		catch { }

		return "127.0.0.1";
	}

	public static bool TryParseAddress(string input, out string host, out int port)
	{
		host = "127.0.0.1";
		port = 7777;
		if (string.IsNullOrWhiteSpace(input))
		{
			return false;
		}

		string clean = input.Trim();
		if (clean.Contains(":"))
		{
			string[] parts = clean.Split(':');
			host = string.IsNullOrWhiteSpace(parts[0]) ? "127.0.0.1" : parts[0].Trim();
			return int.TryParse(parts[1].Trim(), out port) && port > 0 && port <= 65535;
		}

		host = clean;
		return true;
	}

	private static bool TryParseEndpoint(string input, out IPEndPoint ep)
	{
		ep = null;
		if (!TryParseAddress(input, out string host, out int port))
		{
			return false;
		}

		if (IPAddress.TryParse(host, out IPAddress ip))
		{
			ep = new IPEndPoint(ip, port);
			return true;
		}
		return false;
	}

	private static IPAddress ResolveHostAddress(string host)
	{
		if (IPAddress.TryParse(host, out IPAddress ip))
		{
			return ip;
		}

		try
		{
			foreach (IPAddress addr in Dns.GetHostAddresses(host))
			{
				if (addr.AddressFamily == AddressFamily.InterNetwork)
				{
					return addr;
				}
			}
		}
		catch { }

		return IPAddress.Loopback;
	}

	private void SetStatus(string status)
	{
		StatusMessage = status;
		OnStatusChanged?.Invoke();
	}

	private static float ParseFloat(string s)
	{
		return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;
	}

	private static Vector3 ParseVec3(string s)
	{
		string[] p = s.Split(',');
		if (p.Length < 3) return Vector3.zero;
		return new Vector3(ParseFloat(p[0]), ParseFloat(p[1]), ParseFloat(p[2]));
	}

	private static Quaternion ParseQuat(string s)
	{
		string[] p = s.Split(',');
		if (p.Length < 4) return Quaternion.identity;
		return new Quaternion(ParseFloat(p[0]), ParseFloat(p[1]), ParseFloat(p[2]), ParseFloat(p[3]));
	}
}

public class RemotePlayerAvatar : MonoBehaviour
{
	private Transform headTransform;
	private Transform torsoTransform;
	private Transform pelvisTransform;
	private Transform leftUpperLeg;
	private Transform leftLowerLeg;
	private Transform leftFoot;
	private Transform rightUpperLeg;
	private Transform rightLowerLeg;
	private Transform rightFoot;
	private Transform leftUpperArm;
	private Transform leftForearm;
	private Transform rightUpperArm;
	private Transform rightForearm;

	private Vector3 targetPosition;
	private float targetYaw;
	private float targetPitch;
	private float targetCrouch;
	private float targetSpeed;
	private bool isGrounded = true;
	private Rigidbody heldObject;
	private bool isHoldingGun;
	private float walkCycle;
	private bool initializedPos;

	public void Initialize(string peerId)
	{
		Renderer templateRenderer = FindAnyObjectByType<MeshRenderer>();
		Material templateMat = templateRenderer != null ? templateRenderer.sharedMaterial : null;
		Shader shader = templateMat != null && templateMat.shader != null
			? templateMat.shader
			: (Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));

		Material shirtMat = CreateMat(templateMat, shader, new Color(0.85f, 0.45f, 0.20f));
		Material skinMat = CreateMat(templateMat, shader, new Color(0.90f, 0.73f, 0.58f));
		Material pantsMat = CreateMat(templateMat, shader, new Color(0.18f, 0.21f, 0.27f));
		Material shoeMat = CreateMat(templateMat, shader, new Color(0.12f, 0.11f, 0.10f));
		Material visorMat = CreateMat(templateMat, shader, new Color(0.15f, 0.18f, 0.22f));

		headTransform = CreatePart("Head", PrimitiveType.Sphere, transform, skinMat);
		headTransform.localScale = new Vector3(0.32f, 0.34f, 0.32f);

		Transform visor = CreatePart("Visor", PrimitiveType.Cube, headTransform, visorMat);
		visor.localPosition = new Vector3(0f, 0.08f, 0.42f);
		visor.localScale = new Vector3(0.72f, 0.26f, 0.30f);

		torsoTransform = CreatePart("Torso", PrimitiveType.Capsule, transform, shirtMat);
		pelvisTransform = CreatePart("Pelvis", PrimitiveType.Capsule, transform, pantsMat);

		leftUpperLeg = CreatePart("LeftUpperLeg", PrimitiveType.Capsule, transform, pantsMat);
		leftLowerLeg = CreatePart("LeftLowerLeg", PrimitiveType.Capsule, transform, pantsMat);
		leftFoot = CreatePart("LeftFoot", PrimitiveType.Cube, transform, shoeMat);
		leftFoot.localScale = new Vector3(0.12f, 0.09f, 0.24f);

		rightUpperLeg = CreatePart("RightUpperLeg", PrimitiveType.Capsule, transform, pantsMat);
		rightLowerLeg = CreatePart("RightLowerLeg", PrimitiveType.Capsule, transform, pantsMat);
		rightFoot = CreatePart("RightFoot", PrimitiveType.Cube, transform, shoeMat);
		rightFoot.localScale = new Vector3(0.12f, 0.09f, 0.24f);

		leftUpperArm = CreatePart("LeftUpperArm", PrimitiveType.Capsule, transform, shirtMat);
		leftForearm = CreatePart("LeftForearm", PrimitiveType.Capsule, transform, skinMat);
		rightUpperArm = CreatePart("RightUpperArm", PrimitiveType.Capsule, transform, shirtMat);
		rightForearm = CreatePart("RightForearm", PrimitiveType.Capsule, transform, skinMat);
	}

	private static Material CreateMat(Material templateMat, Shader shader, Color color)
	{
		Material m = templateMat != null ? new Material(templateMat) : new Material(shader);
		m.color = color;
		if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
		return m;
	}

	private static Transform CreatePart(string name, PrimitiveType type, Transform parent, Material mat)
	{
		GameObject go = GameObject.CreatePrimitive(type);
		go.name = name;
		go.transform.SetParent(parent, false);
		Collider c = go.GetComponent<Collider>();
		if (c != null) Destroy(c);
		Renderer r = go.GetComponent<Renderer>();
		if (r != null && mat != null) r.sharedMaterial = mat;
		return go.transform;
	}

	public void ApplyNetworkState(Vector3 pos, float yaw, float pitch, float crouch, float speed, bool grounded, Rigidbody held, bool holdingGun)
	{
		if (!initializedPos)
		{
			transform.position = pos;
			initializedPos = true;
		}

		targetPosition = pos;
		targetYaw = yaw;
		targetPitch = pitch;
		targetCrouch = crouch;
		targetSpeed = speed;
		isGrounded = grounded;
		heldObject = held;
		isHoldingGun = holdingGun;
	}

	private void LateUpdate()
	{
		if (!initializedPos) return;

		float dt = Time.deltaTime;
		transform.position = Vector3.Lerp(transform.position, targetPosition, dt * 16f);
		transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, targetYaw, 0f), dt * 16f);

		if (isGrounded && targetSpeed > 0.1f)
		{
			walkCycle += dt * Mathf.Lerp(5.5f, 11f, Mathf.InverseLerp(1.5f, 8f, targetSpeed));
		}

		float bottomY = -1.0f;
		float crouchDrop = targetCrouch * 0.72f;
		float headY = 0.72f - crouchDrop;
		float shoulderY = 0.50f - crouchDrop;
		float hipY = -0.16f - crouchDrop * 0.55f;

		Vector3 headWorld = transform.TransformPoint(new Vector3(0f, headY, 0.04f));
		headTransform.position = headWorld;
		headTransform.rotation = transform.rotation * Quaternion.Euler(targetPitch, 0f, 0f);

		Vector3 hipWorld = transform.TransformPoint(new Vector3(0f, hipY, -0.06f));
		Vector3 shoulderWorld = transform.TransformPoint(new Vector3(0f, shoulderY, -0.04f));

		PositionCapsule(torsoTransform, hipWorld, shoulderWorld, 0.28f, transform.forward);
		torsoTransform.localScale = new Vector3(0.32f, torsoTransform.localScale.y, 0.20f);

		pelvisTransform.position = hipWorld;
		pelvisTransform.rotation = transform.rotation * Quaternion.Euler(0f, 0f, 90f);
		pelvisTransform.localScale = new Vector3(0.18f, 0.19f, 0.18f);

		UpdateLeg(leftUpperLeg, leftLowerLeg, leftFoot, -0.145f, 0f, hipY, bottomY);
		UpdateLeg(rightUpperLeg, rightLowerLeg, rightFoot, 0.145f, Mathf.PI, hipY, bottomY);

		UpdateArm(leftUpperArm, leftForearm, -1f, shoulderY);
		UpdateArm(rightUpperArm, rightForearm, 1f, shoulderY);
	}

	private void UpdateLeg(Transform upper, Transform lower, Transform foot, float sideX, float phaseOffset, float hipY, float bottomY)
	{
		float moveFactor = Mathf.Clamp01(targetSpeed / 5f);
		float phase = walkCycle + phaseOffset;
		float strideZ = isGrounded ? Mathf.Sin(phase) * 0.30f * moveFactor : 0.08f * Mathf.Sign(sideX);
		float liftY = isGrounded ? Mathf.Max(0f, Mathf.Cos(phase)) * 0.10f * moveFactor : 0.10f;

		Vector3 hipWorld = transform.TransformPoint(new Vector3(sideX, hipY, -0.04f));
		Vector3 ankleWorld = transform.TransformPoint(new Vector3(sideX, bottomY + 0.08f + liftY, strideZ));
		Vector3 kneeWorld = (hipWorld + ankleWorld) * 0.5f + transform.forward * (0.08f + targetCrouch * 0.22f);

		PositionCapsule(upper, hipWorld, kneeWorld, 0.13f, transform.forward);
		PositionCapsule(lower, kneeWorld, ankleWorld, 0.115f, transform.forward);

		foot.position = ankleWorld + transform.forward * 0.05f;
		foot.rotation = transform.rotation;
	}

	private void UpdateArm(Transform upper, Transform forearm, float sideSign, float shoulderY)
	{
		Vector3 shoulderWorld = transform.TransformPoint(new Vector3(sideSign * 0.24f, shoulderY, -0.02f));
		Vector3 wristWorld;

		if (heldObject != null)
		{
			wristWorld = heldObject.position + transform.right * (sideSign * (isHoldingGun ? 0.04f : 0.16f));
		}
		else
		{
			float swing = -Mathf.Sin(walkCycle + (sideSign > 0f ? Mathf.PI : 0f)) * 0.12f * Mathf.Clamp01(targetSpeed / 5f);
			wristWorld = transform.TransformPoint(new Vector3(sideSign * 0.26f, shoulderY - 0.36f, 0.22f + swing));
		}

		Vector3 elbowWorld = (shoulderWorld + wristWorld) * 0.5f
			+ transform.right * (sideSign * 0.08f)
			- Vector3.up * 0.10f;

		PositionCapsule(upper, shoulderWorld, elbowWorld, 0.085f, transform.forward);
		PositionCapsule(forearm, elbowWorld, wristWorld, 0.075f, transform.forward);
	}

	private static void PositionCapsule(Transform part, Vector3 start, Vector3 end, float thickness, Vector3 forwardHint)
	{
		Vector3 delta = end - start;
		float len = delta.magnitude;
		if (len < 0.001f) return;

		Vector3 up = delta / len;
		Vector3 fwd = forwardHint - up * Vector3.Dot(forwardHint, up);
		if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.Cross(up, Vector3.right);

		part.position = (start + end) * 0.5f;
		part.rotation = Quaternion.LookRotation(fwd.normalized, up);
		part.localScale = new Vector3(thickness, len * 0.5f + thickness * 0.3f, thickness);
	}
}
