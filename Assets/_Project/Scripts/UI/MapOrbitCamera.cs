using OmsiMapEditor.World;
using UnityEngine;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The camera of the 3D view as <see cref="MapGroundRenderer.SetOrbitView"/> places it, in the coordinates of the
    /// map view: world metres relative to the map origin with y up, and element pixels with y down. The map view uses
    /// it to draw handles over the 3D scene and to turn pointer positions into rays.
    /// </summary>
    internal readonly struct MapOrbitCamera
    {
        private const float NearPlane = 0.5f;

        private readonly Vector3 position;
        private readonly Quaternion rotation;
        private readonly Quaternion inverse;
        private readonly Vector2 size;

        // Element pixels per unit of tangent; the same scale on both axes, as the camera fills the element.
        private readonly float focal;

        /// <param name="target">The point the camera looks at.</param>
        /// <param name="yawDegrees">Heading of the camera, clockwise from north.</param>
        /// <param name="pitchDegrees">Tilt below the horizon.</param>
        /// <param name="viewSize">Size of the map view in element pixels.</param>
        public MapOrbitCamera(Vector3 target, float yawDegrees, float pitchDegrees, float distance, Vector2 viewSize)
        {
            rotation = Quaternion.Euler(pitchDegrees, yawDegrees, 0f);
            inverse = Quaternion.Inverse(rotation);
            position = target - rotation * Vector3.forward * distance;
            size = viewSize;
            focal = viewSize.y * 0.5f / Mathf.Tan(MapGroundRenderer.OrbitFieldOfView * 0.5f * Mathf.Deg2Rad);
        }

        public Vector3 Position => position;

        /// <summary>The direction the camera looks.</summary>
        public Vector3 Forward => rotation * Vector3.forward;

        /// <summary>Element pixels per metre at this distance in front of the camera.</summary>
        public float PixelsPerMeter(float depth)
        {
            return focal / Mathf.Max(depth, NearPlane);
        }

        /// <summary>Distance of a point in front of the camera along its view direction; negative behind it.</summary>
        public float Depth(Vector3 world)
        {
            return Vector3.Dot(world - position, Forward);
        }

        /// <summary>The element position of a world point; false when it lies behind the near plane.</summary>
        public bool TryProject(Vector3 world, out Vector2 screen)
        {
            var local = inverse * (world - position);
            if (local.z < NearPlane)
            {
                screen = Vector2.zero;
                return false;
            }
            screen = new Vector2(size.x * 0.5f + local.x / local.z * focal, size.y * 0.5f - local.y / local.z * focal);
            return true;
        }

        /// <summary>The ray from the camera through an element position.</summary>
        public Ray RayThrough(Vector2 screen)
        {
            var direction = new Vector3((screen.x - size.x * 0.5f) / focal, -(screen.y - size.y * 0.5f) / focal, 1f);
            return new Ray(position, rotation * direction.normalized);
        }
    }
}
