using System;
using System.Collections.Generic;
using UnityEngine;

namespace MikanXR.Unity
{
	// The root of the mirrored Mikan scene and the owner of the connection. Place it where the
	// Mikan world should sit in the Unity scene; everything the editor reports is spawned under
	// it. One client per editor connection.
	//
	// Requests are answered on the transport's receive thread, so this never attaches a
	// continuation to a future: it keeps the pending futures in a list and polls them from Update,
	// which is where every callback runs.
	[DisallowMultipleComponent]
	public class MikanClient : MonoBehaviour
	{
		[Header("Connection")]
		[Tooltip("The id the editor knows this client by, and the prefix of its Spout sender names. A compositor's client texture source names it, so keep it stable.")]
		[SerializeField] private string _clientName = "MikanXR_Unity";
		[SerializeField] private string _host = "";
		[SerializeField] private string _port = "";
		[SerializeField] private bool _connectOnStart = true;
		[SerializeField] private float _reconnectIntervalSeconds = 1f;
		[SerializeField] private MikanLogLevel _minLogLevel = MikanLogLevel.Info;

		[Header("Scene")]
		[Tooltip("Layer the stencil meshes render on. The Mikan cameras cull it until the knockout pass is enabled.")]
		[SerializeField] private int _stencilLayer = 30;
		[Tooltip("Layer the camera frustums and other helpers render on. The Mikan cameras cull it.")]
		[SerializeField] private int _visualizationLayer = 31;
		[SerializeField] private bool _showCameraFrustums = true;
		[Tooltip("Render the stencils into the Mikan cameras as knockout holdouts.")]
		[SerializeField] private bool _renderStencils = true;
		[Tooltip("How dark a fully shadowed point on a stencil gets in the shadow buffer: 1 is black, 0 is no shadow.")]
		[Range(0f, 1f)]
		[SerializeField] private float _shadowStrength = 0.7f;
		[Tooltip("Persist the settings to a JSON file under the persistent data path when no host provider is installed.")]
		[SerializeField] private bool _persistSettingsToJson = false;

		private const float k_requestTimeoutSeconds = 5f;
		private const int k_uiLayer = 5;

		private class PendingRequest
		{
			public MikanResponseFuture future;
			public Action<MikanResponse> onResponse;
			public float deadline;
		}

		private MikanAPI _api;
		private MikanSpoutRenderTargetWriter _renderTargetWriter;
		private MikanDataStore _dataStore;
		private MikanSceneMirror _sceneMirror;
		private MikanFramePublisher _framePublisher;
		private MikanSettings _settings = new MikanSettings();
		private IMikanSettingsProvider _settingsProvider;
		private readonly List<PendingRequest> _pendingRequests = new List<PendingRequest>();
		private float _reconnectTimer = 0f;
		private bool _wasConnected = false;
		private bool _clientInitialized = false;

		public MikanAPI API => _api;
		public MikanDataStore DataStore => _dataStore;
		public MikanSceneMirror SceneMirror => _sceneMirror;
		public MikanFramePublisher FramePublisher => _framePublisher;
		public MikanSpoutRenderTargetWriter RenderTargetWriter => _renderTargetWriter;
		public MikanSettings Settings => _settings;
		public bool IsConnected => _api != null && _api.GetIsConnected();
		// Connected and past the client handshake, which is when scene data starts flowing
		public bool IsClientInitialized => _clientInitialized;
		public int StencilLayer => _settings.stencilLayer;
		public int VisualizationLayer => _settings.visualizationLayer;
		public bool ShowCameraFrustums => _settings.showCameraFrustums;
		public bool RenderStencils => _settings.renderStencils;
		public float ShadowStrength => _settings.shadowStrength;

		// Host-facing setters. The client name must be set before Awake, since it is the id the
		// editor and the Spout senders are keyed by; the rest take effect at once.
		public void SetClientName(string clientName)
		{
			_clientName = clientName;
		}

		public void SetRenderStencils(bool renderStencils)
		{
			_settings.renderStencils = renderStencils;
		}

		public void SetShadowStrength(float shadowStrength)
		{
			_settings.shadowStrength = Mathf.Clamp01(shadowStrength);
		}

		public void SetShowCameraFrustums(bool show)
		{
			_settings.showCameraFrustums = show;
			if (_sceneMirror != null)
			{
				foreach (MikanCameraObject camera in _sceneMirror.FindAll<MikanCameraObject>())
				{
					camera.SetFrustumVisible(show);
				}
			}
		}

		public event Action Connected;
		public event Action Disconnected;
		public event Action<MikanEvent> EventReceived;
		public event Action<MikanLogLevel, string> LogMessage;

		// What the Mikan cameras render: everything but the UI, the helpers, and the stencils
		// until the knockout pass is on
		public int RenderCullingMask
		{
			get
			{
				int mask = ~((1 << k_uiLayer) | (1 << VisualizationLayer));
				if (!RenderStencils)
				{
					mask &= ~(1 << StencilLayer);
				}

				return mask;
			}
		}

