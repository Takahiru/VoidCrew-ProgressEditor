using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using CG.Profile;
using HarmonyLib;
using ResourceAssets;
using UnityEngine;

namespace VoidCrewProgressEditor
{
    [Serializable] public sealed class PluginItemRecord { public string id; public bool unseen; public List<string> categories = new List<string>(); }
    [Serializable] public sealed class PluginAchievementRecord
    {
        public string id, steamId, metricId;
        public List<string> categories = new List<string>();
        public bool completed, claimed, metricExisted;
        public int metricProgress, liveProgress;
    }
    [Serializable] public sealed class PluginUnlockData
    {
        public string account;
        public int categorySchema;
        public List<string> enabledCategories = new List<string>();
        public List<PluginItemRecord> items = new List<PluginItemRecord>();
        public List<PluginAchievementRecord> achievements = new List<PluginAchievementRecord>();
    }

    internal sealed class PluginUnlockLedger
    {
        private readonly string path = Path.Combine(Paths.ConfigPath, "ProgressEditor-plugin-unlocks.json");
        internal PluginUnlockData Data = new PluginUnlockData();
        internal bool HasChanges { get { return Data.items.Count > 0 || Data.achievements.Count > 0; } }
        internal PluginUnlockLedger()
        {
            if (File.Exists(path))
            {
                Data = JsonUtility.FromJson<PluginUnlockData>(File.ReadAllText(path));
                if (Data == null || Data.items == null || Data.achievements == null)
                    throw new InvalidOperationException("Plugin unlock ledger is invalid; unlock actions disabled.");
            }
            SteamAwardGuard.Ledger = this;
        }

        internal void CheckAccount()
        {
            string account = PlayerProfile.GetPlayerUniqueID();
            if (string.IsNullOrEmpty(account)) throw new InvalidOperationException("Player account is not ready.");
            if (HasChanges && Data.account != account)
                throw new InvalidOperationException("The unlock ledger belongs to another account.");
            if (!HasChanges && !string.IsNullOrEmpty(Data.account) && Data.account != account)
            {
                Data.enabledCategories = new List<string>();
                Data.categorySchema = 1;
            }
            Data.account = account;
        }

        internal void Save()
        {
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(Data, true));
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
        }

        internal void PrepareCategories()
        {
            CheckAccount();
            if (Data.enabledCategories == null) Data.enabledCategories = new List<string>();
            foreach (var item in Data.items) if (item.categories == null) item.categories = new List<string>();
            foreach (var achievement in Data.achievements) if (achievement.categories == null) achievement.categories = new List<string>();
            if (Data.categorySchema >= 1) return;
            // Upgrade the previous cumulative journal using current asset categories.
            // Existing original-state snapshots remain intact.
            foreach (var category in new[] { "Lootbox", "Seasonal", "Achievement" })
            {
                var plan = new CosmeticPlan(category);
                bool tracked = false;
                foreach (var item in Data.items)
                    if (plan.Items.ContainsKey(new GUIDUnion(item.id)))
                    { if (!item.categories.Contains(category)) item.categories.Add(category); tracked = true; }
                foreach (var achievement in Data.achievements)
                    if (plan.Achievements.Contains(new GUIDUnion(achievement.id)))
                    { if (!achievement.categories.Contains(category)) achievement.categories.Add(category); tracked = true; }
                if (tracked && !Data.enabledCategories.Contains(category)) Data.enabledCategories.Add(category);
            }
            Data.categorySchema = 1;
            Save();
        }

        internal bool IsEnabled(string category)
        {
            PrepareCategories();
            return Data.enabledCategories.Contains(category);
        }

        internal void SetEnabled(string category, bool enabled)
        {
            PrepareCategories();
            if (enabled) { Apply(new CosmeticPlan(category), category); return; }
            var items = Data.items.FindAll(delegate(PluginItemRecord r) { return r.categories.Contains(category) && r.categories.Count == 1; });
            var achievements = Data.achievements.FindAll(delegate(PluginAchievementRecord r) { return r.categories.Contains(category) && r.categories.Count == 1; });
            UndoRecords(items, achievements);
            foreach (var item in Data.items) item.categories.Remove(category);
            foreach (var achievement in Data.achievements) achievement.categories.Remove(category);
            Data.enabledCategories.Remove(category);
            Save();
        }

