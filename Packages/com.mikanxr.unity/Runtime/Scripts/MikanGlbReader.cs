using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MikanXR.Unity
{
	// One triangle list from a Mikan geometry GLB, still in Mikan space (meters, right handed,
	// +Y up) with glTF's V-down texcoords. MikanMath and the caller's UV flip bring it into Unity.
	public class MikanGlbMesh
	{
		public Vector3[] Positions;
		public Vector3[] Normals;
		public Vector2[] UVs;
		public int[] Indices;
		// Index into MikanGlbGeometry.Images of the mesh's base colour texture, or -1
		public int ImageIndex = -1;
	}

	public class MikanGlbGeometry
	{
		public readonly List<MikanGlbMesh> Meshes = new List<MikanGlbMesh>();
		// Encoded PNG or JPEG bytes, one per glTF image
		public readonly List<byte[]> Images = new List<byte[]>();
	}

	// Reads the GLB the Mikan editor serves at a model's geometry_url. It covers what the editor
	// writes: float positions, normals and texcoords, u16 or u32 indices, and base colour images
	// embedded in the binary chunk. Anything outside that fails with an error rather than a guess.
	public static class MikanGlbReader
	{
		private const uint k_glbMagic = 0x46546C67; // "glTF"
		private const uint k_jsonChunkType = 0x4E4F534A; // "JSON"
		private const uint k_binChunkType = 0x004E4942; // "BIN\0"

		private const int k_componentFloat = 5126;
		private const int k_componentUnsignedShort = 5123;
		private const int k_componentUnsignedInt = 5125;

		public static bool TryRead(byte[] glb, out MikanGlbGeometry geometry, out string error)
		{
			geometry = new MikanGlbGeometry();

			try
			{
				if (glb == null || glb.Length < 20)
				{
					error = "too short for a GLB";
					return false;
				}

				if (BitConverter.ToUInt32(glb, 0) != k_glbMagic || BitConverter.ToUInt32(glb, 4) != 2)
				{
					error = "not a version 2 GLB";
					return false;
				}

				int jsonLength = (int)BitConverter.ToUInt32(glb, 12);
				if (BitConverter.ToUInt32(glb, 16) != k_jsonChunkType || 20 + jsonLength > glb.Length)
				{
					error = "missing JSON chunk";
					return false;
				}

				JObject document = JObject.Parse(Encoding.UTF8.GetString(glb, 20, jsonLength));

				// The binary chunk is the GLB's only buffer
				int binStart = 20 + jsonLength;
				int binOffset = 0;
				int binLength = 0;
				if (binStart + 8 <= glb.Length && BitConverter.ToUInt32(glb, binStart + 4) == k_binChunkType)
				{
					binLength = (int)BitConverter.ToUInt32(glb, binStart);
					binOffset = binStart + 8;
					if (binOffset + binLength > glb.Length)
					{
						error = "BIN chunk overruns the file";
						return false;
					}
				}

				var reader = new Reader(document, glb, binOffset, binLength);

				JArray images = document["images"] as JArray;
				if (images != null)
				{
					foreach (JToken image in images)
					{
						geometry.Images.Add(reader.ReadImage(image));
					}
				}

				JArray meshes = document["meshes"] as JArray;
				if (meshes != null)
				{
					foreach (JToken mesh in meshes)
					{
						foreach (JToken primitive in (JArray)mesh["primitives"])
						{
							MikanGlbMesh glbMesh = reader.ReadPrimitive(primitive);
							if (glbMesh != null)
							{
								geometry.Meshes.Add(glbMesh);
							}
						}
					}
				}

				if (geometry.Meshes.Count == 0)
				{
					error = "no triangle meshes";
					return false;
				}

				error = null;
				return true;
			}
			catch (Exception exception)
			{
				error = exception.Message;
				return false;
			}
		}

		private class Reader
		{
			private readonly JObject _document;
			private readonly byte[] _glb;
			private readonly int _binOffset;
			private readonly int _binLength;

			public Reader(JObject document, byte[] glb, int binOffset, int binLength)
			{
				_document = document;
				_glb = glb;
				_binOffset = binOffset;
				_binLength = binLength;
			}

			public byte[] ReadImage(JToken image)
			{
				if (image["bufferView"] == null)
				{
					throw new FormatException("an image is not embedded in the binary chunk");
				}

				GetBufferView((int)image["bufferView"], out int offset, out int length, out int stride);
				var bytes = new byte[length];
				Buffer.BlockCopy(_glb, offset, bytes, 0, length);
				return bytes;
			}

			public MikanGlbMesh ReadPrimitive(JToken primitive)
			{
				// Mode 4 is a triangle list, the default
				if (primitive["mode"] != null && (int)primitive["mode"] != 4)
				{
					return null;
				}

				JObject attributes = (JObject)primitive["attributes"];
				if (attributes["POSITION"] == null || primitive["indices"] == null)
				{
					return null;
				}

				var mesh = new MikanGlbMesh();
				mesh.Positions = ReadVector3s((int)attributes["POSITION"]);
				if (attributes["NORMAL"] != null)
				{
					mesh.Normals = ReadVector3s((int)attributes["NORMAL"]);
				}
				if (attributes["TEXCOORD_0"] != null)
				{
					mesh.UVs = ReadVector2s((int)attributes["TEXCOORD_0"]);
				}
				mesh.Indices = ReadIndices((int)primitive["indices"]);

				if (primitive["material"] != null)
				{
					JToken material = _document["materials"][(int)primitive["material"]];
					JToken textureRef = material["pbrMetallicRoughness"]?["baseColorTexture"];
					if (textureRef != null)
					{
						JToken texture = _document["textures"][(int)textureRef["index"]];
						if (texture["source"] != null)
						{
							mesh.ImageIndex = (int)texture["source"];
						}
					}
				}

				return mesh;
			}

			private Vector3[] ReadVector3s(int accessorIndex)
			{
				float[] floats = ReadFloats(accessorIndex, "VEC3", 3);
				var vectors = new Vector3[floats.Length / 3];
				for (int index = 0; index < vectors.Length; ++index)
				{
					vectors[index] = new Vector3(floats[index * 3], floats[index * 3 + 1], floats[index * 3 + 2]);
				}
				return vectors;
			}

			private Vector2[] ReadVector2s(int accessorIndex)
			{
				float[] floats = ReadFloats(accessorIndex, "VEC2", 2);
				var vectors = new Vector2[floats.Length / 2];
				for (int index = 0; index < vectors.Length; ++index)
				{
					vectors[index] = new Vector2(floats[index * 2], floats[index * 2 + 1]);
				}
				return vectors;
			}

			private float[] ReadFloats(int accessorIndex, string expectedType, int componentCount)
			{
				JToken accessor = GetAccessor(accessorIndex, expectedType, out int count, out int offset, out int stride);
				if ((int)accessor["componentType"] != k_componentFloat)
				{
					throw new FormatException($"accessor {accessorIndex} is not float");
				}

				int elementSize = componentCount * sizeof(float);
				CheckRange(offset, stride == 0 ? elementSize : stride, count, elementSize, accessorIndex);

				var floats = new float[count * componentCount];
				for (int element = 0; element < count; ++element)
				{
					int source = offset + element * (stride == 0 ? elementSize : stride);
					Buffer.BlockCopy(_glb, source, floats, element * elementSize, elementSize);
				}
				return floats;
			}

			private int[] ReadIndices(int accessorIndex)
			{
				JToken accessor = GetAccessor(accessorIndex, "SCALAR", out int count, out int offset, out int stride);
				int componentType = (int)accessor["componentType"];
				int componentSize;
				if (componentType == k_componentUnsignedInt)
				{
					componentSize = 4;
				}
				else if (componentType == k_componentUnsignedShort)
				{
					componentSize = 2;
				}
				else
				{
					throw new FormatException($"index accessor {accessorIndex} is not u16 or u32");
				}

				int step = stride == 0 ? componentSize : stride;
				CheckRange(offset, step, count, componentSize, accessorIndex);

				var indices = new int[count];
				for (int element = 0; element < count; ++element)
				{
					int source = offset + element * step;
					indices[element] = componentSize == 4
						? (int)BitConverter.ToUInt32(_glb, source)
						: BitConverter.ToUInt16(_glb, source);
				}
				return indices;
			}

			private JToken GetAccessor(int accessorIndex, string expectedType, out int count, out int offset, out int stride)
			{
				JToken accessor = _document["accessors"][accessorIndex];
				if ((string)accessor["type"] != expectedType || accessor["sparse"] != null || accessor["bufferView"] == null)
				{
					throw new FormatException($"accessor {accessorIndex} is not a dense {expectedType}");
				}

				count = (int)accessor["count"];
				GetBufferView((int)accessor["bufferView"], out offset, out int viewLength, out stride);
				offset += accessor["byteOffset"] != null ? (int)accessor["byteOffset"] : 0;
				return accessor;
			}

			private void GetBufferView(int viewIndex, out int offset, out int length, out int stride)
			{
				JToken view = _document["bufferViews"][viewIndex];
				if (view["buffer"] != null && (int)view["buffer"] != 0)
				{
					throw new FormatException($"buffer view {viewIndex} is not in the binary chunk");
				}

				int viewOffset = view["byteOffset"] != null ? (int)view["byteOffset"] : 0;
				length = (int)view["byteLength"];
				stride = view["byteStride"] != null ? (int)view["byteStride"] : 0;
				if (viewOffset + length > _binLength)
				{
					throw new FormatException($"buffer view {viewIndex} overruns the binary chunk");
				}

				offset = _binOffset + viewOffset;
			}

			private void CheckRange(int offset, int step, int count, int elementSize, int accessorIndex)
			{
				if (count > 0 && offset + (count - 1) * step + elementSize > _binOffset + _binLength)
				{
					throw new FormatException($"accessor {accessorIndex} overruns the binary chunk");
				}
			}
		}
	}
}
