using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UIElements;

namespace VoidCrewProgressEditor
{
    internal sealed class PauseProgressScreen
    {
        private static ProgressEditor plugin;
        private static readonly Dictionary<object, PauseProgressScreen> screens = new Dictionary<object, PauseProgressScreen>();
        private readonly VisualElement overlay;
        private readonly Button entry, progressUndo;
        private readonly List<Action> synchronize = new List<Action>();
        private readonly List<Slider> sliders = new List<Slider>();
        private VisualElement contentHost, tabHost;
        private VisualTreeAsset settingsAsset;
        private float tabFontSize = 28, tabInset = 48;
        private VisualElement settingsTabLead, settingsTabTail;
        private float rowHeight = 64, rowTop, rowBottom = 14, rowPaddingTop, rowPaddingBottom;
        private readonly List<string> tabTextClasses = new List<string>();
        private bool open;
        private readonly object pauseMenu;
        private readonly VisualElement screenRoot;
        private readonly bool titleMenu;

        internal static void Install(ProgressEditor owner)
        {
            plugin = owner;
            var type = typeof(CG.Profile.PlayerProfile).Assembly.GetType("EscapeMenu");
            var harmony = new Harmony("local.VoidCrewProgressEditor.PauseMenu");
            foreach (string name in new[] { "OnInitialize", "OnEnter" })
                harmony.Patch(type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
                    postfix: new HarmonyMethod(typeof(PauseProgressScreen).GetMethod("Ensure", BindingFlags.Static | BindingFlags.NonPublic)));
            harmony.Patch(type.GetMethod("OnExit", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
                prefix: new HarmonyMethod(typeof(PauseProgressScreen).GetMethod("BeforeExit", BindingFlags.Static | BindingFlags.NonPublic)));
            var titleType = typeof(CG.Profile.PlayerProfile).Assembly.GetType("MainMenu");
            harmony.Patch(titleType.GetMethod("StartAsTitleMenu", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
                postfix: new HarmonyMethod(typeof(PauseProgressScreen).GetMethod("EnsureTitle", BindingFlags.Static | BindingFlags.NonPublic)));
            foreach (string name in new[] { "Shutdown", "Dispose" })
                harmony.Patch(titleType.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
                    prefix: new HarmonyMethod(typeof(PauseProgressScreen).GetMethod("RemoveTitle", BindingFlags.Static | BindingFlags.NonPublic)));
        }

        private static object Field(object owner, string name)
        {
            var field = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return field == null ? null : field.GetValue(owner);
        }

        private static void EnsureTitle(object __instance)
        {
            try
            {
                var menuRoot = Field(__instance, "_mainmenuRoot") as VisualElement;
                var settings = Field(__instance, "_settingsButton") as Button;
                if (menuRoot == null || settings == null || settings.parent == null) return;
                VisualElement root = menuRoot;
                foreach (var document in Resources.FindObjectsOfTypeAll<UIDocument>())
                    if (document.rootVisualElement != null && document.rootVisualElement.Contains(menuRoot))
                    { root = document.rootVisualElement; MainMenuRankPreview.Capture(document); break; }
                EnsureScreen(__instance, root, settings, true);
            }
            catch (Exception ex) { plugin.NativeError(ex); }
        }

        private static void RemoveTitle(object __instance)
        {
            PauseProgressScreen screen;
            if (!screens.TryGetValue(__instance, out screen)) return;
            screen.open = false;
            screen.entry.RemoveFromHierarchy();
            screen.overlay.RemoveFromHierarchy();
            screens.Remove(__instance);
        }

        private static void EnsureScreen(object owner, VisualElement root, Button settings, bool title)
        {
            PauseProgressScreen existing;
            if (screens.TryGetValue(owner, out existing))
            {
                if (root.Contains(existing.entry) && root.Contains(existing.overlay)) return;
                RemoveTitle(owner);
            }
            var stale = new List<VisualElement>();
            root.Query<VisualElement>().ForEach(delegate(VisualElement element)
            {
                if (element.name == "ProgressEditorMenuEntry" || element.name == "ProgressEditorScreen") stale.Add(element);
            });
            foreach (var element in stale) element.RemoveFromHierarchy();
            var screen = new PauseProgressScreen(root, settings, owner, title);
            screens.Add(owner, screen);
            plugin.NativeLog(title ? "Added Edit Progress above Settings in the main menu." : "Added Edit Progress after Settings in the pause menu.");
        }

        private static void Ensure(object __instance)
        {
            try
            {
                var document = Field(__instance, "UIDoc") as UIDocument;
                if (document == null || document.rootVisualElement == null) return;
                var mainMenu = Field(__instance, "_mainMenu");
                if (mainMenu == null) return;
                var settings = Field(mainMenu, "_settingsButton") as Button;
                if (settings == null || settings.parent == null) return;
                EnsureScreen(__instance, document.rootVisualElement, settings, false);
            }
            catch (Exception ex) { plugin.NativeError(ex); }
        }

        private static bool BeforeExit(object __instance, ref bool __result)
        {
            PauseProgressScreen screen;
            if (!screens.TryGetValue(__instance, out screen) || !screen.open) return true;
            screen.Close();
            __result = false;
            return false;
        }

        private PauseProgressScreen(VisualElement root, Button settings, object menu, bool title)
        {
            pauseMenu = menu;
            screenRoot = root;
            titleMenu = title;
            entry = new Button(Show) { text = "Edit Progress", name = "ProgressEditorMenuEntry" };
            foreach (string css in settings.GetClasses()) entry.AddToClassList(css);
            // The title menu can be detached during initialization, making its
            // computed height unavailable. Reuse USS height instead of a 48px fallback.
            entry.style.height = settings.style.height;
            entry.style.minHeight = settings.style.minHeight;
            entry.style.maxHeight = settings.style.maxHeight;
            entry.style.paddingTop = settings.style.paddingTop;
            entry.style.paddingBottom = settings.style.paddingBottom;
            Action matchEntryHeight = delegate
            {
                float height = settings.resolvedStyle.height;
                if (settings.panel != null && height > 0 && !float.IsNaN(height))
                {
                    entry.style.height = height;
                    entry.style.minHeight = height;
                    entry.style.maxHeight = height;
                }
            };
            settings.RegisterCallback<GeometryChangedEvent>(delegate { matchEntryHeight(); });
            entry.RegisterCallback<AttachToPanelEvent>(delegate { entry.schedule.Execute(delegate() { matchEntryHeight(); }); });
            synchronize.Add(matchEntryHeight);
            entry.style.flexShrink = 0;


            overlay = new VisualElement { name = "ProgressEditorScreen" };
            overlay.style.position = Position.Absolute;
            overlay.style.left = overlay.style.right = overlay.style.top = overlay.style.bottom = 0;
            overlay.style.backgroundColor = new Color(0.015f, 0.025f, 0.035f, 1f);
            overlay.style.alignItems = Align.Stretch;
            overlay.style.maxWidth = Length.Percent(100);
            overlay.style.minWidth = 0;
            overlay.style.overflow = Overflow.Hidden;
            overlay.style.display = DisplayStyle.None;

            Button back = ApplySettingsTheme();
            var scroll = new ScrollView();
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.style.width = Length.Percent(100);
            scroll.style.maxWidth = Length.Percent(100);
            scroll.style.minWidth = 0;
            scroll.contentContainer.style.minWidth = 0;
            scroll.contentContainer.style.width = Length.Percent(100);
            scroll.style.flexGrow = 1;
            scroll.style.minHeight = 0;
            scroll.style.paddingLeft = scroll.style.paddingRight = 24;
            scroll.style.paddingTop = scroll.style.paddingBottom = 20;
            scroll.style.color = Color.white;
            (contentHost ?? overlay).Add(scroll);
            var rankPage = new VisualElement { name = "ProgressRankPage" };
            var cosmeticPage = new VisualElement { name = "ProgressCosmeticPage" };
            rankPage.style.flexDirection = FlexDirection.Row;
            var rankControls = new VisualElement { name = "ProgressRankControls" };
            rankControls.style.flexGrow = 1;
            rankControls.style.maxWidth = Length.Percent(50);
            rankControls.style.minWidth = 0;
            rankControls.style.paddingLeft = 24;
            var rankPreview = new VisualElement { name = "ProgressRankPreview" };
            rankPreview.style.width = 657;
            rankPreview.style.maxWidth = Length.Percent(50);
            rankPreview.style.minWidth = 0;
            rankPreview.style.paddingRight = 24;
            rankPreview.style.marginTop = 0;
            rankPreview.style.alignSelf = Align.FlexStart;
            rankPage.Add(rankPreview); rankPage.Add(rankControls);
            var preview = new MainMenuRankPreview(rankPreview, plugin);
            synchronize.Add(preview.Update);
            // TabsRibbon.Headers is initialized by the game's binding lifecycle,
            // not its constructor. Reproduce its decorative elements and USS
            // classes without depending on that controller initialization.
            var tabs = new VisualElement();
            tabs.AddToClassList("tabs-header-panel");
            tabs.style.flexShrink = 0;
            tabs.style.flexDirection = FlexDirection.Row;
            tabs.style.justifyContent = Justify.FlexStart;
            tabs.style.width = Length.Percent(100);
            // Preserve an outer inset before the decorative left slope too.
            tabs.style.paddingLeft = tabInset;
            tabs.style.paddingRight = tabInset;
            var tail = settingsTabTail ?? new VisualElement { name = "HeaderTailingObject" };
            tail.RemoveFromHierarchy();
            tail.AddToClassList("tabs-header-tailing");
            tail.style.flexGrow = 1;
            tail.style.flexShrink = 1;
            tail.style.minWidth = 0;
            var headers = new VisualElement { name = "HeaderContainer" };
            headers.AddToClassList("tabs-header-container");
            headers.style.flexDirection = FlexDirection.Row;
            headers.style.flexGrow = 0;
            headers.style.flexShrink = 0;
            headers.style.justifyContent = Justify.FlexStart;
            tabs.Add(headers);
            var lead = settingsTabLead ?? new VisualElement { name = "HeaderLeadingObject" };
            lead.RemoveFromHierarchy();
            lead.AddToClassList("tabs-header-leading");
            lead.style.flexGrow = 0;
            lead.style.flexShrink = 0;
            tabs.Insert(0, lead);
            tabs.Add(tail);
            var rankTab = new Button { text = "Rank" };
            var cosmeticTab = new Button { text = "Cosmetics" };
            rankTab.AddToClassList("tabs-header");
            cosmeticTab.AddToClassList("tabs-header");
            foreach (var css in tabTextClasses)
            { rankTab.AddToClassList(css); cosmeticTab.AddToClassList(css); }
            rankTab.style.fontSize = cosmeticTab.style.fontSize = tabFontSize;
            headers.Add(rankTab);
            var spacing = new VisualElement { name = "SpacingObject" };
            spacing.AddToClassList("tabs-spacing-object");
            spacing.style.flexGrow = 0;
            spacing.style.minWidth = 16;
            headers.Add(spacing);
            headers.Add(cosmeticTab);
            Action<bool> selectTab = delegate(bool rankSelected)
            {
                rankPage.style.display = rankSelected ? DisplayStyle.Flex : DisplayStyle.None;
                cosmeticPage.style.display = rankSelected ? DisplayStyle.None : DisplayStyle.Flex;
                rankTab.EnableInClassList("tabs-header__selected", rankSelected);
                cosmeticTab.EnableInClassList("tabs-header__selected", !rankSelected);
                rankTab.EnableInClassList("selected", rankSelected);
                cosmeticTab.EnableInClassList("selected", !rankSelected);
                scroll.scrollOffset = Vector2.zero;
            };
            rankTab.clicked += delegate { selectTab(true); };
            cosmeticTab.clicked += delegate { selectTab(false); };
            if (tabHost != null) tabHost.Add(tabs);
            else scroll.parent.Insert(scroll.parent.IndexOf(scroll), tabs);
            scroll.Add(rankPage); scroll.Add(cosmeticPage);
            selectTab(true);

            AddField(rankControls, "Rank", delegate { return plugin.rank; }, delegate(string value) { plugin.rank = value; }, delegate { return 30; });
            AddField(rankControls, "Experience", delegate { return plugin.xp; }, delegate(string value) { plugin.xp = value; }, delegate
            { long maximum; return ProgressEditor.TryGetMaximumXp(plugin.rank, out maximum) ? maximum - 1 : 0; });
            AddField(rankControls, "Favor rank", delegate { return plugin.favor; }, delegate(string value) { plugin.favor = value; }, delegate
            { return plugin.FavorSliderMaximum; });


            AddCosmeticToggle(cosmeticPage, "Unlock All Lootbox Cosmetics", "Lootbox");
            AddCosmeticToggle(cosmeticPage, "Unlock All Seasonal Cosmetics", "Seasonal");
            AddCosmeticToggle(cosmeticPage, "Unlock All Achievement Cosmetics", "Achievement");
            var tabArea = contentHost ?? overlay;
            tabArea.style.flexDirection = FlexDirection.Column;
            tabArea.style.flexGrow = 1;
            tabArea.style.minHeight = 0;
            if (back == null) back = new Button { text = "Back" };
            back.text = "Back";
            back.Query<Label>().ForEach(delegate(Label label) { label.text = "Back"; });
            back.RegisterCallback<AttachToPanelEvent>(delegate
            {
                back.schedule.Execute(delegate()
                {
                    back.text = "Back";
                    back.Query<Label>().ForEach(delegate(Label label) { label.text = "Back"; });
                });
            });
            back.clicked += Close;
            back.style.flexShrink = 0;
            if (back.parent == null) overlay.Add(back);
            var rankActions = new VisualElement { name = "ProgressEditorRankActions" };
            rankActions.style.flexDirection = FlexDirection.Row;
            rankActions.style.justifyContent = Justify.FlexEnd;
            rankActions.style.alignItems = Align.Center;
            rankActions.style.marginTop = 8;
            rankActions.style.marginBottom = 14;
            progressUndo = CreateFooterButton(back, "Undo", delegate { plugin.Undo(); Sync(); });
            var apply = CreateFooterButton(back, "Apply", delegate { plugin.Apply(); Sync(); });
            progressUndo.style.marginRight = 12;
            rankActions.Add(progressUndo); rankActions.Add(apply);
            rankControls.Add(rankActions);
            overlay.RegisterCallback<GeometryChangedEvent>(delegate { ResizeSliders(); });
            // Publish a complete screen only. Failed construction cannot leave
            // a clickable launcher or a partial overlay in the game document.
            root.Add(overlay);
            settings.parent.Insert(settings.parent.IndexOf(settings) + (title ? 0 : 1), entry);
        }

        // Clone the asset, not the live settings controls: no Apply/Cancel game
        // callbacks are carried over into the editor's Back button.
        private Button ApplySettingsTheme()
        {
            try
            {
                var type = typeof(CG.Profile.PlayerProfile).Assembly.GetType("UI.Settings.SettingsUIPanel");
                foreach (var owner in Resources.FindObjectsOfTypeAll(type))
                {
                    var component = owner as Component;
                    var document = component == null ? null : component.GetComponent<UIDocument>();
                    if (document == null || document.visualTreeAsset == null) continue;
                    settingsAsset = document.visualTreeAsset;
                    var template = settingsAsset.CloneTree();
                    CopyStyleSheets(template, overlay);
                    var liveRoot = document.rootVisualElement;
                    if (liveRoot != null)
                    {
                        CopyStyleSheets(liveRoot, overlay);
                        var nativeRow = liveRoot.Q<VisualElement>(className: "settings-entry-type__boolean");
                        if (nativeRow != null)
                        {
                            var dimensions = nativeRow.resolvedStyle;
                            float height = dimensions.height > 0 ? dimensions.height : dimensions.minHeight.value;
                            if (height > 0 && !float.IsNaN(height)) rowHeight = height;
                            rowTop = dimensions.marginTop;
                            rowBottom = dimensions.marginBottom;
                            rowPaddingTop = dimensions.paddingTop;
                            rowPaddingBottom = dimensions.paddingBottom;
                        }
                        var header = liveRoot.Q<VisualElement>(className: "tabs-header");
                        var ribbon = liveRoot.Q<UI.Core.TabsRibbon>();
                        if (header != null && ribbon != null && header.worldBound.width > 0)
                        {
                            float inset = header.worldBound.x - ribbon.worldBound.x;
                            if (inset > 0 && inset < 300) tabInset = inset;
                            float outer = ribbon.worldBound.x - liveRoot.worldBound.x;
                            if (outer > 0 && outer < 300) tabInset = outer;
                        }
                        var text = header as TextElement;
                        if (text == null && header != null) text = header.Q<TextElement>();
                        if (text != null && text.resolvedStyle.fontSize > 0)
                            tabFontSize = text.resolvedStyle.fontSize;
                    }
                    foreach (var css in template.GetClasses()) overlay.AddToClassList(css);
                    var settingsPanel = template.Q<VisualElement>("GeneralSettingsPanel");
                    for (var shell = settingsPanel == null ? null : settingsPanel.parent;
                        shell != null && shell != template; shell = shell.parent)
                        foreach (var css in shell.GetClasses()) overlay.AddToClassList(css);
                    var back = template.Q<Button>("CancelButton");
                    if (settingsPanel != null)
                    {
                        settingsPanel.Clear();
                        settingsPanel.style.display = DisplayStyle.Flex;
                        settingsPanel.style.minHeight = 0;
                        contentHost = settingsPanel;
                    }
                    var oldTabs = template.Q<UI.Core.TabsRibbon>();
                    if (oldTabs != null && oldTabs.parent != null)
                    {
                        settingsTabLead = oldTabs.Q<VisualElement>("HeaderLeadingObject");
                        settingsTabTail = oldTabs.Q<VisualElement>("HeaderTailingObject");
                        var nativeHeader = oldTabs.Q<VisualElement>(className: "tabs-header");
                        var nativeText = nativeHeader as TextElement;
                        if (nativeText == null && nativeHeader != null) nativeText = nativeHeader.Q<TextElement>();
                        if (nativeText != null)
                            foreach (var css in nativeText.GetClasses()) tabTextClasses.Add(css);
                        tabHost = new VisualElement();
                        tabHost.style.flexShrink = 0;
                        oldTabs.parent.Insert(oldTabs.parent.IndexOf(oldTabs), tabHost);
                        oldTabs.RemoveFromHierarchy();
                    }
                    // Keep the actual settings shell hierarchy and artwork.
                    // USS backgrounds can belong to containers with arbitrary
                    // names, and rely on ancestor selectors. Name matching and
                    // copying computed styles from a hidden menu loses these.
                    var remove = new List<VisualElement>();
                    template.Query<VisualElement>().ForEach(delegate(VisualElement element)
                    {
                        // Preserve the original content and footer ancestors:
                        // their height/padding determines the native Back position.
                        if (element == template || element == contentHost || element == tabHost ||
                            (contentHost != null && element.Contains(contentHost)) ||
                            (tabHost != null && element.Contains(tabHost)) ||
                            (back != null && (element == back || element.Contains(back) || back.Contains(element)))) return;
                        if (element is TextElement || element is ScrollView ||
                            element is UI.Core.TabsRibbon || element is NavigatorVE ||
                            element.name.EndsWith("SettingsPanel", StringComparison.Ordinal))
                            remove.Add(element);
                    });
                    foreach (var element in remove) element.RemoveFromHierarchy();
                    // The native shell remains the foreground layout, including
                    // its original footer and any constrained-height containers.
                    template.style.position = Position.Absolute;
                    template.style.left = template.style.right = template.style.top = template.style.bottom = 0;
                    template.style.width = Length.Percent(100);
                    template.style.height = Length.Percent(100);
                    overlay.Insert(0, template);
                    plugin.NativeLog("Progress editor uses settings stylesheets, background elements and Cancel button.");
                    return back;
                }
            }
            catch (Exception ex) { plugin.NativeError(ex); }
            return null;
        }

        private Button CreateFooterButton(Button back, string caption, Action action)
        {
            // Clone the same asset button, including its decorative child labels.
            // Cloned buttons do not carry the live settings controller callbacks.
            Button button = settingsAsset == null ? null : settingsAsset.CloneTree().Q<Button>("CancelButton");
            if (button == null)
            {
                button = new Button();
                foreach (var css in back.GetClasses()) button.AddToClassList(css);
            }
            else button.RemoveFromHierarchy();
            button.text = caption;
            button.Query<Label>().ForEach(delegate(Label label) { label.text = caption; });
            button.RegisterCallback<AttachToPanelEvent>(delegate
            {
                button.schedule.Execute(delegate()
                {
                    button.text = caption;
                    button.Query<Label>().ForEach(delegate(Label label) { label.text = caption; });
                });
            });
            button.style.position = Position.Relative;
            button.style.left = button.style.right = button.style.top = button.style.bottom = StyleKeyword.Auto;
            button.style.marginLeft = button.style.marginTop = button.style.marginBottom = 0;
            button.style.flexGrow = 0;
            button.style.flexShrink = 0;
            button.style.minWidth = 100;
            button.style.width = 140;
            button.style.minHeight = 44;
            button.clicked += action;
            return button;
        }

        private static void CopyStyleSheets(VisualElement source, VisualElement destination)
        {
            for (int i = 0; i < source.styleSheets.count; i++)
                if (!destination.styleSheets.Contains(source.styleSheets[i])) destination.styleSheets.Add(source.styleSheets[i]);
            foreach (var child in source.Children()) CopyStyleSheets(child, destination);
        }

        private void ResizeSliders()
        {
            // Panel units respect UI scaling; physical Screen.width does not.
            float width = overlay.resolvedStyle.width;
            if (width <= 0 || float.IsNaN(width)) return;
            foreach (var slider in sliders) slider.style.maxWidth = width * 0.5f;
        }

        private void SetRowSpacing(VisualElement row)
        {
            row.style.marginTop = rowTop;
            row.style.marginBottom = rowBottom;
            row.style.paddingTop = rowPaddingTop;
            row.style.paddingBottom = rowPaddingBottom;
            row.style.height = rowHeight;
            row.style.minHeight = rowHeight;
            row.style.maxHeight = rowHeight;
            row.style.flexShrink = 0;
            row.style.alignItems = Align.Center;
        }

        private void AddCosmeticToggle(VisualElement parent, string caption, string category)
        {
            // Same hierarchy and classes as BooleanSettingsEntryVE.SetupContainers.
            var row = new VisualElement();
            row.AddToClassList("settings-entry");
            row.AddToClassList("settings-entry-type__boolean");
            var label = new Label(caption) { name = "SettingsLabel" };
            label.AddToClassList("settings-label");
            label.AddToClassList("settings-label-name");
            row.Add(label);
            var controls = new VisualElement { name = "Container" };
            controls.AddToClassList("settings-input-container");
            var off = new Label("Disabled") { name = "Min" };
            off.AddToClassList("settings-label");
            off.AddToClassList("settings-label-min");
            var toggle = new Toggle { name = "Toggle" };
            toggle.AddToClassList("settings-toggle");
            var on = new Label("Enabled") { name = "Max" };
            on.AddToClassList("settings-label");
            on.AddToClassList("settings-label-max");
            controls.Add(off); controls.Add(toggle); controls.Add(on);
            row.Add(controls);
            toggle.RegisterValueChangedCallback(delegate(ChangeEvent<bool> change)
            {
                plugin.SetCosmeticCategory(category, change.newValue);
                Sync();
            });
            SetRowSpacing(row);
            parent.Add(row);
            synchronize.Add(delegate
            {
                bool enabled = plugin.IsCosmeticCategoryEnabled(category);
                toggle.SetValueWithoutNotify(enabled);
                row.EnableInClassList("settings-toggle__true", enabled);
                row.EnableInClassList("settings-toggle__false", !enabled);
            });
        }

        private void AddField(VisualElement parent, string label, Func<string> get, Action<string> set, Func<long> maximum)
        {
            var row = new VisualElement();
            row.AddToClassList("settings-entry");
            row.AddToClassList("settings-entry-type__slider");
            row.style.width = Length.Percent(100);
            row.style.maxWidth = Length.Percent(100);
            row.style.minWidth = 0;
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 0;
            var title = new Label(label);
            title.AddToClassList("settings-label");
            title.AddToClassList("settings-label-name");
            // Reserve the label's measured width. The native input-container
            // stylesheet assumes a full-width settings row, not this half column.
            title.style.position = Position.Relative;
            title.style.left = title.style.right = title.style.top = title.style.bottom = StyleKeyword.Auto;
            title.style.width = 160;
            title.style.minWidth = 160;
            title.style.maxWidth = StyleKeyword.None;
            title.style.flexShrink = 0;
            title.style.flexGrow = 0;
            title.style.whiteSpace = WhiteSpace.NoWrap;
            title.RegisterCallback<GeometryChangedEvent>(delegate
            {
                float width = Math.Max(160, (float)Math.Ceiling(title.MeasureTextSize(label, 0,
                    VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x) + 12);
                if (!float.IsNaN(width) && Math.Abs(title.resolvedStyle.width - width) > 0.5f)
                    title.style.width = title.style.minWidth = width;
            });
            var controls = new VisualElement { name = "Container" };
            controls.AddToClassList("settings-input-container");
            controls.style.flexDirection = FlexDirection.Row;
            controls.style.alignItems = Align.Center;
            controls.style.position = Position.Relative;
            controls.style.left = controls.style.right = controls.style.top = controls.style.bottom = StyleKeyword.Auto;
            controls.style.width = StyleKeyword.Auto;
            controls.style.flexBasis = 0;
            controls.style.flexGrow = 1;
            controls.style.flexShrink = 1;
            controls.style.minWidth = 0;
            controls.style.marginLeft = 12;
            controls.style.overflow = Overflow.Hidden;
            var slider = new Slider(0, 1);
            slider.AddToClassList("settings-slider");
            sliders.Add(slider);
            slider.style.position = Position.Relative;
            slider.style.left = slider.style.right = slider.style.top = slider.style.bottom = StyleKeyword.Auto;
            slider.style.width = StyleKeyword.Auto;
            slider.style.flexBasis = 0;
            slider.style.flexGrow = 1;
            slider.style.flexShrink = 1;
            slider.style.minWidth = 0;
            slider.style.marginRight = 16;
            var text = new TextField();
            text.style.width = 85;
            text.style.maxWidth = Length.Percent(30);
            text.style.minWidth = 0;
            text.style.flexShrink = 1;
            text.style.color = Color.white;
            text.Query<VisualElement>().ForEach(delegate(VisualElement child) { child.style.color = Color.white; });
            var input = text.Q<VisualElement>(className: "unity-text-field__input");
            Action matchInputBackground = delegate
            {
                var track = slider.Q<VisualElement>(className: "unity-base-slider__tracker");
                Color gray = slider.resolvedStyle.backgroundColor;
                if (gray.a == 0) gray = controls.resolvedStyle.backgroundColor;
                if (gray.a == 0) gray = row.resolvedStyle.backgroundColor;
                if (gray.a == 0 && track != null) gray = track.resolvedStyle.backgroundColor;
                if (gray.a == 0) gray = new Color(0.18f, 0.18f, 0.18f, 1f);
                text.style.backgroundColor = gray;
                if (input != null) input.style.backgroundColor = gray;
            };
            slider.RegisterCallback<GeometryChangedEvent>(delegate { matchInputBackground(); });
            text.RegisterCallback<AttachToPanelEvent>(delegate { text.schedule.Execute(delegate() { matchInputBackground(); }); });
            matchInputBackground();
            row.Add(title); controls.Add(slider); controls.Add(text); row.Add(controls); SetRowSpacing(row); parent.Add(row);
            text.RegisterValueChangedCallback(delegate(ChangeEvent<string> e) { set(e.newValue); });
            slider.RegisterValueChangedCallback(delegate(ChangeEvent<float> e)
            {
                long max = maximum();
                long value = e.newValue >= 1 ? max : (long)Math.Round((double)e.newValue * max);
                set(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                text.SetValueWithoutNotify(get());
            });
            synchronize.Add(delegate
            {
                if (text.value != get()) text.SetValueWithoutNotify(get());
                long max = maximum(), value;
                if (!long.TryParse(get(), out value)) value = 0;
                slider.SetEnabled(max > 0);
                slider.SetValueWithoutNotify(max > 0 ? (float)Math.Max(0, Math.Min(1, (double)value / max)) : 0);
            });
        }

        private NavigatorVE GetNavigator()
        {
            if (!titleMenu) return Field(pauseMenu, "_navigator") as NavigatorVE;
            return entry.GetFirstAncestorOfType<NavigatorVE>();
        }

        private void Show()
        {
            plugin.OpenNativeEditor();
            open = true;
            overlay.style.display = DisplayStyle.Flex;
            overlay.BringToFront();
            ResizeSliders();
            var navigator = GetNavigator();
            if (navigator != null) navigator.Disable();
            Sync();
        }
        private void Close()
        {
            open = false;
            overlay.style.display = DisplayStyle.None;
            var navigator = GetNavigator();
            if (navigator != null) navigator.Enable(10);
            entry.Focus();
        }
        private void Sync()
        {
            foreach (var action in synchronize) action();
            progressUndo.SetEnabled(plugin.HasProgressUndo);

        }
        internal static void UpdateOpenScreens()
        {
            var destroyed = new List<object>();
            foreach (var pair in screens)
            {
                var component = pair.Key as UnityEngine.Object;
                if ((pair.Key is UnityEngine.Object && component == null) ||
                    !pair.Value.screenRoot.Contains(pair.Value.entry) || !pair.Value.screenRoot.Contains(pair.Value.overlay))
                { destroyed.Add(pair.Key); continue; }
                if (pair.Value.open) pair.Value.Sync();
            }
            foreach (var item in destroyed)
            {
                screens[item].open = false;
                screens[item].entry.RemoveFromHierarchy();
                screens[item].overlay.RemoveFromHierarchy();
                screens.Remove(item);
            }
        }
        internal static bool IsOpen
        { get { foreach (var screen in screens.Values) if (screen.open) return true; return false; } }
    }
}









