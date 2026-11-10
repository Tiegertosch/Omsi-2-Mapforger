using System;
using System.Collections.Generic;
using System.Linq;

namespace OmsiMapEditor.OMSI
{
    /// <summary>Which way a movement goes through a crossing, from the turn between where it comes in and where it leaves.</summary>
    public enum OmsiMovementTurn
    {
        Straight,
        Left,
        Right,
        UTurn,
        Pedestrian
    }

    /// <summary>
    /// A way through a crossing object (ADR 085, milestone 2): the chain of its [path]s from a path no other leads into to
    /// one that leads nowhere, or a footpath on its own. Vehicles stop at the start of the first path bound to a light, so
    /// the light of a movement is that of the first bound path along its chain.
    /// </summary>
    public sealed class OmsiSignalMovement
    {
        public int Index { get; internal set; }

        /// <summary>The approach it comes from, or -1 (footpaths, objects without two approaches).</summary>
        public int Approach { get; internal set; } = -1;

        /// <summary>Its lane among the approach's lanes, counted from the left as the driver sees them, or -1.</summary>
        public int Lane { get; internal set; } = -1;

        /// <summary>The approach whose side it leaves by, or -1 when none lies near.</summary>
        public int ExitApproach { get; internal set; } = -1;

        public OmsiMovementTurn Turn { get; internal set; }

        /// <summary>The paths of the object it runs along, in order.</summary>
        public List<int> Paths { get; } = new List<int>();

        /// <summary>
        /// The path whose binding decides its light: the first bound path of its chain, or without one its first path, where
        /// a vehicle meets the crossing.
        /// </summary>
        public int ControlPath { get; internal set; }

        /// <summary>The light it obeys, or -1.</summary>
        public int Light { get; internal set; } = -1;

        /// <summary>True when paths of its chain obey different lights (a second stop line inside the crossing).</summary>
        public bool HasSeveralLights { get; internal set; }

        public double EntryX { get; internal set; }
        public double EntryY { get; internal set; }
        public double ExitX { get; internal set; }
        public double ExitY { get; internal set; }
    }

    /// <summary>A lane leading into the crossing: the first path of one or more movements.</summary>
    public sealed class OmsiSignalLane
    {
        public int Approach { get; internal set; }

        /// <summary>Its place among the approach's lanes, from the left as the driver sees them.</summary>
        public int Index { get; internal set; }

        public int EntryPath { get; internal set; }
        public List<int> Movements { get; } = new List<int>();
    }

    /// <summary>An approach of the crossing: the arm traffic comes in by (<see cref="OmsiJunctionPriorities.Arms"/>, ADR 047).</summary>
    public sealed class OmsiSignalApproach
    {
        public int Index { get; internal set; }
        public double X { get; internal set; }
        public double Y { get; internal set; }

        /// <summary>The heading traffic comes in with, clockwise from forward.</summary>
        public double InwardDegrees { get; internal set; }

        public List<OmsiSignalLane> Lanes { get; } = new List<OmsiSignalLane>();
    }

    /// <summary>
    /// The approaches, lanes and movements of a crossing object, read from its paths, with the light each movement obeys
    /// in a program. Pure analysis of the .sco: nothing here changes a file.
    /// </summary>
    public sealed class OmsiSignalIntersection
    {
        /// <summary>A chain longer than this is no way through but a loop of paths; it ends there.</summary>
        private const int LongestChain = 64;

        /// <summary>No more ways through than this are followed in one object.</summary>
        private const int MostMovements = 2000;

        /// <summary>The exit of a movement lies at most this far from the approach it leaves by (m).</summary>
        private const double ExitReach = 25.0;

        /// <summary>Movements whose starts lie this close across the approach use the same lane (m).</summary>
        private const double LaneMeters = 0.5;

        private const double StraightDegrees = 30.0;
        private const double UTurnDegrees = 150.0;

        private OmsiSignalIntersection()
        {
        }

        public List<OmsiSignalApproach> Approaches { get; } = new List<OmsiSignalApproach>();
        public List<OmsiSignalMovement> Movements { get; } = new List<OmsiSignalMovement>();

        /// <summary>Bound paths no movement runs along (paths of no chain, such as detector pieces).</summary>
        public List<int> LooseBoundPaths { get; } = new List<int>();

