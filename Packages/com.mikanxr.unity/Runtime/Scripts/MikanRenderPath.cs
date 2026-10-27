using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MikanXR.Unity
{
	// What differs between render pipelines in a Mikan camera's frame: when the frame may render,
	// how a Unity camera renders, how the depth pack rides on the color render, and how the
	// unshadowed half of the shadow pair turns shadows off. The built-in pipeline's path is the
	// default. A host on a scriptable pipeline installs its own through MikanClient.SetRenderPath.
	public interface IMikanRenderPath
	{
		// Sets up a Mikan camera's two Unity cameras for the pipeline, and undoes it
		void Attach(MikanCameraObject cameraObject);
		void Detach(MikanCameraObject cameraObject);

		// Renders the camera's frame through MikanCameraObject.RenderNow, at once or later in the
		// same frame when the pipeline only renders from inside its own loop
		void RequestRender(MikanCameraObject cameraObject, long frameIndex);

		void RenderCamera(Camera camera);

		// Packs the camera's depth through the material into the target as part of the camera's
		// render. A null target stops it.
		void SetDepthPack(Camera camera, RenderTexture depthTarget, Material packMaterial);

		// Renders the camera with shadows off and everything else as it was
		void RenderUnshadowed(Camera camera);
	}

	public class MikanBuiltinRenderPath : IMikanRenderPath
	{
		private readonly Dictionary<Camera, CommandBuffer> _depthPacks = new Dictionary<Camera, CommandBuffer>();

		public void Attach(MikanCameraObject cameraObject)
		{
			// The screen-space directional shadows resolve against the shadow camera's depth texture
			cameraObject.ShadowCamera.depthTextureMode = DepthTextureMode.Depth;
		}

		public void Detach(MikanCameraObject cameraObject)
		{
			SetDepthPack(cameraObject.RenderCamera, null, null);
		}

		public void RequestRender(MikanCameraObject cameraObject, long frameIndex)
		{
			cameraObject.RenderNow(frameIndex);
		}

		public void RenderCamera(Camera camera)
		{
			camera.Render();
		}

		public void SetDepthPack(Camera camera, RenderTexture depthTarget, Material packMaterial)
		{
			if (_depthPacks.TryGetValue(camera, out CommandBuffer commands))
			{
				if (camera != null)
				{
					camera.RemoveCommandBuffer(CameraEvent.AfterEverything, commands);
				}
				commands.Release();
				_depthPacks.Remove(camera);
			}

			if (camera == null)
			{
				return;
			}

			// The pack runs inside the camera's render, where the camera depth texture is the one
			// this camera produced, and that texture is only rendered while it is wanted
			camera.depthTextureMode = depthTarget != null ? DepthTextureMode.Depth : DepthTextureMode.None;
			if (depthTarget == null)
			{
				return;
			}

			commands = new CommandBuffer() { name = "MikanXR pack depth" };
			commands.Blit(BuiltinRenderTextureType.None, depthTarget, packMaterial);
			camera.AddCommandBuffer(CameraEvent.AfterEverything, commands);
			_depthPacks[camera] = commands;
		}

		public void RenderUnshadowed(Camera camera)
		{
			// The one global switch, restored at once
			ShadowQuality shadowQuality = QualitySettings.shadows;
			QualitySettings.shadows = ShadowQuality.Disable;
			camera.Render();
			QualitySettings.shadows = shadowQuality;
		}
	}
}
