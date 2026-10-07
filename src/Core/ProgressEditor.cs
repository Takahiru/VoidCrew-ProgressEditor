using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using BepInEx;
using CG.Cloud;
using CG.Game;
using CG.Profile;
using CG.Client.PlayerData;
using CG.Network;
using UnityEngine;

namespace VoidCrewProgressEditor
{
    [BepInPlugin("local.VoidCrewProgressEditor", "Progress Editor", "1.0.0")]
    [BepInProcess("Void Crew.exe")]
    public sealed class ProgressEditor : BaseUnityPlugin
    {
        internal string xp = "", rank = "", favor = "", status = "";
        private ProgressUndoHistory progressHistory;
        private string lastError;
        private float refreshAt = -1;
        private long favorSliderMaximum = 100;
        private Channel rankChangedChannel;
        private bool rankReloadPending;
        private PlayerProfileData lastLoadedProfile;
        private long lastLoadedXp;
        private int lastLoadedRank, lastLoadedFavor;
        private PluginUnlockLedger pluginUnlocks;

        private void Awake()
        {
            useGUILayout = false;
            try { pluginUnlocks = new PluginUnlockLedger(); }
            catch (Exception ex) { Logger.LogError(ex); }
            try { progressHistory = new ProgressUndoHistory(Path.Combine(Paths.ConfigPath, "ProgressEditor-rank-history.json")); }
            catch (Exception ex) { Logger.LogError(ex); }
            SteamAwardGuard.Install(Logger);
            try { PauseProgressScreen.Install(this); }
            catch (Exception ex) { Logger.LogError("Pause menu integration failed: " + ex); }
            Logger.LogInfo("Progress Editor initialized. Use Edit Progress in the main or pause menu.");
        }

        private void ReportError(Exception ex)
        {
            status = "Editor error: " + ex.Message + " (see BepInEx log).";
            string error = ex.ToString();
            if (lastError != error) { Logger.LogError(error); lastError = error; }
        }

        internal bool HasProgressUndo
        {
            get { try { return progressHistory != null && progressHistory.Peek(PlayerProfile.GetPlayerUniqueID()) != null; } catch { return false; } }
        }
        internal long FavorSliderMaximum
        {
            get
            {
                int number;
                if (int.TryParse(favor, out number)) favorSliderMaximum = Math.Max(favorSliderMaximum, number);
                return favorSliderMaximum;
            }
        }
        internal void NativeLog(string text) { Logger.LogInfo(text); }
        internal void NativeError(Exception ex) { Logger.LogError(ex); }
        internal void OpenNativeEditor() { try { Reload(); } catch (Exception ex) { ReportError(ex); } }

        private void Update()
        {
            BindRankChangedEvent();
            if (refreshAt >= 0 && Time.unscaledTime >= refreshAt)
            {
                refreshAt = -1;
                ProfileUIRefresh.Refresh(Logger);
            }
            if (rankReloadPending)
            {
                rankReloadPending = false;
                ProfileUIRefresh.RefreshShips(Logger);
                try
                {
                    CloudPlayerProfileDataSync cloud;
                    PlayerProfileData data;
                    if (TryProfile(out cloud, out data) && (!ReferenceEquals(data, lastLoadedProfile) ||
                        data.Xp != lastLoadedXp || data.Rank != lastLoadedRank || data.FavorRank != lastLoadedFavor))
                        Reload();
                }
                catch (Exception ex) { ReportError(ex); }
            }
            PauseProgressScreen.UpdateOpenScreens();
        }

        private void BindRankChangedEvent()
        {
            var game = ClientGame.Current;
            var next = game != null && game.ModelEventBus != null ? game.ModelEventBus.OnRankChanged : null;
            if (ReferenceEquals(next, rankChangedChannel)) return;
            UnbindRankChangedEvent();
            rankChangedChannel = next;
            if (rankChangedChannel != null)
            {
                rankChangedChannel.Subscribe(OnProfileRankChanged, null);
                rankReloadPending = true;
            }
        }

        private void OnProfileRankChanged()
        {
            // Read on the next Update, after rank processing and gene edits complete.
            rankReloadPending = true;
        }

        private void UnbindRankChangedEvent()
        {
            if (rankChangedChannel != null) rankChangedChannel.Unsubscribe(OnProfileRankChanged);
            rankChangedChannel = null;
        }

        private void OnDisable() { UnbindRankChangedEvent(); }
        private void OnDestroy() { UnbindRankChangedEvent(); }

