using System;
using System.Collections.Generic;
using Klak.Spout;
using UnityEngine;

namespace MikanXR.Unity
{
	// The client side of the shared texture seam over KlakSpout. One SpoutSender per buffer
	// per camera, named as the editor expects, each capturing the render texture the camera
	// object registered for it. The senders capture at the end of every frame on their own,
	// so a write request only confirms the sender exists; the frame publisher is what pairs a
	// frame index with what the senders carried.
	//
	// A sender blits its source into a buffer it creates with the default read/write mode,
	// which a linear color space project makes sRGB. Color wants that encoding, but the packed
	// depth and the shadow ratio are raw bytes the editor reads as they are, so in a linear
	// project those two go out through sRGB staging textures written by SrgbRawBytes: the
	// staging write and the sender's blit each decode and re-encode, which leaves the bytes
	// unchanged.
	public class MikanSpoutRenderTargetWriter : IMikanRenderTargetWriter
	{
		private const string k_resourcesAssetPath = "MikanXR/MikanSpoutResources";
		private const string k_blitShaderName = "Hidden/Klak/Spout/Blit";

		private class CameraTextures
		{
			public Texture color;
			public Texture depth;
			public Texture shadow;
			public RenderTexture depthStaging;
			public RenderTexture shadowStaging;

			public Texture DepthSource => depthStaging != null ? depthStaging : depth;
			public Texture ShadowSource => shadowStaging != null ? shadowStaging : shadow;
		}

		private class CameraSenders
		{
			public GameObject root;
			public SpoutSender color;
			public SpoutSender depth;
			public SpoutSender shadow;
		}

		private readonly MikanClient _client;
		private readonly Dictionary<int, CameraTextures> _textures = new Dictionary<int, CameraTextures>();
		private readonly Dictionary<int, CameraSenders> _senders = new Dictionary<int, CameraSenders>();
		private SpoutResources _resources;
		private Material _rawBytesMaterial;

		public MikanSpoutRenderTargetWriter(MikanClient client)
		{
			_client = client;
		}

		public static MikanClientGraphicsApi CurrentGraphicsApi()
		{
			switch (SystemInfo.graphicsDeviceType)
			{
			case UnityEngine.Rendering.GraphicsDeviceType.Direct3D11:
				return MikanClientGraphicsApi.Direct3D11;
			case UnityEngine.Rendering.GraphicsDeviceType.Direct3D12:
				return MikanClientGraphicsApi.Direct3D12;
			case UnityEngine.Rendering.GraphicsDeviceType.OpenGLCore:
				return MikanClientGraphicsApi.OpenGL;
			case UnityEngine.Rendering.GraphicsDeviceType.Vulkan:
				return MikanClientGraphicsApi.Vulkan;
			default:
				return MikanClientGraphicsApi.UNKNOWN;
			}
		}

		public static string SenderName(string clientId, int cameraId, string bufferName)
		{
			return $"{clientId}_camera{cameraId}_{bufferName}";
		}

		// The textures the camera's senders capture. Set before allocating, and again whenever
		// the camera recreates them.
		public void SetTextures(int cameraId, Texture color, Texture depth, Texture shadow)
		{
			if (!_textures.TryGetValue(cameraId, out CameraTextures textures))
			{
				textures = new CameraTextures();
				_textures[cameraId] = textures;
			}

			textures.color = color;
			textures.depth = depth;
			textures.shadow = shadow;
			UpdateStaging(ref textures.depthStaging, depth, $"Mikan camera {cameraId} depth staging");
			UpdateStaging(ref textures.shadowStaging, shadow, $"Mikan camera {cameraId} shadow staging");

			if (_senders.TryGetValue(cameraId, out CameraSenders senders))
			{
				if (senders.color != null) senders.color.sourceTexture = color;
				if (senders.depth != null) senders.depth.sourceTexture = textures.DepthSource;
				if (senders.shadow != null) senders.shadow.sourceTexture = textures.ShadowSource;
			}
		}

		// Copies a camera's depth and shadow into their staging textures. Called once the camera's
		// render has filled them, before the senders capture at the end of the frame.
		public void StageRawBuffers(int cameraId)
		{
			if (!_textures.TryGetValue(cameraId, out CameraTextures textures))
			{
				return;
			}

			if (textures.depthStaging != null)
			{
				Graphics.Blit(textures.depth, textures.depthStaging, _rawBytesMaterial);
			}
			if (textures.shadowStaging != null)
			{
				Graphics.Blit(textures.shadow, textures.shadowStaging, _rawBytesMaterial);
			}
		}

