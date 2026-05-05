using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using HarmonyInstance = HarmonyLib.Harmony;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMonomiPark.SlimeRancher;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.Input;
using Il2CppMonomiPark.SlimeRancher.Options;
using Il2CppMonomiPark.SlimeRancher.Platform;
using Il2CppMonomiPark.SlimeRancher.Script.Util;
using Il2CppMonomiPark.SlimeRancher.UI.Options;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;
using QualityLevel = Il2CppMonomiPark.ScriptedValue.QualityLevel;
using Object = UnityEngine.Object;

namespace rancher_minimap
{
    /// <summary>
    /// Vanilla options integration for the fresh minimap.
    ///
    /// Current SR2 layout, verified against the dump/Ghidra pair:
    /// - OptionsUIRoot.BindCategories receives an OptionsConfiguration.
    /// - OptionsConfiguration owns an Il2Cpp list named items.
    /// - Each item is an OptionsItemCategory.
    /// - OptionsItemCategory owns its own items list of OptionsItemDefinition rows.
    ///
    /// previously tried to mutate OptionsConfiguration through managed reflection.
    /// failed cause IL2CPP wrappers only expose proxy bookkeeping fields to System.Reflection 
    /// thus now generated Il2CppMonomiPark.* wrapper types instead.
    /// </summary>
    internal sealed class OptionsMenuInstaller : IDisposable
    {
        private const int TargetRiderIndex = 6;
        private const string CategoryName = "RancherMinimap";
        private const string ReferencePrefix = "setting.rancherminimap.";

        private static OptionsMenuInstaller Current;
        private static readonly Dictionary<string, RancherMinimapOptionSpec> SpecsByReferenceId = new Dictionary<string, RancherMinimapOptionSpec>();

        private readonly HarmonyInstance _harmony;
        private readonly MinimapSettings _settings;
        private readonly List<RancherMinimapOptionSpec> _specs = new List<RancherMinimapOptionSpec>();
        private readonly List<Object> _createdObjects = new List<Object>();

        private bool _installed;
        private bool _specsBuilt;
        private LocalizedString _categoryTitle;
        private Sprite _categoryIcon;

        public bool IsInstalled => _installed;

        public OptionsMenuInstaller(HarmonyInstance harmony, MinimapSettings settings)
        {
            _harmony = harmony;
            _settings = settings;
            Current = this;
        }

        public void Install()
        {
            if (_installed)
                return;

            Patch(typeof(OptionsUIRoot), "BindCategories", nameof(BindCategoriesPrefix), nameof(BindCategoriesPostfix));
            Patch(typeof(OptionsUIRoot), "SwapCategory", nameof(SwapCategoryPrefix), null);
            Patch(typeof(OptionsUIRoot), "BindItemCategory", nameof(BindItemCategoryPrefix), null);
            Patch(typeof(PresetOptionsItemDefinition), "CreateOptionItemModel", null, nameof(CreateOptionItemModelFinalizer));
            Patch(typeof(PresetOptionsItemModel), "RebuildOptions", null, nameof(RebuildOptionsFinalizer));
            Patch(typeof(ScriptedValuePresetOptionDefinition), "ApplyPresetSelection", nameof(ScriptedApplyPresetSelectionPrefix), null);
            Patch(typeof(ScriptedValuePresetOptionDefinition), "GetDefaultPresetIndex", nameof(ScriptedGetDefaultPresetIndexPrefix), null);

            _installed = true;
            Log.Info("options: typed patches installed");
        }

        public void Dispose()
        {
            SpecsByReferenceId.Clear();

            foreach (var obj in _createdObjects)
            {
                if (obj != null)
                    Object.Destroy(obj);
            }
            _createdObjects.Clear();

            if (ReferenceEquals(Current, this))
                Current = null;
        }

        private void Patch(Type type, string gameMethodName, string prefixName, string postfixOrFinalizerName)
        {
            var original = AccessTools.Method(type, gameMethodName);
            if (original == null)
            {
                Log.Warn($"options: method missing {type.FullName}.{gameMethodName}");
                return;
            }

            var self = typeof(OptionsMenuInstaller);
            var prefix = prefixName == null ? null : new HarmonyMethod(AccessTools.Method(self, prefixName));
            var postfix = postfixOrFinalizerName == null ? null : new HarmonyMethod(AccessTools.Method(self, postfixOrFinalizerName));

            if (postfixOrFinalizerName != null && postfixOrFinalizerName.EndsWith("Finalizer", StringComparison.Ordinal))
                _harmony.Patch(original, prefix: prefix, finalizer: postfix);
            else
                _harmony.Patch(original, prefix: prefix, postfix: postfix);
        }

