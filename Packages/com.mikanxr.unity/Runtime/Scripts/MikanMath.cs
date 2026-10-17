using UnityEngine;

namespace MikanXR.Unity
{
	// The one place Mikan space becomes Unity space. Mikan is right handed with +Y up and -Z
	// forward, in meters; Unity is left handed with +Y up and +Z forward. The two differ by a
	// mirror across the XY plane, so a position negates Z and a quaternion negates its X and Y
	// parts. Scale carries no handedness and passes through.
	public static class MikanMath
	{
		public static Vector3 ToUnityPosition(MikanVector3f v)
		{
			return new Vector3(v.x, v.y, -v.z);
		}

		// The same mirror for a position already held as a Unity vector in Mikan space
		public static Vector3 ToUnityPosition(Vector3 v)
		{
			return new Vector3(v.x, v.y, -v.z);
		}

		public static Vector3 ToUnityDirection(MikanVector3f v)
		{
			return new Vector3(v.x, v.y, -v.z);
		}

		// The same mirror for a direction already held as a Unity vector in Mikan space
		public static Vector3 ToUnityDirection(Vector3 v)
		{
			return new Vector3(v.x, v.y, -v.z);
		}

		public static Quaternion ToUnityRotation(MikanQuatf q)
		{
			return new Quaternion(-q.x, -q.y, q.z, q.w);
		}

		public static Vector3 ToUnityScale(MikanVector3f v)
		{
			return new Vector3(v.x, v.y, v.z);
		}

		public static MikanVector3f ToMikanPosition(Vector3 v)
		{
			return new MikanVector3f() { x = v.x, y = v.y, z = -v.z };
		}

		public static MikanQuatf ToMikanRotation(Quaternion q)
		{
			return new MikanQuatf() { x = -q.x, y = -q.y, z = q.z, w = q.w };
		}

		public static MikanVector3f ToMikanScale(Vector3 v)
		{
			return new MikanVector3f() { x = v.x, y = v.y, z = v.z };
		}

		// A camera pose from the forward and up vectors a camera event carries
		public static Quaternion ToUnityCameraRotation(MikanVector3f forward, MikanVector3f up)
		{
			Vector3 unityForward = ToUnityDirection(forward);
			Vector3 unityUp = ToUnityDirection(up);

			if (unityForward.sqrMagnitude < 1e-8f)
			{
				return Quaternion.identity;
			}

			return Quaternion.LookRotation(unityForward, unityUp);
		}

		// The off-center projection of a calibrated pinhole camera. The intrinsics are in pixels
		// at the given size, with the principal point measured from the image's top-left corner,
		// which is why the top edge uses cy and the bottom edge the remainder.
		public static Matrix4x4 ProjectionFromIntrinsics(
			int pixelWidth, int pixelHeight,
			double focalLengthX, double focalLengthY,
			double principalPointX, double principalPointY,
			float zNear, float zFar)
		{
			float left = (float)(-principalPointX / focalLengthX) * zNear;
			float right = (float)((pixelWidth - principalPointX) / focalLengthX) * zNear;
			float top = (float)(principalPointY / focalLengthY) * zNear;
			float bottom = (float)(-(pixelHeight - principalPointY) / focalLengthY) * zNear;

			return Matrix4x4.Frustum(left, right, bottom, top, zNear, zFar);
		}
	}
}
