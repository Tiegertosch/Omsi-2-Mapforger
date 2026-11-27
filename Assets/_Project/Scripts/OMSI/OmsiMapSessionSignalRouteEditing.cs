using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace OmsiMapEditor.OMSI
{
    /// <summary>One optional value of an edit: keep it as it is, set it, or take it out.</summary>
    public readonly struct OmsiSignalRouteFieldEdit<T> where T : struct
    {
        private OmsiSignalRouteFieldEdit(bool changes, T? value)
        {
            Changes = changes;
            Value = value;
        }

        /// <summary>False keeps the value as it is (the default).</summary>
        public bool Changes { get; }

        /// <summary>The new value; null takes the section out.</summary>
        public T? Value { get; }

        public static OmsiSignalRouteFieldEdit<T> Keep => default(OmsiSignalRouteFieldEdit<T>);

        public static OmsiSignalRouteFieldEdit<T> Remove => new OmsiSignalRouteFieldEdit<T>(true, null);

        public static OmsiSignalRouteFieldEdit<T> Set(T value)
        {
            return new OmsiSignalRouteFieldEdit<T>(true, value);
        }
    }

    /// <summary>
    /// What to change on a signal route (ADR 105, M3); every value left as it is stays unchanged. Setting or removing <see cref="SpeedLimit"/> keeps the
    /// aspect in step (with a limit 2, without 1, as on the stock maps and as openOMSI reads it) unless <see cref="Aspect"/> is given as well; an aspect
    /// above 2, whose meaning is open, is left alone.
    /// </summary>
    public sealed class OmsiSignalRouteEdit
    {
        /// <summary>The value of [signalroute], 0 or more.</summary>
        public int? Kind { get; set; }

        /// <summary>The aspect in [signal], 1 or more.</summary>
        public int? Aspect { get; set; }

        /// <summary>[distsignal]: the ID of a signal object of the map.</summary>
        public OmsiSignalRouteFieldEdit<int> DistantSignal { get; set; }

        /// <summary>[nextsignal]: the ID of a signal object of the map.</summary>
        public OmsiSignalRouteFieldEdit<int> NextSignal { get; set; }

        /// <summary>[speedlimit] in km/h, above 0 and at most <see cref="OmsiMapSession.MaximumSignalRouteSpeed"/>.</summary>
        public OmsiSignalRouteFieldEdit<double> SpeedLimit { get; set; }
    }

    public enum OmsiSignalRouteChangeKind
    {
        Edit,
        Deletion,
        /// <summary>A new route, added at the end of the file (ADR 105, M4).</summary>
        Creation
    }

    /// <summary>An edited, deleted or created signal route as one step: signalroutes.cfg before and after, for undo and redo.</summary>
    public sealed class OmsiSignalRouteChange
    {
        internal OmsiSignalRouteChange(OmsiSignalRouteChangeKind kind, int routeIndex)
        {
            Kind = kind;
            RouteIndex = routeIndex;
        }

        public OmsiSignalRouteChangeKind Kind { get; }

        /// <summary>The route's place in <see cref="OmsiSignalRoutes.Routes"/> when the step was made.</summary>
        public int RouteIndex { get; }

        public bool IsApplied { get; internal set; } = true;

        internal List<OmsiObjectChange.FileState> Files { get; } = new List<OmsiObjectChange.FileState>();
    }

    /// <summary>Editing and deleting signal routes (ADR 105, M3): one step each, written through the workspace, the rest of the file byte for byte.</summary>
    public sealed partial class OmsiMapSession
    {
        /// <summary>The highest speed limit a route takes, in km/h.</summary>
        public const double MaximumSignalRouteSpeed = 500.0;

        private Func<string, bool?> signalAsset;
        private bool signalAssetKnown;

        /// <summary>
        /// Whether an object file is a signal (it has [signal]); null when that cannot be read, then only the ID is checked. By default read from the
        /// installation that holds the map. Tools and tests may set their own.
        /// </summary>
        public Func<string, bool?> IsSignalAsset
        {
            get
            {
                if (!signalAssetKnown)
                {
                    var installation = OmsiObjectOutlineCatalog.InstallationRootOfMap(Path.GetDirectoryName(Path.GetFullPath(GlobalConfigPath)));
                    signalAsset = !string.IsNullOrEmpty(installation) && Directory.Exists(installation) ? SignalAssetReader(installation) : null;
                    signalAssetKnown = true;
                }
                return signalAsset;
            }
            set
            {
                signalAsset = value;
                signalAssetKnown = true;
            }
        }

        /// <summary>
        /// Changes a signal route as one undoable step. Optional sections go where the OMSI editor writes them (after the [entry] records, in the order
        /// [distsignal], [nextsignal], [speedlimit], with its blank lines); values that only change are written in place. Null when nothing changes.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">No route has this index.</exception>
        /// <exception cref="ArgumentException">A value is out of range, or a signal ID names no signal of the map.</exception>
        /// <exception cref="OmsiStepRefusedException">The file cannot take the edit (no file, a section twice, an unusual order) or changed meanwhile.</exception>
        /// <param name="summary">The map as it stands, to find the signals by ID; null reads it (on Berlin-Spandau about half a second).</param>
        public OmsiSignalRouteChange UpdateSignalRoute(int routeIndex, OmsiSignalRouteEdit edit, OmsiMapSummary summary = null)
        {
            if (edit == null)
                throw new ArgumentNullException(nameof(edit));
            var routes = EditableSignalRoutes(out var before);
            if (routeIndex < 0 || routeIndex >= routes.Routes.Count)
                throw new ArgumentOutOfRangeException(nameof(routeIndex), routeIndex, "The map has " + routes.Routes.Count + " signal route(s).");
            var route = routes.Routes[routeIndex];
            var label = OmsiSignalRoutes.Describe(route);

            if (edit.Kind.HasValue && edit.Kind.Value < 0)
                throw new ArgumentException(label + ": the kind must be 0 or more, not " + edit.Kind.Value + ".", nameof(edit));
            if (edit.Aspect.HasValue && edit.Aspect.Value < 1)
                throw new ArgumentException(label + ": the aspect must be 1 or more, not " + edit.Aspect.Value + ".", nameof(edit));
            var speed = edit.SpeedLimit.Value;
            if (edit.SpeedLimit.Changes && speed.HasValue &&
                (double.IsNaN(speed.Value) || double.IsInfinity(speed.Value) || speed.Value <= 0.0 || speed.Value > MaximumSignalRouteSpeed))
                throw new ArgumentException(label + ": the speed limit must be above 0 and at most " +
                                            MaximumSignalRouteSpeed.ToString("0", CultureInfo.InvariantCulture) + " km/h.", nameof(edit));
            if ((edit.DistantSignal.Changes && edit.DistantSignal.Value.HasValue) || (edit.NextSignal.Changes && edit.NextSignal.Value.HasValue))
            {
                var places = OmsiSignalRouteMap.Resolve(routes, summary ?? Read(), null, null);
                if (edit.DistantSignal.Changes && edit.DistantSignal.Value.HasValue)
                    CheckSignal(places, edit.DistantSignal.Value.Value, label, "distant signal");
                if (edit.NextSignal.Changes && edit.NextSignal.Value.HasValue)
                    CheckSignal(places, edit.NextSignal.Value.Value, label, "next signal");
            }

            if (edit.Kind.HasValue && route.Kind != edit.Kind.Value)
                SetRouteField(route.RouteRecord, 0, OmsiNumberFormat.Format(edit.Kind.Value), label);

            var aspect = edit.Aspect;
            if (!aspect.HasValue && edit.SpeedLimit.Changes && (route.SpeedLimitRecord != null) != speed.HasValue &&
                (!route.Aspect.HasValue || route.Aspect.Value <= 2))
                aspect = speed.HasValue ? 2 : 1;
            if (aspect.HasValue && route.Aspect != aspect.Value)
            {
                if (route.SignalRecord == null)
                {
                    if (edit.Aspect.HasValue)
                        throw new OmsiStepRefusedException(label + " has no [signal] to give an aspect.");
                }
                else
                    SetRouteField(route.SignalRecord, 1, OmsiNumberFormat.Format(aspect.Value), label);
            }

            var layoutChanges = false;
            layoutChanges |= EditOptional(route.DistantSignalRecord, edit.DistantSignal.Changes, edit.DistantSignal.Value.HasValue,
                edit.DistantSignal.Value.HasValue ? OmsiNumberFormat.Format(edit.DistantSignal.Value.Value) : null, route.DistantSignalId == edit.DistantSignal.Value, label);
            layoutChanges |= EditOptional(route.NextSignalRecord, edit.NextSignal.Changes, edit.NextSignal.Value.HasValue,
                edit.NextSignal.Value.HasValue ? OmsiNumberFormat.Format(edit.NextSignal.Value.Value) : null, route.NextSignalId == edit.NextSignal.Value, label);
            layoutChanges |= EditOptional(route.SpeedLimitRecord, edit.SpeedLimit.Changes, speed.HasValue,
                speed.HasValue ? SpeedText(speed.Value) : null, route.SpeedLimit.HasValue && speed.HasValue && Math.Abs(route.SpeedLimit.Value - speed.Value) < 1e-9, label);
            if (layoutChanges)
            {
                var distant = edit.DistantSignal.Changes ? edit.DistantSignal.Value.HasValue : route.DistantSignalRecord != null;
                var next = edit.NextSignal.Changes ? edit.NextSignal.Value.HasValue : route.NextSignalRecord != null;
                var limited = edit.SpeedLimit.Changes ? speed.HasValue : route.SpeedLimitRecord != null;
                LayOutOptionalSections(routes, route, label,
                    distant ? route.DistantSignalRecord ?? routes.File.CreateRecord("distsignal", new[] { OmsiNumberFormat.Format(edit.DistantSignal.Value.Value) }, 0) : null,
                    next ? route.NextSignalRecord ?? routes.File.CreateRecord("nextsignal", new[] { OmsiNumberFormat.Format(edit.NextSignal.Value.Value) }, 0) : null,
                    limited ? route.SpeedLimitRecord ?? routes.File.CreateRecord("speedlimit", new[] { SpeedText(speed.Value) }, 0) : null);
            }

            return WriteSignalRoutes(OmsiSignalRouteChangeKind.Edit, routeIndex, before, routes.File.ToBytes());
        }

        /// <summary>
        /// Deletes a signal route with its "   n:" line as one undoable step; the routes after it are numbered on when the file numbered every route by
        /// its place, as the OMSI editor writes it. Deleting a route frees its track and signals for deletion (rule 4).
        /// </summary>
        public OmsiSignalRouteChange DeleteSignalRoute(int routeIndex)
        {
            var routes = EditableSignalRoutes(out var before);
            if (routeIndex < 0 || routeIndex >= routes.Routes.Count)
                throw new ArgumentOutOfRangeException(nameof(routeIndex), routeIndex, "The map has " + routes.Routes.Count + " signal route(s).");
            var route = routes.Routes[routeIndex];
            var file = routes.File;
            var numbered = true;
            foreach (var each in routes.Routes)
                numbered &= each.Number == each.Index;

            var first = file.Records.IndexOf(route.RouteRecord);
            var last = route.Records[route.Records.Count - 1];
            var holder = first > 0 ? file.Records[first - 1].Trailer : file.Preamble;
            var comment = OmsiSignalRoutes.CommentLineIndex(holder);
            if (comment >= 0)
                holder.RemoveRange(comment, holder.Count - comment);
            // The lines after the route's blank lines (the next route's "   n:" line and free text) stay.
            holder.AddRange(Rest(last.Trailer));
            foreach (var record in route.Records)
                file.RemoveRecord(record);

            var after = file.ToBytes();
            if (numbered)
                after = Renumbered(after);
            return WriteSignalRoutes(OmsiSignalRouteChangeKind.Deletion, routeIndex, before, after);
        }

        /// <summary>Takes back an edited or deleted signal route; fails without writing when signalroutes.cfg changed since.</summary>
        public void Undo(OmsiSignalRouteChange change)
        {
            if (change == null)
                throw new ArgumentNullException(nameof(change));
            if (!change.IsApplied)
                throw new OmsiStepRefusedException("The signal route step is already taken back.");
            ReplayFiles(change.Files, true);
            change.IsApplied = false;
        }

        /// <summary>Does an edited or deleted signal route again from its stored file.</summary>
        public void Redo(OmsiSignalRouteChange change)
        {
            if (change == null)
                throw new ArgumentNullException(nameof(change));
            if (change.IsApplied)
                throw new OmsiStepRefusedException("The signal route step is already done.");
            ReplayFiles(change.Files, false);
            change.IsApplied = true;
        }

        private OmsiSignalRoutes EditableSignalRoutes(out byte[] bytes)
        {
            var workspace = RequireWorkspace();
            if (!workspace.FileExists(OmsiSignalRoutes.FileName))
                throw new OmsiStepRefusedException("The map has no " + OmsiSignalRoutes.FileName + ".");
            bytes = workspace.ReadAllBytes(OmsiSignalRoutes.FileName);
            var routes = OmsiSignalRoutes.Parse(bytes);
            if (!routes.File.IsExactRoundTrip)
                throw new OmsiStepRefusedException(OmsiSignalRoutes.FileName + " does not read back exactly; it is left as it is.");
            return routes;
        }

        private OmsiSignalRouteChange WriteSignalRoutes(OmsiSignalRouteChangeKind kind, int routeIndex, byte[] before, byte[] after)
        {
            // A new file (before is null) is always a change.
            if (before != null && SameBytes(before, after))
                return null;
            var change = new OmsiSignalRouteChange(kind, routeIndex);
            change.Files.Add(new OmsiObjectChange.FileState { RelativePath = OmsiSignalRoutes.FileName, Before = before, After = after });
            // Only signalroutes.cfg changes, so no path number of a route can move.
            ApplyFiles(change.Files, false, false);
            return change;
        }

        private void CheckSignal(OmsiSignalRouteMap places, int id, string label, string role)
        {
            var place = places.FindElement(id);
            if (place == null)
                throw new ArgumentException(label + ": the map has no element " + id + " for the " + role + ".");
            if (place.Kind == OmsiMapElementKind.Spline)
                throw new ArgumentException(label + ": element " + id + " is a spline, not a signal, and cannot be the " + role + ".");
            var isSignal = IsSignalAsset?.Invoke(place.AssetPath);
            if (isSignal == false)
                throw new ArgumentException(label + ": element " + id + " (" + place.AssetPath + ") is not a signal and cannot be the " + role + ".");
        }

        private static void SetRouteField(OmsiRecord record, int field, string value, string label)
        {
            if (record == null || field >= record.Fields.Count)
                throw new OmsiStepRefusedException(label + ": [" + (record?.Name ?? "?") + "] is incomplete and is left as it is.");
            record.SetField(field, value);
        }

        /// <summary>Writes a changed value in place; true when the section comes or goes, which <see cref="LayOutOptionalSections"/> then does.</summary>
        private static bool EditOptional(OmsiRecord record, bool changes, bool wanted, string text, bool same, string label)
        {
            if (!changes)
                return false;
            if ((record != null) != wanted)
                return true;
            if (record != null && !same)
                SetRouteField(record, 0, text, label);
            return false;
        }

        /// <summary>
        /// Puts the optional sections of a route after its last other section as the OMSI editor writes them: a blank line after the last entry, then
        /// [distsignal] with one blank line, a blank line, [nextsignal] with one blank line, [speedlimit] with one blank line, and a closing blank line.
        /// Only done when a section comes or goes; the "   n:" line of the next route and any free text after the blank lines stay.
        /// </summary>
        private static void LayOutOptionalSections(OmsiSignalRoutes routes, OmsiSignalRoute route, string label, OmsiRecord distant, OmsiRecord next, OmsiRecord limit)
        {
            var file = routes.File;
            var optional = new List<OmsiRecord>();
            OmsiRecord anchor = null;
            foreach (var record in route.Records)
            {
                if (IsOptionalSection(record))
                    optional.Add(record);
                else
                    anchor = record;
            }
            if (anchor == null)
                throw new OmsiStepRefusedException(label + " has no [signalroute] section to edit.");
            foreach (var record in optional)
            {
                if (record != route.DistantSignalRecord && record != route.NextSignalRecord && record != route.SpeedLimitRecord)
                    throw new OmsiStepRefusedException(label + " has [" + record.Name + "] twice; edit signalroutes.cfg by hand first.");
                if (route.Records.IndexOf(record) < route.Records.IndexOf(anchor))
                    throw new OmsiStepRefusedException(label + " has [" + record.Name + "] before its other sections; edit signalroutes.cfg by hand first.");
            }

            var last = route.Records[route.Records.Count - 1];
            foreach (var record in optional)
                if (record != last && Rest(record.Trailer).Count > 0)
                    throw new OmsiStepRefusedException(label + " has text after [" + record.Name + "]; edit signalroutes.cfg by hand first.");
            if (anchor != last && Rest(anchor.Trailer).Count > 0)
                throw new OmsiStepRefusedException(label + " has text after [" + anchor.Name + "]; edit signalroutes.cfg by hand first.");
            var lastLine = last.LastLine;
            var rest = Rest(last.Trailer);
            var endsWithoutBreak = rest.Count == 0 && lastLine.Ending.Length == 0;
            foreach (var record in optional)
                file.RemoveRecord(record);

            var ending = file.DefaultLineEnding;
            var current = anchor;
            current.Trailer.Clear();
            current.Trailer.Add(new OmsiLine(string.Empty, ending));
            current.Trailer.Add(new OmsiLine(string.Empty, ending));
            var place = file.Records.IndexOf(anchor) + 1;
            var sections = new[] { distant, next, limit };
            for (var slot = 0; slot < sections.Length; slot++)
            {
                var record = sections[slot];
                if (record != null)
                {
                    record.Trailer.Clear();
                    file.InsertRecord(place++, record);
                    current = record;
                    current.Trailer.Add(new OmsiLine(string.Empty, ending));
                }
                // The OMSI editor writes a blank line between the place of [distsignal] and [nextsignal], whether or not there is one.
                if (slot == 0)
                    current.Trailer.Add(new OmsiLine(string.Empty, ending));
            }
            current.Trailer.Add(new OmsiLine(string.Empty, ending));
            current.Trailer.AddRange(rest);
            if (endsWithoutBreak)
                current.Trailer[current.Trailer.Count - 1].Ending = string.Empty;
        }

        private static bool IsOptionalSection(OmsiRecord record)
        {
            return record.HasKnownLayout && (string.Equals(record.Name, "distsignal", StringComparison.OrdinalIgnoreCase) ||
                                             string.Equals(record.Name, "nextsignal", StringComparison.OrdinalIgnoreCase) ||
                                             string.Equals(record.Name, "speedlimit", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The lines of a trailer from its first non-blank line on.</summary>
        private static List<OmsiLine> Rest(List<OmsiLine> trailer)
        {
            var first = trailer.FindIndex(line => line.Text.Trim().Length > 0);
            return first < 0 ? new List<OmsiLine>() : trailer.GetRange(first, trailer.Count - first);
        }

        /// <summary>The OMSI editor writes speed limits with three decimals, such as 40.000.</summary>
        private static string SpeedText(double speed)
        {
            return speed.ToString("0.000", CultureInfo.InvariantCulture);
        }

        /// <summary>Gives every "   n:" line the place of its route again, keeping the spaces around the number.</summary>
        private static byte[] Renumbered(byte[] bytes)
        {
            var routes = OmsiSignalRoutes.Parse(bytes);
            var file = routes.File;
            foreach (var route in routes.Routes)
            {
                if (route.Number == route.Index)
                    continue;
                var first = file.Records.IndexOf(route.RouteRecord);
                var holder = first > 0 ? file.Records[first - 1].Trailer : file.Preamble;
                var line = holder[OmsiSignalRoutes.CommentLineIndex(holder)];
                var text = line.Text;
                var start = 0;
                while (start < text.Length && !char.IsDigit(text[start]))
                    start++;
                var end = start;
                while (end < text.Length && char.IsDigit(text[end]))
                    end++;
                line.Text = text.Substring(0, start) + route.Index.ToString(CultureInfo.InvariantCulture) + text.Substring(end);
            }
            return file.ToBytes();
        }

        private static Func<string, bool?> SignalAssetReader(string installation)
        {
            var known = new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase);
            return asset =>
            {
                if (string.IsNullOrWhiteSpace(asset))
                    return null;
                // A search for the next signal asks from a worker thread while the map thread may ask as well.
                lock (known)
                {
                    if (known.TryGetValue(asset, out var cached))
                        return cached;
                }
                bool? result = null;
                try
                {
                    var path = OmsiRelativePath.Resolve(installation, asset);
                    if (File.Exists(path) && asset.EndsWith(".sco", StringComparison.OrdinalIgnoreCase))
                    {
                        result = false;
                        foreach (var record in OmsiRecordFile.Parse(File.ReadAllBytes(path), null).Records)
                            if (string.Equals(record.Name, "signal", StringComparison.OrdinalIgnoreCase))
                                result = true;
                    }
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is FormatException || exception is ArgumentException)
                {
                    // An unreadable file leaves only the ID to check.
                }
                lock (known)
                    known[asset] = result;
                return result;
            };
        }
    }
}
