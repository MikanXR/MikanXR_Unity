using UnityEngine;

namespace MikanXR.Unity
{
	// Draws a Mikan camera's frustum from its intrinsics, so the host's own view shows where each
	// camera looks from. It hides around every Mikan render, so the published frames never carry it.
	public class MikanCameraVisualizer : MonoBehaviour
	{
		private const int k_frustumPointCount = 26;

		private LineRenderer _lineRenderer;
		private bool _visible = true;
		private static Material s_lineMaterial;

		public void Initialize()
		{
			_lineRenderer = gameObject.AddComponent<LineRenderer>();
			_lineRenderer.useWorldSpace = false;
			_lineRenderer.startWidth = 0.01f;
			_lineRenderer.endWidth = 0.01f;
			_lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			_lineRenderer.receiveShadows = false;
			_lineRenderer.positionCount = 0;

			if (s_lineMaterial == null)
			{
				s_lineMaterial = MikanShaderLibrary.CreateMaterial(MikanShaderLibrary.UnlitColor);
				if (s_lineMaterial != null)
				{
					s_lineMaterial.color = Color.yellow;
				}
			}

			if (s_lineMaterial != null)
			{
				_lineRenderer.sharedMaterial = s_lineMaterial;
			}
			_lineRenderer.startColor = Color.yellow;
			_lineRenderer.endColor = Color.yellow;
		}

		public void SetVisible(bool visible)
		{
			_visible = visible;
			EndRender();
		}

		// A Mikan camera is about to render; EndRender gives the host view back
		public void BeginRender()
		{
			if (_lineRenderer != null)
			{
				_lineRenderer.enabled = false;
			}
		}

		public void EndRender()
		{
			if (_lineRenderer != null)
			{
				_lineRenderer.enabled = _visible;
			}
		}

		// The frustum in the camera's own space: +Z forward, with the off-center extents the
		// principal point implies
		public void Rebuild(int pixelWidth, int pixelHeight, double fx, double fy, double cx, double cy, float zNear, float zFar)
		{
			if (_lineRenderer == null || pixelWidth <= 0 || pixelHeight <= 0 || fx <= 0 || fy <= 0)
			{
				return;
			}

			float leftRatio = (float)(-cx / fx);
			float rightRatio = (float)((pixelWidth - cx) / fx);
			float topRatio = (float)(cy / fy);
			float bottomRatio = (float)(-(pixelHeight - cy) / fy);

			Vector3[] near = Corners(zNear, leftRatio, rightRatio, topRatio, bottomRatio);
			Vector3[] far = Corners(zFar, leftRatio, rightRatio, topRatio, bottomRatio);
			Vector3 origin = Vector3.zero;

			var points = new Vector3[k_frustumPointCount];
			int point = 0;

			// Four edges from the origin through the far corners, each returning along a far edge
			for (int corner = 0; corner < 4; ++corner)
			{
				points[point++] = origin;
				points[point++] = far[corner];
				points[point++] = far[(corner + 1) % 4];
			}

			// The same through the near corners
			for (int corner = 0; corner < 4; ++corner)
			{
				points[point++] = origin;
				points[point++] = near[corner];
				points[point++] = near[(corner + 1) % 4];
			}

			// The forward axis to the near plane's center
			points[point++] = origin;
			points[point++] = new Vector3((leftRatio + rightRatio) * 0.5f, (topRatio + bottomRatio) * 0.5f, 1f) * zNear;

			_lineRenderer.positionCount = point;
			_lineRenderer.SetPositions(points);
		}

		private static Vector3[] Corners(float distance, float left, float right, float top, float bottom)
		{
			return new Vector3[]
			{
				new Vector3(right * distance, top * distance, distance),
				new Vector3(left * distance, top * distance, distance),
				new Vector3(left * distance, bottom * distance, distance),
				new Vector3(right * distance, bottom * distance, distance),
			};
		}
	}
}
