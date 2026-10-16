using UnityEngine;
using UnityEngine.Rendering;

namespace MikanXR.Unity
{
	// A Mikan camera: this object's transform is the camera's stage-relative pose, so it sits
	// under its stage object and the stage's placement in the host scene carries it. Two disabled
	// Unity cameras on children render on demand with the projection the editor's intrinsics
	// describe: the color camera into the color texture (with the depth pack riding on its
	// command buffer), and the shadow camera into the shadow texture. The Spout senders capture
	// all three.
	//
	// The shadow pass is the Unreal plugin's: the scene renders twice into white-cleared targets,
	// with shadows and without, and the shadow texture is the ratio. The stencils carry the
	// catcher material for those renders, which writes light survival and exactly 1 without
	// shadows, so they come through as their shadow while everything else, rendered the same
	// way both times, divides out to white. The host's own materials are never touched.
	//
	// Pacing follows the editor's frame sync mode: a video-frame synced camera renders once per
	// MikanCameraNewFrameEvent and publishes that frame's index, a free-running one renders on
	// its own clock, capped at the video rate, while the editor's compositor runs for it.
	[DefaultExecutionOrder(900)]
	public class MikanCameraObject : MikanTransformObject
	{
		private const float k_defaultFreeRunningRate = 30f;
		private const long k_freeRunningFrameIndex = -1;

		private Camera _camera;
		private Camera _shadowCamera;
		private MikanCameraVisualizer _visualizer;
		private RenderTexture _colorTexture;
		private RenderTexture _depthTexture;
		private RenderTexture _shadowTexture;
		private RenderTexture _shadowedTexture;
		private RenderTexture _unshadowedTexture;
		private Material _packDepthMaterial;
		private Material _shadowDivideMaterial;
		private CommandBuffer _packDepthCommands;

		// The last camera state the editor sent
		private bool _hasState = false;
		private Vector2Int _pixelSize = Vector2Int.zero;
		private Vector2Int _auxPixelSize = Vector2Int.zero;
		private Vector2 _zBounds = new Vector2(0.1f, 20f);

		private bool _compositorRunning = false;
		private bool _allocated = false;
		private bool _allocationInFlight = false;

		private bool _frameDirty = false;
		private long _pendingFrameIndex = k_freeRunningFrameIndex;
		private float _freeRunningInterval = 1f / k_defaultFreeRunningRate;
		private float _lastRenderTime = -1f;
		private long _renderCount = 0;

		public MikanCameraComponentValues CameraValues => TransformValues as MikanCameraComponentValues;
		public Camera RenderCamera => _camera;
		public Camera ShadowCamera => _shadowCamera;
		public RenderTexture ColorTexture => _colorTexture;
		public RenderTexture DepthTexture => _depthTexture;
		public RenderTexture ShadowTexture => _shadowTexture;
		// The shadowed render the shadow texture is divided from, for diagnostics
		public RenderTexture ShadowedTexture => _shadowedTexture;
		public bool IsAllocated => _allocated;
		public bool CompositorRunning => _compositorRunning;
		public long RenderCount => _renderCount;
		public Vector2Int PixelSize => _pixelSize;

		// A camera lives under its stage rather than its transform parent
		public override int DesiredParentId => CameraValues != null && CameraValues.stage_id != (int)MikanConstants.InvalidMikanID
			? CameraValues.stage_id
			: base.DesiredParentId;

		public override bool IsParentField(string fieldName)
		{
			return fieldName == "stage_id" || base.IsParentField(fieldName);
		}

		public void SetFrustumVisible(bool visible)
		{
			_visualizer?.SetVisible(visible);
		}

		public bool IsVideoFrameSynced
		{
			get
			{
				if (CameraValues == null)
				{
					return true;
				}

				switch (CameraValues.frame_sync_mode)
				{
				case MikanCameraFrameSyncMode.VideoFrame:
					return true;
				case MikanCameraFrameSyncMode.FreeRunning:
					return false;
				default:
					return CameraValues.pose_driven_per_frame;
				}
			}
		}

		// The shadow buffer exists while stencils render as holdouts and the divide shader
		// resolved; without stencils there is nothing to catch a shadow
		private bool WantsShadowBuffer => Client.RenderStencils && _shadowDivideMaterial != null;

