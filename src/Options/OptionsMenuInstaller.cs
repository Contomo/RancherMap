using System;
using System.Collections.Generic;
using System.Linq;
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
        private const string ReferencePrefix = "setting.rancherminimap.";

        private static OptionsMenuInstaller Current;
        private static readonly Dictionary<string, IMinimapOption> OptionsByReferenceId = new Dictionary<string, IMinimapOption>();

        private readonly HarmonyInstance _harmony;
        private readonly MinimapSettings _settings;
        private readonly List<Object> _createdObjects = new List<Object>();

        private bool _installed;
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
            OptionsByReferenceId.Clear();

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
            if (!TryGetOption(__instance?.ReferenceId, out var option))
                return true;

            option.ApplyIndex(index);
            return false;
        }

        private static bool ScriptedGetDefaultPresetIndexPrefix(ScriptedValuePresetOptionDefinition __instance, ref int __result)
        {
            if (!TryGetOption(__instance?.ReferenceId, out var option))
                return true;

            __result = option.CurrentIndex();
            return false;
        }

        private void Inject(OptionsConfiguration configuration, string source)
        {
            if (configuration == null)
                return;


            EnsureLocalization();

            if (configuration.items == null)
            {
                configuration.items = new Il2CppSystem.Collections.Generic.List<OptionsItemCategory>();
                Log.Warn("options: OptionsConfiguration.items was null; created list");
            }

            var category = FindCategory(configuration);
            if (category == null)
            {
                category = ScriptableObject.CreateInstance<OptionsItemCategory>();
                category.name = MinimapSettings.CategoryName;
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

        private void EnsureLocalization()
        {
            if (_categoryTitle != null)
                return;

            _categoryTitle = Localized(MinimapSettings.RiderTitle, "category");
        }

        internal static bool TryGetOption(string referenceId, out IMinimapOption option)
        {
            option = null;
            return !string.IsNullOrWhiteSpace(referenceId) && OptionsByReferenceId.TryGetValue(referenceId, out option);
        }

        private void RebuildRows(OptionsItemCategory category)
        {
            if (CategoryMatches(category))
                return;

            category.items.Clear();

            foreach (var option in _settings.MenuOptions)
                category.items.Add(CreateDefinition(option));
        }

        private bool CategoryMatches(OptionsItemCategory category)
        {
            if (category?.items == null || category.items.Count != _settings.MenuOptions.Count)
                return false;

            for (var i = 0; i < _settings.MenuOptions.Count; i++)
            {
                var definition = category.items[i] as ScriptedValuePresetOptionDefinition;
                var option = _settings.MenuOptions[i];
                if (definition == null || !string.Equals(definition.ReferenceId, ReferencePrefix + option.Id, StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        private ScriptedValuePresetOptionDefinition CreateDefinition(IMinimapOption option)
        {
            var template = Resources.FindObjectsOfTypeAll<ScriptedValuePresetOptionDefinition>().FirstOrDefault();
            if (template == null)
                throw new InvalidOperationException("Could not find ScriptedValuePresetOptionDefinition template");

            var definition = ScriptableObject.CreateInstance<ScriptedValuePresetOptionDefinition>();
            var referenceId = ReferencePrefix + option.Id;
            definition.name = "RancherMinimap_" + option.Id;
            definition._referenceId = referenceId;
            definition._label = Localized(option.Label, option.Id);
            definition._detailsText = Localized(option.Description, option.Id + ".desc");
            definition._applyImmediately = true;
            definition._requireConfirmation = false;
            definition._wrapAround = false;
            definition._defaultValueIndex = option.CurrentIndex();
            definition._isProfileSetting = true;
            definition._showTutorialDisclaimer = false;
            definition._optionsItemModels = new Il2CppSystem.Collections.Generic.List<PresetOptionsItemModel>();
            definition.SupportedInputDeviceAssets = new Il2CppSystem.Collections.Generic.List<InputDeviceAsset>();
            definition.SupportedPlatforms = new Il2CppSystem.Collections.Generic.List<StoreAndPlatform>();
            definition._controlPrefab = template._controlPrefab;
            definition._confirmationPopupConfig = template._confirmationPopupConfig;

            var presets = new Il2CppReferenceArray<ScriptedValuePresetOptionDefinition.ScriptedValuePreset>(option.ChoiceCount);
            for (var i = 0; i < option.ChoiceCount; i++)
            {
                var preset = new ScriptedValuePresetOptionDefinition.ScriptedValuePreset();
                preset._presetLabel = Localized(option.ChoiceLabel(i), option.Id + "." + i);
                preset._referenceId = referenceId + ".preset." + i;
                preset._scriptedBoolSettings = new Il2CppReferenceArray<ScriptedValuePresetOptionDefinition.ScriptedValueSetting<Il2CppMonomiPark.ScriptedValue.ScriptedBool, bool>>(0);
                preset._scriptedFloatSettings = new Il2CppReferenceArray<ScriptedValuePresetOptionDefinition.ScriptedValueSetting<Il2CppMonomiPark.ScriptedValue.ScriptedFloat, float>>(0);
                preset._scriptedIntSettings = new Il2CppReferenceArray<ScriptedValuePresetOptionDefinition.ScriptedValueSetting<Il2CppMonomiPark.ScriptedValue.ScriptedInt, int>>(0);
                preset._scriptedQualitySettings = new Il2CppSystem.Collections.Generic.List<ScriptedValuePresetOptionDefinition.ScriptedValueSetting<Il2CppMonomiPark.ScriptedValue.ScriptedQuality, QualityLevel>>();
                presets[i] = preset;
            }

            definition._optionsPresets = presets;
            OptionsByReferenceId[referenceId] = option;
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
            return category != null && category.name == MinimapSettings.CategoryName;
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

            _categoryIcon = ModSpriteAssets.CreateIconCategoryWorld(_createdObjects);
            if (_categoryIcon == null)
                Log.Error("options: failed to create embedded iconCategoryWorld sprite");

            return _categoryIcon;
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

}
