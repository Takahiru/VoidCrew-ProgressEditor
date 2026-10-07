using System;
using System.Collections.Generic;
using CG.Profile;
using UnityEngine;
using UnityEngine.UIElements;

namespace VoidCrewProgressEditor
{
    internal sealed class MainMenuRankPreview
    {
        private static VisualTreeAsset titleAsset;
        private static float mainBarOffsetX = 72, mainBarWidth = 633, mainBarHeight = 131, mainBackgroundWidth = 563;
        private sealed class NativeSize
        {
            internal float Width, Height;
            internal Scale Scale;
        }
        private static readonly Dictionary<string, NativeSize> nativeSizes = new Dictionary<string, NativeSize>();
        private readonly VisualElement host;
        private readonly ProgressEditor plugin;
        private VisualElement bar, card;
        private long lastXp = -1;
        private int lastRank = -1, lastFavor = -1;
        private bool failed;
        private string lastName;

        internal static void Capture(UIDocument document)
        {
            if (document == null || document.visualTreeAsset == null) return;
            titleAsset = document.visualTreeAsset;
            var main = document.rootVisualElement == null ? null : document.rootVisualElement.Q<VisualElement>("mainmenu");
            var background = main == null ? null : main.Q<VisualElement>("background");
            var sourceBar = document.rootVisualElement == null ? null : document.rootVisualElement.Q<VisualElement>("ExpProgressBar");
            if (background != null && sourceBar != null && sourceBar.worldBound.width > 0 && background.worldBound.height > 0)
            {
                // Convert the native bar's panel position into the background's local units.
                var offset = background.WorldToLocal(sourceBar.worldBound.position);
                mainBarOffsetX = offset.x;
                mainBarWidth = sourceBar.resolvedStyle.width;
                mainBarHeight = sourceBar.resolvedStyle.height;
                mainBackgroundWidth = background.resolvedStyle.width;
                foreach (var name in new[] { "MainBarContainer", "MainBar", "ProgressBar", "RankDisplayVE", "LowerText", "LowerRank", "LowerEXP" })
                {
                    var element = sourceBar.Q<VisualElement>(name);
                    if (element == null || element.resolvedStyle.width <= 0 || element.resolvedStyle.height <= 0) continue;
                    nativeSizes[name] = new NativeSize { Width = element.resolvedStyle.width,
                        Height = element.resolvedStyle.height, Scale = element.resolvedStyle.scale };
                }
            }
        }

        internal MainMenuRankPreview(VisualElement container, ProgressEditor owner)
        { host = container; plugin = owner; }