		public MikanAPIResult AllocateRenderTargetTextures(int cameraId, ref MikanRenderTargetDescriptor descriptor)
		{
			if (!_textures.TryGetValue(cameraId, out CameraTextures textures) || textures.color == null)
			{
				_client.Log(MikanLogLevel.Error, $"MikanSpoutRenderTargetWriter: no color texture registered for camera {cameraId}");
				return MikanAPIResult.InvalidParam;
			}

			SpoutResources resources = GetResources();
			if (resources == null)
			{
				_client.Log(MikanLogLevel.Error, "MikanSpoutRenderTargetWriter: KlakSpout resources not found");
				return MikanAPIResult.RequestFailed;
			}

			FreeRenderTargetTextures(cameraId);

			// KlakSpout sends R8G8B8A8 only, and depth from a managed client is the packed
			// RGBA encoding, so the editor is told those whatever was asked for
			descriptor.color_buffer_type = MikanColorBufferType.RGBA32;
			descriptor.depth_buffer_type = descriptor.depth_buffer_type != MikanDepthBufferType.NODEPTH && textures.depth != null
				? MikanDepthBufferType.PACK_DEPTH_RGBA
				: MikanDepthBufferType.NODEPTH;
			descriptor.shadow_buffer_type = descriptor.shadow_buffer_type != MikanShadowBufferType.NOSHADOW && textures.shadow != null
				? MikanShadowBufferType.RGBA32
				: MikanShadowBufferType.NOSHADOW;
			descriptor.graphicsAPI = CurrentGraphicsApi();

			string clientId = _client.API.GetClientUniqueID();
			var senders = new CameraSenders();
			senders.root = new GameObject($"MikanSpout camera{cameraId}");
			senders.root.transform.SetParent(_client.transform, false);

			senders.color = CreateSender(senders.root, resources, SenderName(clientId, cameraId, "color"), textures.color);
			if (descriptor.depth_buffer_type != MikanDepthBufferType.NODEPTH)
			{
				senders.depth = CreateSender(senders.root, resources, SenderName(clientId, cameraId, "depth"), textures.DepthSource);
			}
			if (descriptor.shadow_buffer_type != MikanShadowBufferType.NOSHADOW)
			{
				senders.shadow = CreateSender(senders.root, resources, SenderName(clientId, cameraId, "shadow"), textures.ShadowSource);
			}

			_senders[cameraId] = senders;

			return MikanAPIResult.Success;
		}

		public MikanAPIResult FreeRenderTargetTextures(int cameraId)
		{
			if (_senders.TryGetValue(cameraId, out CameraSenders senders))
			{
				_senders.Remove(cameraId);
				if (senders.root != null)
				{
					UnityEngine.Object.Destroy(senders.root);
				}
			}

			return MikanAPIResult.Success;
		}

		public MikanAPIResult WriteColorRenderTargetTexture(int cameraId, IntPtr apiTexturePtr)
		{
			return HasSender(cameraId, senders => senders.color) ? MikanAPIResult.Success : MikanAPIResult.RequestFailed;
		}

		public MikanAPIResult WriteDepthRenderTargetTexture(int cameraId, IntPtr apiTexturePtr, float zNear, float zFar)
		{
			return HasSender(cameraId, senders => senders.depth) ? MikanAPIResult.Success : MikanAPIResult.RequestFailed;
		}

		public MikanAPIResult WriteShadowRenderTargetTexture(int cameraId, IntPtr apiTexturePtr)
		{
			return HasSender(cameraId, senders => senders.shadow) ? MikanAPIResult.Success : MikanAPIResult.RequestFailed;
		}

		public bool IsAllocated(int cameraId)
		{
			return _senders.ContainsKey(cameraId);
		}

		public void FreeAll()
		{
			var cameraIds = new List<int>(_senders.Keys);
			foreach (int cameraId in cameraIds)
			{
				FreeRenderTargetTextures(cameraId);
			}
		}

		private bool HasSender(int cameraId, Func<CameraSenders, SpoutSender> select)
		{
			return _senders.TryGetValue(cameraId, out CameraSenders senders) && select(senders) != null;
		}

		private static SpoutSender CreateSender(GameObject root, SpoutResources resources, string senderName, Texture texture)
		{
			var holder = new GameObject(senderName);
			holder.transform.SetParent(root.transform, false);

			// Configured before it is enabled, so its first end-of-frame capture already sees
			// the texture and the name
			holder.SetActive(false);
			var sender = holder.AddComponent<SpoutSender>();
			sender.SetResources(resources);
			sender.captureMethod = CaptureMethod.Texture;
			sender.keepAlpha = true;
			sender.sourceTexture = texture;
			sender.spoutName = senderName;
			holder.SetActive(true);

			return sender;
		}

		// A staging texture exists while its source does, the sender buffers are sRGB, and the
		// shader resolved. Without the shader the raw texture is sent as before, encoded.
		private void UpdateStaging(ref RenderTexture staging, Texture source, string textureName)
		{
			bool wanted = source != null && QualitySettings.activeColorSpace == ColorSpace.Linear
				&& GetRawBytesMaterial() != null;
			if (staging != null && (!wanted || staging.width != source.width || staging.height != source.height))
			{
				staging.Release();
				UnityEngine.Object.Destroy(staging);
				staging = null;
			}

			if (wanted && staging == null)
			{
				staging = new RenderTexture(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
				{
					name = textureName,
					antiAliasing = 1,
					useMipMap = false,
					filterMode = FilterMode.Point,
					hideFlags = HideFlags.DontSave
				};
				staging.Create();
			}
		}

		private Material GetRawBytesMaterial()
		{
			if (_rawBytesMaterial == null)
			{
				_rawBytesMaterial = MikanShaderLibrary.CreateMaterial(MikanShaderLibrary.SrgbRawBytes);
			}

			return _rawBytesMaterial;
		}

		// The package ships a SpoutResources asset in a Resources folder so a host that loads the
		// client as a plugin finds it in its bundle. Failing that, one already loaded by the host
		// serves, and failing that one is built from the blit shader if the build kept it.
		private SpoutResources GetResources()
		{
			if (_resources != null)
			{
				return _resources;
			}

			_resources = MikanShaderLibrary.LoadAsset<SpoutResources>(k_resourcesAssetPath);
			if (_resources != null && _resources.blitShader != null)
			{
				return _resources;
			}

			foreach (SpoutResources candidate in Resources.FindObjectsOfTypeAll<SpoutResources>())
			{
				if (candidate.blitShader != null)
				{
					_resources = candidate;
					return _resources;
				}
			}

			Shader blitShader = MikanShaderLibrary.Find(k_blitShaderName);
			if (blitShader != null)
			{
				_resources = ScriptableObject.CreateInstance<SpoutResources>();
				_resources.hideFlags = HideFlags.DontSave;
				_resources.blitShader = blitShader;
				return _resources;
			}

			_resources = null;
			return null;
		}
	}
}