		public override void Bind(MikanClient client, string systemName, int componentId, MikanComponentValues values)
		{
			base.Bind(client, systemName, componentId, values);

			_camera = CreateRenderCamera("Render Camera", new Color(0f, 0f, 0f, 0f));
			_camera.depthTextureMode = DepthTextureMode.Depth;

			_shadowCamera = CreateRenderCamera("Shadow Camera", Color.white);
			_shadowCamera.depthTextureMode = DepthTextureMode.Depth;
			_shadowDivideMaterial = MikanShaderLibrary.CreateMaterial(MikanShaderLibrary.ShadowDivide);
			if (_shadowDivideMaterial == null)
			{
				client.Log(MikanLogLevel.Warning, "MikanCameraObject: shadow divide shader not found, publishing no shadow buffer");
			}

			var visualizerObject = new GameObject("Frustum");
			visualizerObject.transform.SetParent(transform, false);
			_visualizer = visualizerObject.AddComponent<MikanCameraVisualizer>();
			_visualizer.Initialize(client.VisualizationLayer);
			_visualizer.SetVisible(client.ShowCameraFrustums);

			_packDepthMaterial = MikanShaderLibrary.CreateMaterial(MikanShaderLibrary.PackDepthRGBA);

			// A camera bound while the compositor already runs hears no event until something
			// changes, so its state is pulled once
			client.SendRequest(new GetCameraProperties() { camera_id = componentId }, response =>
			{
				if (this == null || !IsBound)
				{
					return;
				}

				if (response is MikanCameraPropertiesResponse properties && response.resultCode == MikanAPIResult.Success)
				{
					ApplyState(properties.camera_position, properties.camera_forward, properties.camera_up,
						properties.pixel_size, properties.aux_pixel_size, properties.focal_length,
						properties.principal_point, properties.z_bounds);
					_compositorRunning = properties.compositor_running;
				}
			});

			RequestVideoSourceRate();
		}

		private Camera CreateRenderCamera(string objectName, Color clearColor)
		{
			var cameraObject = new GameObject(objectName);
			cameraObject.transform.SetParent(transform, false);

			Camera camera = cameraObject.AddComponent<Camera>();
			camera.enabled = false;
			camera.clearFlags = CameraClearFlags.SolidColor;
			camera.backgroundColor = clearColor;
			camera.cullingMask = Client.RenderCullingMask;
			camera.allowHDR = false;
			camera.allowMSAA = false;
			camera.useOcclusionCulling = false;
			camera.stereoTargetEye = StereoTargetEyeMask.None;
			camera.forceIntoRenderTexture = true;

			return camera;
		}

		public override void Unbind()
		{
			if (IsBound && _allocated)
			{
				Client.SendRequest(new FreeCameraRenderTargetTextures() { camera_id = ComponentId });
			}

			_allocated = false;
			ReleaseTextures();

			if (_packDepthMaterial != null)
			{
				Destroy(_packDepthMaterial);
				_packDepthMaterial = null;
			}

			if (_shadowDivideMaterial != null)
			{
				Destroy(_shadowDivideMaterial);
				_shadowDivideMaterial = null;
			}

			base.Unbind();
		}

		public override void OnValuesChanged(string fieldName)
		{
			base.OnValuesChanged(fieldName);

			if (fieldName == "video_source_id")
			{
				RequestVideoSourceRate();
			}
		}

		// The camera pose comes from the camera events, not the component's transform values
		public override void ApplyTransform()
		{
		}

		public void HandleCameraEvent(MikanEvent mikanEvent)
		{
			switch (mikanEvent)
			{
			case MikanCameraNewFrameEvent frame:
				ApplyState(frame.camera_position, frame.camera_forward, frame.camera_up, frame.pixel_size,
					frame.aux_pixel_size, frame.focal_length, frame.principal_point, frame.z_bounds);
				_pendingFrameIndex = frame.frame;
				_frameDirty = true;
				break;
			case MikanCameraNewPropertiesEvent properties:
				ApplyState(properties.camera_position, properties.camera_forward, properties.camera_up,
					properties.pixel_size, properties.aux_pixel_size, properties.focal_length,
					properties.principal_point, properties.z_bounds);
				break;
			case MikanCompositorStartedEvent _:
				_compositorRunning = true;
				break;
			case MikanCompositorStoppedEvent _:
				_compositorRunning = false;
				break;
			}
		}

		private void ApplyState(
			MikanVector3f position, MikanVector3f forward, MikanVector3f up,
			MikanVector2i pixelSize, MikanVector2i auxPixelSize,
			MikanVector2d focalLength, MikanVector2d principalPoint, MikanVector2d zBounds)
		{
			if (position == null || forward == null || up == null || pixelSize == null || focalLength == null ||
				principalPoint == null || zBounds == null)
			{
				return;
			}

			transform.localPosition = MikanMath.ToUnityPosition(position);
			transform.localRotation = MikanMath.ToUnityCameraRotation(forward, up);

			_pixelSize = new Vector2Int(pixelSize.x, pixelSize.y);
			_auxPixelSize = auxPixelSize != null && auxPixelSize.x > 0 && auxPixelSize.y > 0
				? new Vector2Int(auxPixelSize.x, auxPixelSize.y)
				: _pixelSize;
			_zBounds = new Vector2((float)zBounds.x, (float)zBounds.y);

			if (_pixelSize.x > 0 && _pixelSize.y > 0 && focalLength.x > 0 && focalLength.y > 0)
			{
				Matrix4x4 projection = MikanMath.ProjectionFromIntrinsics(
					_pixelSize.x, _pixelSize.y, focalLength.x, focalLength.y,
					principalPoint.x, principalPoint.y, _zBounds.x, _zBounds.y);

				ApplyProjection(_camera, projection);
				ApplyProjection(_shadowCamera, projection);

				_visualizer.Rebuild(_pixelSize.x, _pixelSize.y, focalLength.x, focalLength.y,
					principalPoint.x, principalPoint.y, _zBounds.x, _zBounds.y);

				_hasState = true;
			}
		}