        internal void Apply(CosmeticPlan plan, string category)
        {
            CheckAccount();
            var player = PlayerProfile.Instance;
            if (plan.Achievements.Count > 0 && !SteamAwardGuard.Ready)
                throw new InvalidOperationException("Steam-award guard is unavailable; achievement unlock cancelled.");
            foreach (var id in plan.Achievements)
            {
                bool recorded = Data.achievements.Exists(delegate(PluginAchievementRecord r) { return r.id == id.AsHex(); });
                if (recorded)
                {
                    var existing = Data.achievements.Find(delegate(PluginAchievementRecord r) { return r.id == id.AsHex(); });
                    if (!existing.categories.Contains(category)) existing.categories.Add(category);
                    continue;
                }
                if (player.Achievements.IsCompleted(id) && player.Achievements.IsClaimed(id)) continue;
                var asset = AchievementContainer.Instance.GetAssetDefById(id, true).Asset;
                var record = new PluginAchievementRecord { id = id.AsHex(), steamId = asset.AchievementID,
                    completed = player.Achievements.IsCompleted(id), claimed = player.Achievements.IsClaimed(id) };
                if (asset.Source == AchievementSource.LocalMetric && asset.LocalMetric != null)
                {
                    record.liveProgress = asset.LocalMetric.Progress;
                    foreach (var earlier in Data.achievements)
                    {
                        var previousAsset = AchievementContainer.Instance.GetAssetDefById(new GUIDUnion(earlier.id), true).Asset;
                        if (previousAsset.Source == AchievementSource.LocalMetric && previousAsset.LocalMetric == asset.LocalMetric)
                        { record.liveProgress = earlier.liveProgress; break; }
                    }
                }
                if (asset.Source == AchievementSource.TrackedMetric && asset.ExternalMetric != null)
                {
                    var metricId = asset.ExternalMetric.AssetGuid;
                    record.metricId = metricId.AsHex();
                    record.metricExisted = player.Metrics.TrackedMetricProgress.TryGetValue(metricId, out record.metricProgress);
                    if (asset.ExternalMetric.Asset != null && asset.ExternalMetric.Asset.Metric != null)
                        record.liveProgress = asset.ExternalMetric.Asset.Metric.Progress;
                    var earlier = Data.achievements.Find(delegate(PluginAchievementRecord r) { return r.metricId == record.metricId; });
                    if (earlier != null)
                    {
                        record.metricProgress = earlier.metricProgress;
                        record.metricExisted = earlier.metricExisted;
                        record.liveProgress = earlier.liveProgress;
                    }
                }
                record.categories.Add(category);
                Data.achievements.Add(record);
            }
            foreach (var item in plan.Items)
            {
                var record = Data.items.Find(delegate(PluginItemRecord r) { return r.id == item.Key.AsHex(); });
                if (record != null)
                {
                    if (!record.categories.Contains(category)) record.categories.Add(category);
                    continue;
                }
                if (player.UnlockedItems.GetItemsInCategory(item.Value, true).Contains(item.Key)) continue;
                record = new PluginItemRecord { id = item.Key.AsHex(), unseen = player.Unseen.HasItem(item.Value, item.Key) };
                record.categories.Add(category);
                Data.items.Add(record);
            }
            if (!Data.enabledCategories.Contains(category)) Data.enabledCategories.Add(category);
            // Record original values and activate Steam protection before any mutation.
            Save();
            foreach (var id in plan.Achievements)
            {
                if (!player.Achievements.CompletedAchievements.Contains(id)) player.Achievements.CompletedAchievements.Add(id);
                if (!player.Achievements.ClaimedAchievements.Contains(id)) player.Achievements.ClaimedAchievements.Add(id);
            }
            foreach (var item in plan.Items)
                if (!player.UnlockedItems.GetItemsInCategory(item.Value, true).Contains(item.Key))
                    player.UnlockedItems.AddItem(item.Value, item.Key, true);
            // Write profile state directly; no completion, claim, or platform APIs.
            if (plan.Achievements.Count > 0)
            {
                CG.Cloud.CloudProfileWriter.WritePlayerAchievementsProgress(player.Achievements);
                CG.Cloud.CloudProfileWriter.WritePlayerClaimedAchievements(player.Achievements);
            }
        }

        internal void Undo()
        {
            PrepareCategories();
            UndoRecords(new List<PluginItemRecord>(Data.items), new List<PluginAchievementRecord>(Data.achievements));
            Data.enabledCategories.Clear();
            Save();
        }

