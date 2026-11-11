using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace OmsiMapEditor.OMSI
{
    /// <summary>Whether a role names the movements that start in a left-turn lane of their own, the others, or both.</summary>
    public enum OmsiSignalRoleLane
    {
        Any,
        OwnLane,
        SharedLane
    }

    /// <summary>
    /// What a signal group of a template controls, without any object ID: the movements that come in by arm <see cref="Arm"/>
    /// (counted from 0 as the crossing numbers its approaches) and turn <see cref="Turn"/>, optionally only those from a
    /// left-turn lane of their own or only the others.
    /// </summary>
    public readonly struct OmsiSignalRole : IEquatable<OmsiSignalRole>
    {
        public OmsiSignalRole(int arm, OmsiMovementTurn turn, OmsiSignalRoleLane lane = OmsiSignalRoleLane.Any)
        {
            Arm = arm;
            Turn = turn;
            Lane = lane;
        }

        public int Arm { get; }
        public OmsiMovementTurn Turn { get; }
        public OmsiSignalRoleLane Lane { get; }

        public bool Matches(OmsiSignalMovement movement, bool ownLane)
        {
            return movement.Approach == Arm && movement.Turn == Turn &&
                   (Lane == OmsiSignalRoleLane.Any || (Lane == OmsiSignalRoleLane.OwnLane) == ownLane);
        }

        public bool Equals(OmsiSignalRole other) => Arm == other.Arm && Turn == other.Turn && Lane == other.Lane;
        public override bool Equals(object obj) => obj is OmsiSignalRole other && Equals(other);
        public override int GetHashCode() => (Arm * 31 + (int)Turn) * 31 + (int)Lane;
    }

    /// <summary>
    /// A traffic light program that can be kept and applied to another crossing (ADR 085, milestone 6; §12 of the brief): the
    /// cycle, the lights with their phases and stop points, and for every light the <see cref="OmsiSignalRole"/>s it
    /// controls. Nothing in it names an object or a path, so it fits any crossing with the arms it needs; applying it asks
    /// <see cref="OmsiSignalTemplateMatch"/> whether the crossing fits.
    /// </summary>
    public sealed class OmsiSignalTemplate
    {
        public const int FormatVersion = 1;

        /// <summary>The id of a built-in template (its texts come from the locales), or null for a template of the user.</summary>
        public string Id { get; set; }

        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        /// <summary>Cycle, lights and stop points; never bound to paths.</summary>
        public OmsiSignalProgram Program { get; set; } = new OmsiSignalProgram();

        /// <summary>What each light of <see cref="Program"/> controls, in the same order.</summary>
        public List<List<OmsiSignalRole>> Roles { get; } = new List<List<OmsiSignalRole>>();

        public bool BuiltIn => Id != null;

        /// <summary>The arms a crossing needs to have for every role to find its movements.</summary>
        public int ArmsNeeded => Roles.SelectMany(roles => roles).Select(role => role.Arm + 1).DefaultIfEmpty(0).Max();

        public OmsiSignalTemplate Copy()
        {
            var copy = new OmsiSignalTemplate { Id = Id, Name = Name, Description = Description, Program = Program.Copy() };
            copy.Program.PathLights.Clear();
            foreach (var roles in Roles)
                copy.Roles.Add(new List<OmsiSignalRole>(roles));
            return copy;
        }

        /// <summary>Checks that the template can be applied: a program that can be written, and one list of roles per light.</summary>
        /// <exception cref="InvalidDataException">The template cannot be used; the message says why.</exception>
        public void Validate()
        {
            if (Program == null || Program.Lights.Count == 0)
                throw new InvalidDataException("The template has no signal groups.");
            if (Roles.Count != Program.Lights.Count)
                throw new InvalidDataException("The template has " + Program.Lights.Count + " signal groups but " + Roles.Count + " lists of movements.");
            try
            {
                Program.Validate();
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException(exception.Message);
            }
            foreach (var role in Roles.SelectMany(roles => roles))
                if (role.Arm < 0 || role.Turn == OmsiMovementTurn.Pedestrian)
                    throw new InvalidDataException("A signal group names a movement that cannot be bound.");
        }

        /// <summary>
        /// A template from the program of a crossing as it is: what each light controls, by arm, turn and lane kind. The
        /// second value counts movements the roles cannot tell apart from others of the same arm and turn (they then share
        /// the group of the first); footpaths cannot be bound and are left out.
        /// </summary>
        public static (OmsiSignalTemplate Template, int Imprecise) FromCrossing(string name, string description, OmsiSignalProgram program,
            OmsiSignalIntersection crossing, ICollection<int> ownLaneMovements)
        {
            if (program == null)
                throw new ArgumentNullException(nameof(program));
            if (crossing == null)
                throw new ArgumentNullException(nameof(crossing));
            bool Own(OmsiSignalMovement movement) => ownLaneMovements != null && ownLaneMovements.Contains(movement.Index);
            var template = new OmsiSignalTemplate { Name = (name ?? string.Empty).Trim(), Description = (description ?? string.Empty).Trim(), Program = program.Copy() };
            template.Program.PathLights.Clear();
            var imprecise = 0;
            var vehicles = crossing.Movements.Where(movement => movement.Approach >= 0 && movement.Turn != OmsiMovementTurn.Pedestrian).ToList();
            for (var light = 0; light < program.Lights.Count; light++)
            {
                var roles = new List<OmsiSignalRole>();
                foreach (var group in vehicles.Where(movement => movement.Light == light).GroupBy(movement => (movement.Approach, movement.Turn)))
                {
                    var all = vehicles.Where(movement => movement.Approach == group.Key.Approach && movement.Turn == group.Key.Turn).ToList();
                    var mine = group.ToList();
                    var others = all.Where(movement => movement.Light != light).ToList();
                    if (others.Count == 0)
                        roles.Add(new OmsiSignalRole(group.Key.Approach, group.Key.Turn));
                    else if (mine.All(Own) && others.All(movement => !Own(movement)))
                        roles.Add(new OmsiSignalRole(group.Key.Approach, group.Key.Turn, OmsiSignalRoleLane.OwnLane));
                    else if (mine.All(movement => !Own(movement)) && others.All(Own))
                        roles.Add(new OmsiSignalRole(group.Key.Approach, group.Key.Turn, OmsiSignalRoleLane.SharedLane));
                    else
                    {
                        roles.Add(new OmsiSignalRole(group.Key.Approach, group.Key.Turn));
                        imprecise += mine.Count;
                    }
                }
                template.Roles.Add(roles.OrderBy(role => role.Arm).ThenBy(role => role.Turn).ThenBy(role => role.Lane).ToList());
            }
            return (template, imprecise);
        }

        // ---- text ---------------------------------------------------------------------------------------------------------

        /// <summary>The template as the text of a file (JSON, one key per line).</summary>
        public string ToText()
        {
            var text = new StringBuilder();
            text.Append("{\n  \"formatVersion\": ").Append(FormatVersion).Append(",\n");
            text.Append("  \"name\": ").Append(Quote(Name)).Append(",\n");
            text.Append("  \"description\": ").Append(Quote(Description)).Append(",\n");
            text.Append("  \"cycle\": ").Append(Number(Program.CycleSeconds ?? 0)).Append(",\n  \"lights\": [\n");
            for (var index = 0; index < Program.Lights.Count; index++)
            {
                var light = Program.Lights[index];
                text.Append("    { \"name\": ").Append(Quote(light.Name));
                if (light.ApproachMeters.HasValue)
                    text.Append(", \"approachMeters\": ").Append(Number(light.ApproachMeters.Value));
                text.Append(", \"phases\": [").Append(string.Join(", ", light.Phases.Select(phase =>
                    "[" + phase.State.ToString(CultureInfo.InvariantCulture) + ", " + Number(phase.Seconds) + "]"))).Append("], \"roles\": [");
                text.Append(string.Join(", ", Roles[index].Select(role => "{ \"arm\": " + role.Arm.ToString(CultureInfo.InvariantCulture) + ", \"turn\": " +
                    Quote(role.Turn.ToString().ToLowerInvariant()) + ", \"lane\": " + Quote(role.Lane.ToString().ToLowerInvariant()) + " }")));
                text.Append("] }").Append(index + 1 < Program.Lights.Count ? ",\n" : "\n");
            }
            text.Append("  ],\n  \"holds\": [");
            text.Append(string.Join(", ", Program.Holds.Select(hold => "{ \"light\": " + hold.Light.ToString(CultureInfo.InvariantCulture) + ", \"seconds\": " +
                Number(hold.Seconds) + ", \"ifNoRequest\": " + (hold.IfNoRequest ? "true" : "false") +
                (hold.JumpTo.HasValue ? ", \"jumpTo\": " + Number(hold.JumpTo.Value) : string.Empty) + " }")));
            text.Append("]\n}\n");
            return text.ToString();
        }

        /// <exception cref="InvalidDataException">The text is no template; the message names what is wrong.</exception>
        public static OmsiSignalTemplate FromText(string text)
        {
            if (text == null)
                throw new ArgumentNullException(nameof(text));
            object root;
            try
            {
                root = OmsiJson.Parse(text);
            }
            catch (InvalidDataException exception)
            {
                throw new InvalidDataException("The file is not a template: " + exception.Message);
            }
            if (!(root is Dictionary<string, object> map))
                throw new InvalidDataException("The file is not a template: it does not hold an object.");
            var version = Number(map, "formatVersion", 0);
            if (version < 1 || version > FormatVersion)
                throw new InvalidDataException("The template has format version " + version.ToString(CultureInfo.InvariantCulture) + ", which this program does not know.");
            var template = new OmsiSignalTemplate
            {
                Name = Text(map, "name"), Description = Text(map, "description"),
                Program = new OmsiSignalProgram { CycleSeconds = Number(map, "cycle", 0) }
            };
            if (!map.TryGetValue("lights", out var lightsValue) || !(lightsValue is List<object> lights))
                throw new InvalidDataException("The template has no list of signal groups.");
            foreach (var entry in lights)
            {
                if (!(entry is Dictionary<string, object> lightMap))
                    throw new InvalidDataException("A signal group of the template is not an object.");
                var light = new OmsiSignalLight { Name = Text(lightMap, "name") };
                if (lightMap.TryGetValue("approachMeters", out var meters) && meters is double metres)
                    light.ApproachMeters = metres;
                if (!lightMap.TryGetValue("phases", out var phasesValue) || !(phasesValue is List<object> phases))
                    throw new InvalidDataException("The signal group '" + light.Name + "' has no phases.");
                foreach (var phase in phases)
                {
                    if (!(phase is List<object> pair) || pair.Count != 2 || !(pair[0] is double state) || !(pair[1] is double seconds) || state != Math.Floor(state))
                        throw new InvalidDataException("The signal group '" + light.Name + "' has a phase that is not [state, seconds].");
                    light.Phases.Add(new OmsiSignalPhase((int)state, seconds));
                }
                template.Program.Lights.Add(light);
                var roles = new List<OmsiSignalRole>();
                if (lightMap.TryGetValue("roles", out var rolesValue) && rolesValue is List<object> roleList)
                    foreach (var roleEntry in roleList)
                    {
                        if (!(roleEntry is Dictionary<string, object> roleMap) || !TryParse(Text(roleMap, "turn"), out OmsiMovementTurn turn) ||
                            !TryParse(Text(roleMap, "lane"), out OmsiSignalRoleLane lane))
                            throw new InvalidDataException("The signal group '" + light.Name + "' names a movement the program does not know.");
                        roles.Add(new OmsiSignalRole((int)Number(roleMap, "arm", -1), turn, lane));
                    }
                template.Roles.Add(roles);
            }
            if (map.TryGetValue("holds", out var holdsValue) && holdsValue is List<object> holds)
                foreach (var entry in holds)
                {
                    if (!(entry is Dictionary<string, object> holdMap))
                        throw new InvalidDataException("A stop point of the template is not an object.");
                    template.Program.Holds.Add(new OmsiSignalHold
                    {
                        Light = (int)Number(holdMap, "light", -1), Seconds = Number(holdMap, "seconds", 0),
                        IfNoRequest = holdMap.TryGetValue("ifNoRequest", out var request) && request is bool flag && flag,
                        JumpTo = holdMap.TryGetValue("jumpTo", out var jump) && jump is double target ? target : (double?)null
                    });
                }
            template.Validate();
            return template;
        }

        private static bool TryParse<T>(string text, out T value) where T : struct
        {
            return Enum.TryParse(text, true, out value) && Enum.IsDefined(typeof(T), value);
        }

        private static string Text(Dictionary<string, object> map, string key)
        {
            return map.TryGetValue(key, out var value) && value is string text ? text : string.Empty;
        }

        private static double Number(Dictionary<string, object> map, string key, double fallback)
        {
            return map.TryGetValue(key, out var value) && value is double number ? number : fallback;
        }

        private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        private static string Quote(string text)
        {
            var result = new StringBuilder("\"");
            foreach (var character in text ?? string.Empty)
            {
                switch (character)
                {
                    case '"': result.Append("\\\""); break;
                    case '\\': result.Append("\\\\"); break;
                    case '\n': result.Append("\\n"); break;
                    case '\r': result.Append("\\r"); break;
                    case '\t': result.Append("\\t"); break;
                    default:
                        if (character < ' ')
                            result.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            result.Append(character);
                        break;
                }
            }
            return result.Append('"').ToString();
        }
    }

    /// <summary>The templates the program brings: the basic crossings of §12 of the brief, with the times of the signal assistant.</summary>
    public static class OmsiSignalTemplates
    {
        public const string TwoPhase = "two_phase";
        public const string FourArms = "four_arms";
        public const string TJunction = "t_junction";
        public const string SeparateLeft = "separate_left";
        public const string PedestrianRequest = "pedestrian_request";

        private const double Green = 20;
        private const double LeftGreen = 10;

        public static IReadOnlyList<OmsiSignalTemplate> BuiltIn { get; } = new List<OmsiSignalTemplate>
        {
            // Two phases: arms 1 and 3 together, then 2 and 4, left turns going with the rest (they give way).
            Phased(TwoPhase, Green, new[] { new[] { Arms(0, 2) }, new[] { Arms(1, 3) } }),
            Phased(FourArms, Green, new[] { new[] { Arms(0) }, new[] { Arms(1) }, new[] { Arms(2) }, new[] { Arms(3) } }),
            Phased(TJunction, Green, new[] { new[] { Arms(0) }, new[] { Arms(1) }, new[] { Arms(2) } }),
            SeparateLeftTemplate(),
            PedestrianTemplate()
        };

        public static OmsiSignalTemplate Find(string id) => BuiltIn.FirstOrDefault(template => template.Id == id);

        private static List<OmsiSignalRole> Arms(params int[] arms)
        {
            return arms.SelectMany(arm => new[] { OmsiMovementTurn.Straight, OmsiMovementTurn.Right, OmsiMovementTurn.Left, OmsiMovementTurn.UTurn }
                .Select(turn => new OmsiSignalRole(arm, turn))).ToList();
        }

        /// <summary>One phase per entry; every entry has one group of roles.</summary>
        private static OmsiSignalTemplate Phased(string id, double green, List<OmsiSignalRole>[][] phases)
        {
            var template = new OmsiSignalTemplate { Id = id };
            var plan = new OmsiSignalPhasePlan { RedYellowSeconds = 1, YellowSeconds = 3, ClearanceSeconds = 2 };
            for (var phase = 0; phase < phases.Length; phase++)
            {
                var planPhase = new OmsiSignalPlanPhase { GreenSeconds = green };
                foreach (var group in phases[phase])
                {
                    planPhase.Lights.Add(template.Program.AddLight(Name(group)));
                    template.Roles.Add(group);
                }
                plan.Phases.Add(planPhase);
            }
            plan.WriteTo(template.Program);
            return template;
        }

        private static string Name(List<OmsiSignalRole> roles, bool left = false)
        {
            return "Arm " + string.Join("+", roles.Select(role => role.Arm + 1).Distinct().OrderBy(arm => arm).Select(arm => arm.ToString(CultureInfo.InvariantCulture))) +
                   (left ? " left" : string.Empty);
        }

        /// <summary>Arms 1 and 3, their left turns from lanes of their own, arms 2 and 4, theirs; the left turns have a phase of their own.</summary>
        private static OmsiSignalTemplate SeparateLeftTemplate()
        {
            List<OmsiSignalRole> Main(params int[] arms) => arms.SelectMany(arm => new[]
            {
                new OmsiSignalRole(arm, OmsiMovementTurn.Straight), new OmsiSignalRole(arm, OmsiMovementTurn.Right),
                new OmsiSignalRole(arm, OmsiMovementTurn.UTurn), new OmsiSignalRole(arm, OmsiMovementTurn.Left, OmsiSignalRoleLane.SharedLane)
            }).ToList();
            List<OmsiSignalRole> Left(params int[] arms) => arms.Select(arm => new OmsiSignalRole(arm, OmsiMovementTurn.Left, OmsiSignalRoleLane.OwnLane)).ToList();
            var template = new OmsiSignalTemplate { Id = SeparateLeft };
            var plan = new OmsiSignalPhasePlan { RedYellowSeconds = 1, YellowSeconds = 3, ClearanceSeconds = 2 };
            foreach (var (main, left) in new[] { (Main(0, 2), Left(0, 2)), (Main(1, 3), Left(1, 3)) })
            {
                var mainPhase = new OmsiSignalPlanPhase { GreenSeconds = Green };
                mainPhase.Lights.Add(template.Program.AddLight(Name(main)));
                template.Roles.Add(main);
                var leftPhase = new OmsiSignalPlanPhase { GreenSeconds = LeftGreen };
                leftPhase.Lights.Add(template.Program.AddLight(Name(left, true)));
                template.Roles.Add(left);
                plan.Phases.Add(mainPhase);
                plan.Phases.Add(leftPhase);
            }
            plan.WriteTo(template.Program);
            return template;
        }

        /// <summary>
        /// A crossing of two arms for a footpath across the road: the traffic has green until someone asks (a stop point that
        /// holds while nobody asks, after 10 s of green so that the traffic is not stopped at once), then yellow, red for the
        /// people to cross, red and yellow, and green again. The footpath itself has no light: footpaths cannot be bound.
        /// </summary>
        private static OmsiSignalTemplate PedestrianTemplate()
        {
            var template = new OmsiSignalTemplate { Id = PedestrianRequest };
            var roles = Arms(0, 1);
            template.Roles.Add(roles);
            template.Program.AddLight("Arm 1+2");
            var green = OmsiSignalAspects.StateOf(OmsiSignalAspect.Green);
            var yellow = OmsiSignalAspects.StateOf(OmsiSignalAspect.Yellow);
            var red = OmsiSignalAspects.StateOf(OmsiSignalAspect.Red);
            var redYellow = OmsiSignalAspects.StateOf(OmsiSignalAspect.RedYellow);
            template.Program.CycleSeconds = 35;
            OmsiSignalTimeline.SetSegments(template.Program, 0, new List<OmsiSignalSegment>
            {
                new OmsiSignalSegment { Start = 0, End = 20, State = green },
                new OmsiSignalSegment { Start = 20, End = 23, State = yellow },
                new OmsiSignalSegment { Start = 23, End = 34, State = red },
                new OmsiSignalSegment { Start = 34, End = 35, State = redYellow }
            });
            template.Program.Holds.Add(new OmsiSignalHold { Light = 0, Seconds = 10, IfNoRequest = true });
            return template;
        }

        // ---- the user's templates -----------------------------------------------------------------------------------------

        /// <summary>The templates in <paramref name="folder"/> (files ending in .json), by name; files that cannot be read come with the reason.</summary>
        public static (List<OmsiSignalTemplate> Templates, List<(string File, string Problem)> Problems) Load(string folder)
        {
            var templates = new List<OmsiSignalTemplate>();
            var problems = new List<(string, string)>();
            if (folder == null || !Directory.Exists(folder))
                return (templates, problems);
            foreach (var file in Directory.GetFiles(folder, "*.json").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var template = OmsiSignalTemplate.FromText(File.ReadAllText(file, Encoding.UTF8));
                    if (template.Name.Length == 0)
                        template.Name = Path.GetFileNameWithoutExtension(file);
                    templates.Add(template);
                }
                catch (Exception exception) when (exception is InvalidDataException || exception is IOException || exception is UnauthorizedAccessException)
                {
                    problems.Add((Path.GetFileName(file), exception.Message));
                }
            }
            return (templates.OrderBy(template => template.Name, StringComparer.CurrentCultureIgnoreCase).ToList(), problems);
        }

        /// <summary>The file name a template of this name has: letters, digits, space, dash and underscore; others become an underscore.</summary>
        public static string FileNameOf(string name)
        {
            var text = new StringBuilder();
            foreach (var character in (name ?? string.Empty).Trim())
                text.Append(char.IsLetterOrDigit(character) || character == ' ' || character == '-' || character == '_' ? character : '_');
            var result = text.ToString().Trim();
            return result.Length == 0 ? null : result.Length > 80 ? result.Substring(0, 80) : result;
        }

        /// <summary>Writes the template to <paramref name="folder"/> through a temporary file; returns its path.</summary>
        /// <exception cref="ArgumentException">The name gives no file name, or the template cannot be used.</exception>
        public static string Save(string folder, OmsiSignalTemplate template)
        {
            if (template == null)
                throw new ArgumentNullException(nameof(template));
            var fileName = FileNameOf(template.Name);
            if (fileName == null)
                throw new ArgumentException("A template needs a name.");
            try
            {
                template.Validate();
            }
            catch (InvalidDataException exception)
            {
                throw new ArgumentException(exception.Message);
            }
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, fileName + ".json");
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, template.ToText(), new UTF8Encoding(false));
            if (File.Exists(path))
                File.Delete(path);
            File.Move(temporary, path);
            return path;
        }

        public static bool Exists(string folder, string name)
        {
            var fileName = FileNameOf(name);
            return fileName != null && File.Exists(Path.Combine(folder, fileName + ".json"));
        }

        public static void Delete(string folder, string name)
        {
            var fileName = FileNameOf(name);
            if (fileName == null)
                return;
            var path = Path.Combine(folder, fileName + ".json");
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    /// <summary>
    /// Whether a template fits a crossing (§12: "MapWorks must check whether the lanes, signals and movements fit"): which light
    /// each movement gets, and what does not fit, so it can be corrected before the template is applied.
    /// </summary>
    public sealed class OmsiSignalTemplateMatch
    {
        private OmsiSignalTemplateMatch()
        {
        }

        public OmsiSignalTemplate Template { get; private set; }

        /// <summary>The light of every movement of the crossing (-1: none; footpaths always -1).</summary>
        public int[] MovementLights { get; private set; }

        /// <summary>Arms the template names that the crossing does not have.</summary>
        public List<int> MissingArms { get; } = new List<int>();

        /// <summary>Vehicle movements no role of the template covers: they would obey no light.</summary>
        public List<int> Unlit { get; } = new List<int>();

        /// <summary>Lights of the template that no movement of the crossing obeys.</summary>
        public List<int> EmptyLights { get; } = new List<int>();

        /// <summary>Movements two lights claim; the first light has them.</summary>
        public List<int> Contested { get; } = new List<int>();

        /// <summary>True when every vehicle movement has a light, every light has a movement and no arm is missing.</summary>
        public bool Fits => MissingArms.Count == 0 && Unlit.Count == 0 && EmptyLights.Count == 0;

        /// <param name="overrides">Lights the user chose for movements instead (index of the movement; light, or -1 for none).</param>
        public static OmsiSignalTemplateMatch Match(OmsiSignalTemplate template, OmsiSignalIntersection crossing, ICollection<int> ownLaneMovements,
            IReadOnlyDictionary<int, int> overrides = null)
        {
            if (template == null)
                throw new ArgumentNullException(nameof(template));
            if (crossing == null)
                throw new ArgumentNullException(nameof(crossing));
            var match = new OmsiSignalTemplateMatch { Template = template, MovementLights = Enumerable.Repeat(-1, crossing.Movements.Count).ToArray() };
            for (var arm = 0; arm < template.ArmsNeeded; arm++)
                if (arm >= crossing.Approaches.Count && template.Roles.Any(roles => roles.Any(role => role.Arm == arm)))
                    match.MissingArms.Add(arm);
            foreach (var movement in crossing.Movements)
            {
                if (movement.Approach < 0 || movement.Turn == OmsiMovementTurn.Pedestrian)
                    continue;
                if (overrides != null && overrides.TryGetValue(movement.Index, out var chosen))
                {
                    match.MovementLights[movement.Index] = chosen >= 0 && chosen < template.Program.Lights.Count ? chosen : -1;
                    continue;
                }
                var own = ownLaneMovements != null && ownLaneMovements.Contains(movement.Index);
                var claimed = Enumerable.Range(0, template.Roles.Count).Where(light => template.Roles[light].Any(role => role.Matches(movement, own))).ToList();
                if (claimed.Count > 0)
                    match.MovementLights[movement.Index] = claimed[0];
                if (claimed.Count > 1)
                    match.Contested.Add(movement.Index);
            }
            foreach (var movement in crossing.Movements)
                if (movement.Approach >= 0 && movement.Turn != OmsiMovementTurn.Pedestrian && match.MovementLights[movement.Index] < 0)
                    match.Unlit.Add(movement.Index);
            for (var light = 0; light < template.Program.Lights.Count; light++)
                if (!match.MovementLights.Contains(light))
                    match.EmptyLights.Add(light);
            return match;
        }
    }
}