		private void ApplyProjection(Camera camera, Matrix4x4 projection)
		{
			camera.nearClipPlane = _zBounds.x;
			camera.farClipPlane = _zBounds.y;
			camera.projectionMatrix = projection;
		}

		private void RequestVideoSourceRate()
		{
			if (CameraValues == null || CameraValues.video_source_id == (int)MikanConstants.InvalidMikanID)
			{
				return;
			}

			Client.SendRequest(new GetVideoSourceMode() { video_source_id = CameraValues.video_source_id }, response =>
			{
				if (this != null && response is MikanVideoSourceModeResponse mode && mode.frame_rate > 0f)
				{
					_freeRunningInterval = 1f / mode.frame_rate;
				}
			});
		}

		private void LateUpdate()
		{
			if (!IsBound || !_hasState || !Client.IsClientInitialized)
			{
				return;
			}

			_camera.cullingMask = Client.RenderCullingMask;
			_shadowCamera.cullingMask = Client.RenderCullingMask;

			if (IsVideoFrameSynced)
			{
				if (_frameDirty)
				{
					_frameDirty = false;
					RenderFrame(_pendingFrameIndex);
				}
			}
			else if (_compositorRunning && Time.unscaledTime - _lastRenderTime >= _freeRunningInterval)
			{
				RenderFrame(k_freeRunningFrameIndex);
			}
		}

		private void RenderFrame(long frameIndex)
		{
			EnsureTextures();

			if (!_allocated)
			{
				TryAllocate();
				return;
			}

			if (_packDepthMaterial != null)
			{
				_packDepthMaterial.SetFloat("_MikanZNear", _zBounds.x);
				_packDepthMaterial.SetFloat("_MikanZFar", _zBounds.y);
			}

			// The editor's cull rule is per camera, so each stencil is shown or hidden for this one,
			// and the stencils carry the holdout material for the color render
			if (Client.RenderStencils)
			{
				Vector3 cameraPosition = transform.position;
				Vector3 cameraForward = transform.forward;
				foreach (MikanStencilObject stencil in Client.SceneMirror.FindAll<MikanStencilObject>())
				{
					stencil.SetVisibleForCamera(stencil.IsFacingCamera(cameraPosition, cameraForward));
					stencil.SetPass(MikanStencilPass.Holdout);
				}
			}

			_camera.Render();

			if (_shadowTexture != null)
			{
				RenderShadowPass();
			}

			_lastRenderTime = Time.unscaledTime;
			++_renderCount;

			Client.FramePublisher.QueuePublish(ComponentId, frameIndex);
		}

		private void RenderShadowPass()
		{
			foreach (MikanStencilObject stencil in Client.SceneMirror.FindAll<MikanStencilObject>())
			{
				stencil.SetPass(MikanStencilPass.ShadowCatcher);
			}

			_shadowCamera.targetTexture = _shadowedTexture;
			_shadowCamera.Render();

			// The same frame without shadows, through the one global switch, restored at once
			ShadowQuality shadowQuality = QualitySettings.shadows;
			QualitySettings.shadows = ShadowQuality.Disable;
			_shadowCamera.targetTexture = _unshadowedTexture;
			_shadowCamera.Render();
			QualitySettings.shadows = shadowQuality;

			_shadowDivideMaterial.SetTexture("_MikanReferenceTex", _unshadowedTexture);
			Graphics.Blit(_shadowedTexture, _shadowTexture, _shadowDivideMaterial);
		}