        /// <summary>True when following the ways through stopped at <see cref="MostMovements"/>.</summary>
        public bool Truncated { get; private set; }

        public static OmsiSignalIntersection Analyse(OmsiSceneryObject sceneryObject, OmsiSignalProgram program)
        {
            if (sceneryObject == null)
                throw new ArgumentNullException(nameof(sceneryObject));
            if (program == null)
                throw new ArgumentNullException(nameof(program));
            var result = new OmsiSignalIntersection();
            var paths = sceneryObject.Paths;
            int LightOf(int path) => path < program.PathLights.Count ? program.PathLights[path] : -1;

            // Vehicle paths (roads and rails) and how they lead into one another.
            var ends = new Dictionary<int, OmsiJunctionPriorities.Ends>();
            for (var index = 0; index < paths.Count; index++)
                if (paths[index].Kind != 1 && paths[index].LengthMeters > 0.0 && !double.IsInfinity(paths[index].LengthMeters))
                    ends[index] = OmsiJunctionPriorities.EndsOf(paths[index]);
            var next = ends.Keys.ToDictionary(key => key, key => new List<int>());
            var fed = new HashSet<int>();
            foreach (var from in ends)
            foreach (var to in ends)
                if (from.Key != to.Key &&
                    OmsiJunctionPriorities.Joins(from.Value.ExitX, from.Value.ExitY, from.Value.ExitHeading, to.Value.EntryX, to.Value.EntryY, to.Value.EntryHeading))
                {
                    next[from.Key].Add(to.Key);
                    fed.Add(to.Key);
                }

            var arms = OmsiJunctionPriorities.Arms(sceneryObject, null);
            var armOf = new Dictionary<int, int>();
            for (var arm = 0; arm < arms.Count; arm++)
            {
                result.Approaches.Add(new OmsiSignalApproach { Index = arm, X = arms[arm].X, Y = arms[arm].Y, InwardDegrees = arms[arm].InwardDegrees });
                foreach (var path in arms[arm].Paths)
                    armOf[path] = arm;
            }

            // Every chain from an entry to a path that leads nowhere is a movement.
            foreach (var entry in ends.Keys.Where(key => !fed.Contains(key)).OrderBy(key => key))
            {
                var stack = new List<int> { entry };
                result.Follow(stack, next, ends, armOf, LightOf);
                if (result.Truncated)
                    break;
            }

            // Footpaths cross on their own.
            for (var index = 0; index < paths.Count && !result.Truncated; index++)
            {
                if (paths[index].Kind != 1)
                    continue;
                var path = paths[index];
                var pathEnds = OmsiJunctionPriorities.EndsOf(path);
                var movement = new OmsiSignalMovement
                {
                    Index = result.Movements.Count, Turn = OmsiMovementTurn.Pedestrian, ControlPath = index, Light = LightOf(index),
                    EntryX = pathEnds.EntryX, EntryY = pathEnds.EntryY, ExitX = pathEnds.ExitX, ExitY = pathEnds.ExitY
                };
                movement.Paths.Add(index);
                result.Movements.Add(movement);
            }

            // Lanes: the movements of an approach that start on the same path, ordered from the left.
            foreach (var group in result.Movements.Where(movement => movement.Approach >= 0).GroupBy(movement => movement.Approach))
            {
                var approach = result.Approaches[group.Key];
                var radians = approach.InwardDegrees * Math.PI / 180.0;
                double Across(OmsiSignalMovement movement) =>
                    (movement.EntryX - approach.X) * Math.Cos(radians) - (movement.EntryY - approach.Y) * Math.Sin(radians);
                // Movements that start where another does (within half a metre across) use the same lane, even on paths of
                // their own, as the arcs of a junction made in MapWorks do.
                OmsiSignalLane current = null;
                var currentAcross = double.NaN;
                foreach (var movement in group.OrderBy(Across).ThenBy(movement => movement.Index))
                {
                    var across = Across(movement);
                    if (current == null || Math.Abs(across - currentAcross) > LaneMeters)
                    {
                        current = new OmsiSignalLane { Approach = approach.Index, Index = approach.Lanes.Count, EntryPath = movement.Paths[0] };
                        approach.Lanes.Add(current);
                        currentAcross = across;
                    }
                    movement.Lane = current.Index;
                    current.Movements.Add(movement.Index);
                }
            }

            var onMovements = new HashSet<int>(result.Movements.SelectMany(movement => movement.Paths));
            for (var index = 0; index < program.PathLights.Count; index++)
                if (program.PathLights[index] >= 0 && !onMovements.Contains(index))
                    result.LooseBoundPaths.Add(index);
            return result;
        }

