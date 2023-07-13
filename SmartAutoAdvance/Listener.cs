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

            // To achieve consistent results, we set it to false upon plugin load
            this.clientFunctions.SetAutoAdvance(false);
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

        public void ToggleAutoAdvance()
        {
            this.clientFunctions.AutoAdvanceEnabled = !this.clientFunctions.AutoAdvanceEnabled;
        }

        private bool IsInParty()
        {
            // Greater than 1 is used here just in case there's such thing as a "solo party"
            return this.Plugin.PartyList.Length > 1;
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
                    if (this.Plugin.Configuration.ForceEnableInParty && IsInParty())
                    {
                        PluginLog.Information("Cutscene started in a party, enabling auto-advance!");

                        this.clientFunctions.AutoAdvanceEnabled = true;
                    }
                    else
                    {
                        PluginLog.Information("Cutscene started, disabling auto-advance!");

                        this.clientFunctions.AutoAdvanceEnabled = false;
                    }
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
