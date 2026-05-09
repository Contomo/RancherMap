using System;
using HarmonyLib;
using HarmonyInstance = HarmonyLib.Harmony;
using MelonLoader;

[assembly: MelonInfo(typeof(rancher_minimap.Rancher_Minimap), "Rancher Minimap", "1.2.0", "Contomo")]
[assembly: MelonGame("MonomiPark", "SlimeRancher2")]

namespace rancher_minimap
{
    /// <summary>
    /// MelonLoader entry point. This class deliberately owns only high-level lifetime wiring.
    /// Game-specific lookup and UI mutation live in separate modules so the next SR2 update does
    /// not turn the entry file into another patch graveyard.
    /// </summary>
    public sealed class Rancher_Minimap : MelonMod
    {
        private HarmonyInstance _harmony;
        private MinimapSettings _settings;
        private OptionsMenuInstaller _optionsMenu;
        private MinimapController _controller;
        private MapVisualCapture _mapVisualCapture;
        private float _nextOptionsInstallProbe;

        public override void OnInitializeMelon()
        {
            _settings = MinimapSettings.Load();
            Log.ConfigureDiagnostics(_settings.DiagnosticsEnabled);
            TimeTracker.Configure(_settings.DiagnosticsEnabled, _settings.PerformanceLoggingEnabled);
            _harmony = new HarmonyInstance("sr2.rancher_minimap");

            _optionsMenu = new OptionsMenuInstaller(_harmony, _settings);
            _mapVisualCapture = new MapVisualCapture(_harmony, _settings);
            _mapVisualCapture.Install();
            _controller = new MinimapController(_settings);
        }

        public override void OnUpdate()
        {
            if (_settings != null)
            {
                Log.ConfigureDiagnostics(_settings.DiagnosticsEnabled);
                TimeTracker.Configure(_settings.DiagnosticsEnabled, _settings.PerformanceLoggingEnabled);
            }

            using (TimeTracker.Measure("melon.update"))
            {
                _settings?.TickPendingSave();

                if (_optionsMenu != null && !_optionsMenu.IsInstalled && UnityEngine.Time.realtimeSinceStartup >= _nextOptionsInstallProbe)
                {
                    _nextOptionsInstallProbe = UnityEngine.Time.realtimeSinceStartup + 1.0f;
                    using (TimeTracker.Measure("options.install-probe"))
                        _optionsMenu.Install();
                }


                _controller?.Tick();

                using (TimeTracker.Measure("map-capture.tick"))
                    _mapVisualCapture?.Tick();
            }

            TimeTracker.TickSummary();
        }

        public override void OnDeinitializeMelon()
        {
            _controller?.Dispose();
            _mapVisualCapture?.Dispose();
            _optionsMenu?.Dispose();
            _harmony?.UnpatchSelf();
            _settings?.SaveNow();
        }
    }
}