        private static void BindCategoriesPrefix(OptionsConfiguration _optionsConfiguration)
        {
            Current?.Inject(_optionsConfiguration, "BindCategories.Prefix");
        }

        private static void BindCategoriesPostfix(OptionsUIRoot __instance)
        {
            Current?.LogCategoryState(__instance, "BindCategories.Postfix");
        }

        private static void SwapCategoryPrefix(OptionsUIRoot __instance, int categoryIndex)
        {
            if (Current == null)
                return;

            var category = Current.GetUiCategory(__instance, categoryIndex);
            if (category != null && Current.IsOurCategory(category))
                Current.EnsureModels(category, "SwapCategory.Prefix");
        }

        private static void BindItemCategoryPrefix(OptionsUIRoot __instance, OptionsItemCategory category)
        {
            if (Current == null || category == null || !Current.IsOurCategory(category))
                return;

            Current.EnsureModels(category, "BindItemCategory.Prefix");
        }

        private static Exception CreateOptionItemModelFinalizer(PresetOptionsItemDefinition __instance, Exception __exception)
        {
            if (__exception == null || !IsRancherDefinition(__instance))
                return __exception;

            Log.Warn($"options: suppressed model-create exception for {__instance.ReferenceId}: {__exception.GetType().Name}");
            return null;
        }

        private static Exception RebuildOptionsFinalizer(PresetOptionsItemModel __instance, Exception __exception)
        {
            if (__exception == null)
                return null;

            var definition = __instance?._presetOptionsItemDefinition;
            if (!IsRancherDefinition(definition))
                return __exception;

            Log.Warn($"options: suppressed model-rebuild exception for {definition.ReferenceId}: {__exception.GetType().Name}");
            return null;
        }

        private static bool ScriptedApplyPresetSelectionPrefix(ScriptedValuePresetOptionDefinition __instance, int index)
        {
            if (!TryGetSpec(__instance?.ReferenceId, out var spec))
                return true;

            spec.Apply(index);
            return false;
        }

        private static bool ScriptedGetDefaultPresetIndexPrefix(ScriptedValuePresetOptionDefinition __instance, ref int __result)
        {
            if (!TryGetSpec(__instance?.ReferenceId, out var spec))
                return true;

            __result = spec.CurrentIndex();
            return false;
        }

        private void Inject(OptionsConfiguration configuration, string source)
        {
            if (configuration == null)
                return;

            EnsureSpecs();

            if (configuration.items == null)
            {
                configuration.items = new Il2CppSystem.Collections.Generic.List<OptionsItemCategory>();
                Log.Warn("options: OptionsConfiguration.items was null; created list");
            }

            var category = FindCategory(configuration);
            if (category == null)
            {
                category = ScriptableObject.CreateInstance<OptionsItemCategory>();
                category.name = CategoryName;
                category.items = new Il2CppSystem.Collections.Generic.List<OptionsItemDefinition>();
                category._showRebindButton = false;
                _createdObjects.Add(category);

                var insertIndex = Math.Min(TargetRiderIndex, configuration.items.Count);
                configuration.items.Insert(insertIndex, category);
            }

            category._title = _categoryTitle;
            category._icon = ResolveIcon();
            if (category.items == null)
                category.items = new Il2CppSystem.Collections.Generic.List<OptionsItemDefinition>();

            RebuildRows(category);
            EnsureModels(category, source);

        }

