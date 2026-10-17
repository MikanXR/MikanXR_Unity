using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MikanXR.Unity
{
	public enum MikanStencilPass
	{
		Holdout,
		ShadowCatcher,
	}

	// A stencil mesh. In the host's own view it rests on a translucent preview, or hidden, by the
	// client's show-stencils setting. A Mikan camera turns it into a holdout for its color pass and
	// a shadow catcher for its shadow pass around its renders, then hands it back to the host view.
	public abstract class MikanStencilObject : MikanTransformObject
	{
		private static readonly Material[] s_previewMaterials = new Material[2];
		private static readonly Material[] s_passMaterials = new Material[4];

		private MeshFilter _meshFilter;
		private MeshRenderer _meshRenderer;
		private bool _inPass;

		public MikanStencilComponentValues StencilValues => TransformValues as MikanStencilComponentValues;
		public MeshRenderer MeshRenderer => _meshRenderer;
		public bool IsDisabled => StencilValues != null && StencilValues.is_disabled;
		protected virtual bool IsDoubleSided => false;

		public override void Bind(MikanClient client, string systemName, int componentId, MikanComponentValues values)
		{
			base.Bind(client, systemName, componentId, values);

			_meshFilter = gameObject.AddComponent<MeshFilter>();
			_meshRenderer = gameObject.AddComponent<MeshRenderer>();
			_meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
			_meshRenderer.receiveShadows = true;

			RebuildMesh();
			RefreshHostView();
		}

		public override void Unbind()
		{
			if (_meshFilter != null && _meshFilter.sharedMesh != null)
			{
				Destroy(_meshFilter.sharedMesh);
			}

			base.Unbind();
		}

		public override void OnValuesChanged(string fieldName)
		{
			base.OnValuesChanged(fieldName);

			if (fieldName == "is_disabled")
			{
				RefreshHostView();
			}
			else if (IsGeometryField(fieldName))
			{
				RebuildMesh();
				RefreshHostView();
			}
		}

		// The editor's cull rule: a stencil with a cull axis is drawn only while the camera sits on
		// that axis's positive side of it and the stencil lies ahead of the camera. A camera calls
		// this for each stencil before it renders, since the answer differs per camera.
		public bool IsFacingCamera(Vector3 cameraPosition, Vector3 cameraForward)
		{
			if (StencilValues == null || StencilValues.cull_mode == MikanStencilCullMode.NONE)
			{
				return true;
			}

			Vector3 stencilAxis;
			switch (StencilValues.cull_mode)
			{
			case MikanStencilCullMode.X_Axis:
				stencilAxis = transform.right;
				break;
			case MikanStencilCullMode.Y_Axis:
				stencilAxis = transform.up;
				break;
			default:
				// Mikan's +Z is Unity's -Z
				stencilAxis = -transform.forward;
				break;
			}

			Vector3 cameraToStencil = transform.position - cameraPosition;
			return Vector3.Dot(cameraToStencil, cameraForward) > 0f && Vector3.Dot(-cameraToStencil, stencilAxis) > 0f;
		}

		// A Mikan camera is about to render: the stencil shows or hides for that camera and carries
		// the material of its first pass, until EndPass hands it back to the host view
		public void BeginPass(MikanStencilPass pass, bool visibleForCamera)
		{
			if (_meshRenderer == null)
			{
				return;
			}

			_inPass = true;
			_meshRenderer.enabled = visibleForCamera && !IsDisabled;
			SetPass(pass);
		}

		// The camera's next pass, keeping the visibility BeginPass decided
		public void SetPass(MikanStencilPass pass)
		{
			if (_meshRenderer == null || !_inPass)
			{
				return;
			}

			Material material = GetPassMaterial(pass, IsDoubleSided);
			if (material != null)
			{
				if (pass == MikanStencilPass.ShadowCatcher)
				{
					material.SetFloat("_ShadowStrength", Client.ShadowStrength);
				}

				_meshRenderer.sharedMaterial = material;
			}
		}

		public void EndPass()
		{
			_inPass = false;
			RefreshHostView();
		}

		// What the host's own cameras see between Mikan renders: the translucent preview while the
		// client shows stencils, nothing otherwise
		public void RefreshHostView()
		{
			if (_meshRenderer == null || !IsBound || _inPass)
			{
				return;
			}

			_meshRenderer.enabled = Client.ShowStencils && !IsDisabled;
			Material material = GetPreviewMaterial(IsDoubleSided);
			if (material != null)
			{
				_meshRenderer.sharedMaterial = material;
			}
		}

		protected abstract bool IsGeometryField(string fieldName);
		protected abstract Mesh BuildMesh();

		protected void RebuildMesh()
		{
			if (_meshFilter == null)
			{
				return;
			}

			SetMesh(BuildMesh());
		}

		protected void SetMesh(Mesh mesh)
		{
			if (_meshFilter == null)
			{
				return;
			}

			Mesh previous = _meshFilter.sharedMesh;
			_meshFilter.sharedMesh = mesh;

			if (previous != null && previous != mesh)
			{
				Destroy(previous);
			}
		}

		private static Material GetPreviewMaterial(bool doubleSided)
		{
			int slot = doubleSided ? 1 : 0;
			if (s_previewMaterials[slot] == null)
			{
				Material material = MikanShaderLibrary.CreateMaterial(MikanShaderLibrary.StencilPreview);
				if (material != null)
				{
					material.SetFloat("_Cull", doubleSided ? (float)CullMode.Off : (float)CullMode.Back);
				}

				s_previewMaterials[slot] = material;
			}

			return s_previewMaterials[slot];
		}

		private static Material GetPassMaterial(MikanStencilPass pass, bool doubleSided)
		{
			int slot = (int)pass * 2 + (doubleSided ? 1 : 0);
			if (s_passMaterials[slot] == null)
			{
				string shaderName = pass == MikanStencilPass.Holdout
					? MikanShaderLibrary.StencilHoldout
					: MikanShaderLibrary.ShadowCatcher;
				Material material = MikanShaderLibrary.CreateMaterial(shaderName);
				if (material != null)
				{
					material.SetFloat("_Cull", doubleSided ? (float)CullMode.Off : (float)CullMode.Back);
				}

				s_passMaterials[slot] = material;
			}

			return s_passMaterials[slot];
		}

		// Builds a mesh from the editor's vertices, which arrive in Mikan space: every position
		// mirrors in Z and every triangle reverses its winding so it still faces outward
		protected static Mesh BuildMeshFromMikan(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> indices)
		{
			var mesh = new Mesh();
			mesh.indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
			mesh.SetVertices(vertices);

			if (normals != null && normals.Count == vertices.Count)
			{
				mesh.SetNormals(normals);
			}

			if (uvs != null && uvs.Count == vertices.Count)
			{
				mesh.SetUVs(0, uvs);
			}

			var triangles = new int[indices.Count - indices.Count % 3];
			for (int index = 0; index + 2 < indices.Count; index += 3)
			{
				triangles[index] = indices[index + 2];
				triangles[index + 1] = indices[index + 1];
				triangles[index + 2] = indices[index];
			}

			mesh.SetTriangles(triangles, 0);

			if (normals == null || normals.Count != vertices.Count)
			{
				mesh.RecalculateNormals();
			}

			mesh.RecalculateBounds();
			return mesh;
		}
	}

	// A quad of quad_width by quad_height in its own XY plane. The editor's quad faces +Z in
	// Mikan space, which is -Z here after the mirror; a double sided quad is drawn with culling off.
	public class MikanQuadStencilObject : MikanStencilObject
	{
		public MikanQuadStencilComponentValues QuadValues => TransformValues as MikanQuadStencilComponentValues;

		protected override bool IsDoubleSided => QuadValues != null && QuadValues.is_double_sided;

		protected override bool IsGeometryField(string fieldName)
		{
			return fieldName == "quad_width" || fieldName == "quad_height" || fieldName == "is_double_sided";
		}

		protected override Mesh BuildMesh()
		{
			float halfWidth = (QuadValues != null ? QuadValues.quad_width : 1f) * 0.5f;
			float halfHeight = (QuadValues != null ? QuadValues.quad_height : 1f) * 0.5f;

			var vertices = new List<Vector3>()
			{
				new Vector3(-halfWidth, -halfHeight, 0f),
				new Vector3(halfWidth, -halfHeight, 0f),
				new Vector3(halfWidth, halfHeight, 0f),
				new Vector3(-halfWidth, halfHeight, 0f),
			};
			var uvs = new List<Vector2>()
			{
				new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f)
			};
			var normals = new List<Vector3>() { Vector3.back, Vector3.back, Vector3.back, Vector3.back };

			// Wound to face -Z in Unity
			var triangles = new List<int>() { 0, 1, 2, 0, 2, 3 };

			var mesh = new Mesh();
			mesh.SetVertices(vertices);
			mesh.SetUVs(0, uvs);
			mesh.SetNormals(normals);
			mesh.SetTriangles(triangles, 0);
			mesh.RecalculateBounds();
			return mesh;
		}
	}

	// A box of the given full extents, centered on its transform
	public class MikanBoxStencilObject : MikanStencilObject
	{
		public MikanBoxStencilComponentValues BoxValues => TransformValues as MikanBoxStencilComponentValues;

		protected override bool IsGeometryField(string fieldName)
		{
			return fieldName == "box_x_size" || fieldName == "box_y_size" || fieldName == "box_z_size";
		}

		protected override Mesh BuildMesh()
		{
			Vector3 half = BoxValues != null
				? new Vector3(BoxValues.box_x_size, BoxValues.box_y_size, BoxValues.box_z_size) * 0.5f
				: Vector3.one * 0.5f;

			var vertices = new List<Vector3>();
			var normals = new List<Vector3>();
			var uvs = new List<Vector2>();
			var triangles = new List<int>();

			Vector3[] faceNormals = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
			Vector3[] faceUps = { Vector3.up, Vector3.up, Vector3.back, Vector3.forward, Vector3.up, Vector3.up };

			for (int face = 0; face < faceNormals.Length; ++face)
			{
				Vector3 normal = faceNormals[face];
				Vector3 up = faceUps[face];
				Vector3 right = Vector3.Cross(up, normal);
				Vector3 center = Vector3.Scale(normal, half);
				Vector3 rightExtent = Vector3.Scale(right, half);
				Vector3 upExtent = Vector3.Scale(up, half);
				int baseIndex = vertices.Count;

				vertices.Add(center - rightExtent - upExtent);
				vertices.Add(center + rightExtent - upExtent);
				vertices.Add(center + rightExtent + upExtent);
				vertices.Add(center - rightExtent + upExtent);
				for (int index = 0; index < 4; ++index)
				{
					normals.Add(normal);
				}
				uvs.Add(new Vector2(0f, 0f));
				uvs.Add(new Vector2(1f, 0f));
				uvs.Add(new Vector2(1f, 1f));
				uvs.Add(new Vector2(0f, 1f));

				triangles.AddRange(new int[]
				{
					baseIndex, baseIndex + 2, baseIndex + 1,
					baseIndex, baseIndex + 3, baseIndex + 2
				});
			}

			var mesh = new Mesh();
			mesh.SetVertices(vertices);
			mesh.SetNormals(normals);
			mesh.SetUVs(0, uvs);
			mesh.SetTriangles(triangles, 0);
			mesh.RecalculateBounds();
			return mesh;
		}
	}

	// A model stencil, whose triangles come from the editor on request and again whenever its
	// model path changes
	public class MikanModelStencilObject : MikanStencilObject
	{
		private string _fetchedModelPath;
		private bool _fetchInFlight;

		public MikanModelStencilComponentValues ModelValues => TransformValues as MikanModelStencilComponentValues;

		protected override bool IsGeometryField(string fieldName)
		{
			return fieldName == "model_path";
		}

		protected override Mesh BuildMesh()
		{
			// The mesh arrives asynchronously; an empty placeholder holds the slot until then
			RequestGeometry();
			return new Mesh();
		}

		private void RequestGeometry()
		{
			if (!IsBound || _fetchInFlight)
			{
				return;
			}

			string modelPath = ModelValues != null ? ModelValues.model_path : "";
			_fetchInFlight = true;

			Client.SendRequest(new GetModelStencilRenderGeometry() { stencilId = ComponentId }, response =>
			{
				_fetchInFlight = false;

				if (this == null || !IsBound)
				{
					return;
				}

				if (!(response is MikanStencilModelRenderGeometryResponse geometryResponse) ||
					response.resultCode != MikanAPIResult.Success)
				{
					Client.Log(MikanLogLevel.Warning,
						$"MikanModelStencilObject: geometry for stencil {ComponentId} failed ({response.resultCode})");
					return;
				}

				_fetchedModelPath = modelPath;
				SetMesh(BuildMeshFromGeometry(geometryResponse.render_geometry));

				// The path moved on while the request was out
				if (ModelValues != null && ModelValues.model_path != _fetchedModelPath)
				{
					RequestGeometry();
				}
			});
		}

		private static Mesh BuildMeshFromGeometry(MikanStencilModelRenderGeometry geometry)
		{
			var vertices = new List<Vector3>();
			var normals = new List<Vector3>();
			var uvs = new List<Vector2>();
			var indices = new List<int>();

			if (geometry?.meshes != null)
			{
				foreach (MikanTriagulatedMesh mesh in geometry.meshes)
				{
					int baseIndex = vertices.Count;
					bool hasNormals = mesh.normals != null && mesh.normals.Count == mesh.vertices.Count;
					bool hasUvs = mesh.texels != null && mesh.texels.Count == mesh.vertices.Count;

					for (int index = 0; index < mesh.vertices.Count; ++index)
					{
						vertices.Add(MikanMath.ToUnityPosition(mesh.vertices[index]));
						normals.Add(hasNormals ? MikanMath.ToUnityDirection(mesh.normals[index]) : Vector3.zero);
						uvs.Add(hasUvs ? new Vector2(mesh.texels[index].x, mesh.texels[index].y) : Vector2.zero);
					}

					if (mesh.indices != null)
					{
						foreach (int vertexIndex in mesh.indices)
						{
							indices.Add(baseIndex + vertexIndex);
						}
					}
				}
			}

			bool allNormals = true;
			foreach (Vector3 normal in normals)
			{
				if (normal == Vector3.zero)
				{
					allNormals = false;
					break;
				}
			}

			return BuildMeshFromMikan(vertices, allNormals ? normals : null, uvs, indices);
		}
	}
}
