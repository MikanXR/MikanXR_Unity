using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MikanXR.Unity
{
	// The editor's captured lighting probe: an order-2 spherical harmonic radiance environment
	// plus a key light direction, both in the capturing camera's stage space. The capture
	// belongs to the camera it sits under and lights only that camera's renders: the camera
	// brackets each render with BeginRender and EndRender, which make the probe the scene's
	// ambient and turn the directional key light on, then put the host's ambient back and the
	// key light off. The host's own views keep the host's lighting, a host that rewrites its
	// ambient every frame (Warudo) cannot undo it, and one key light at a time leaves the
	// forward renderer's per-pixel light slots to the spot lights.
	public class MikanLightEnvironmentObject : MikanTransformObject
	{
		// Below this the recovered direction is noise and the key light stays off
		private const float k_keyLightDirectionalityThreshold = 0.25f;

		private Light _keyLight;
		private SphericalHarmonicsL2 _probe;
		private bool _probeDirty = true;
		private Quaternion _probeStageRotation = Quaternion.identity;
		private float _probeIntensityScale = -1f;
		private bool _rendering;

		public MikanLightEnvironmentComponentValues EnvironmentValues => TransformValues as MikanLightEnvironmentComponentValues;

		public override void Bind(MikanClient client, string systemName, int componentId, MikanComponentValues values)
		{
			base.Bind(client, systemName, componentId, values);

			var keyLightObject = new GameObject("Key light");
			keyLightObject.transform.SetParent(transform, false);
			_keyLight = keyLightObject.AddComponent<Light>();
			_keyLight.type = LightType.Directional;
			_keyLight.color = Color.white;
			_keyLight.shadows = LightShadows.Soft;
			_keyLight.enabled = false;

			_probeDirty = true;
		}

		public override void OnValuesChanged(string fieldName)
		{
			base.OnValuesChanged(fieldName);

			switch (fieldName)
			{
			case "sh_coefficients":
			case "exposure_scale":
				_probeDirty = true;
				break;
			}
		}

		public void RefreshLighting()
		{
			_probeDirty = true;
		}

		// Lights the scene for one render of the camera this capture belongs to. Answers false,
		// with nothing changed, while the client does not apply light environments.
		public bool BeginRender()
		{
			MikanLightEnvironmentComponentValues values = EnvironmentValues;
			if (!IsBound || values == null || !Client.ApplyLightEnvironment || _rendering)
			{
				return false;
			}

			// The coefficients and the direction are in the stage's space, so the stage's world
			// rotation (and the root's, which it includes) turns them into world lighting
			MikanStageObject stage = GetComponentInParent<MikanStageObject>();
			Quaternion stageRotation = stage != null ? stage.transform.rotation : transform.rotation;

			if (_probeDirty ||
				Quaternion.Angle(stageRotation, _probeStageRotation) > 0.01f ||
				!Mathf.Approximately(Client.LightIntensityScale, _probeIntensityScale))
			{
				_probe = MikanAmbientProbe.Project(
					values.sh_coefficients, values.exposure_scale * Client.LightIntensityScale, stageRotation);
				_probeDirty = false;
				_probeStageRotation = stageRotation;
				_probeIntensityScale = Client.LightIntensityScale;
			}

			MikanAmbientProbe.BeginOverride(_probe);
			UpdateKeyLight(stageRotation);
			_rendering = true;
			return true;
		}

		public void EndRender()
		{
			if (!_rendering)
			{
				return;
			}

			_rendering = false;
			if (_keyLight != null)
			{
				_keyLight.enabled = false;
			}
			MikanAmbientProbe.EndOverride();
		}

		private void UpdateKeyLight(Quaternion stageRotation)
		{
			MikanLightEnvironmentComponentValues values = EnvironmentValues;
			if (_keyLight == null || values == null)
			{
				return;
			}

			Vector3 keyDirection = values.key_light_direction != null
				? MikanMath.ToUnityDirection(values.key_light_direction)
				: Vector3.zero;
			bool show = values.directionality >= k_keyLightDirectionalityThreshold && keyDirection.sqrMagnitude > 1e-6f;
			_keyLight.enabled = show;
			if (!show)
			{
				return;
			}

			// The direction points from the scene toward the light, so the light shines back along it
			Vector3 worldDirection = (stageRotation * keyDirection).normalized;
			_keyLight.transform.rotation = Quaternion.LookRotation(-worldDirection);
			_keyLight.intensity = Client.LightIntensityScale;
		}
	}

	// The scene's ambient probe for the length of one Mikan render, with what the scene had
	// before put back after it
	public static class MikanAmbientProbe
	{
		// Enough directions that a clamped order-2 environment projects back without visible error
		private const int k_sampleCount = 256;

		// Unity keeps a probe in the form its shaders read: each radiance coefficient times its
		// basis constant, times the clamped cosine lobe's band factor (pi, 2pi/3, pi/4) over pi,
		// so that evaluating the bare polynomials gives the diffuse shading directly
		private static readonly float[] k_unityFromRadiance =
		{
			0.282095f,
			0.325735f, 0.325735f, 0.325735f,
			0.273137f, 0.273137f, 0.078848f, 0.273137f, 0.136569f,
		};

		private static bool s_overriding;
		private static AmbientMode s_savedMode;
		private static SphericalHarmonicsL2 s_savedProbe;

		public static void BeginOverride(SphericalHarmonicsL2 probe)
		{
			if (!s_overriding)
			{
				s_savedMode = RenderSettings.ambientMode;
				s_savedProbe = RenderSettings.ambientProbe;
				s_overriding = true;
			}

			RenderSettings.ambientMode = AmbientMode.Custom;
			RenderSettings.ambientProbe = probe;
		}

		public static void EndOverride()
		{
			if (s_overriding)
			{
				RenderSettings.ambientMode = s_savedMode;
				RenderSettings.ambientProbe = s_savedProbe;
				s_overriding = false;
			}
		}

		// The editor's coefficients are 9 RGB triples of a radiance environment in Mikan stage
		// space. A spherical function cannot be mirrored into Unity's frame by rewriting its
		// coefficients, so the environment is sampled over the sphere, each sample's direction is
		// converted and rotated like any other direction, and the samples are projected again.
		// The radiance is clamped at zero first, as every other consumer of the probe does,
		// since order-2 harmonics ring negative around sharp lights.
		public static SphericalHarmonicsL2 Project(List<float> mikanCoefficients, float scale, Quaternion stageToWorld)
		{
			var probe = new SphericalHarmonicsL2();
			if (mikanCoefficients == null || mikanCoefficients.Count < 27)
			{
				return probe;
			}

			var basis = new float[9];
			float weight = 4f * Mathf.PI / k_sampleCount;
			float goldenAngle = Mathf.PI * (3f - Mathf.Sqrt(5f));

			for (int sample = 0; sample < k_sampleCount; ++sample)
			{
				// A Fibonacci sphere: evenly spread directions in Mikan space
				float y = 1f - 2f * (sample + 0.5f) / k_sampleCount;
				float radius = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
				float angle = goldenAngle * sample;
				var mikanDirection = new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);

				EvaluateBasis(mikanDirection, basis);
				float r = 0f, g = 0f, b = 0f;
				for (int coefficient = 0; coefficient < 9; ++coefficient)
				{
					r += mikanCoefficients[coefficient * 3] * basis[coefficient];
					g += mikanCoefficients[coefficient * 3 + 1] * basis[coefficient];
					b += mikanCoefficients[coefficient * 3 + 2] * basis[coefficient];
				}
				r = Mathf.Max(0f, r) * scale * weight;
				g = Mathf.Max(0f, g) * scale * weight;
				b = Mathf.Max(0f, b) * scale * weight;

				Vector3 worldDirection = stageToWorld * MikanMath.ToUnityDirection(mikanDirection);
				EvaluateBasis(worldDirection, basis);
				for (int coefficient = 0; coefficient < 9; ++coefficient)
				{
					probe[0, coefficient] += r * basis[coefficient];
					probe[1, coefficient] += g * basis[coefficient];
					probe[2, coefficient] += b * basis[coefficient];
				}
			}

			for (int coefficient = 0; coefficient < 9; ++coefficient)
			{
				probe[0, coefficient] *= k_unityFromRadiance[coefficient];
				probe[1, coefficient] *= k_unityFromRadiance[coefficient];
				probe[2, coefficient] *= k_unityFromRadiance[coefficient];
			}

			return probe;
		}

		// The real order-2 basis in the order both the editor and Unity use
		public static void EvaluateBasis(Vector3 d, float[] basis)
		{
			basis[0] = 0.282095f;
			basis[1] = 0.488603f * d.y;
			basis[2] = 0.488603f * d.z;
			basis[3] = 0.488603f * d.x;
			basis[4] = 1.092548f * d.x * d.y;
			basis[5] = 1.092548f * d.y * d.z;
			basis[6] = 0.315392f * (3f * d.z * d.z - 1f);
			basis[7] = 1.092548f * d.x * d.z;
			basis[8] = 0.546274f * (d.x * d.x - d.y * d.y);
		}
	}

	// A DMX driven RGB spot light on a stage: a Unity spot light whose cone comes from the
	// fixture and whose color and output come from its three channels and its emitter spec,
	// with a cone outline the host's own cameras see
	public class MikanRGBSpotLightObject : MikanTransformObject
	{
		// Unity's built-in light intensity has no unit. The fixture's output is turned into its
		// on-axis candela, lumens over the cone's solid angle, so a narrow fixture reads as bright
		// as it is, and this many candela map to an intensity of one at a scale of one. Built-in
		// lights have no exposure to sit under, so this is tuned for a small LED fixture to show
		// against a scene's own key light, and the client's intensity scale takes it from there.
		private const float k_candelaPerUnitIntensity = 5f;

		private const int k_rimPointCount = 16;
		private const int k_spokeCount = 4;
		private static readonly Color k_darkConeColor = new Color(0.4f, 0.4f, 0.4f, 1f);
		private static Material s_lineMaterial;

		private Light _light;
		private LineRenderer _coneLines;
		private bool _coneHiddenForRender;
		private float _appliedWorldScale = -1f;

		public MikanRGBSpotLightComponentValues SpotLightValues => TransformValues as MikanRGBSpotLightComponentValues;

		// A fixture sits under its stage
		public override int DesiredParentId => SpotLightValues != null && SpotLightValues.stage_id != (int)MikanConstants.InvalidMikanID
			? SpotLightValues.stage_id
			: base.DesiredParentId;

		public override bool IsParentField(string fieldName)
		{
			return fieldName == "stage_id" || base.IsParentField(fieldName);
		}

		public override void Bind(MikanClient client, string systemName, int componentId, MikanComponentValues values)
		{
			base.Bind(client, systemName, componentId, values);

			// The editor's cone has its tip at the origin and opens along Mikan -Z, which is this
			// object's +Z, the axis a Unity spot light shines along
			_light = gameObject.AddComponent<Light>();
			_light.type = LightType.Spot;
			_light.shadows = LightShadows.None;
			// A fixture's cone only reads per pixel. Left to the renderer's choice the scene's
			// directional lights take the few per-pixel slots and the fixtures fall back to vertex
			// lighting, where a cone on a character is a faint smear.
			_light.renderMode = LightRenderMode.ForcePixel;
			_light.enabled = false;

			// The cone outline is built in meters under this object, so the root's scale sizes it
			var coneObject = new GameObject("Cone");
			coneObject.transform.SetParent(transform, false);
			_coneLines = coneObject.AddComponent<LineRenderer>();
			_coneLines.useWorldSpace = false;
			_coneLines.startWidth = 0.01f;
			_coneLines.endWidth = 0.01f;
			_coneLines.shadowCastingMode = ShadowCastingMode.Off;
			_coneLines.receiveShadows = false;
			_coneLines.positionCount = 0;
			if (s_lineMaterial == null)
			{
				s_lineMaterial = MikanShaderLibrary.CreateMaterial(MikanShaderLibrary.UnlitColor);
			}
			if (s_lineMaterial != null)
			{
				_coneLines.sharedMaterial = s_lineMaterial;
			}

			client.DMXStore.Changed += RefreshOutput;
			RefreshCone();
			RefreshOutput();
		}

		public override void Unbind()
		{
			if (IsBound)
			{
				Client.DMXStore.Changed -= RefreshOutput;
			}

			base.Unbind();
		}

		public override void HideHelpersForRender()
		{
			_coneHiddenForRender = true;
			RefreshConeVisibility();
		}

		public override void RestoreHelpers()
		{
			_coneHiddenForRender = false;
			RefreshConeVisibility();
		}

		public void RefreshConeVisibility()
		{
			if (_coneLines != null)
			{
				_coneLines.enabled = !_coneHiddenForRender && IsBound && Client.ShowLightCones;
			}
		}

		public override void OnValuesChanged(string fieldName)
		{
			base.OnValuesChanged(fieldName);

			switch (fieldName)
			{
			case "cone_angle_degrees":
			case "cone_range_meters":
				RefreshCone();
				break;
			case "is_disabled":
			case "dmx_universe":
			case "dmx_start_channel":
			case "max_wattage":
			case "lumens_per_watt":
				RefreshOutput();
				break;
			}
		}

		private void LateUpdate()
		{
			if (IsBound && !Mathf.Approximately(Client.WorldScale, _appliedWorldScale))
			{
				RefreshCone();
			}
		}

		private void RefreshCone()
		{
			MikanRGBSpotLightComponentValues values = SpotLightValues;
			if (_light == null || values == null)
			{
				return;
			}

			_light.spotAngle = Mathf.Clamp(values.cone_angle_degrees, 1f, 179f);
			// The range is a world distance, which the root's scale does not reach
			_light.range = values.cone_range_meters * Client.WorldScale;
			_appliedWorldScale = Client.WorldScale;

			RebuildCone(_light.spotAngle, values.cone_range_meters);
			RefreshConeVisibility();
		}

		// The editor's cone as an outline: the rim circle at the range, then spokes from the tip
		// at the origin, opening along +Z here. One strip, so each spoke is walked out and back.
		private void RebuildCone(float angleDegrees, float rangeMeters)
		{
			if (_coneLines == null)
			{
				return;
			}

			float radius = rangeMeters * Mathf.Tan(angleDegrees * 0.5f * Mathf.Deg2Rad);
			var rim = new Vector3[k_rimPointCount];
			for (int point = 0; point < k_rimPointCount; ++point)
			{
				float angle = point * 2f * Mathf.PI / k_rimPointCount;
				rim[point] = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, rangeMeters);
			}

			var points = new List<Vector3>(rim) { rim[0] };
			for (int spoke = 0; spoke < k_spokeCount; ++spoke)
			{
				points.Add(Vector3.zero);
				points.Add(rim[(spoke + 1) * k_rimPointCount / k_spokeCount % k_rimPointCount]);
			}

			_coneLines.positionCount = points.Count;
			_coneLines.SetPositions(points.ToArray());
		}

		// The three channels are the red, green and blue fractions; their mean is the fraction of
		// the fixture's wattage drawn, and the emitter spec turns that into lumens
		public void RefreshOutput()
		{
			MikanRGBSpotLightComponentValues values = SpotLightValues;
			if (_light == null || values == null || !IsBound)
			{
				return;
			}

			MikanDMXStore store = Client.DMXStore;
			float red = store.ReadChannel(values.dmx_universe, values.dmx_start_channel) / 255f;
			float green = store.ReadChannel(values.dmx_universe, values.dmx_start_channel + 1) / 255f;
			float blue = store.ReadChannel(values.dmx_universe, values.dmx_start_channel + 2) / 255f;

			float wattFraction = (red + green + blue) / 3f;
			float lumens = values.max_wattage * wattFraction * values.lumens_per_watt;

			float halfAngle = Mathf.Clamp(values.cone_angle_degrees, 1f, 179f) * 0.5f * Mathf.Deg2Rad;
			float solidAngle = 2f * Mathf.PI * (1f - Mathf.Cos(halfAngle));
			float candela = lumens / solidAngle;

			_light.color = new Color(red, green, blue);
			_light.intensity = candela / k_candelaPerUnitIntensity * Client.LightIntensityScale;
			_light.enabled = Client.ApplySpotLights && !values.is_disabled && lumens > 0f;

			// The outline carries the light's color, grey while the fixture is dark, as in the editor
			if (_coneLines != null)
			{
				Color coneColor = red + green + blue > 0.01f ? _light.color : k_darkConeColor;
				_coneLines.startColor = coneColor;
				_coneLines.endColor = coneColor;
			}
		}
	}
}