        private void EnsureSpecs()
        {
            if (_specsBuilt)
                return;

            _categoryTitle = Localized("Minimap", "category");

            _specs.Add(RancherMinimapOptionSpec.Toggle(
                "enabled", Localized(MinimapOptionText.EnabledLabel, "enabled"), Localized(MinimapOptionText.EnabledDescription, "enabled.desc"),
                () => _settings.Enabled, v => _settings.Enabled = v));

            _specs.Add(RancherMinimapOptionSpec.Toggle(
                "rotate", Localized(MinimapOptionText.RotateMapLabel, "rotate"), Localized(MinimapOptionText.RotateMapDescription, "rotate.desc"),
                () => _settings.RotateMap, v => _settings.RotateMap = v));

            _specs.Add(RancherMinimapOptionSpec.Choice(
                "size", Localized(MinimapOptionText.SizeLabel, "size"), Localized(MinimapOptionText.SizeDescription, "size.desc"),
                new[] { 10f, 12.5f, 15f, 17.5f, 20f, 22.5f, 25f, 27.5f, 30f, 32.5f, 35f, 37.5f, 40f, 45f, 50f, 55f, 60f },
                v => $"{v:0.#}%", () => _settings.SizePercent, v => _settings.SizePercent = v));

            _specs.Add(RancherMinimapOptionSpec.Choice(
                "edge_offset", Localized(MinimapOptionText.EdgeOffsetLabel, "edge_offset"), Localized(MinimapOptionText.EdgeOffsetDescription, "edge_offset.desc"),
                new[] { 0f, 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f, 10f, 11f, 12f, 13f, 14f, 15f },
                v => $"{v:0}%", () => _settings.EdgeOffsetPercent, v => _settings.EdgeOffsetPercent = v));

            _specs.Add(RancherMinimapOptionSpec.Choice(
                "zoom", Localized(MinimapOptionText.ZoomLabel, "zoom"), Localized(MinimapOptionText.ZoomDescription, "zoom.desc"),
                new[] { 0.50f, 0.75f, 1.0f, 1.25f, 1.5f, 2.0f, 2.5f, 3.0f, 3.5f, 4.0f, 4.5f, 5.0f, 5.5f, 6.0f },
                v => $"{v:0.##}x", () => _settings.Zoom, v => _settings.Zoom = v));

            _specs.Add(RancherMinimapOptionSpec.Choice(
                "dynamic_zoom_max_out", Localized(MinimapOptionText.DynamicZoomAmountLabel, "dynamic_zoom_max_out"), Localized(MinimapOptionText.DynamicZoomAmountDescription, "dynamic_zoom_max_out.desc"),
                new[] { 0.0f, 0.05f, 0.10f, 0.15f, 0.20f, 0.25f, 0.35f, 0.50f, 0.75f, 1.0f },
                v => v <= 0f ? "Off" : $"{v:0.##}x", () => _settings.DynamicZoomMaxOut, v => _settings.DynamicZoomMaxOut = v));

            _specs.Add(RancherMinimapOptionSpec.Choice(
                "opacity", Localized(MinimapOptionText.OpacityLabel, "opacity"), Localized(MinimapOptionText.OpacityDescription, "opacity.desc"),
                new[] { 0.0f, 0.05f, 0.10f, 0.15f, 0.25f, 0.4f, 0.55f, 0.7f, 0.82f, 0.9f, 1.0f },
                v => $"{v * 100f:0}%", () => _settings.Opacity, v => _settings.Opacity = v));

            _specs.Add(RancherMinimapOptionSpec.Choice(
                "icon_scale", Localized(MinimapOptionText.IconScaleLabel, "icon_scale"), Localized(MinimapOptionText.IconScaleDescription, "icon_scale.desc"),
                new[] { 0.10f, 0.20f, 0.33f, 0.50f, 0.75f, 1.0f, 1.25f, 1.5f, 2.0f, 2.5f, 3.5f, 5.0f, 7.5f, 10.0f },
                v => $"{v:0.##}x", () => _settings.IconScale, v => _settings.IconScale = v));

            _specs.Add(RancherMinimapOptionSpec.Toggle(
                "show_markers", Localized(MinimapOptionText.ShowMarkersLabel, "show_markers"), Localized(MinimapOptionText.ShowMarkersDescription, "show_markers.desc"),
                () => _settings.ShowMarkers, v => _settings.ShowMarkers = v));

            _specs.Add(RancherMinimapOptionSpec.Toggle(
                "show_map_background", Localized(MinimapOptionText.ShowMapBackgroundLabel, "show_map_background"), Localized(MinimapOptionText.ShowMapBackgroundDescription, "show_map_background.desc"),
                () => _settings.ShowMapBackground, v => _settings.ShowMapBackground = v));

            _specs.Add(RancherMinimapOptionSpec.Toggle(
                "show_decorative_clouds", Localized(MinimapOptionText.ShowDecorativeCloudsLabel, "show_decorative_clouds"), Localized(MinimapOptionText.ShowDecorativeCloudsDescription, "show_decorative_clouds.desc"),
                () => _settings.ShowDecorativeClouds, v => _settings.ShowDecorativeClouds = v));

            _specsBuilt = true;
        }