        internal void Update()
        {
            if (failed) return;
            try
            {
                if (bar == null)
                {
                    if (titleAsset == null)
                        foreach (var menu in Resources.FindObjectsOfTypeAll<TitleMenu>()) Capture(menu.UIDoc);
                    if (titleAsset == null) return;
                    var tree = titleAsset.CloneTree();
                    bar = tree.Q<VisualElement>("ExpProgressBar");
                    if (bar == null) throw new InvalidOperationException("Main menu rank display asset is missing.");
                    CopySheets(tree, host);
                    foreach (var menu in Resources.FindObjectsOfTypeAll<TitleMenu>()) Capture(menu.UIDoc);
                    card = new VisualElement { name = "ProgressEditorProfileCard" };
                    // Fit the preview column to the native card instead of reserving half the page.
                    host.style.width = mainBarWidth + 24;
                    host.style.maxWidth = StyleKeyword.None;
                    host.style.flexShrink = 0;
                    card.style.width = mainBarWidth;
                    card.style.maxWidth = StyleKeyword.None;
                    card.style.height = 350;
                    card.style.position = Position.Relative;
                    card.style.overflow = Overflow.Visible;
                    var diamond = new RankCardDiamond { name = "ProgressEditorRankDiamond" };
                    diamond.style.position = Position.Absolute;
                    diamond.style.left = -mainBarOffsetX;
                    diamond.style.top = 0;
                    diamond.style.width = mainBackgroundWidth;
                    float artworkHeight = mainBackgroundWidth * diamond.AspectRatio;
                    diamond.style.height = artworkHeight;
                    card.style.height = artworkHeight;
                    card.Add(diamond);
                    bar.RemoveFromHierarchy();
                    // Keep the native internal layout and align its badge center to the centered artwork.
                    bar.style.position = Position.Absolute;
                    bar.style.left = 0;
                    bar.style.top = (artworkHeight - mainBarHeight) / 2;
                    bar.style.right = bar.style.bottom = StyleKeyword.Auto;
                    bar.style.marginTop = bar.style.marginBottom = bar.style.marginLeft = bar.style.marginRight = 0;
                    bar.style.height = mainBarHeight;
                    bar.style.width = mainBarWidth;
                    bar.style.maxWidth = StyleKeyword.None;
                    bar.style.flexShrink = 0;
                    // Use native computed sizes; parent settings styles and flex constraints
                    // must not compress the main-menu foreground.
                    foreach (var entry in nativeSizes)
                    {
                        var element = bar.Q<VisualElement>(entry.Key);
                        if (element == null) continue;
                        element.style.width = entry.Value.Width;
                        element.style.height = entry.Value.Height;
                        element.style.flexShrink = 0;
                        element.style.scale = entry.Value.Scale;
                    }
                    bar.style.minWidth = 0;
                    bar.style.display = DisplayStyle.Flex;
                    bar.RemoveFromClassList("hidden-element");
                    card.Add(bar);
                    host.Add(card);
                    var centerBadge = bar.Q<UI.Core.RankDisplayVE>();
                    EventCallback<GeometryChangedEvent> align = delegate(GeometryChangedEvent evt)
                    {
                        if (centerBadge == null || centerBadge.worldBound.height <= 0 || bar.worldBound.height <= 0) return;
                        float center = bar.WorldToLocal(centerBadge.worldBound.center).y;
                        float top = artworkHeight / 2 - center;
                        if (!float.IsNaN(top) && !float.IsInfinity(top) && Mathf.Abs(bar.style.top.value.value - top) > 0.1f)
                            bar.style.top = top;
                    };
                    bar.RegisterCallback<GeometryChangedEvent>(align);
                    if (centerBadge != null) centerBadge.RegisterCallback<GeometryChangedEvent>(align);
                    SetText("PlayerName", ""); // Clear the asset's placeholder before binding profile data.
                }
                var player = PlayerProfile.Instance;
                if (player == null || player.Profile == null) return;
                int rank = player.Profile.Rank, favor = player.Profile.FavorRank;
                long xp = player.Profile.Xp;
                string playerName = PlayerProfile.SanitizedUserAccountNickname() ?? "";
                if (rank == lastRank && favor == lastFavor && xp == lastXp && playerName == lastName) return;
                SetText("PlayerName", playerName);
                var sequence = XPSliderFactory.GetSequence(rank, favor, xp, new List<XPSource>());
                if (sequence == null || sequence.Count == 0) return;
                var state = sequence[sequence.Count - 1];
                // RewardExpBar.Recolour applies these classes to reveal the native rank/favor artwork.
                bar.EnableInClassList("style__metem-rank", !state.IsInFavorLevels);
                bar.EnableInClassList("style__favor-level", state.IsInFavorLevels);
                var badge = bar.Q<UI.Core.RankDisplayVE>();
                if (badge != null) badge.Show(state.Rank, state.IsInFavorLevels, false, default(Times));
                SetText("RankNumber", state.Rank.ToString());
                SetText("Title", ". " + state.title);
                SetText("CurrentXp", state.TargetXP.ToString());
                SetText("MaxXp", state.MaxXP.ToString());
                float percent = state.MaxXP > 0 ? (float)Math.Max(0, Math.Min(100, (double)state.TargetXP * 100 / state.MaxXP)) : 0;
                foreach (var name in new[] { "FrontProgress", "BackProgress" })
                {
                    var fill = bar.Q<VisualElement>(name);
                    if (fill != null) fill.style.width = Length.Percent(percent);
                }
                foreach (var name in new[] { "SourceContainer", "RankUpContainer", "RankRewards", "TotalXP" })
                {
                    var element = bar.Q<VisualElement>(name);
                    if (element != null) element.style.display = DisplayStyle.None;
                }
                lastRank = rank; lastFavor = favor; lastXp = xp; lastName = playerName;
            }
            catch (Exception ex) { failed = true; plugin.NativeError(ex); }
        }

        private void SetText(string name, string value)
        { var label = bar.Q<Label>(name); if (label != null) label.text = value; }

        private static void CopySheets(VisualElement source, VisualElement destination)
        {
            for (int i = 0; i < source.styleSheets.count; i++)
                if (!destination.styleSheets.Contains(source.styleSheets[i])) destination.styleSheets.Add(source.styleSheets[i]);
            foreach (var child in source.Children()) CopySheets(child, destination);
        }
    }
}





