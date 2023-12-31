using System;
using System.Text.RegularExpressions;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Logging;

namespace SmartAutoAdvance
{
    public static partial class VoValidator
    {
        [GeneratedRegex(
            @"^cut/\w+/sound/[\w/]+/vo_\w+\.scd$",
            RegexOptions.CultureInvariant,
            matchTimeoutMilliseconds: 1000)]
        private static partial Regex MatchIfValid();
        public static bool IsValid(string path) => MatchIfValid().IsMatch(path);
    }

    public unsafe class Listener : IDisposable
    {
        private SmartAutoAdvancePlugin Plugin { get; }

        private bool InNewCutscene { get; set; }

        private ClientFunctions clientFunctions { get; } = new();

        public Listener(SmartAutoAdvancePlugin plugin)
        {
            this.Plugin = plugin;

            this.InNewCutscene = false;
        }

        public void Enable()
        {
            this.Plugin.Condition.ConditionChange += this.OnConditionChanged;
            this.clientFunctions.OnCutsceneChanged += this.OnCutsceneChanged;
            this.clientFunctions.OnPlaySpecificSound += this.OnPlaySpecificSound;

            this.clientFunctions.Enable();
        }

        public void Disable()
        {
            this.Plugin.Condition.ConditionChange -= this.OnConditionChanged;
            this.clientFunctions.OnCutsceneChanged -= this.OnCutsceneChanged;
            this.clientFunctions.OnPlaySpecificSound -= this.OnPlaySpecificSound;

            this.clientFunctions.Disable();
        }

        public void Dispose()
        {
            this.Plugin.Condition.ConditionChange -= this.OnConditionChanged;
            this.clientFunctions.OnCutsceneChanged -= this.OnCutsceneChanged;
            this.clientFunctions.OnPlaySpecificSound -= this.OnPlaySpecificSound;

            this.clientFunctions.Dispose();
        }

        public void ToggleAutoAdvance()
        {
            this.clientFunctions.AutoAdvanceEnabled = !this.clientFunctions.AutoAdvanceEnabled;
        }

        public void LogAutoAdvance()
        {
            PluginLog.Information($"Client Auto-Advance is currently: {this.clientFunctions.AutoAdvanceEnabled}");
        }

        private bool IsInPartyWithOthers => this.Plugin.PartyList is { Length: > 1 };

        private void OnConditionChanged(ConditionFlag flag, bool value)
        {
#if DEBUG
            PluginLog.Verbose($"Flag [{flag}] changed to [{value}]", flag, value);
#endif
            if (value != this.InNewCutscene && (
                flag == ConditionFlag.WatchingCutscene ||
                flag == ConditionFlag.WatchingCutscene78 ||
                flag == ConditionFlag.OccupiedInCutSceneEvent
            ))
            {
                if (value)
                {
                    this.OnCutsceneChanged();
                }
                else
                {
                    this.InNewCutscene = false;
                }
            }
        }

        private void OnCutsceneChanged()
        {
            if (this.InNewCutscene)
            {
                return;
            }

            if (this.Plugin.Configuration.ForceEnableInParty && this.IsInPartyWithOthers)
            {
                PluginLog.Information("Cutscene started/ended in a party, enabling auto-advance!");

                this.clientFunctions.AutoAdvanceEnabled = true;
            }
            else
            {
                PluginLog.Information("Cutscene started/ended, disabling auto-advance!");

                this.clientFunctions.AutoAdvanceEnabled = false;
            }

            this.InNewCutscene = true;
        }

        private void OnPlaySpecificSound(PlaySpecificSoundEventArgs e)
        {
            if (!this.InNewCutscene)
            {
                return;
            }

            // return early if .scd audio isn't cutscene, to avoid the RegEx check
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