		// A host installs its store before the client awakes (create the object inactive, set this,
		// then activate), so the stored settings override the inspector's
		public IMikanSettingsProvider SettingsProvider
		{
			get { return _settingsProvider; }
			set { _settingsProvider = value; }
		}

		private void Awake()
		{
			_settings.host = _host;
			_settings.port = _port;
			_settings.stencilLayer = _stencilLayer;
			_settings.visualizationLayer = _visualizationLayer;
			_settings.showCameraFrustums = _showCameraFrustums;
			_settings.renderStencils = _renderStencils;
			_settings.shadowStrength = _shadowStrength;

			if (_settingsProvider == null && _persistSettingsToJson)
			{
				_settingsProvider = new MikanJsonFileSettingsProvider();
			}

			if (_settingsProvider != null && _settingsProvider.Load(_settings) && _settings.applyRootTransform)
			{
				transform.localPosition = _settings.rootPosition;
				transform.localEulerAngles = _settings.rootEulerAngles;
				transform.localScale = _settings.rootScale;
			}

			// A stable name, since a compositor's client texture source refers to it by name; a
			// second instance with the same name would collide on the Spout sender names
			string clientName = string.IsNullOrEmpty(_clientName) ? "MikanXR_Unity" : _clientName;

			_api = new MikanAPI(new MikanWebSocketTransport(), null);
			_api.SetLogCallback(HandleLog);

			MikanAPIResult result = _api.Initialize(clientName, _minLogLevel);
			if (result != MikanAPIResult.Success)
			{
				Log(MikanLogLevel.Error, $"MikanClient: failed to initialize the client API ({result})");
				enabled = false;
				return;
			}

			_renderTargetWriter = new MikanSpoutRenderTargetWriter(this);
			_api.RenderTargetWriter = _renderTargetWriter;

			_dataStore = new MikanDataStore(this);
			_sceneMirror = new MikanSceneMirror(this);
			_framePublisher = gameObject.AddComponent<MikanFramePublisher>();
			_framePublisher.hideFlags = HideFlags.HideInInspector;
		}

		private void Start()
		{
			if (_connectOnStart)
			{
				Connect();
			}
		}

		private void OnDestroy()
		{
			if (_api == null)
			{
				return;
			}

			_sceneMirror?.Clear();
			_dataStore?.Clear();

			if (_api.GetIsConnected())
			{
				// Fire and forget: the editor drops the client on the socket close anyway
				_api.SendRequest(new DisposeClientRequest() { clientId = _api.GetClientUniqueID() });
				_api.Disconnect();
			}

			_api.Dispose();
			_api = null;
		}

		public void Connect()
		{
			if (_api == null || _api.GetIsConnected())
			{
				return;
			}

			MikanAPIResult result = _api.Connect(_settings.host, _settings.port);
			if (result != MikanAPIResult.Success && result != MikanAPIResult.AlreadyConnected)
			{
				Log(MikanLogLevel.Warning, $"MikanClient: connect failed ({result})");
			}

			_reconnectTimer = _reconnectIntervalSeconds;
		}

		public void Disconnect()
		{
			_api?.Disconnect();
		}

		public void SaveSettings()
		{
			_settings.rootPosition = transform.localPosition;
			_settings.rootEulerAngles = transform.localEulerAngles;
			_settings.rootScale = transform.localScale;

			_settingsProvider?.Save(_settings);
		}

		// Sends a request and delivers its response on the main thread. A response that never
		// arrives is delivered as a Timeout result.
		public void SendRequest(MikanRequest request, Action<MikanResponse> onResponse = null)
		{
			if (_api == null)
			{
				return;
			}

			MikanResponseFuture future = _api.SendRequest(request);

			if (onResponse == null)
			{
				return;
			}

			_pendingRequests.Add(new PendingRequest()
			{
				future = future,
				onResponse = onResponse,
				deadline = Time.unscaledTime + k_requestTimeoutSeconds
			});
		}

		private void Update()
		{
			if (_api == null)
			{
				return;
			}

			PumpEvents();
			PollPendingRequests();

			bool connected = _api.GetIsConnected();
			if (!connected)
			{
				if (_wasConnected)
				{
					_wasConnected = false;
					HandleDisconnected(null);
				}

				if (_connectOnStart)
				{
					_reconnectTimer -= Time.unscaledDeltaTime;
					if (_reconnectTimer <= 0f)
					{
						Connect();
					}
				}
			}
			else
			{
				_wasConnected = true;
			}
		}

		private void PumpEvents()
		{
			while (_api != null && _api.FetchNextEvent(out MikanEvent mikanEvent) == MikanAPIResult.Success)
			{
				HandleEvent(mikanEvent);
			}
		}

