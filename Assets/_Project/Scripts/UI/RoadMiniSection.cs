using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// A small cross-section of a road type for the template cards: one block per element, as wide as the element is relative to the others
    /// (the proportions are true), in the colour of its kind, tram lanes with their rails. A light variant of <see cref="RoadCrossSectionElement"/>
    /// without labels, selection or surfaces; it takes no input, so the card around it receives the clicks.
    /// </summary>
    internal sealed class RoadMiniSection : VisualElement
    {
        public RoadMiniSection()
        {
            AddToClassList("road-mini");
            pickingMode = PickingMode.Ignore;
        }

        /// <summary>Draws the road type's elements left to right; null or no elements draws nothing.</summary>
        public void SetType(OmsiRoadType type)
        {
            Clear();
            if (type == null)
                return;
            var first = true;
            foreach (var lane in type.Lanes)
            {
                var block = new VisualElement { pickingMode = PickingMode.Ignore };
                block.AddToClassList("road-mini-lane");
                block.AddToClassList("road-mini-lane-" + OmsiRoadLane.Word(lane.Kind));
                // Proportional: the share of the width, with no least width, so a narrow kerb stays as narrow as it is.
                block.style.flexGrow = Mathf.Max(0.01f, (float)lane.Width);
                if (!first)
                    block.AddToClassList("road-mini-lane-gap");
                first = false;
                Add(block);
            }
        }
    }
}
