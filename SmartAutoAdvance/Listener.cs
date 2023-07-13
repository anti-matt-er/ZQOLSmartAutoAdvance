using System;
using System.Text.RegularExpressions;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Logging;

namespace SmartAutoAdvance
{
    internal static partial class VoValidator
    {
        [GeneratedRegex(
            @"^cut/\w+/sound/[\w/]+/vo_\w+\.scd$",
            RegexOptions.CultureInvariant,
            matchTimeoutMilliseconds: 1000)]
        private static partial Regex MatchIfValid();
        public static bool IsValid(string path) => MatchIfValid().IsMatch(path);
    }

    internal unsafe class Listener : IDisposable
    {
        private SmartAutoAdvancePlugin Plugin { get; }

        private bool InNewCutscene { get; set; }

        private ClientFunctions clientFunctions { get; } = new();

        internal Listener(SmartAutoAdvancePlugin plugin)
        {
            this.Plugin = plugin;

            this.InNewCutscene = false;

            // Because true/false of the client function is cursed
            this.clientFunctions.SetInitialAutoAdvance();
        }

        internal void Enable()
        {
            this.Plugin.Condition.ConditionChange += OnConditionChanged;
            this.clientFunctions.PlaySpecificSoundEvent += OnPlaySpecificSound;
        }

        internal void Disable()
        {
            this.clientFunctions.Disable();
        }

        public void Dispose()
        {
            this.Plugin.Condition.ConditionChange -= OnConditionChanged;

            this.clientFunctions.Dispose();
        }

        internal void OnConditionChanged(ConditionFlag flag, bool value)
        {
#if DEBUG
            PluginLog.Information($"Flag [{flag}] changed to [{value}]", flag, value);
#endif
            if (value != this.InNewCutscene && (
                flag == ConditionFlag.WatchingCutscene ||
                flag == ConditionFlag.WatchingCutscene78 ||
                flag == ConditionFlag.OccupiedInCutSceneEvent
            ))
            {

                if (value)
                {
                    PluginLog.Information("Cutscene started, disabling auto-advance!");

                    this.clientFunctions.AutoAdvanceEnabled = false;
                }

                this.InNewCutscene = value;
            }
        }

        internal void OnPlaySpecificSound(object? sender, PlaySpecificSoundEventArgs e)
        {
            if (!this.InNewCutscene)
            {
                return;
            }

            // return early if .scd audio isn't cutscene
            if (!e.Path.StartsWith("cut/"))
            {
                return;
            }

            // We're only concerned with voice line sounds
            if (!VoValidator.IsValid(e.Path))
            {
                return;
            }

            PluginLog.Information("Voice line detected, enabling auto-advance!");

            // Cutscene is still playing, but no longer "new". We don't need to listen for new sounds
            this.InNewCutscene = false;
            this.clientFunctions.AutoAdvanceEnabled = true;
        }
    }
}
