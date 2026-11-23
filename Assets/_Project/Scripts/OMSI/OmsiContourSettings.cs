namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// What the user chose for the topology view: on or off, automatic or manual line spacing, and whether a colour band
    /// by height lies under the lines. Kept for the session only (the view starts off).
    /// </summary>
    public sealed class OmsiContourSettings
    {
        private double manualInterval = OmsiContourRules.DefaultInterval;

        public bool Enabled { get; set; }

        /// <summary>True: the spacing follows zoom and terrain. False: <see cref="ManualInterval"/> applies.</summary>
        public bool Automatic { get; set; } = true;

        /// <summary>The chosen spacing in metres, always within the allowed range.</summary>
        public double ManualInterval
        {
            get => manualInterval;
            set => manualInterval = OmsiContourRules.ClampInterval(value);
        }

        /// <summary>A colour band by height under the lines; off by default (only lines).</summary>
        public bool ShowBand { get; set; }

        /// <summary>The spacing to draw with at this zoom for a map whose heights span <paramref name="spanMeters"/>.</summary>
        public double EffectiveInterval(double metersPerPixel, double spanMeters)
        {
            return Automatic ? OmsiContourRules.PickInterval(metersPerPixel, spanMeters) : manualInterval;
        }
    }
}