		// The textures follow the sizes the editor asks for and whether a shadow buffer is wanted;
		// a change frees the senders so the next frame allocates them again
		private void EnsureTextures()
		{
			bool wantsShadow = WantsShadowBuffer;
			bool changed = _colorTexture == null ||
				_colorTexture.width != _pixelSize.x || _colorTexture.height != _pixelSize.y ||
				_depthTexture == null ||
				_depthTexture.width != _auxPixelSize.x || _depthTexture.height != _auxPixelSize.y ||
				(_shadowTexture != null) != wantsShadow;
			if (!changed)
			{
				return;
			}

			if (_allocated)
			{
				Client.SendRequest(new FreeCameraRenderTargetTextures() { camera_id = ComponentId });
				_allocated = false;
			}

			ReleaseTextures();

			_colorTexture = CreateTexture($"Mikan camera {ComponentId} color", _pixelSize, 24);
			_depthTexture = CreateTexture($"Mikan camera {ComponentId} depth", _auxPixelSize, 0);
			if (wantsShadow)
			{
				_shadowTexture = CreateTexture($"Mikan camera {ComponentId} shadow", _auxPixelSize, 0);
				_shadowedTexture = CreateTexture($"Mikan camera {ComponentId} shadowed", _auxPixelSize, 24);
				_unshadowedTexture = CreateTexture($"Mikan camera {ComponentId} unshadowed", _auxPixelSize, 24);
			}

			_camera.targetTexture = _colorTexture;

			// The depth pack runs inside the camera's render, where the camera depth texture is
			// the one this camera produced. Without the pack shader the camera publishes color only.
			if (_packDepthMaterial != null)
			{
				_packDepthCommands = new CommandBuffer() { name = "MikanXR pack depth" };
				_packDepthCommands.Blit(BuiltinRenderTextureType.None, _depthTexture, _packDepthMaterial);
				_camera.AddCommandBuffer(CameraEvent.AfterEverything, _packDepthCommands);
			}

			Client.RenderTargetWriter.SetTextures(
				ComponentId, _colorTexture, _packDepthMaterial != null ? _depthTexture : null, _shadowTexture);
		}

		private static RenderTexture CreateTexture(string textureName, Vector2Int size, int depthBits)
		{
			var texture = new RenderTexture(size.x, size.y, depthBits, RenderTextureFormat.ARGB32)
			{
				name = textureName,
				antiAliasing = 1,
				useMipMap = false,
				hideFlags = HideFlags.DontSave
			};
			texture.Create();

			return texture;
		}

		private void ReleaseTextures()
		{
			if (_camera != null)
			{
				if (_packDepthCommands != null)
				{
					_camera.RemoveCommandBuffer(CameraEvent.AfterEverything, _packDepthCommands);
				}

				_camera.targetTexture = null;
			}

			if (_shadowCamera != null)
			{
				_shadowCamera.targetTexture = null;
			}

			_packDepthCommands?.Release();
			_packDepthCommands = null;

			ReleaseTexture(ref _colorTexture);
			ReleaseTexture(ref _depthTexture);
			ReleaseTexture(ref _shadowTexture);
			ReleaseTexture(ref _shadowedTexture);
			ReleaseTexture(ref _unshadowedTexture);

			if (IsBound)
			{
				Client.RenderTargetWriter.FreeRenderTargetTextures(ComponentId);
			}
		}

		private static void ReleaseTexture(ref RenderTexture texture)
		{
			if (texture != null)
			{
				texture.Release();
				Destroy(texture);
				texture = null;
			}
		}

		private void TryAllocate()
		{
			if (_allocationInFlight)
			{
				return;
			}

			_allocationInFlight = true;
			Vector2Int requestedSize = _pixelSize;
			Vector2Int requestedAuxSize = _auxPixelSize;
			bool withShadow = _shadowTexture != null;

			var request = new AllocateCameraRenderTargetTextures()
			{
				camera_id = ComponentId,
				descriptor = new MikanRenderTargetDescriptor()
				{
					color_buffer_type = MikanColorBufferType.RGBA32,
					depth_buffer_type = _packDepthMaterial != null ? MikanDepthBufferType.PACK_DEPTH_RGBA : MikanDepthBufferType.NODEPTH,
					shadow_buffer_type = withShadow ? MikanShadowBufferType.RGBA32 : MikanShadowBufferType.NOSHADOW,
					width = (uint)requestedSize.x,
					height = (uint)requestedSize.y,
					aux_width = (uint)requestedAuxSize.x,
					aux_height = (uint)requestedAuxSize.y,
					graphicsAPI = MikanSpoutRenderTargetWriter.CurrentGraphicsApi()
				}
			};

			Client.SendRequest(request, response =>
			{
				_allocationInFlight = false;

				if (this == null || !IsBound)
				{
					return;
				}

				if (response.resultCode != MikanAPIResult.Success)
				{
					Client.Log(MikanLogLevel.Warning,
						$"MikanCameraObject: allocating render targets for camera {ComponentId} failed ({response.resultCode})");
					return;
				}

				_allocated = true;
				Client.Log(MikanLogLevel.Info,
					$"MikanCameraObject: camera {ComponentId} render targets allocated at {requestedSize.x}x{requestedSize.y} (aux {requestedAuxSize.x}x{requestedAuxSize.y}, shadow: {withShadow})");
			});
		}
	}
}