        private void Follow(List<int> chain, Dictionary<int, List<int>> next, Dictionary<int, OmsiJunctionPriorities.Ends> ends, Dictionary<int, int> armOf,
            Func<int, int> lightOf)
        {
            if (Movements.Count >= MostMovements)
            {
                Truncated = true;
                return;
            }
            var last = chain[chain.Count - 1];
            var onward = chain.Count >= LongestChain ? new List<int>() : next[last].Where(path => !chain.Contains(path)).ToList();
            if (onward.Count == 0)
            {
                AddMovement(chain, ends, armOf, lightOf);
                return;
            }
            foreach (var path in onward)
            {
                chain.Add(path);
                Follow(chain, next, ends, armOf, lightOf);
                chain.RemoveAt(chain.Count - 1);
                if (Truncated)
                    return;
            }
        }

        private void AddMovement(List<int> chain, Dictionary<int, OmsiJunctionPriorities.Ends> ends, Dictionary<int, int> armOf, Func<int, int> lightOf)
        {
            var first = ends[chain[0]];
            var last = ends[chain[chain.Count - 1]];
            var movement = new OmsiSignalMovement
            {
                Index = Movements.Count, Approach = armOf.TryGetValue(chain[0], out var arm) ? arm : -1, ControlPath = chain[0],
                EntryX = first.EntryX, EntryY = first.EntryY, ExitX = last.ExitX, ExitY = last.ExitY
            };
            movement.Paths.AddRange(chain);
            var lights = chain.Select(lightOf).Where(light => light >= 0).ToList();
            if (lights.Count > 0)
            {
                movement.Light = lights[0];
                movement.ControlPath = chain.First(path => lightOf(path) >= 0);
                movement.HasSeveralLights = lights.Distinct().Count() > 1;
            }
            var turn = OmsiRoadPlanner.Normalize(last.ExitHeading - first.EntryHeading);
            movement.Turn = Math.Abs(turn) < StraightDegrees ? OmsiMovementTurn.Straight
                : Math.Abs(turn) > UTurnDegrees ? OmsiMovementTurn.UTurn
                : turn > 0.0 ? OmsiMovementTurn.Right : OmsiMovementTurn.Left;
            var nearest = ExitReach;
            foreach (var approach in Approaches)
            {
                var distance = Math.Sqrt((approach.X - last.ExitX) * (approach.X - last.ExitX) + (approach.Y - last.ExitY) * (approach.Y - last.ExitY));
                if (distance < nearest && approach.Index != movement.Approach)
                {
                    nearest = distance;
                    movement.ExitApproach = approach.Index;
                }
            }
            Movements.Add(movement);
        }

        /// <summary>
        /// Binds a movement to light <paramref name="light"/> (or to none with -1) at its control path, the path where its
        /// vehicles stop. Returns every movement the change reaches: those sharing the control path follow too (a shared
        /// lane cannot give its movements different lights at one stop line, §5.2 of the brief).
        /// </summary>
        public List<OmsiSignalMovement> Assign(OmsiSignalProgram program, OmsiSignalMovement movement, int light)
        {
            if (program == null)
                throw new ArgumentNullException(nameof(program));
            if (movement == null)
                throw new ArgumentNullException(nameof(movement));
            if (light < -1 || light >= program.Lights.Count)
                throw new ArgumentOutOfRangeException(nameof(light));
            if (movement.ControlPath >= program.PathLights.Count)
                throw new ArgumentException("The movement's path is not part of the program.", nameof(movement));
            var reached = SharingControl(movement);
            program.PathLights[movement.ControlPath] = light;
            foreach (var other in reached)
                other.Light = light;
            return reached;
        }

        /// <summary>The movements that obey the same path as <paramref name="movement"/>, itself included: one change binds them all.</summary>
        public List<OmsiSignalMovement> SharingControl(OmsiSignalMovement movement)
        {
            return Movements.Where(other => other.ControlPath == movement.ControlPath).ToList();
        }
    }
}
