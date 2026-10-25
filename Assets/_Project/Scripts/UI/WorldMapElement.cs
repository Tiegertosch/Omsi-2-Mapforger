using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// A map of the world from the air to find a place and lay the map on it, as openMapper's slippy map: dragged with any
    /// button, zoomed with the wheel around the pointer, the map's tiles outlined over it and the anchor tile filled. A
    /// click without dragging reports its Web Mercator position. Tiles come through the disk cache, the newest wish first,
    /// and a coarser tile stands in until a finer one is there.
    /// </summary>
    internal sealed class WorldMapElement : VisualElement
    {
        private const int MaxTextures = 400;
        private const int Workers = 3;

        private static readonly CustomStyleProperty<Color> OutlineProperty = new CustomStyleProperty<Color>("--world-map-outline");
        private static readonly CustomStyleProperty<Color> AnchorProperty = new CustomStyleProperty<Color>("--world-map-anchor");

        private readonly VisualElement tileLayer = new VisualElement();
        private readonly VisualElement outlineLayer = new VisualElement();
        private readonly Dictionary<TileId, TileEntry> tiles = new Dictionary<TileId, TileEntry>();
        private readonly List<VisualElement> shown = new List<VisualElement>();
        private readonly ConcurrentQueue<KeyValuePair<TileId, byte[]>> fetched = new ConcurrentQueue<KeyValuePair<TileId, byte[]>>();
        private readonly List<TileId> wanted = new List<TileId>();
        private readonly object wantedLock = new object();
        private readonly SemaphoreSlim wishes = new SemaphoreSlim(0);
        private readonly CancellationTokenSource stop = new CancellationTokenSource();

        private OmsiTileFetcher fetcher;
        private OmsiImagerySource source;
        private bool started;
        private int frame;
        private Vector2? pressedAt;
        private Vector2 lastPointer;
        private bool dragged;
        private Color outlineColor = Color.white;
        private Color anchorColor = new Color(0.94f, 0.54f, 0.2f);

        public WorldMapElement()
        {
            AddToClassList("world-map");
            style.overflow = Overflow.Hidden;
            tileLayer.pickingMode = PickingMode.Ignore;
            tileLayer.style.position = Position.Absolute;
            tileLayer.style.left = tileLayer.style.top = tileLayer.style.right = tileLayer.style.bottom = 0;
            Add(tileLayer);
            outlineLayer.pickingMode = PickingMode.Ignore;
            outlineLayer.style.position = Position.Absolute;
            outlineLayer.style.left = outlineLayer.style.top = outlineLayer.style.right = outlineLayer.style.bottom = 0;
            outlineLayer.generateVisualContent += DrawOutlines;
            Add(outlineLayer);

            RegisterCallback<GeometryChangedEvent>(_ => Relayout());
            RegisterCallback<CustomStyleResolvedEvent>(evt =>
            {
                if (evt.customStyle.TryGetValue(OutlineProperty, out var outline))
                    outlineColor = outline;
                if (evt.customStyle.TryGetValue(AnchorProperty, out var anchor))
                    anchorColor = anchor;
                outlineLayer.MarkDirtyRepaint();
            });
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<AttachToPanelEvent>(_ => schedule.Execute(TakeFetched).Every(30));
            RegisterCallback<DetachFromPanelEvent>(_ => ForgetTextures());
        }

        /// <summary>A click on the map without dragging, at its Web Mercator position.</summary>
        public event Action<OmsiMapPoint> Clicked;

        /// <summary>The Web Mercator position at the middle of the element, x east and y south from 0 to 1.</summary>
        public OmsiMapPoint Centre { get; private set; } = new OmsiMapPoint(0.5, 0.36);

        public double Zoom { get; private set; } = 3.0;

        /// <summary>The map's tiles as corner rings in Web Mercator, the first filled as the anchor.</summary>
        public List<OmsiMapPoint[]> Outlines { get; } = new List<OmsiMapPoint[]>();

        /// <summary>The tile filled as the anchor, or -1 for none.</summary>
        public int AnchorOutline { get; set; } = -1;

        /// <summary>A place marked with a dot, as one found by name; null for none.</summary>
        public OmsiMapPoint? Marker { get; set; }

        public void SetSource(OmsiTileFetcher tileFetcher, OmsiImagerySource imagerySource)
        {
            if (fetcher == tileFetcher && source == imagerySource)
                return;
            fetcher = tileFetcher;
            source = imagerySource;
            ForgetTextures();
            Relayout();
        }

        public void LookAt(OmsiMapPoint mercator, double zoom)
        {
            Centre = new OmsiMapPoint(Clamp01(mercator.X), Clamp01(mercator.Y));
            Zoom = Math.Max(1.0, Math.Min(21.0, zoom));
            Relayout();
        }

        /// <summary>The outlines changed: draws them again.</summary>
        public void RefreshOutlines()
        {
            outlineLayer.MarkDirtyRepaint();
        }

        public void Shutdown()
        {
            stop.Cancel();
            ForgetTextures();
        }

        private double Scale => 256.0 * Math.Pow(2.0, Zoom);

        private Vector2 ToLocal(OmsiMapPoint mercator)
        {
            var size = contentRect.size;
            return new Vector2((float)((mercator.X - Centre.X) * Scale + size.x * 0.5), (float)((mercator.Y - Centre.Y) * Scale + size.y * 0.5));
        }

        private OmsiMapPoint ToMercator(Vector2 local)
        {
            var size = contentRect.size;
            return new OmsiMapPoint(Centre.X + (local.x - size.x * 0.5) / Scale, Centre.Y + (local.y - size.y * 0.5) / Scale);
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            pressedAt = evt.localPosition;
            lastPointer = evt.localPosition;
            dragged = false;
            this.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (!pressedAt.HasValue || !this.HasPointerCapture(evt.pointerId))
                return;
            var position = (Vector2)evt.localPosition;
            if ((position - pressedAt.Value).sqrMagnitude > 16f)
                dragged = true;
            if (!dragged)
                return;
            var delta = position - lastPointer;
            lastPointer = position;
            Centre = new OmsiMapPoint(Clamp01(Centre.X - delta.x / Scale), Clamp01(Centre.Y - delta.y / Scale));
            Relayout();
            evt.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (!this.HasPointerCapture(evt.pointerId))
                return;
            this.ReleasePointer(evt.pointerId);
            if (pressedAt.HasValue && !dragged)
                Clicked?.Invoke(ToMercator(evt.localPosition));
            pressedAt = null;
            evt.StopPropagation();
        }

        private void OnWheel(WheelEvent evt)
        {
            var at = ToMercator(evt.localMousePosition);
            var before = ToLocal(at);
            Zoom = Math.Max(1.0, Math.Min(21.0, Zoom - evt.delta.y / 6.0));
            var after = ToLocal(at);
            Centre = new OmsiMapPoint(Clamp01(Centre.X + (after.x - before.x) / Scale), Clamp01(Centre.Y + (after.y - before.y) / Scale));
            Relayout();
            // The wheel over the map is the map's: the panel around it must not scroll with it.
            evt.StopPropagation();
        }

        /// <summary>Places the tiles under the element for the zoom shown, a coarser one where a finer is not there yet.</summary>
        private void Relayout()
        {
            outlineLayer.MarkDirtyRepaint();
            var size = contentRect.size;
            if (source == null || fetcher == null || size.x < 1f || size.y < 1f || float.IsNaN(size.x))
                return;
            frame++;
            foreach (var element in shown)
                element.RemoveFromHierarchy();
            shown.Clear();
            var zoom = (int)Math.Max(0, Math.Min(source.MaxZoom, Math.Round(Zoom)));
            var count = 1L << zoom;
            var low = ToMercator(Vector2.zero);
            var high = ToMercator(size);
            int Cell(double value)
            {
                return (int)Math.Max(0, Math.Min(count - 1, Math.Floor(value * count)));
            }
            var stand = new List<TileId>();
            var standSeen = new HashSet<TileId>();
            var fine = new List<TileId>();
            var missing = new List<TileId>();
            for (var y = Cell(low.Y); y <= Cell(high.Y); y++)
                for (var x = Cell(low.X); x <= Cell(high.X); x++)
                {
                    var id = new TileId(zoom, x, y);
                    if (tiles.TryGetValue(id, out var entry))
                    {
                        entry.Used = frame;
                        if (entry.Texture != null)
                        {
                            fine.Add(id);
                            continue;
                        }
                    }
                    else
                    {
                        tiles[id] = new TileEntry { Used = frame };
                        missing.Add(id);
                    }
                    // Meanwhile a coarser tile, enlarged.
                    for (var up = 1; up <= Math.Min(6, zoom); up++)
                    {
                        var parent = new TileId(zoom - up, x >> up, y >> up);
                        if (tiles.TryGetValue(parent, out var coarse) && coarse.Texture != null)
                        {
                            coarse.Used = frame;
                            if (standSeen.Add(parent))
                                stand.Add(parent);
                            break;
                        }
                    }
                }
            stand.Sort((a, b) => a.Zoom.CompareTo(b.Zoom));
            foreach (var id in stand)
                Show(id);
            foreach (var id in fine)
                Show(id);
            if (missing.Count > 0)
                Wish(missing);
            if (tiles.Count > MaxTextures)
                Trim();
        }

        private void Show(TileId id)
        {
            var count = (double)(1L << id.Zoom);
            var corner = ToLocal(new OmsiMapPoint(id.X / count, id.Y / count));
            var far = ToLocal(new OmsiMapPoint((id.X + 1) / count, (id.Y + 1) / count));
            var element = new VisualElement { pickingMode = PickingMode.Ignore };
            element.style.position = Position.Absolute;
            element.style.left = Mathf.Floor(corner.x);
            element.style.top = Mathf.Floor(corner.y);
            element.style.width = Mathf.Ceil(far.x) - Mathf.Floor(corner.x);
            element.style.height = Mathf.Ceil(far.y) - Mathf.Floor(corner.y);
            element.style.backgroundImage = new StyleBackground(tiles[id].Texture);
            tileLayer.Add(element);
            shown.Add(element);
        }

        /// <summary>Hands the tiles wanted now to the workers; wishes not fetched yet are forgotten, as the view moved on.</summary>
        private void Wish(List<TileId> ids)
        {
            if (!started)
            {
                started = true;
                for (var worker = 0; worker < Workers; worker++)
                    Task.Run(Work);
            }
            lock (wantedLock)
            {
                foreach (var old in wanted)
                    if (tiles.TryGetValue(old, out var entry) && entry.Texture == null && !entry.Missing)
                        tiles.Remove(old);
                wanted.Clear();
                wanted.AddRange(ids);
            }
            wishes.Release(ids.Count);
        }

        private void Work()
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    wishes.Wait(stop.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                TileId id;
                OmsiImagerySource from;
                lock (wantedLock)
                {
                    if (wanted.Count == 0)
                        continue;
                    // The newest wish first: what is on the screen now.
                    id = wanted[wanted.Count - 1];
                    wanted.RemoveAt(wanted.Count - 1);
                    from = source;
                }
                byte[] bytes = null;
                try
                {
                    bytes = fetcher.Tile(from, id.Zoom, id.X, id.Y, stop.Token);
                }
                catch (Exception exception) when (!(exception is OperationCanceledException))
                {
                    Debug.LogWarning("World map tile " + id.Zoom + "/" + id.X + "/" + id.Y + ": " + exception.Message);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                fetched.Enqueue(new KeyValuePair<TileId, byte[]>(id, bytes));
            }
        }

        /// <summary>Turns fetched tiles into textures, a few each pass, and shows them.</summary>
        private void TakeFetched()
        {
            var changed = false;
            var begun = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - begun < 0.01f && fetched.TryDequeue(out var pair))
            {
                if (!tiles.TryGetValue(pair.Key, out var entry))
                    entry = tiles[pair.Key] = new TileEntry { Used = frame };
                if (pair.Value == null)
                {
                    entry.Missing = true;
                    continue;
                }
                var texture = new Texture2D(2, 2, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp, name = "World map tile" };
                if (texture.LoadImage(pair.Value, true))
                {
                    if (entry.Texture != null)
                        UnityEngine.Object.Destroy(entry.Texture);
                    entry.Texture = texture;
                    changed = true;
                }
                else
                {
                    UnityEngine.Object.Destroy(texture);
                    entry.Missing = true;
                }
            }
            if (changed)
                Relayout();
        }

        /// <summary>Keeps the textures used most recently.</summary>
        private void Trim()
        {
            var used = new List<KeyValuePair<int, TileId>>();
            foreach (var pair in tiles)
                if (pair.Value.Texture != null && pair.Value.Used != frame)
                    used.Add(new KeyValuePair<int, TileId>(pair.Value.Used, pair.Key));
            used.Sort((a, b) => a.Key.CompareTo(b.Key));
            for (var index = 0; index < used.Count && tiles.Count > MaxTextures * 3 / 4; index++)
            {
                UnityEngine.Object.Destroy(tiles[used[index].Value].Texture);
                tiles.Remove(used[index].Value);
            }
        }

        private void ForgetTextures()
        {
            foreach (var element in shown)
                element.RemoveFromHierarchy();
            shown.Clear();
            foreach (var entry in tiles.Values)
                if (entry.Texture != null)
                    UnityEngine.Object.Destroy(entry.Texture);
            tiles.Clear();
            lock (wantedLock)
                wanted.Clear();
        }

        private void DrawOutlines(MeshGenerationContext context)
        {
            var painter = context.painter2D;
            for (var index = 0; index < Outlines.Count; index++)
            {
                var ring = Outlines[index];
                if (ring == null || ring.Length < 3)
                    continue;
                painter.BeginPath();
                painter.MoveTo(ToLocal(ring[0]));
                for (var corner = 1; corner < ring.Length; corner++)
                    painter.LineTo(ToLocal(ring[corner]));
                painter.ClosePath();
                if (index == AnchorOutline)
                {
                    painter.fillColor = new Color(anchorColor.r, anchorColor.g, anchorColor.b, 0.35f);
                    painter.Fill();
                }
                painter.strokeColor = index == AnchorOutline ? anchorColor : new Color(outlineColor.r, outlineColor.g, outlineColor.b, 0.8f);
                painter.lineWidth = index == AnchorOutline ? 2f : 1f;
                painter.Stroke();
            }
            if (Marker.HasValue)
            {
                var at = ToLocal(Marker.Value);
                painter.BeginPath();
                painter.Arc(at, 6f, 0f, 360f);
                painter.fillColor = anchorColor;
                painter.Fill();
                painter.strokeColor = outlineColor;
                painter.lineWidth = 2f;
                painter.Stroke();
            }
        }

        private static double Clamp01(double value)
        {
            return Math.Max(0.0, Math.Min(1.0, value));
        }

        private readonly struct TileId : IEquatable<TileId>
        {
            public TileId(int zoom, int x, int y)
            {
                Zoom = zoom;
                X = x;
                Y = y;
            }

            public readonly int Zoom;
            public readonly int X;
            public readonly int Y;

            public bool Equals(TileId other)
            {
                return Zoom == other.Zoom && X == other.X && Y == other.Y;
            }

            public override bool Equals(object obj)
            {
                return obj is TileId other && Equals(other);
            }

            public override int GetHashCode()
            {
                return (Zoom * 397 ^ X) * 397 ^ Y;
            }
        }

        private sealed class TileEntry
        {
            public Texture2D Texture;
            public bool Missing;
            public int Used;
        }
    }
}
