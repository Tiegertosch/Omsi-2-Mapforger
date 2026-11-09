using UnityEngine;

namespace OmsiMapEditor.World
{
    /// <summary>One object of a group about to be put down: its file, where it would stand on the map (metres from the origin) and how.</summary>
    public sealed class GroupPreviewObject
    {
        public string Asset;
        public Vector2 MapPosition;
        public double HeadingDegrees;
        public double PitchDegrees;
        public double BankDegrees;

        /// <summary>The height as written: above the ground for most objects, absolute for junctions and [absheight] objects.</summary>
        public double Height;

        /// <summary>The terrain height under the original, so an object of absolute height keeps its distance from the ground; null when unknown.</summary>
        public double? SourceGround;
    }
}