        private bool TryProfile(out CloudPlayerProfileDataSync cloud, out PlayerProfileData data)
        {
            cloud = null;
            data = null;
            var player = PlayerProfile.Instance;
            if (player == null) return false;
            cloud = player.Profile as CloudPlayerProfileDataSync;
            var photon = GetSource(cloud) as PhotonPlayerDataSync;
            data = GetSource(photon) as PlayerProfileData;
            return data != null;
        }

        private static object GetSource(object wrapper)
        {
            if (wrapper == null) return null;
            // The installed game protects this property; upstream builds against publicized libraries.
            for (Type type = wrapper.GetType(); type != null; type = type.BaseType)
            {
                var property = type.GetProperty("source", BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (property != null) return property.GetValue(wrapper, null);
            }
            return null;
        }

        private void Reload()
        {
            CloudPlayerProfileDataSync cloud;
            PlayerProfileData data;
            if (!TryProfile(out cloud, out data)) { status = "Profile unavailable. Load your profile first."; return; }
            xp = data.Xp.ToString(CultureInfo.InvariantCulture);
            rank = data.Rank.ToString(CultureInfo.InvariantCulture);
            favor = data.FavorRank.ToString(CultureInfo.InvariantCulture);
            favorSliderMaximum = Math.Max(100, data.FavorRank);
            lastLoadedProfile = data;
            lastLoadedXp = data.Xp;
            lastLoadedRank = data.Rank;
            lastLoadedFavor = data.FavorRank;
            status = "Values loaded from your profile.";
        }

        internal bool IsCosmeticCategoryEnabled(string category)
        {
            try { return pluginUnlocks != null && pluginUnlocks.IsEnabled(category); }
            catch { return false; } // A profile may still be loading at the title screen.
        }

        internal void SetCosmeticCategory(string category, bool enabled)
        {
            try
            {
                if (category != "Lootbox" && category != "Seasonal" && category != "Achievement")
                    throw new ArgumentException("Unknown cosmetic category.");
                if (pluginUnlocks == null) throw new InvalidOperationException("Plugin unlock ledger is unavailable.");
                pluginUnlocks.SetEnabled(category, enabled);
                RefreshProfileDisplays();
                ProfileUIRefresh.RefreshCosmetics(Logger);
                status = enabled ? category + " cosmetics enabled." : category + " plugin unlocks removed; previous ownership and achievement state restored.";
                Logger.LogInfo(status);
            }
            catch (Exception ex) { Logger.LogError(ex); status = "Cosmetic change failed: " + ex.Message; }
        }

        internal static bool TryGetMaximumXp(string rankText, out long maximum)
        {
            maximum = 0;
            int selectedRank;
            if (!int.TryParse(rankText, NumberStyles.None, CultureInfo.InvariantCulture, out selectedRank) || selectedRank > 30)
                return false;
            var ranks = PlayerRanksTable.Instance;
            if (ranks == null) return false;
            if (selectedRank == 30) maximum = ranks.FavorXpPerRank;
            else
            {
                if (ranks.titles == null || ranks.titles.Length <= selectedRank + 1 || ranks.titles[selectedRank + 1] == null)
                    return false;
                maximum = ranks.titles[selectedRank + 1].rankExp;
            }
            return maximum > 0;
        }

