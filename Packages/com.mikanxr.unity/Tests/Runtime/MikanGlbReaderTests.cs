using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace MikanXR.Unity.Tests
{
	// The GLB reader against a fixture the Mikan editor served from its geometry route: a textured
	// quad (OBJ material with a 2x2 PNG) and an untextured triangle (a second material), so the
	// fixture carries what the editor writes rather than what this reader expects.
	public class MikanGlbReaderTests
	{
		private const string k_fixtureDirectory = "Packages/com.mikanxr.unity/Tests/Runtime/Fixtures";

		private static byte[] ReadFixture(string fileName)
		{
			return File.ReadAllBytes(Path.GetFullPath(Path.Combine(k_fixtureDirectory, fileName)));
		}

		[Test]
		public void ReadsBothMeshesAndTheEmbeddedImage()
		{
			byte[] glb = ReadFixture("model_geometry.glb.bytes");
			Assert.IsTrue(MikanGlbReader.TryRead(glb, out MikanGlbGeometry geometry, out string error), error);

			Assert.AreEqual(2, geometry.Meshes.Count);
			Assert.AreEqual(1, geometry.Images.Count);
			CollectionAssert.AreEqual(ReadFixture("model_geometry_texture.png.bytes"), geometry.Images[0]);

			MikanGlbMesh textured = geometry.Meshes[0];
			Assert.AreEqual(0, textured.ImageIndex);
			Assert.AreEqual(4, textured.Positions.Length);
			Assert.AreEqual(4, textured.Normals.Length);
			Assert.AreEqual(4, textured.UVs.Length);
			Assert.AreEqual(6, textured.Indices.Length);
			Assert.AreEqual(new Vector3(1f, 1f, 0f), textured.Positions[2]);

			MikanGlbMesh plain = geometry.Meshes[1];
			Assert.AreEqual(-1, plain.ImageIndex);
			Assert.AreEqual(3, plain.Positions.Length);
			Assert.AreEqual(3, plain.Indices.Length);
		}

		[Test]
		public void KeepsTexcoordsVDown()
		{
			Assert.IsTrue(MikanGlbReader.TryRead(ReadFixture("model_geometry.glb.bytes"), out MikanGlbGeometry geometry,
												 out string error), error);

			// The OBJ's (0, 0) corner, its bottom left, arrives as glTF's (0, 1)
			MikanGlbMesh textured = geometry.Meshes[0];
			Assert.AreEqual(new Vector2(0f, 1f), textured.UVs[0]);
			Assert.AreEqual(new Vector2(1f, 0f), textured.UVs[2]);
		}

		[Test]
		public void TheEmbeddedImageDecodes()
		{
			Assert.IsTrue(MikanGlbReader.TryRead(ReadFixture("model_geometry.glb.bytes"), out MikanGlbGeometry geometry,
												 out string error), error);

			var texture = new Texture2D(2, 2);
			try
			{
				Assert.IsTrue(texture.LoadImage(geometry.Images[0]));
				Assert.AreEqual(2, texture.width);

				// Unity's row 0 is the image's bottom row: blue then white
				Color32 bottomLeft = texture.GetPixel(0, 0);
				Assert.AreEqual(new Color32(0, 0, 255, 255), bottomLeft);
			}
			finally
			{
				Object.DestroyImmediate(texture);
			}
		}

		[Test]
		public void RejectsBytesThatAreNotAGlb()
		{
			Assert.IsFalse(MikanGlbReader.TryRead(new byte[64], out MikanGlbGeometry _, out string error));
			Assert.IsFalse(string.IsNullOrEmpty(error));

			byte[] truncated = ReadFixture("model_geometry.glb.bytes");
			System.Array.Resize(ref truncated, truncated.Length / 2);
			Assert.IsFalse(MikanGlbReader.TryRead(truncated, out MikanGlbGeometry _, out error));
		}
	}
}
