using System.Collections.Generic;
using System.Linq;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.UI
{
    /// <summary>The problems of the line and track planners in the language of the interface; the English text of a problem stays for the log.</summary>
    public static class LinePlanProblems
    {
        /// <summary>The locale key of a problem code; null for a code without one.</summary>
        public static string Key(OmsiPlanProblemCode code)
        {
            switch (code)
            {
                case OmsiPlanProblemCode.TooFewStops:
                    return "line.problem.too_few_stops";
                case OmsiPlanProblemCode.StopHasNoLane:
                    return "line.problem.stop_no_lane";
                case OmsiPlanProblemCode.NoWay:
                    return "line.problem.no_way";
                case OmsiPlanProblemCode.NoWayOverWaypoints:
                    return "line.problem.no_way_waypoints";
                case OmsiPlanProblemCode.WaypointNotNearLane:
                    return "line.problem.waypoint_no_lane";
                case OmsiPlanProblemCode.LaneOnUnlistedTile:
                    return "line.problem.lane_unlisted_tile";
                case OmsiPlanProblemCode.StopHasNoRail:
                    return "line.problem.stop_no_rail";
                case OmsiPlanProblemCode.WaypointHasNoRail:
                    return "line.problem.waypoint_no_rail";
                case OmsiPlanProblemCode.NoRailWay:
                    return "line.problem.no_rail_way";
                case OmsiPlanProblemCode.NoRailWayFirstToLast:
                    return "line.problem.no_rail_way_first_last";
                default:
                    return null;
            }
        }

        /// <summary>A problem in the language of the interface; the English text when its code has no key.</summary>
        public static string Text(OmsiPlanProblem problem)
        {
            var key = Key(problem.Code);
            if (key == null)
                return problem.ToString();
            var from = problem.FromIsWaypoint ? Loc.T("line.problem.a_waypoint") : problem.From;
            var to = problem.ToIsWaypoint ? Loc.T("line.problem.a_waypoint") : problem.To;
            return Loc.T(key, ("stop", problem.Stop), ("from", from), ("to", to), ("metres", problem.Meters)); // i18n:keys
        }

        /// <summary>All problems of a plan, joined by spaces as the result line shows them.</summary>
        public static string Join(IEnumerable<OmsiPlanProblem> problems)
        {
            return string.Join(" ", problems.Select(Text));
        }
    }
}