        internal void Apply()
        {
            long nextXp;
            int nextRank, nextFavor;
            if (!long.TryParse(xp, NumberStyles.None, CultureInfo.InvariantCulture, out nextXp) ||
                !int.TryParse(rank, NumberStyles.None, CultureInfo.InvariantCulture, out nextRank) ||
                !int.TryParse(favor, NumberStyles.None, CultureInfo.InvariantCulture, out nextFavor))
            { status = "Use non-negative whole numbers within the field's integer range."; return; }
            if (nextRank > 30) { status = "Rank must be between 0 and 30; nothing changed."; return; }
            string favorError = ProgressValidation.ValidateFavor(nextRank, nextFavor);
            if (favorError != null) { status = favorError; return; }
            var ranks = PlayerRanksTable.Instance;
            if (ranks == null || (nextRank < 30 && (ranks.titles == null ||
                ranks.titles.Length <= nextRank + 1 || ranks.titles[nextRank + 1] == null)))
            { status = "The game's XP thresholds are not loaded; nothing changed."; return; }
            // XP is progress within the rank. At rank 30, the next progression
            // threshold is FavorXpPerRank, rather than a nonexistent rank 31.
            long maximumXp = nextRank == 30 ? ranks.FavorXpPerRank : ranks.titles[nextRank + 1].rankExp;
            string validationError = ProgressValidation.Validate(nextXp, nextRank, maximumXp);
            if (validationError != null) { status = validationError; return; }
            CloudPlayerProfileDataSync cloud;
            PlayerProfileData data;
            if (!TryProfile(out cloud, out data)) { status = "Profile unavailable; nothing changed."; return; }
            // Compare parsed values against the live profile before taking a
            // snapshot or writing anything; a repeated Apply preserves Undo.
            if (data.Xp == nextXp && data.Rank == nextRank && data.FavorRank == nextFavor)
            { status = "No values changed."; return; }
            try
            {
                var genes = GeneSnapshot.Capture();
                // Validate all node mappings before changing rank or writing cloud data.
                var genePlan = nextRank < data.Rank ? new GenePlan(genes) : null;
                if (progressHistory == null) throw new InvalidOperationException("Rank undo history is unavailable; edit cancelled.");
                int difference = Math.Max(0, data.Rank - nextRank);
                progressHistory.Push(PlayerProfile.GetPlayerUniqueID(), new ProgressUndoEntry {
                    savedUtc = DateTime.UtcNow.ToString("o"), xp = data.Xp, rank = data.Rank, favor = data.FavorRank,
                    loadouts = genes.SaveLevels() });
                SetValues(cloud, data, nextXp, nextRank, nextFavor, false);
                string removalStatus = "";
                if (difference > 0)
                {
                    System.Collections.Generic.List<int> removed;
                    var target = genePlan.Reduce(genes, difference, out removed);
                    genes.Commit(target);
                    for (int i = 0; i < removed.Count; i++)
                    {
                        string detail = "Loadout " + (i + 1) + ": removed " + removed[i] + "/" + difference + " gene points. ";
                        removalStatus += detail;
                        Logger.LogInfo(detail);
                    }
                }
                RefreshProfileDisplays();
                Reload();
                status = "Applied. " + removalStatus;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                status = "Edit failed or sync incomplete. Reload to inspect; undo is available if values changed.";
            }
        }

        internal void Undo()
        {
            CloudPlayerProfileDataSync cloud;
            PlayerProfileData data;
            if (!TryProfile(out cloud, out data))
            { status = "Profile unavailable; undo was not applied."; return; }
            try
            {
                if (progressHistory == null) throw new InvalidOperationException("Rank undo history is unavailable.");
                string account = PlayerProfile.GetPlayerUniqueID();
                var saved = progressHistory.Peek(account);
                if (saved == null) { status = "No saved rank edits to undo."; return; }
                var genes = GeneSnapshot.Capture();
                var target = genes.ResolveLevels(saved.loadouts);
                // Resolve every saved node before modifying the profile. Keep the
                // history entry until both profile and gene restoration succeed.
                SetValues(cloud, data, saved.xp, saved.rank, saved.favor, false);
                genes.Commit(target);
                progressHistory.Pop(account, saved);
                RefreshProfileDisplays();
                Reload();
                status = "Restored rank, XP, favor rank, and saved gene loadouts from history.";
            }
            catch (Exception ex) { Logger.LogError(ex); status = "Undo failed or sync incomplete; the history entry was retained if restoration did not finish."; }
        }

        private static void SetValues(CloudPlayerProfileDataSync cloud, PlayerProfileData data, long newXp, int newRank, int newFavor, bool publish)
        {
            data.Xp = newXp;
            data.Rank = newRank;
            data.FavorRank = newFavor;
            // ResetProgress uses this call to propagate edits through the profile wrappers.
            cloud.AddXp(0);
            if (publish) PublishRankChanged();
        }

        private void RefreshProfileDisplays()
        {
            ProfileUIRefresh.NotifyPlayerProperties(Logger);
            ProfileUIRefresh.Refresh(Logger);
            // One bounded follow-up after the game's queued property/UI updates.
            refreshAt = Time.unscaledTime + 0.25f;
            Logger.LogInfo("Refreshed profile displays; one follow-up refresh scheduled.");
        }

        private static void PublishRankChanged()
        {
            if (GameSessionManager.ActiveSession != null && ClientGame.Current != null)
                ClientGame.Current.ModelEventBus.OnRankChanged.Publish();
        }
    }
}















