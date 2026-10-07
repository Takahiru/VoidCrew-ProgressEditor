using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using CG.Game;
using CG.Profile;
using Photon.Pun;
using UnityEngine;
using UnityEngine.UIElements;

namespace VoidCrewProgressEditor
{
    internal static class ProfileUIRefresh
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly HashSet<string> reportedErrors = new HashSet<string>();

        internal static void RefreshCosmetics(ManualLogSource log)
        {
            ForSceneObjects("AchievementsUIController", log, delegate(object controller)
            {
                if (Field(controller, "tabHeader") == null) return;
                Invoke(controller, "UpdateAllEntries", null);
                Invoke(controller, "UpdateUnclaimed", null);
                Invoke(controller, "SortEntries", null);
            });
            ForSceneObjects("UI.PersonalLoadout.PersonalLoadoutTerminal", log, delegate(object terminal)
            {
                if (Field(terminal, "root") == null || Field(terminal, "selectionPanel") == null) return;
                var unseen = terminal.GetType().GetMethod("UpdateUnseen", Members, null, Type.EmptyTypes, null);
                if (unseen != null) unseen.Invoke(terminal, null);
                var category = Field(terminal, "shownCategory");
                if (category != null) Invoke(terminal, "SelectedCategory", new object[] { category, Field(terminal, "selectedMultiSlotIndex") });
            });
        }

        internal static void RefreshShips(ManualLogSource log)
        {
            ForSceneObjects("UI.ShipLoadoutTerminal.ShipLoadoutTerminal", log, delegate(object terminal)
            {
                var list = Field(terminal, "loadoutList");
                if (Field(terminal, "root") == null || list == null) return;
                // Quest/save restrictions deliberately bypass rank requirements.
                // Preserve that list and rebuild only its presentation.
                if ((bool)Field(list, "IgnoreRankRequirements"))
                {
                    Invoke(list, "RefreshLoadouts", null);
                    return;
                }
                var container = ResourceAssets.ShipLoadoutDataContainer.Instance;
                if (container == null) return;
                var source = new List<ResourceAssets.ShipLoadoutDataDef>();
                source.AddRange(container.GetUnlockedShipDefinitionsFromHighestRankPlayer());
                source.AddRange(container.GetLockedShipDefinitionsFromHighestRankPlayer());
                if (source.Count == 0) return;
                var ship = list.GetType().GetProperty("ShipListed", Members).GetValue(list, null);
                // Rebind the cached rows with current rank checks, preserving the displayed ship.
                Invoke(list, "SetLoadouts", new object[] { source, ship });
            });
        }

        internal static void NotifyPlayerProperties(ManualLogSource log)
        {
            Attempt("player rank properties", log, delegate
            {
                var profile = PlayerProfile.Instance;
                if (profile == null || !PhotonNetwork.InRoom || PhotonNetwork.LocalPlayer == null) return;
                // These are the keys used by PhotonPlayerDataSync.OnXpChange. Direct edits
                // bypass its old/new comparison, so update the cached values explicitly.
                var properties = new ExitGames.Client.Photon.Hashtable();
                properties.Add("RP_PR", profile.Profile.Rank);
                properties.Add("RP_FR", profile.Profile.FavorRank);
                PhotonNetwork.LocalPlayer.SetCustomProperties(properties);
            });
        }

        internal static void Refresh(ManualLogSource log)
        {
            if (PlayerProfile.Instance == null) return;
            Attempt("rank event", log, delegate
            {
                if (ClientGame.Current != null) ClientGame.Current.ModelEventBus.OnRankChanged.Publish();
            });
            RefreshShips(log);
            RefreshBars("TitleMenu", "_expBar", null, log);
            RefreshBars("EscapeMenu", "expBar", null, log);
            RefreshBars("UI.PersonalLoadout.PersonalLoadoutTerminal", "expBar", "rankingOverview", log);
            ForSceneObjects("RankingPanel", log, delegate(object panel)
            {
                var root = Field(panel, "root") as VisualElement;
                if (root == null || Field(panel, "leftPanel") == null || Field(panel, "rightPanel") == null) return;
                var entries = Field(panel, "rankEntries") as List<VisualElement>;
                int rank = PlayerProfile.Instance.Profile.Rank;
                if (entries != null) for (int i = 0; i < entries.Count; i++)
                {
                    entries[i].RemoveFromClassList("rank__past");
                    entries[i].RemoveFromClassList("rank__current");
                    entries[i].RemoveFromClassList("rank__future");
                    entries[i].AddToClassList(i < rank ? "rank__past" : i == rank ? "rank__current" : "rank__future");
                }
                // Enter refreshes ascension/favor panels using current profile values.
                Invoke(panel, "Enter", new object[] { null });
            });
            // Enter above starts an XP animation too; settle the ranking bar last.
            RefreshBars("RankingPanel", "expBar", "root", log);
            ForSceneObjects("UI.Token.TokenTerminal", log, delegate(object terminal)
            {
                // Hidden, initialized scene terminals are included; prefabs are excluded.
                if (Field(terminal, "mainLabel") == null) return;
                Invoke(terminal, "UpdateLeftSide", null);
                Invoke(terminal, "UpdateTrees", null);
            });
        }

        private static void RefreshBars(string type, string barField, string rootField, ManualLogSource log)
        {
            ForSceneObjects(type, log, delegate(object owner)
            {
                object bar = Field(owner, barField);
                if (bar == null) return;
                if (rootField != null && Field(owner, rootField) == null) return;
                // Init is normally done on first menu entry. Do not invoke a partially
                // constructed bar; that menu will read current values when it initializes.
                if (rootField == null && Field(bar, "rankDisplay") == null) return;
                Invoke(owner, "UpdateXpBar", null);
                // UpdateXpBar uses the ordinary XP sequence; settle it immediately so an
                // old animation cannot overwrite the newly refreshed labels afterwards.
                Invoke(bar, "SkipToEndSequence", null);
            });
        }

        private static object Field(object owner, string name)
        {
            var field = owner.GetType().GetField(name, Members);
            if (field == null) throw new MissingFieldException(owner.GetType().FullName, name);
            return field.GetValue(owner);
        }

        private static void Invoke(object owner, string name, object[] args)
        {
            var method = owner.GetType().GetMethod(name, Members);
            if (method == null) throw new MissingMethodException(owner.GetType().FullName, name);
            method.Invoke(owner, args);
        }

        private static void ForSceneObjects(string typeName, ManualLogSource log, Action<object> action)
        {
            var type = typeof(PlayerProfile).Assembly.GetType(typeName);
            if (type == null) { Attempt(typeName, log, delegate { throw new TypeLoadException(typeName); }); return; }
            foreach (var item in Resources.FindObjectsOfTypeAll(type))
            {
                var component = item as Component;
                if (component == null || !component.gameObject.scene.IsValid()) continue;
                Attempt(typeName, log, delegate { action(item); });
            }
        }

        private static void Attempt(string label, ManualLogSource log, Action action)
        {
            try { action(); }
            catch (Exception ex)
            {
                var actual = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                string key = label + ": " + actual.Message;
                if (reportedErrors.Add(key)) log.LogWarning("Profile UI refresh skipped " + label + ": " + actual);
            }
        }
    }
}
