using System.Collections.Generic;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.Rendering;

namespace OmsiMapEditor.World
{
    // The topology view: contour lines over the terrain. One surface per tile shares the mesh of the ground (as the aerial
    // pictures do), one material draws them all from the world height of each fragment (MapContourOverlay.shader).
    public sealed partial class MapGroundRenderer
    {
        /// <summary>After the aerial pictures (3090), before the spline surfaces (3100), which cover the lines.</summary>
        private const int ContourQueue = 3095;

        private readonly Dictionary<long, MeshRenderer> contourSurfaces = new Dictionary<long, MeshRenderer>();
        private Shader contourShader;
        private Material contourMaterial;
        private Transform contourRoot;
        private bool contourSurfacesBuilt;
        private string contourSignature;
        private double contourMetersPerPixel = 1.0;
        private double contourMinimum;
        private double contourMaximum;
        private bool contourRangeKnown;
        private int contourRangeVersion = -1;

        /// <summary>What the user chose for the topology view; the renderer follows it, also while the view zooms.</summary>
        public OmsiContourSettings ContourSettings { get; } = new OmsiContourSettings();

        /// <summary>The lowest height of the loaded terrain; see <see cref="ContourRangeKnown"/>.</summary>
        public double ContourMinimum => contourMinimum;

        public double ContourMaximum => contourMaximum;

        /// <summary>True when the loaded tiles have terrain heights.</summary>
        public bool ContourRangeKnown => contourRangeKnown;

        /// <summary>True when the map has no height differences, so contour lines would show nothing.</summary>
        public bool ContourFlat => !contourRangeKnown || OmsiContourRules.IsFlat(contourMinimum, contourMaximum);

        /// <summary>The line spacing in metres drawn now.</summary>
        public double ContourIntervalMeters { get; private set; } = OmsiContourRules.DefaultInterval;

        /// <summary>True while the contour lines are on screen (switched on and the map not flat).</summary>
        public bool ContourShown { get; private set; }

        private void LoadContourShader()
        {
            contourShader = Resources.Load<Shader>("MapContourOverlay");
            if (contourShader == null)
                Debug.LogError("MapContourOverlay.shader was not found in Resources.");
        }

        private void SetViewScale(double metersPerPixel)
        {
            if (metersPerPixel > 0)
                contourMetersPerPixel = metersPerPixel;
        }

        /// <summary>
        /// Reads the height range of the loaded terrain when it has changed since the last time, also while the lines are off, so the
        /// legend of the topology settings can say the heights of the map.
        /// </summary>
        public void UpdateContourRange()
        {
            if (contourRangeVersion == TerrainVersion && contourRangeKnown)
                return;
            contourRangeVersion = TerrainVersion;
            contourRangeKnown = OmsiContourRules.TryGetRange(terrains.Values, out contourMinimum, out contourMaximum);
        }

        /// <summary>Follows the settings, the zoom and the terrain; called every frame, it works only on a change.</summary>
        private void UpdateContours()
        {
            var settings = ContourSettings;
            if (!settings.Enabled && !ContourShown && contourRoot == null)
                return;
            var signature = settings.Enabled + "|" + settings.Automatic + "|" + settings.ManualInterval + "|" + settings.ShowBand + "|" +
                            TerrainVersion + "|" + System.Math.Round(System.Math.Log(contourMetersPerPixel) * 8.0);
            if (signature == contourSignature)
                return;
            contourSignature = signature;
            ApplyContours();
        }

        private void ApplyContours()
        {
            var settings = ContourSettings;
            contourRangeKnown = OmsiContourRules.TryGetRange(terrains.Values, out contourMinimum, out contourMaximum);
            var show = settings.Enabled && contourShader != null && contourRangeKnown && !OmsiContourRules.IsFlat(contourMinimum, contourMaximum);
            if (show && !contourSurfacesBuilt)
                BuildContourSurfaces();
            if (contourRoot != null)
                contourRoot.gameObject.SetActive(show);
            ContourShown = show;
            if (!show || contourMaterial == null)
                return;
            ContourIntervalMeters = settings.EffectiveInterval(contourMetersPerPixel, contourMaximum - contourMinimum);
            contourMaterial.SetFloat("_Interval", (float)ContourIntervalMeters);
            contourMaterial.SetFloat("_MajorEvery", OmsiContourRules.MajorEvery);
            contourMaterial.SetFloat("_MinHeight", (float)contourMinimum);
            contourMaterial.SetFloat("_MaxHeight", (float)contourMaximum);
            contourMaterial.SetFloat("_Band", settings.ShowBand ? 1f : 0f);
        }

        private void BuildContourSurfaces()
        {
            contourMaterial = new Material(contourShader) { renderQueue = ContourQueue, name = "Contour lines" };
            contourRoot = new GameObject("Map Contours").transform;
            contourRoot.SetParent(transform, false);
            contourRoot.gameObject.SetActive(false);
            contourSurfacesBuilt = true;
            foreach (var state in tileStates.Values)
                EnsureContourSurface(state);
        }

        /// <summary>Gives a tile its contour surface, or its current mesh when it has one; does nothing while the view is not built.</summary>
        private void EnsureContourSurface(TileState state)
        {
            if (!contourSurfacesBuilt || state.Mesh == null)
                return;
            if (contourSurfaces.TryGetValue(state.Key, out var existing) && existing != null)
            {
                existing.GetComponent<MeshFilter>().sharedMesh = state.Mesh;
                return;
            }
            var surface = new GameObject("Contours " + state.X + ", " + state.Y) { layer = GroundLayer };
            surface.transform.SetParent(contourRoot, false);
            surface.transform.localPosition = state.Position;
            surface.AddComponent<MeshFilter>().sharedMesh = state.Mesh;
            var meshRenderer = surface.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = contourMaterial;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            contourSurfaces[state.Key] = meshRenderer;
        }

        private void ClearContours()
        {
            contourSurfaces.Clear();
            if (contourRoot != null)
                Destroy(contourRoot.gameObject);
            contourRoot = null;
            if (contourMaterial != null)
                Destroy(contourMaterial);
            contourMaterial = null;
            contourSurfacesBuilt = false;
            contourSignature = null;
            contourRangeKnown = false;
            contourRangeVersion = -1;
            contourMinimum = 0;
            contourMaximum = 0;
            ContourShown = false;
        }
    }
}
