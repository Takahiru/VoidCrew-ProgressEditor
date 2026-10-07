using System;
using System.Collections.Generic;

namespace VoidCrewProgressEditor
{
    // Pure planning: never changes the live game's allocations.
    public sealed class GeneNode
    {
        public int Id, Tree, Row, Column, Points;
        public GeneNode Copy() { return (GeneNode)MemberwiseClone(); }
    }

    public static class GeneReduction
    {
        public static int Remove(List<GeneNode> nodes, int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException("count");
            int removed = 0;
            while (removed < count)
            {
                var totals = new SortedDictionary<int, long>();
                foreach (var node in nodes)
                {
                    if (node.Points < 0) throw new InvalidOperationException("Negative gene allocation.");
                    if (!totals.ContainsKey(node.Tree)) totals.Add(node.Tree, 0);
                    totals[node.Tree] += node.Points;
                }
                int tree = -1;
                long largest = 0;
                foreach (var total in totals)
                    if (total.Value > largest) { tree = total.Key; largest = total.Value; }
                if (largest == 0) break;
                GeneNode selected = null;
                foreach (var node in nodes)
                {
                    if (node.Tree != tree || node.Points == 0) continue;
                    if (selected == null || node.Row > selected.Row ||
                        (node.Row == selected.Row && node.Column < selected.Column) ||
                        (node.Row == selected.Row && node.Column == selected.Column && node.Id < selected.Id))
                        selected = node;
                }
                selected.Points--;
                removed++;
            }
            return removed;
        }
    }
}