        private void UndoRecords(List<PluginItemRecord> items, List<PluginAchievementRecord> achievements)
        {
            CheckAccount();
            var player = PlayerProfile.Instance;
            // Resolve every recorded asset before changing anything.
            var owners = new Dictionary<string, IResourceAssetContainer>();
            foreach (var record in items)
            {
                var owner = ResourceAssetContainerRegister.Instance.FindOwner(new GUIDUnion(record.id)) as IResourceAssetContainer;
                if (owner == null) throw new InvalidOperationException("A recorded unlock cannot be resolved; undo cancelled.");
                owners.Add(record.id, owner);
            }
            foreach (var record in achievements)
                if (AchievementContainer.Instance.GetAssetDefById(new GUIDUnion(record.id), true).Asset == null)
                    throw new InvalidOperationException("A recorded achievement cannot be resolved; undo cancelled.");
            foreach (var record in achievements)
            {
                var id = new GUIDUnion(record.id);
                var asset = AchievementContainer.Instance.GetAssetDefById(id, true).Asset;
                SetMembership(player.Achievements.CompletedAchievements, id, record.completed);
                SetMembership(player.Achievements.ClaimedAchievements, id, record.claimed);
                // Shared metrics stay intact while another checked category
                // still owns an achievement using that metric.
                if (!MetricStillUsed(record, achievements))
                {
                // Set the backing field to avoid OnProgressChange completing achievements.
                if (asset.Source == AchievementSource.LocalMetric && asset.LocalMetric != null)
                    SetMetricWithoutEvents(asset.LocalMetric, record.liveProgress);
                if (!string.IsNullOrEmpty(record.metricId))
                {
                    var metricId = new GUIDUnion(record.metricId);
                    if (record.metricExisted) player.Metrics.TrackedMetricProgress[metricId] = record.metricProgress;
                    else player.Metrics.TrackedMetricProgress.Remove(metricId);
                    if (asset.ExternalMetric.Asset != null && asset.ExternalMetric.Asset.Metric != null)
                        SetMetricWithoutEvents(asset.ExternalMetric.Asset.Metric, record.liveProgress);
                }
                }
            }
            foreach (var record in items)
            {
                var id = new GUIDUnion(record.id);
                player.UnlockedItems.RemoveItem(owners[record.id], id);
                if (record.unseen) player.Unseen.AddItem(owners[record.id], id, true);
                else player.Unseen.RemoveItem(owners[record.id], id);
            }
            CG.Cloud.CloudProfileWriter.WritePlayerAchievementsProgress(player.Achievements);
            CG.Cloud.CloudProfileWriter.WritePlayerClaimedAchievements(player.Achievements);
            player.Metrics.Save();
            foreach (var record in items) Data.items.Remove(record);
            foreach (var record in achievements) Data.achievements.Remove(record);
        }

        private bool MetricStillUsed(PluginAchievementRecord record, List<PluginAchievementRecord> removing)
        {
            var asset = AchievementContainer.Instance.GetAssetDefById(new GUIDUnion(record.id), true).Asset;
            foreach (var other in Data.achievements)
            {
                if (removing.Contains(other)) continue;
                if (!string.IsNullOrEmpty(record.metricId) && record.metricId == other.metricId) return true;
                if (asset.Source == AchievementSource.LocalMetric && asset.LocalMetric != null)
                {
                    var otherAsset = AchievementContainer.Instance.GetAssetDefById(new GUIDUnion(other.id), true).Asset;
                    if (otherAsset.Source == AchievementSource.LocalMetric && otherAsset.LocalMetric == asset.LocalMetric) return true;
                }
            }
            return false;
        }

        private static void SetMembership(List<GUIDUnion> list, GUIDUnion id, bool present)
        {
            if (present) { if (!list.Contains(id)) list.Add(id); }
            else list.RemoveAll(delegate(GUIDUnion existing) { return existing == id; });
        }
        private static void SetMetricWithoutEvents(PlayerMetric metric, int progress)
        { typeof(PlayerMetric).GetField("progress", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(metric, progress); }
    }

    internal static class SteamAwardGuard
    {
        internal static PluginUnlockLedger Ledger;
        internal static bool Ready;
        private static ManualLogSource logger;
        internal static void Install(ManualLogSource log)
        {
            logger = log;
            try
            {
                Type type = null;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                { type = assembly.GetType("Steamworks.SteamUserStats"); if (type != null) break; }
                if (type == null) throw new TypeLoadException("SteamUserStats not loaded");
                var target = type.GetMethod("SetAchievement", new Type[] { typeof(string) });
                if (target == null) throw new MissingMethodException("SteamUserStats.SetAchievement");
                new Harmony("local.VoidCrewProgressEditor.SteamAwardGuard").Patch(target,
                    prefix: new HarmonyMethod(typeof(SteamAwardGuard).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)));
                Ready = true;
                log.LogInfo("Steam-award guard installed for plugin-tracked achievements.");
            }
            catch (Exception ex) { Ready = false; log.LogError("Steam-award guard failed; achievement unlocks disabled: " + ex); }
        }
        private static bool Prefix(string __0, ref bool __result)
        {
            if (Ledger == null || !Ledger.Data.achievements.Exists(delegate(PluginAchievementRecord r) { return r.steamId == __0; })) return true;
            __result = false;
            if (logger != null) logger.LogInfo("Blocked Steam achievement award for plugin-tracked achievement: " + __0);
            return false;
        }
    }
}