        internal static bool TryGetSpec(string referenceId, out RancherMinimapOptionSpec spec)
        {
            spec = null;
            return !string.IsNullOrWhiteSpace(referenceId) && SpecsByReferenceId.TryGetValue(referenceId, out spec);
        }

        private void RebuildRows(OptionsItemCategory category)
        {
            if (CategoryMatches(category))
                return;

            category.items.Clear();

            foreach (var spec in _specs)
            {
                var definition = CreateDefinition(spec);
                category.items.Add(definition);
            }
        }

        private bool CategoryMatches(OptionsItemCategory category)
        {
            if (category?.items == null || category.items.Count != _specs.Count)
                return false;

            for (var i = 0; i < _specs.Count; i++)
            {
                var definition = category.items[i] as ScriptedValuePresetOptionDefinition;
                if (definition == null || !string.Equals(definition.ReferenceId, ReferencePrefix + _specs[i].Id, StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        private ScriptedValuePresetOptionDefinition CreateDefinition(RancherMinimapOptionSpec spec)
        {
            var template = Resources.FindObjectsOfTypeAll<ScriptedValuePresetOptionDefinition>().FirstOrDefault();
            if (template == null)
                throw new InvalidOperationException("Could not find ScriptedValuePresetOptionDefinition template");

            var definition = ScriptableObject.CreateInstance<ScriptedValuePresetOptionDefinition>();
            var referenceId = ReferencePrefix + spec.Id;
            definition.name = "RancherMinimap_" + spec.Id;
            definition._referenceId = referenceId;
            definition._label = spec.Label;
            definition._detailsText = spec.Details;
            definition._applyImmediately = true;
            definition._requireConfirmation = false;
            definition._wrapAround = false;
            definition._defaultValueIndex = spec.CurrentIndex();
            definition._isProfileSetting = true;
            definition._showTutorialDisclaimer = false;
            definition._optionsItemModels = new Il2CppSystem.Collections.Generic.List<PresetOptionsItemModel>();
            definition.SupportedInputDeviceAssets = new Il2CppSystem.Collections.Generic.List<InputDeviceAsset>();
            definition.SupportedPlatforms = new Il2CppSystem.Collections.Generic.List<StoreAndPlatform>();
            definition._controlPrefab = template._controlPrefab;
            definition._confirmationPopupConfig = template._confirmationPopupConfig;

            var presets = new Il2CppReferenceArray<ScriptedValuePresetOptionDefinition.ScriptedValuePreset>(spec.Count);
            for (var i = 0; i < spec.Count; i++)
            {
                var preset = new ScriptedValuePresetOptionDefinition.ScriptedValuePreset();
                preset._presetLabel = spec.LabelForIndex(i);
                preset._referenceId = referenceId + ".preset." + i;
                preset._scriptedBoolSettings = new Il2CppReferenceArray<ScriptedValuePresetOptionDefinition.ScriptedValueSetting<Il2CppMonomiPark.ScriptedValue.ScriptedBool, bool>>(0);
                preset._scriptedFloatSettings = new Il2CppReferenceArray<ScriptedValuePresetOptionDefinition.ScriptedValueSetting<Il2CppMonomiPark.ScriptedValue.ScriptedFloat, float>>(0);
                preset._scriptedIntSettings = new Il2CppReferenceArray<ScriptedValuePresetOptionDefinition.ScriptedValueSetting<Il2CppMonomiPark.ScriptedValue.ScriptedInt, int>>(0);
                preset._scriptedQualitySettings = new Il2CppSystem.Collections.Generic.List<ScriptedValuePresetOptionDefinition.ScriptedValueSetting<Il2CppMonomiPark.ScriptedValue.ScriptedQuality, QualityLevel>>();
                presets[i] = preset;
            }

            definition._optionsPresets = presets;
            SpecsByReferenceId[referenceId] = spec;
            _createdObjects.Add(definition);
            return definition;
        }

        private void EnsureModels(OptionsItemCategory category, string source)
        {
            if (category?.items == null)
                return;

            foreach (var obj in category.items)
            {
                var definition = obj as ScriptedValuePresetOptionDefinition;
                if (definition == null)
                    continue;

                if (definition._optionsItemModels == null)
                    definition._optionsItemModels = new Il2CppSystem.Collections.Generic.List<PresetOptionsItemModel>();

                if (definition._optionsItemModels.Count == 0)
                {
                    try
                    {
                        definition.CreateOptionItemModel();
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"options: {source}: CreateOptionItemModel threw for {definition.ReferenceId}: {ex.GetType().Name}");
                    }
                }

                if (definition._optionsItemModels.Count == 0)
                {
                    Log.Warn($"options: {source}: no model for {definition.ReferenceId}");
                    continue;
                }

                var optionsModel = definition._optionsItemModels[0].TryCast<OptionsItemModel>();
                RegisterModel(definition.ReferenceId, optionsModel, source);
            }
        }

        private static bool RegisterModel(string referenceId, OptionsItemModel model, string source)
        {
            if (string.IsNullOrWhiteSpace(referenceId) || model == null)
                return false;

            var optionsModel = GameContext.Instance?.OptionsDirector?._optionsModel;
            if (optionsModel?.optionsById == null)
            {
                Log.Every("options-model-not-ready", 3f, $"options: {source}: OptionsModel not ready");
                return false;
            }

            if (optionsModel.optionsById.ContainsKey(referenceId))
                optionsModel.optionsById.Remove(referenceId);

            optionsModel.optionsById.Add(referenceId, model);
            return true;
        }

        private OptionsItemCategory FindCategory(OptionsConfiguration configuration)
        {
            if (configuration?.items == null)
                return null;

            foreach (var obj in configuration.items)
            {
                var category = obj as OptionsItemCategory;
                if (IsOurCategory(category))
                    return category;
            }

            return null;
        }

        private bool IsOurCategory(OptionsItemCategory category)
        {
            return category != null && category.name == CategoryName;
        }

        private OptionsItemCategory GetUiCategory(OptionsUIRoot root, int index)
        {
            if (root?.categories == null || index < 0 || index >= root.categories.Count)
                return null;

            return root.categories[index].TryCast<OptionsItemCategory>();
        }

        private void LogCategoryState(OptionsUIRoot root, string source)
        {
            if (root?.categories == null)
                return;

            for (var i = 0; i < root.categories.Count; i++)
            {
                var category = root.categories[i].TryCast<OptionsItemCategory>();
                if (IsOurCategory(category))
                {
                    Log.Info($"options: {source}: minimap rider visible index={i} rows={category.items?.Count ?? -1}");
                    return;
                }
            }
        }

        private static int IndexOf(Il2CppSystem.Collections.Generic.List<OptionsItemCategory> list, OptionsItemCategory category)
        {
            if (list == null || category == null)
                return -1;

            for (var i = 0; i < list.Count; i++)
                if (list[i] == category)
                    return i;
            return -1;
        }

        private static LocalizedString Localized(string text, string key)
        {
            var table = GetStringTable();
            var entry = table.GetEntry("rancherminimap." + key);
            if (entry == null)
                entry = table.AddEntry("rancherminimap." + key, text);
            else
                entry.Value = text;

            return new LocalizedString(table.SharedData.TableCollectionName, entry.SharedEntry.Id);
        }

        private static StringTable GetStringTable()
        {
            var table = LocalizationUtil.GetTable("UI");
            if (table != null)
                return table;

            table = LocalizationUtil.GetTable("Tutorial");
            if (table != null)
                return table;

            throw new InvalidOperationException("Could not resolve SR2 localization table UI or Tutorial");
        }

        private Sprite ResolveIcon()
        {
            if (_categoryIcon != null)
                return _categoryIcon;

            var worldCategoryIcon = TryResolveNamedGameIcon("iconCategoryWorld", "options category world");
            if (worldCategoryIcon != null)
            {
                _categoryIcon = worldCategoryIcon;
                return _categoryIcon;
            }

            _categoryIcon = SpriteFactory.RingSprite(new Color(0.25f, 0.9f, 0.75f, 0.95f), 48);
            _categoryIcon.name = "RancherMinimapOptionsFallbackIcon";
            _createdObjects.Add(_categoryIcon);
            Log.Info("options: using generated fallback sprite for minimap rider icon");
            return _categoryIcon;
        }

        private Sprite TryResolveNamedGameIcon(string name, string logLabel)
        {
            try
            {
                foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
                {
                    if (sprite != null && string.Equals(sprite.name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        Log.Info("options: using in-game " + logLabel + " sprite for minimap rider icon");
                        return sprite;
                    }
                }

                foreach (var texture in Resources.FindObjectsOfTypeAll<Texture2D>())
                {
                    if (texture == null || !string.Equals(texture.name, name, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var sprite = Sprite.Create(
                        texture,
                        new Rect(0f, 0f, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f),
                        100f);
                    sprite.name = "RancherMinimapOptions_" + name;
                    _createdObjects.Add(sprite);
                    Log.Info("options: using in-game " + logLabel + " texture for minimap rider icon");
                    return sprite;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("options: failed to resolve in-game " + logLabel + " icon: " + ex.GetType().Name);
                return null;
            }

            return null;
        }

        private static bool IsRancherDefinition(PresetOptionsItemDefinition definition)
        {
            try
            {
                return definition != null &&
                       !string.IsNullOrWhiteSpace(definition.ReferenceId) &&
                       definition.ReferenceId.StartsWith(ReferencePrefix, StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }
    }

    internal sealed class RancherMinimapOptionSpec
    {
        public readonly string Id;
        public readonly LocalizedString Label;
        public readonly LocalizedString Details;

        private readonly LocalizedString[] _presetLabels;
        private readonly Func<int> _getIndex;
        private readonly Action<int> _applyIndex;

        public int Count => _presetLabels.Length;

        private RancherMinimapOptionSpec(string id, LocalizedString label, LocalizedString details, LocalizedString[] presetLabels, Func<int> getIndex, Action<int> applyIndex)
        {
            Id = id;
            Label = label;
            Details = details;
            _presetLabels = presetLabels;
            _getIndex = getIndex;
            _applyIndex = applyIndex;
        }

        public static RancherMinimapOptionSpec Toggle(string id, LocalizedString label, LocalizedString details, Func<bool> getValue, Action<bool> setValue)
        {
            return new RancherMinimapOptionSpec(
                id,
                label,
                details,
                new[] { OptionsMenuText("Off", id + ".off"), OptionsMenuText("On", id + ".on") },
                () => getValue() ? 1 : 0,
                i => setValue(i > 0));
        }

        public static RancherMinimapOptionSpec Choice(string id, LocalizedString label, LocalizedString details, float[] values, Func<float, string> format, Func<float> getValue, Action<float> setValue)
        {
            var presetLabels = values.Select((v, i) => OptionsMenuText(format(v), id + "." + i)).ToArray();
            return new RancherMinimapOptionSpec(
                id,
                label,
                details,
                presetLabels,
                () => NearestIndex(values, getValue()),
                i => setValue(values[Math.Max(0, Math.Min(values.Length - 1, i))]));
        }

        public int CurrentIndex()
        {
            var value = _getIndex();
            if (value < 0)
                return 0;
            return value >= Count ? Count - 1 : value;
        }

        public void Apply(int index)
        {
            _applyIndex(Math.Max(0, Math.Min(Count - 1, index)));
        }

        public LocalizedString LabelForIndex(int index)
        {
            return _presetLabels[Math.Max(0, Math.Min(Count - 1, index))];
        }

        private static int NearestIndex(float[] values, float current)
        {
            var best = 0;
            var bestDist = float.MaxValue;
            for (var i = 0; i < values.Length; i++)
            {
                var dist = Math.Abs(values[i] - current);
                if (dist < bestDist)
                {
                    best = i;
                    bestDist = dist;
                }
            }
            return best;
        }

        private static LocalizedString OptionsMenuText(string text, string key)
        {
            var table = LocalizationUtil.GetTable("UI") ?? LocalizationUtil.GetTable("Tutorial");
            if (table == null)
                throw new InvalidOperationException("Could not resolve localization table for option text");

            var entry = table.GetEntry("rancherminimap." + key);
            if (entry == null)
                entry = table.AddEntry("rancherminimap." + key, text);
            else
                entry.Value = text;

            return new LocalizedString(table.SharedData.TableCollectionName, entry.SharedEntry.Id);
        }
    }

}
