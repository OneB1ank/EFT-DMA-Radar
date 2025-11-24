using LoneEftDmaRadar;
using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.UI.Misc;
using System;
using System.ComponentModel;
using System.Text;
using System.Windows.Input;
using System.Windows.Threading;

namespace LoneEftDmaRadar.UI.Radar.ViewModels
{
    public sealed class DebugTabViewModel : INotifyPropertyChanged
    {
        private readonly DispatcherTimer _timer;
        private string _makcuDebugText = "Makcu Aimbot: (no data)";
        private bool _showMakcuDebug = App.Config.Makcu.ShowDebug;

        public DebugTabViewModel()
        {
            ToggleDebugConsoleCommand = new SimpleCommand(DebugLogger.Toggle);

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += (_, _) => RefreshMakcuDebug();
            _timer.Start();
            RefreshMakcuDebug();
        }

        public ICommand ToggleDebugConsoleCommand { get; }

        public bool ShowMakcuDebug
        {
            get => _showMakcuDebug;
            set
            {
                if (_showMakcuDebug == value)
                    return;
                _showMakcuDebug = value;
                App.Config.Makcu.ShowDebug = value;
                OnPropertyChanged(nameof(ShowMakcuDebug));
            }
        }

        public string MakcuDebugText
        {
            get => _makcuDebugText;
            private set
            {
                if (_makcuDebugText != value)
                {
                    _makcuDebugText = value;
                    OnPropertyChanged(nameof(MakcuDebugText));
                }
            }
        }

        private void RefreshMakcuDebug()
        {
            var snapshot = MemDMA.MakcuAimbot?.GetDebugSnapshot();
            if (snapshot == null)
            {
                MakcuDebugText = "Makcu Aimbot: not running or no data yet.";
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("=== Makcu Aimbot ===");
            sb.AppendLine($"Status: {snapshot.Status}");
            sb.AppendLine($"Key: {(snapshot.KeyEngaged ? "ENGAGED" : "Idle")} | Enabled: {snapshot.Enabled} | Device: {(snapshot.DeviceConnected ? "Connected" : "Disconnected")}");
            sb.AppendLine($"InRaid: {snapshot.InRaid} | FOV: {snapshot.ConfigFov:F0}px | MaxDist: {snapshot.ConfigMaxDistance:F0}m | Mode: {snapshot.TargetingMode}");
            sb.AppendLine($"Filters -> PMC:{App.Config.Makcu.TargetPMC} PScav:{App.Config.Makcu.TargetPlayerScav} AI:{App.Config.Makcu.TargetAIScav} Boss:{App.Config.Makcu.TargetBoss} Raider:{App.Config.Makcu.TargetRaider}");
            sb.AppendLine($"Candidates: total {snapshot.CandidateTotal}, type {snapshot.CandidateTypeOk}, dist {snapshot.CandidateInDistance}, skeleton {snapshot.CandidateWithSkeleton}, w2s {snapshot.CandidateW2S}, final {snapshot.CandidateCount}");
            sb.AppendLine($"Target: {(snapshot.LockedTargetName ?? "None")} [{snapshot.LockedTargetType?.ToString() ?? "-"}] valid={snapshot.TargetValid}");
            if (snapshot.LockedTargetDistance.HasValue)
                sb.AppendLine($"  Dist {snapshot.LockedTargetDistance.Value:F1}m | FOVDist {(float.IsNaN(snapshot.LockedTargetFov) ? "n/a" : snapshot.LockedTargetFov.ToString("F1"))} | Bone {snapshot.TargetBone}");
            sb.AppendLine($"Fireport: {(snapshot.HasFireport ? snapshot.FireportPosition?.ToString() : "None")}");
            var bulletSpeedText = snapshot.BulletSpeed.HasValue ? snapshot.BulletSpeed.Value.ToString("F1") : "?";
            sb.AppendLine($"Ballistics: {(snapshot.BallisticsValid ? $"OK (Speed {bulletSpeedText} m/s, Predict {(snapshot.PredictionEnabled ? "ON" : "OFF")})" : "Invalid/None")}");

            MakcuDebugText = sb.ToString();
        }

        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        #endregion
    }
}
