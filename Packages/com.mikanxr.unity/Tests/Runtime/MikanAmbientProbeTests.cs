using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace MikanXR.Unity.Tests
{
	// The ambient probe projection, with no editor needed: the projected probe has to shade a
	// surface in Unity's frame the way the source environment would, and a rotated stage has to
	// rotate the lighting. Unity's Evaluate returns the diffuse shading, the radiance convolved
	// with the clamped cosine lobe over pi, so that is what the source is reduced to here.
	public class MikanAmbientProbeTests
	{
		// A soft lobe toward Mikan +X: l0 plus the l1 x term, positive everywhere
		private static List<float> LobeTowardPlusX()
		{
			var coefficients = new List<float>(new float[27]);
			for (int channel = 0; channel < 3; ++channel)
			{
				coefficients[0 * 3 + channel] = 1.0f;
				coefficients[3 * 3 + channel] = 0.5f;
			}
			return coefficients;
		}

		private static float Evaluate(SphericalHarmonicsL2 probe, Vector3 direction)
		{
			var directions = new Vector3[] { direction.normalized };
			var results = new Color[1];
			probe.Evaluate(directions, results);
			return results[0].r;
		}

		private static readonly float[] k_bandOverPi = { 1f, 2f / 3f, 2f / 3f, 2f / 3f, 0.25f, 0.25f, 0.25f, 0.25f, 0.25f };

		private static float SourceShading(List<float> coefficients, Vector3 mikanDirection)
		{
			var basis = new float[9];
			MikanAmbientProbe.EvaluateBasis(mikanDirection.normalized, basis);
			float shading = 0f;
			for (int coefficient = 0; coefficient < 9; ++coefficient)
			{
				shading += coefficients[coefficient * 3] * basis[coefficient] * k_bandOverPi[coefficient];
			}
			return shading;
		}

		[Test]
		public void ProjectedProbeShadesLikeTheSourceInUnitySpace()
		{
			List<float> coefficients = LobeTowardPlusX();
			SphericalHarmonicsL2 probe = MikanAmbientProbe.Project(coefficients, 1f, Quaternion.identity);

			foreach (Vector3 mikanDirection in new Vector3[] { Vector3.right, Vector3.left, Vector3.up, Vector3.forward, new Vector3(1f, 1f, -1f) })
			{
				float expected = SourceShading(coefficients, mikanDirection);
				float actual = Evaluate(probe, MikanMath.ToUnityDirection(mikanDirection));
				Assert.That(actual, Is.EqualTo(expected).Within(0.02f), $"shading along Mikan {mikanDirection}");
			}
		}

		[Test]
		public void TheStageRotationTurnsTheEnvironment()
		{
			List<float> coefficients = LobeTowardPlusX();
			Quaternion stageRotation = Quaternion.Euler(0f, 90f, 0f);
			SphericalHarmonicsL2 probe = MikanAmbientProbe.Project(coefficients, 1f, stageRotation);

			// The lobe sat on Unity +X; a 90 degree yaw carries it to Unity -Z
			float peak = SourceShading(coefficients, Vector3.right);
			Assert.That(Evaluate(probe, stageRotation * Vector3.right), Is.EqualTo(peak).Within(0.02f));
			Assert.That(Evaluate(probe, Vector3.right), Is.EqualTo(SourceShading(coefficients, Vector3.forward)).Within(0.02f));
		}

		[Test]
		public void TheScaleMultipliesTheShading()
		{
			List<float> coefficients = LobeTowardPlusX();
			SphericalHarmonicsL2 probe = MikanAmbientProbe.Project(coefficients, 2f, Quaternion.identity);

			Assert.That(Evaluate(probe, Vector3.right), Is.EqualTo(2f * SourceShading(coefficients, Vector3.right)).Within(0.04f));
		}
	}
}
