using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MikanXR.Unity.Tests
{
	// Runs against a live MikanXR editor on the default port with a project loaded. The tests
	// are skipped, not failed, when no editor answers, so the suite is safe to run anywhere; with
	// an editor up they check the mirror end to end: connection, the component mirror, and, when
	// the editor's compositor runs a camera, allocation, rendering and publishing.
	public class MikanLiveEditorTests
	{
		private const float k_connectTimeoutSeconds = 10f;
		private const float k_mirrorSettleSeconds = 3f;
		private const float k_renderTimeoutSeconds = 15f;

		private GameObject _root;
		private MikanClient _client;
		private readonly System.Collections.Generic.List<GameObject> _props = new System.Collections.Generic.List<GameObject>();

		[UnitySetUp]
		public IEnumerator SetUp()
		{
			_root = new GameObject("MikanClient (test)");
			_root.SetActive(false);
			_client = _root.AddComponent<MikanClient>();
			_root.SetActive(true);

			float deadline = Time.realtimeSinceStartup + k_connectTimeoutSeconds;
			while (!_client.IsClientInitialized && Time.realtimeSinceStartup < deadline)
			{
				yield return null;
			}

			if (!_client.IsClientInitialized)
			{
				Object.Destroy(_root);
				_root = null;
				Assert.Ignore("No MikanXR editor answered on the default port");
			}
		}

		[UnityTearDown]
		public IEnumerator TearDown()
		{
			if (_root != null)
			{
				Object.Destroy(_root);
				_root = null;
			}

			foreach (GameObject prop in _props)
			{
				Object.Destroy(prop);
			}
			_props.Clear();

			yield return null;
		}

		// The test runner's scene is empty, so the buffer test furnishes it: a shadow casting
		// light and a cube at the origin, where the editor's stencils and camera are aimed
		private void SpawnTestProps()
		{
			var lightObject = new GameObject("Test Light");
			Light light = lightObject.AddComponent<Light>();
			light.type = LightType.Directional;
			light.shadows = LightShadows.Soft;
			light.intensity = 1f;
			// Nearly straight down, so the caster's shadow lands on the catcher right under it
			lightObject.transform.rotation = Quaternion.Euler(80f, 0f, 0f);
			_props.Add(lightObject);

			GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
			cube.name = "Test Caster";
			// Floating above the catcher, so its shadow is not hidden under it
			cube.transform.position = new Vector3(0f, 0.5f, 0f);
			cube.transform.localScale = Vector3.one * 0.3f;
			_props.Add(cube);

			// A catcher of the test's own under the cube, on the stencil layer, so the shadow pass
			// is checked without depending on where the editor's stencils happen to sit
			GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Quad);
			floor.name = "Test Catcher";
			floor.transform.position = Vector3.zero;
			floor.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
			floor.transform.localScale = Vector3.one * 2f;
			Material catcher = MikanShaderLibrary.CreateMaterial(MikanShaderLibrary.ShadowCatcher);
			if (catcher != null)
			{
				catcher.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
				floor.GetComponent<MeshRenderer>().sharedMaterial = catcher;
			}
			floor.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			_props.Add(floor);
		}

		[UnityTest]
		public IEnumerator MirrorsTheEditorsComponents()
		{
			yield return new WaitForSecondsRealtime(k_mirrorSettleSeconds);

			int objectCount = _client.SceneMirror.Objects.Count;
			int cameraCount = _client.DataStore.GetComponents(MikanSystemNames.CameraSystem).Count;
			int stageCount = _client.DataStore.GetComponents(MikanSystemNames.StageSystem).Count;

			Debug.Log($"[MikanXR test] mirrored {objectCount} objects: {cameraCount} cameras, {stageCount} stages");

			Assert.That(objectCount, Is.GreaterThan(0), "no components were mirrored from the editor");
			Assert.That(objectCount, Is.EqualTo(CountCachedComponents()), "every cached component has an object");

			foreach (MikanTransformObject mikanObject in _client.SceneMirror.Objects.Values)
			{
				Assert.That(mikanObject.transform.IsChildOf(_root.transform), $"{mikanObject.name} is not under the client root");
			}

			foreach (MikanCameraObject camera in _client.SceneMirror.FindAll<MikanCameraObject>())
			{
				int stageId = camera.CameraValues.stage_id;
				if (stageId != (int)MikanConstants.InvalidMikanID && _client.SceneMirror.Find(stageId) != null)
				{
					Assert.That(camera.transform.parent, Is.EqualTo(_client.SceneMirror.Find(stageId).transform),
						$"camera {camera.ComponentId} is not under its stage");
				}
			}
		}

		[UnityTest]
		public IEnumerator ModelStencilsCarryGeometry()
		{
			yield return new WaitForSecondsRealtime(k_mirrorSettleSeconds);

			int modelStencils = 0;
			foreach (MikanModelStencilObject stencil in _client.SceneMirror.FindAll<MikanModelStencilObject>())
			{
				++modelStencils;
				MeshFilter meshFilter = stencil.GetComponent<MeshFilter>();
				Assert.That(meshFilter != null && meshFilter.sharedMesh != null, $"stencil {stencil.ComponentId} has no mesh");
				Assert.That(meshFilter.sharedMesh.vertexCount, Is.GreaterThan(0), $"stencil {stencil.ComponentId} mesh is empty");
				Debug.Log($"[MikanXR test] model stencil {stencil.ComponentId}: {meshFilter.sharedMesh.vertexCount} vertices");
			}

			if (modelStencils == 0)
			{
				Assert.Ignore("The editor's project has no model stencil");
			}
		}

		[UnityTest]
		public IEnumerator RendersAndPublishesWhileTheCompositorRuns()
		{
			yield return new WaitForSecondsRealtime(k_mirrorSettleSeconds);

			MikanCameraObject activeCamera = null;
			float deadline = Time.realtimeSinceStartup + k_renderTimeoutSeconds;
			while (activeCamera == null && Time.realtimeSinceStartup < deadline)
			{
				foreach (MikanCameraObject camera in _client.SceneMirror.FindAll<MikanCameraObject>())
				{
					if (camera.RenderCount > 0)
					{
						activeCamera = camera;
						break;
					}
				}

				yield return null;
			}

			if (activeCamera == null)
			{
				Assert.Ignore("No camera rendered: the editor's compositor is not running a camera this client can serve");
			}

			Assert.That(activeCamera.IsAllocated, "the rendering camera never allocated its render targets");
			Assert.That(activeCamera.ColorTexture, Is.Not.Null);
			Assert.That(activeCamera.ColorTexture.width, Is.EqualTo(activeCamera.PixelSize.x));
			if (_client.RenderDepth)
			{
				Assert.That(activeCamera.DepthTexture, Is.Not.Null, "depth is on but no packed depth texture was created");
			}
			else
			{
				Assert.That(activeCamera.DepthTexture, Is.Null, "depth is off but a depth texture was created");
			}
			if (_client.RenderStencils && _client.RenderShadows)
			{
				Assert.That(activeCamera.ShadowTexture, Is.Not.Null, "stencils render but no shadow texture was created");
			}

			long publishesBefore = _client.FramePublisher.PublishCount;
			yield return new WaitForSecondsRealtime(2f);
			Debug.Log($"[MikanXR test] camera {activeCamera.ComponentId} rendered {activeCamera.RenderCount} frames, published {_client.FramePublisher.PublishCount}");
			Assert.That(_client.FramePublisher.PublishCount, Is.GreaterThan(publishesBefore), "no frames were published");
		}

		// Reads the rendered buffers back: with a stencil in the editor's scene and a shadow casting
		// light in the Unity scene, the shadow buffer must hold shadowed pixels, and the color buffer
		// must hold both covered and clear pixels. Skipped when the editor's scene has no stencil.
		[UnityTest]
		public IEnumerator ShadowAndColorBuffersCarryTheStencilPasses()
		{
			SpawnTestProps();
			yield return new WaitForSecondsRealtime(k_mirrorSettleSeconds);

			int stencilCount = 0;
			foreach (MikanStencilObject stencil in _client.SceneMirror.FindAll<MikanStencilObject>())
			{
				++stencilCount;
			}
			Debug.Log($"[MikanXR test] {stencilCount} stencils mirrored, render stencils: {_client.RenderStencils}");

			if (stencilCount == 0 || !_client.RenderStencils || !_client.RenderShadows)
			{
				Assert.Ignore("The editor's project has no stencil, or stencil or shadow rendering is off");
			}

			MikanCameraObject activeCamera = null;
			float deadline = Time.realtimeSinceStartup + k_renderTimeoutSeconds;
			while (activeCamera == null && Time.realtimeSinceStartup < deadline)
			{
				foreach (MikanCameraObject camera in _client.SceneMirror.FindAll<MikanCameraObject>())
				{
					if (camera.RenderCount > 2 && camera.ShadowTexture != null)
					{
						activeCamera = camera;
						break;
					}
				}

				yield return null;
			}

			if (activeCamera == null)
			{
				Assert.Ignore("No camera rendered with a shadow buffer");
			}

			yield return null;

			var shadowStats = new PixelStats();
			yield return ReadPixels(activeCamera.ShadowTexture, shadowStats, "shadow");
			var shadowedStats = new PixelStats();
			yield return ReadPixels(activeCamera.ShadowedTexture, shadowedStats, "shadowed");
			var colorStats = new PixelStats();
			yield return ReadPixels(activeCamera.ColorTexture, colorStats, "color");

			Debug.Log($"[MikanXR test] shadow buffer: {shadowStats.darkPixels} of {shadowStats.totalPixels} pixels shadowed " +
				$"(shadowed render: {shadowedStats.darkPixels} dark, {shadowedStats.opaquePixels} opaque); " +
				$"color buffer: {colorStats.opaquePixels} covered, {colorStats.clearPixels} clear of {colorStats.totalPixels}");

			Assert.That(shadowStats.totalPixels, Is.GreaterThan(0), "shadow buffer readback failed");
			Assert.That(shadowStats.darkPixels, Is.GreaterThan(0), "nothing in the shadow buffer is shadowed");
			Assert.That(shadowStats.darkPixels, Is.LessThan(shadowStats.totalPixels), "the whole shadow buffer is shadowed");
			Assert.That(colorStats.opaquePixels, Is.GreaterThan(0), "nothing was rendered into the color buffer");
			Assert.That(colorStats.clearPixels, Is.GreaterThan(0), "the color buffer has no clear pixels for the video to show through");
		}

		private class PixelStats
		{
			public int totalPixels;
			public int darkPixels;
			public int opaquePixels;
			public int clearPixels;
		}

		// Reads a buffer back and keeps a PNG of it beside the persistent data, so a failing run
		// can be looked at
		private static IEnumerator ReadPixels(RenderTexture texture, PixelStats stats, string dumpName)
		{
			var request = UnityEngine.Rendering.AsyncGPUReadback.Request(texture, 0, TextureFormat.RGBA32);
			while (!request.done)
			{
				yield return null;
			}

			if (request.hasError)
			{
				yield break;
			}

			var pixels = request.GetData<Color32>();
			stats.totalPixels = pixels.Length;

			try
			{
				var dump = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
				dump.SetPixelData(pixels, 0);
				dump.Apply();
				string folder = System.IO.Path.Combine(Application.persistentDataPath, "MikanXRTest");
				System.IO.Directory.CreateDirectory(folder);
				string path = System.IO.Path.Combine(folder, dumpName + ".png");
				System.IO.File.WriteAllBytes(path, dump.EncodeToPNG());
				Object.Destroy(dump);
				Debug.Log($"[MikanXR test] wrote {path}");
			}
			catch (System.Exception e)
			{
				Debug.LogWarning($"[MikanXR test] could not dump {dumpName}: {e.Message}");
			}
			for (int index = 0; index < pixels.Length; ++index)
			{
				Color32 pixel = pixels[index];
				if (pixel.r < 230 || pixel.g < 230 || pixel.b < 230)
				{
					++stats.darkPixels;
				}

				if (pixel.a > 200)
				{
					++stats.opaquePixels;
				}
				else if (pixel.a < 20)
				{
					++stats.clearPixels;
				}
			}
		}

		private int CountCachedComponents()
		{
			int count = 0;
			foreach (MikanDataStore.SystemEntry entry in MikanDataStore.MirroredSystems)
			{
				count += _client.DataStore.GetComponents(entry.SystemName).Count;
			}

			return count;
		}
	}
}
