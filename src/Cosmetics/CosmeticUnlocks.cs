using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using CG.Profile;
using CG.Client.UserData;
using ResourceAssets;

namespace VoidCrewProgressEditor
{
    internal sealed class CosmeticPlan
    {
        internal readonly Dictionary<GUIDUnion, IResourceAssetContainer> Items = new Dictionary<GUIDUnion, IResourceAssetContainer>();
        internal readonly HashSet<GUIDUnion> Achievements = new HashSet<GUIDUnion>();

        private static bool IsCosmetic(IUnlockableDataContainer owner)
        {
            switch (owner.Category)
            {
                case IUnlockableDataContainer.EUnlockCategory.Helmet:
                case IUnlockableDataContainer.EUnlockCategory.Shoulder:
                case IUnlockableDataContainer.EUnlockCategory.Chest:
                case IUnlockableDataContainer.EUnlockCategory.Suit:
                case IUnlockableDataContainer.EUnlockCategory.Projection:
                case IUnlockableDataContainer.EUnlockCategory.ArmorPattern:
                case IUnlockableDataContainer.EUnlockCategory.ColorScheme:
                case IUnlockableDataContainer.EUnlockCategory.ProjectionColor:
                case IUnlockableDataContainer.EUnlockCategory.JetPack:
                case IUnlockableDataContainer.EUnlockCategory.JetPackTrail:
                case IUnlockableDataContainer.EUnlockCategory.Flashlight:
                case IUnlockableDataContainer.EUnlockCategory.Emote: return true;
                default: return false;
            }
        }

        internal CosmeticPlan(string category)
        {
            GUIDUnion hunter = GUIDUnion.Empty();
            if (category == "Hunter")
            {
                var matches = new HashSet<GUIDUnion>();
                var achievementDefinitions = new List<AchievementDef>(AchievementContainer.Instance.AssetDescriptions);
                if (AchievementContainer.Instance.RuntimeDescriptions != null)
                    achievementDefinitions.AddRange(AchievementContainer.Instance.RuntimeDescriptions);
                foreach (var def in achievementDefinitions)
                {
                    var context = def.ContextInfo as Client.Player.Interactions.ContextInfo;
                    string header = context == null || context.HeaderDefaultableLocalizedString == null ? "" : context.HeaderDefaultableLocalizedString.FallBackString;
                    string candidate = System.Text.RegularExpressions.Regex.Replace(
                        (header + " " + (context == null ? "" : context.HeaderText) + " " + def.Path + " " + def.Asset.AchievementID).ToLowerInvariant(), "[^a-z0-9]", "");
                    if (candidate.Contains("hunterofhollow") || candidate.Contains("hunterofthehollow")) matches.Add(def.AssetGuid);
                }
                if (matches.Count != 1) throw new InvalidOperationException("Hunter of Hollow could not be identified uniquely; nothing unlocked.");
                foreach (var id in matches) hunter = id;
                Achievements.Add(hunter);
            }
            var register = ResourceAssetContainerRegister.Instance;
            var unlocks = UnlockContainer.Instance;
            var pools = RewardPoolContainer.Instance;
            if (register == null || unlocks == null || pools == null)
                throw new InvalidOperationException("Cosmetic definitions are not loaded.");
            var normal = new HashSet<GUIDUnion>();
            var seasonal = new HashSet<GUIDUnion>();
            var allPoolItems = new HashSet<GUIDUnion>();
            var poolDefinitions = new List<RewardPoolDef>(pools.AssetDescriptions);
            if (pools.RuntimeDescriptions != null) poolDefinitions.AddRange(pools.RuntimeDescriptions);
            if (category == "Lootbox" || category == "Seasonal") foreach (var def in poolDefinitions)
            {
                var pool = def.Asset;
                if (pool == null || pool.Rewards == null) throw new InvalidOperationException("A reward pool could not be read.");
                foreach (var item in pool.Rewards)
                {
                    allPoolItems.Add(item.AssetGuid);
                    if (def.category == RewardPoolDef.ERewardPoolCategory.Normal) normal.Add(item.AssetGuid);
                    if (def.category == RewardPoolDef.ERewardPoolCategory.Seasonal ||
                        def.category == RewardPoolDef.ERewardPoolCategory.TimeLimited) seasonal.Add(item.AssetGuid);
                }
            }
            var definitions = new List<UnlockItemDef>(unlocks.AssetDescriptions);
            if (unlocks.RuntimeDescriptions != null) definitions.AddRange(unlocks.RuntimeDescriptions);
            foreach (var def in definitions)
            {
                var owner = register.FindOwner(def.AssetGuid);
                if (owner == null || !IsCosmetic(owner)) continue;
                var options = def.UnlockOptions;
                bool selected = category == "Seasonal" ? seasonal.Contains(def.AssetGuid) :
                    category == "Lootbox" ? normal.Contains(def.AssetGuid) ||
                        (options.UnlockCriteria == UnlockCriteriaType.RewardPool && !allPoolItems.Contains(def.AssetGuid)) :
                    options.UnlockCriteria == UnlockCriteriaType.Achievement &&
                    (category != "Hunter" || (options.RequiredAchievement != null && options.RequiredAchievement.AssetGuid == hunter));
                if (!selected) continue;
                Items[def.AssetGuid] = (IResourceAssetContainer)owner;
                if (category == "Achievement" || category == "Hunter")
                {
                    var achievement = options.RequiredAchievement;
                    if (achievement == null || achievement.Asset == null)
                        throw new InvalidOperationException("A cosmetic achievement definition could not be read.");
                    Achievements.Add(achievement.AssetGuid);
                }
            }
        }

    }
}

