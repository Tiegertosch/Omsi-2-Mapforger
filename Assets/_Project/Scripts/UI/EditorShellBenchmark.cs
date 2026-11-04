using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using OmsiMapEditor.OMSI;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The benchmark run (<c>-benchmark report.txt</c>, Documentation/PerformanceBaseline.md): measures how long the map took to
    /// open and load, frame and redraw times while the view pans and zooms from above and turns in 3D, picking in 3D and the
    /// refresh after an edit, and writes them with the machine to a text file. It changes no files of the map; with a
    /// project open it refreshes the views as after an edit, so it is meant for throw-away projects.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private const int BenchmarkFrames = 120;

        private float benchmarkOpenStartedAt;
        private float benchmarkOpenFinishedAt;

        private sealed class BenchmarkPhase
        {
            public string Name;
            public readonly List<double> Frames = new List<double>();
            public readonly List<double> Draws = new List<double>();
            public readonly List<double> MainThread = new List<double>();
            public readonly List<double> RenderThread = new List<double>();
            public readonly List<double> Allocated = new List<double>();
            public int Collections;
        }

        private IEnumerator RunBenchmark(string reportPath)
        {
            var report = new StringBuilder();
            void Line(string text)
            {
                report.AppendLine(text);
                Debug.Log("Benchmark: " + text);
            }

            Line("OMSI MapWorks " + Application.version + (Debug.isDebugBuild ? " (development build)" : " (release build)") + ", " +
                 DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            Line("CPU " + SystemInfo.processorType + " (" + SystemInfo.processorCount + " threads), " + SystemInfo.systemMemorySize + " MB RAM, GPU " +
                 SystemInfo.graphicsDeviceName + " (" + SystemInfo.graphicsMemorySize + " MB, " + SystemInfo.graphicsDeviceType + ")");
            Line("Screen " + Screen.width + " x " + Screen.height + ", map " + currentSummary.Tiles.Count + " tiles, " + currentSummary.ObjectCount +
                 " objects, " + currentSummary.SplineCount + " splines, " + (CanEdit ? "project" : "read-only"));

            // Loading: the summary first, then ground, objects and outlines in the background.
            Line("Startup until the map was opened: " + BenchmarkSeconds(benchmarkOpenStartedAt) + " (Unity start included)");
            Line("Open map (read the summary, set up the views): " + BenchmarkSeconds(benchmarkOpenFinishedAt - benchmarkOpenStartedAt));
            float groundAt = 0f, objectsAt = 0f, outlinesAt = 0f;
            while (Time.realtimeSinceStartup - benchmarkOpenStartedAt < 600f)
            {
                var now = Time.realtimeSinceStartup;
                if (groundAt == 0f && (groundRenderer == null || !groundRenderer.IsLoading))
                    groundAt = now;
                if (objectsAt == 0f && (objectRenderer == null || !objectRenderer.IsLoading))
                    objectsAt = now;
                if (outlinesAt == 0f && !outlinesBuilding)
                    outlinesAt = now;
                if (groundAt > 0f && objectsAt > 0f && outlinesAt > 0f)
                    break;
                yield return null;
            }
            Line("Loaded after opening: ground " + BenchmarkSeconds(groundAt - benchmarkOpenStartedAt) + ", objects " + BenchmarkSeconds(objectsAt - benchmarkOpenStartedAt) +
                 ", outlines " + BenchmarkSeconds(outlinesAt - benchmarkOpenStartedAt));
            for (var frame = 0; frame < 10; frame++)
                yield return null;
            Line(BenchmarkMemory());

            // Frames as fast as they go, so the times show the work rather than the display rate.
            var vSync = QualitySettings.vSyncCount;
            var frameRate = Application.targetFrameRate;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 1000;
            var mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
            var renderThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Render Thread", 1);
            var allocated = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            var phases = new List<BenchmarkPhase>();

            // From above: still, then panning at four scales around the busiest tile, or the -view tile.
            var tile = BenchmarkTile();
            if (mapCanvas.ThreeDimensional)
                ToggleThreeDimensional();
            mapCanvas.ShowArea(tile, currentSummary.TileSizeMeters / 2.0, currentSummary.TileSizeMeters / 2.0, 0.4f);
            yield return MeasureFrames(phases, "2D still at 0.4 m/px", null, mainThread, renderThread, allocated);
            foreach (var scale in new[] { 0.1f, 0.4f, 2f, 8f })
            {
                var step = scale * 4.0;
                yield return MeasureFrames(phases, "2D pan at " + scale.ToString("0.#", CultureInfo.InvariantCulture) + " m/px",
                    frame => mapCanvas.ShowArea(tile, currentSummary.TileSizeMeters / 2.0 + (frame - BenchmarkFrames / 2) * step,
                        currentSummary.TileSizeMeters / 2.0, scale), mainThread, renderThread, allocated);
            }

            // In 3D: turning around the middle of the tile close up and from afar; panning left the view beside it.
            mapCanvas.ShowArea(tile, currentSummary.TileSizeMeters / 2.0, currentSummary.TileSizeMeters / 2.0, 0.4f);
            yield return null;
            ToggleThreeDimensional();
            foreach (var distance in new[] { 300f, 1500f })
                yield return MeasureFrames(phases, "3D orbit at " + distance.ToString("0", CultureInfo.InvariantCulture) + " m",
                    frame => mapCanvas.SetOrbit(frame * 3f, 35f, distance), mainThread, renderThread, allocated);

            // Picking in 3D: rays through a 5 x 5 grid over the view, made as the map view makes them for clicks.
            mapCanvas.SetOrbit(0f, 35f, 300f);
            for (var frame = 0; frame < 5; frame++)
                yield return null;
            if (objectRenderer != null && groundRenderer != null)
            {
                var target = mapCanvas.OrbitTarget;
                var size = mapCanvas.layout.size;
                var orbit = new MapOrbitCamera(new Vector3(target.x, groundRenderer.HeightAt(target), target.y), mapCanvas.OrbitYaw, mapCanvas.OrbitPitch,
                    mapCanvas.OrbitDistance, size);
                var picks = new List<double>();
                var hits = 0;
                for (var row = 1; row <= 5; row++)
                    for (var column = 1; column <= 5; column++)
                    {
                        var ray = orbit.RayThrough(new Vector2(size.x * column / 6f, size.y * row / 6f));
                        var watch = System.Diagnostics.Stopwatch.StartNew();
                        if (objectRenderer.TryPick(ray, out _, out _, out _))
                            hits++;
                        picks.Add(watch.Elapsed.TotalMilliseconds);
                    }
                Line("3D pick (25 rays, " + hits + " hits): " + BenchmarkSummary(picks));
            }
            ToggleThreeDimensional();

            // The refresh after an object edit, without changing a file, and the reference index a deletion builds. The
            // object renderer builds the edited tile again, as after a real edit.
            if (CanEdit)
            {
                var refreshes = new List<double>();
                var after = new BenchmarkPhase { Name = "Frames after a refresh" };
                var edited = BenchmarkTile();
                for (var repeat = 0; repeat < 10; repeat++)
                {
                    objectRenderer?.InvalidateTile(edited);
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    RefreshAfterChange(null);
                    refreshes.Add(watch.Elapsed.TotalMilliseconds);
                    // The renderers rebuild in their next Update; the first two frames carry that.
                    yield return null;
                    yield return null;
                    after.Frames.Add(Time.unscaledDeltaTime * 1000.0);
                    for (var frame = 0; frame < 5; frame++)
                        yield return null;
                }
                Line("Refresh after an object edit (shell call): " + BenchmarkSummary(refreshes));
                Line("Second frame after that refresh: " + BenchmarkSummary(after.Frames));
                var indexTimes = new List<double>();
                for (var repeat = 0; repeat < 3; repeat++)
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    session.BuildReferenceIndex();
                    indexTimes.Add(watch.Elapsed.TotalMilliseconds);
                }
                Line("Reference index (each deletion or move to another tile): " + BenchmarkSummary(indexTimes));
            }

            mainThread.Dispose();
            renderThread.Dispose();
            allocated.Dispose();
            QualitySettings.vSyncCount = vSync;
            Application.targetFrameRate = frameRate;

            Line("Phase\tframes\tframe avg/p95/max ms\tredraw avg/p95 ms\tmain thread avg ms\trender thread avg ms\tGC alloc KB/frame\tGC runs");
            foreach (var phase in phases)
                Line(phase.Name + "\t" + phase.Frames.Count + "\t" + BenchmarkTriple(phase.Frames) + "\t" +
                     (phase.Draws.Count > 0 ? BenchmarkAverage(phase.Draws).ToString("0.0", CultureInfo.InvariantCulture) + " / " +
                                              BenchmarkPercentile(phase.Draws, 0.95).ToString("0.0", CultureInfo.InvariantCulture) + " (" + phase.Draws.Count + ")"
                         : "-") + "\t" +
                     BenchmarkOptional(phase.MainThread) + "\t" + BenchmarkOptional(phase.RenderThread) + "\t" + BenchmarkOptional(phase.Allocated) + "\t" + phase.Collections);
            Line(BenchmarkMemory());

            try
            {
                File.WriteAllText(reportPath, report.ToString());
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                Debug.LogWarning("Benchmark: the report could not be written: " + exception.Message);
            }
        }

        /// <summary>The tile of the -view argument, or else the one with the most objects.</summary>
        private OmsiTileSummary BenchmarkTile()
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "-view");
            if (index >= 0 && index + 2 < args.Length && int.TryParse(args[index + 1], out var x) && int.TryParse(args[index + 2], out var y) &&
                currentSummary.Tiles.Find(candidate => candidate.X == x && candidate.Y == y) is OmsiTileSummary chosen)
                return chosen;
            OmsiTileSummary busiest = null;
            foreach (var tile in currentSummary.Tiles)
                if (busiest == null || tile.Objects.Count > busiest.Objects.Count)
                    busiest = tile;
            return busiest;
        }

        /// <summary>Some frames with an optional change of the view before each, after a few to settle.</summary>
        private IEnumerator MeasureFrames(List<BenchmarkPhase> phases, string name, Action<int> step, ProfilerRecorder mainThread,
            ProfilerRecorder renderThread, ProfilerRecorder allocated)
        {
            step?.Invoke(0);
            for (var frame = 0; frame < 5; frame++)
                yield return null;
            var phase = new BenchmarkPhase { Name = name };
            var collections = GC.CollectionCount(0);
            for (var frame = 0; frame < BenchmarkFrames; frame++)
            {
                var draws = mapCanvas.DrawCount;
                step?.Invoke(frame);
                yield return null;
                phase.Frames.Add(Time.unscaledDeltaTime * 1000.0);
                if (mapCanvas.DrawCount != draws)
                    phase.Draws.Add(mapCanvas.LastDrawMilliseconds);
                // Recorders hold the last finished frame: nanoseconds for the threads, bytes for allocations.
                if (mainThread.Valid && mainThread.LastValue > 0)
                    phase.MainThread.Add(mainThread.LastValue / 1e6);
                if (renderThread.Valid && renderThread.LastValue > 0)
                    phase.RenderThread.Add(renderThread.LastValue / 1e6);
                if (allocated.Valid)
                    phase.Allocated.Add(allocated.LastValue / 1024.0);
            }
            phase.Collections = GC.CollectionCount(0) - collections;
            phases.Add(phase);
        }

        private static string BenchmarkMemory()
        {
            return "Memory: managed used " + BenchmarkMegabytes(Profiler.GetMonoUsedSizeLong()) + " of heap " + BenchmarkMegabytes(Profiler.GetMonoHeapSizeLong()) +
                   ", Unity allocated " + BenchmarkMegabytes(Profiler.GetTotalAllocatedMemoryLong()) + " of reserved " + BenchmarkMegabytes(Profiler.GetTotalReservedMemoryLong()) +
                   ", graphics driver " + BenchmarkMegabytes(Profiler.GetAllocatedMemoryForGraphicsDriver());
        }

        private static string BenchmarkMegabytes(long bytes)
        {
            return bytes > 0 ? (bytes / 1048576.0).ToString("0", CultureInfo.InvariantCulture) + " MB" : "n/a";
        }

        private static string BenchmarkSeconds(float seconds)
        {
            return seconds.ToString("0.00", CultureInfo.InvariantCulture) + " s";
        }

        private static string BenchmarkSummary(List<double> values)
        {
            return "avg " + BenchmarkAverage(values).ToString("0.0", CultureInfo.InvariantCulture) + " ms, p95 " +
                   BenchmarkPercentile(values, 0.95).ToString("0.0", CultureInfo.InvariantCulture) + " ms, max " +
                   BenchmarkPercentile(values, 1.0).ToString("0.0", CultureInfo.InvariantCulture) + " ms";
        }

        private static string BenchmarkTriple(List<double> values)
        {
            return BenchmarkAverage(values).ToString("0.0", CultureInfo.InvariantCulture) + " / " + BenchmarkPercentile(values, 0.95).ToString("0.0", CultureInfo.InvariantCulture) +
                   " / " + BenchmarkPercentile(values, 1.0).ToString("0.0", CultureInfo.InvariantCulture);
        }

        private static string BenchmarkOptional(List<double> values)
        {
            return values.Count > 0 ? BenchmarkAverage(values).ToString("0.00", CultureInfo.InvariantCulture) : "n/a";
        }

        private static double BenchmarkAverage(List<double> values)
        {
            if (values.Count == 0)
                return 0.0;
            var sum = 0.0;
            foreach (var value in values)
                sum += value;
            return sum / values.Count;
        }

        private static double BenchmarkPercentile(List<double> values, double fraction)
        {
            if (values.Count == 0)
                return 0.0;
            var sorted = new List<double>(values);
            sorted.Sort();
            var index = Math.Max(0, (int)Math.Ceiling(fraction * sorted.Count) - 1);
            return sorted[Math.Min(sorted.Count - 1, index)];
        }
    }
}