		private void PollPendingRequests()
		{
			for (int index = _pendingRequests.Count - 1; index >= 0; --index)
			{
				PendingRequest pending = _pendingRequests[index];
				MikanResponse response = null;

				if (pending.future.TryFetchResponse(out response))
				{
					_pendingRequests.RemoveAt(index);
				}
				else if (Time.unscaledTime >= pending.deadline)
				{
					_pendingRequests.RemoveAt(index);
					response = new MikanResponse()
					{
						responseTypeName = typeof(MikanResponse).Name,
						requestId = (int)MikanConstants.InvalidMikanID,
						resultCode = MikanAPIResult.Timeout
					};
				}

				if (response != null)
				{
					try
					{
						pending.onResponse(response);
					}
					catch (Exception e)
					{
						Debug.LogException(e, this);
					}
				}
			}
		}

		private void HandleEvent(MikanEvent mikanEvent)
		{
			switch (mikanEvent)
			{
			case MikanConnectedEvent connectedEvent:
				HandleConnected(connectedEvent);
				break;
			case MikanDisconnectedEvent disconnectedEvent:
				_wasConnected = false;
				HandleDisconnected(disconnectedEvent);
				break;
			case MikanPropertyUpdateEvent propertyUpdate:
				_dataStore.HandlePropertyUpdate(propertyUpdate);
				break;
			case MikanCameraNewFrameEvent newFrame:
				_sceneMirror.RouteCameraEvent(newFrame.camera_id, mikanEvent);
				break;
			case MikanCameraNewPropertiesEvent newProperties:
				_sceneMirror.RouteCameraEvent(newProperties.camera_id, mikanEvent);
				break;
			case MikanCompositorStartedEvent started:
				_sceneMirror.RouteCameraEvent(started.camera_id, mikanEvent);
				break;
			case MikanCompositorStoppedEvent stopped:
				_sceneMirror.RouteCameraEvent(stopped.camera_id, mikanEvent);
				break;
			}

			EventReceived?.Invoke(mikanEvent);
		}

		private void HandleConnected(MikanConnectedEvent connectedEvent)
		{
			Log(MikanLogLevel.Info,
				$"MikanClient: connected (server version {connectedEvent.serverVersion?.version}, compatible: {connectedEvent.isClientCompatible})");

			if (!connectedEvent.isClientCompatible)
			{
				Log(MikanLogLevel.Error, "MikanClient: the editor reports this client API version as incompatible");
			}

			MikanClientInfo clientInfo = _api.AllocateClientInfo();
			clientInfo.engineName = "Unity";
			clientInfo.engineVersion = Application.unityVersion;
			clientInfo.applicationName = Application.productName;
			clientInfo.applicationVersion = Application.version;
			clientInfo.xrDeviceName = "";
			clientInfo.graphicsAPI = MikanSpoutRenderTargetWriter.CurrentGraphicsApi();
			clientInfo.supportsRGB24 = false;
			clientInfo.supportsRGBA32 = true;
			clientInfo.supportsBGRA32 = false;
			clientInfo.supportsDepth = true;

			SendRequest(new InitClientRequest() { clientInfo = clientInfo }, response =>
			{
				if (response.resultCode != MikanAPIResult.Success)
				{
					Log(MikanLogLevel.Error, $"MikanClient: InitClientRequest failed ({response.resultCode})");
					return;
				}

				// Without this the editor publishes no property updates, and the create and
				// destroy notifications ride on them
				SendRequest(new SetPropertyNotifyMode()
				{
					systemFilter = "",
					componentFilter = "",
					propertyFilter = "",
					notifyMode = MikanPropertyNotifyMode.NAME_AND_VALUE
				});

				_clientInitialized = true;
				_dataStore.RefreshAll();
				Connected?.Invoke();
			});
		}

		private void HandleDisconnected(MikanDisconnectedEvent disconnectedEvent)
		{
			if (disconnectedEvent != null)
			{
				Log(MikanLogLevel.Info, $"MikanClient: disconnected ({disconnectedEvent.code}: {disconnectedEvent.reason})");
			}

			bool wasInitialized = _clientInitialized;
			_clientInitialized = false;
			_pendingRequests.Clear();

			// The mirrored scene is rebuilt from scratch on the next connection
			_sceneMirror.Clear();
			_dataStore.Clear();

			if (wasInitialized)
			{
				Disconnected?.Invoke();
			}

			_reconnectTimer = _reconnectIntervalSeconds;
		}

		private void HandleLog(MikanLogLevel level, string message)
		{
			Log(level, message);
		}

		public void Log(MikanLogLevel level, string message)
		{
			if (level < _minLogLevel)
			{
				return;
			}

			LogMessage?.Invoke(level, message);

			switch (level)
			{
			case MikanLogLevel.Error:
			case MikanLogLevel.Fatal:
				Debug.LogError("[MikanXR] " + message, this);
				break;
			case MikanLogLevel.Warning:
				Debug.LogWarning("[MikanXR] " + message, this);
				break;
			default:
				Debug.Log("[MikanXR] " + message, this);
				break;
			}
		}
	}
}
