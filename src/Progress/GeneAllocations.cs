using System;
using System.Collections.Generic;
using CG.Cloud;
using CG.Profile;
using Gameplay.Perks;
using ResourceAssets;

namespace VoidCrewProgressEditor
{
    internal sealed class GeneSnapshot
    {
        internal IPlayerPerkData Owner;
        internal List<PerkLoadout> Loadouts;
        internal List<Dictionary<PerkBuff, int>> Levels;

        internal static GeneSnapshot Capture()
        {
            var owner = PlayerProfile.Instance.Perks;
            if (owner == null || owner.Loadouts == null)
                throw new InvalidOperationException("Gene loadouts are not available.");
            var snapshot = new GeneSnapshot { Owner = owner, Loadouts = owner.Loadouts,
                Levels = new List<Dictionary<PerkBuff, int>>() };
            foreach (var loadout in snapshot.Loadouts)
            {
                if (loadout == null || loadout.Perks == null)
                    throw new InvalidOperationException("A gene loadout could not be read.");
                snapshot.Levels.Add(new Dictionary<PerkBuff, int>(loadout.Perks));
            }
            return snapshot;
        }

        internal List<SavedGeneLoadout> SaveLevels()
        {
            var result = new List<SavedGeneLoadout>();
            foreach (var levels in Levels)
            {
                var saved = new SavedGeneLoadout();
                foreach (var point in levels)
                    saved.points.Add(new SavedGenePoint { id = point.Key.ContainerGuid.AsHex(), level = point.Value });
                result.Add(saved);
            }
            return result;
        }

        internal List<Dictionary<PerkBuff, int>> ResolveLevels(List<SavedGeneLoadout> saved)
        {
            CheckOwner();
            if (saved == null || saved.Count != Loadouts.Count)
                throw new InvalidOperationException("Saved gene loadout count differs; undo cancelled.");
            var register = ResourceAssetContainerRegister.Instance;
            var container = register == null ? null : register.GetContainer<PerkBuffContainer, PerkBuff, PerkBuffDef>();
            if (container == null) throw new InvalidOperationException("Gene definitions are not loaded.");
            var result = new List<Dictionary<PerkBuff, int>>();
            foreach (var loadout in saved)
            {
                var levels = new Dictionary<PerkBuff, int>();
                foreach (var point in loadout.points)
                {
                    var definition = container.GetAssetDefById(new GUIDUnion(point.id), true);
                    var buff = definition == null ? null : definition.Asset;
                    if (buff == null || point.level < 0 || point.level > buff.Levels.Count || levels.ContainsKey(buff))
                        throw new InvalidOperationException("A saved gene allocation cannot be restored; undo cancelled.");
                    levels.Add(buff, point.level);
                }
                result.Add(levels);
            }
            return result;
        }

        internal void CheckOwner()
        {
            if (!ReferenceEquals(PlayerProfile.Instance.Perks, Owner) ||
                !ReferenceEquals(Owner.Loadouts, Loadouts) || Loadouts.Count != Levels.Count)
                throw new InvalidOperationException("Gene loadouts changed since the edit; restoration was not applied.");
        }

        // Apply final dictionaries together, then use the game's normal cloud and Photon paths.
        internal void Commit(List<Dictionary<PerkBuff, int>> target)
        {
            CheckOwner();
            int active = Owner.ActiveLoudoutIndex;
            if (active < 0 || active >= Loadouts.Count)
                throw new InvalidOperationException("Active gene loadout index is invalid.");
            var changed = new List<int>();
            PerkBuff activeTrigger = null;
            for (int i = 0; i < target.Count; i++)
            {
                var current = Loadouts[i].Perks;
                bool different = current.Count != target[i].Count;
                foreach (var item in current)
                {
                    int desired;
                    if (!target[i].TryGetValue(item.Key, out desired) || desired != item.Value)
                    { different = true; if (i == active) activeTrigger = item.Key; }
                }
                if (!different) foreach (var item in target[i])
                    if (!current.ContainsKey(item.Key)) { different = true; break; }
                if (!different) continue;
                if (i == active && activeTrigger == null)
                    foreach (var item in target[i]) { activeTrigger = item.Key; break; }
                changed.Add(i);
            }
            foreach (int i in changed)
            {
                Loadouts[i].Perks.Clear();
                foreach (var item in target[i]) Loadouts[i].Perks.Add(item.Key, item.Value);
            }
            foreach (int i in changed) CloudProfileWriter.WritePerkLoadout(i, Loadouts[i].Perks);
            if (activeTrigger != null)
            {
                int value;
                Loadouts[active].Perks.TryGetValue(activeTrigger, out value);
                Owner.SetPerkBuffLevel(activeTrigger, value);
            }
            if (changed.Count > 0)
            {
                var refresh = TokenTerminalEvents.Instance.OnPerkLoadoutChanged;
                if (refresh != null) refresh();
            }
        }
    }

    internal sealed class GenePlan
    {
        private readonly List<PerkBuff> buffs = new List<PerkBuff>();
        private readonly Dictionary<PerkBuff, GeneNode> definitions = new Dictionary<PerkBuff, GeneNode>();

        internal GenePlan(GeneSnapshot snapshot)
        {
            var register = ResourceAssetContainerRegister.Instance;
            if (register == null) throw new InvalidOperationException("Gene-tree registry is not loaded.");
            var container = register.GetContainer<PerkTreeContainer, PerkTree, PerkTreeDef>();
            if (container == null) throw new InvalidOperationException("Gene-tree definitions are not loaded.");
            var trees = new List<PerkTreeDef>(container.AssetDescriptions);
            if (container.RuntimeDescriptions != null) trees.AddRange(container.RuntimeDescriptions);
            trees.Sort(delegate(PerkTreeDef a, PerkTreeDef b) { return ((int)a.perkTree).CompareTo((int)b.perkTree); });
            foreach (var definition in trees)
            {
                var tree = definition.Asset;
                if (tree == null || tree.Perks == null) continue;
                foreach (var reference in tree.Perks)
                {
                    var buff = reference.Asset;
                    if (buff == null || definitions.ContainsKey(buff)) continue;
                    definitions.Add(buff, new GeneNode { Id = buffs.Count, Tree = (int)definition.perkTree,
                        Row = buff.Row, Column = buff.Column });
                    buffs.Add(buff);
                }
            }
            foreach (var loadout in snapshot.Levels)
                foreach (var item in loadout)
                    if (item.Key == null || item.Value < 0 || !definitions.ContainsKey(item.Key))
                        throw new InvalidOperationException("An allocated gene node cannot be mapped to a tree. Edit cancelled.");
        }

        internal List<Dictionary<PerkBuff, int>> Reduce(GeneSnapshot snapshot, int decrease, out List<int> removed)
        {
            var result = new List<Dictionary<PerkBuff, int>>();
            removed = new List<int>();
            foreach (var loadout in snapshot.Levels)
            {
                var nodes = new List<GeneNode>();
                foreach (var item in loadout)
                {
                    var node = definitions[item.Key].Copy();
                    node.Points = item.Value;
                    nodes.Add(node);
                }
                removed.Add(GeneReduction.Remove(nodes, decrease));
                var levels = new Dictionary<PerkBuff, int>();
                foreach (var node in nodes) if (node.Points > 0) levels.Add(buffs[node.Id], node.Points);
                result.Add(levels);
            }
            return result;
        }
    }
}
