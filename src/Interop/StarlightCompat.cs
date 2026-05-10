using System;
using System.Collections;
using System.Linq;
using System.Reflection;

namespace rancher_minimap
{
    /// <summary>
    /// Optional Starlight/SR2E bridge.
    ///
    /// Starlight already displays normal MelonPreferences categories in its Mod Config tab.
    /// This bridge only registers live-apply callbacks with Starlight when that menu exists.
    /// It deliberately uses reflection so Rancher Minimap stays usable without Starlight installed.
    /// </summary>
    internal sealed class StarlightCompat
    {
        private const string ModMenuTypeName = "Starlight.Menus.StarlightModMenu";
        private const string EntriesWithActionsFieldName = "EntriesWithActions";

        private readonly MinimapSettings _settings;
        private readonly Action _applyExternalConfigEdit;
        private bool _registered;
        private bool _warnedUnsupportedShape;

        public bool IsRegistered => _registered;

        public StarlightCompat(MinimapSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _applyExternalConfigEdit = ApplyExternalConfigEdit;
        }

        public void TryRegister()
        {
            if (_registered)
                return;

            var menuType = FindType(ModMenuTypeName);
            if (menuType == null)
                return;

            var field = menuType.GetField(EntriesWithActionsFieldName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null || !typeof(IDictionary).IsAssignableFrom(field.FieldType))
            {
                WarnUnsupportedShape(menuType, "missing compatible EntriesWithActions dictionary");
                return;
            }

            var actions = field.GetValue(null) as IDictionary;
            if (actions == null)
            {
                WarnUnsupportedShape(menuType, "EntriesWithActions is null");
                return;
            }

            try
            {
                foreach (var option in _settings.MenuOptions)
                    actions[option.PreferenceEntry] = _applyExternalConfigEdit;
            }
            catch (ArgumentException ex)
            {
                WarnUnsupportedShape(menuType, ex.Message);
                return;
            }
            catch (InvalidCastException ex)
            {
                WarnUnsupportedShape(menuType, ex.Message);
                return;
            }

            _registered = true;
            Log.Info("starlight: registered live config callbacks");
        }

        private void ApplyExternalConfigEdit()
        {
            foreach (var option in _settings.MenuOptions)
                option.NormalizeStoredValue();

            _settings.MarkDirty();
            _settings.SaveNow();
        }

        private void WarnUnsupportedShape(Type menuType, string reason)
        {
            if (_warnedUnsupportedShape)
                return;

            _warnedUnsupportedShape = true;
            Log.Warn($"starlight: detected {menuType.FullName}, but cannot register live config callbacks: {reason}");
        }

        private static Type FindType(string fullName)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(fullName, false))
                .FirstOrDefault(type => type != null);
        }
    }
}
