using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using ImGuiNET;

namespace SmartAutoAdvance.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration configuration;

    private SmartAutoAdvancePlugin Plugin { get; }

    public ConfigWindow(SmartAutoAdvancePlugin plugin) : base(
        "A Wonderful Configuration Window",
        ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
        ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.Plugin = plugin;

        this.Size = new Vector2(232, 75);
        this.SizeCondition = ImGuiCond.Always;

        this.configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var enabled = this.configuration.Enabled;
        if (ImGui.Checkbox("Enabled", ref enabled))
        {
            this.configuration.Enabled = enabled;

            if (enabled)
            {
                this.Plugin.Listener.Enable();
            }
            else
            {
                this.Plugin.Listener.Disable();
            }

            this.configuration.Save();
        }

        var enabledInParty = this.configuration.ForceEnableInParty;
        if (ImGui.Checkbox("Re-enable auto-advance for unvoiced cutscenes when in a party", ref enabledInParty))
        {
            this.configuration.ForceEnableInParty = enabledInParty;
            // can save immediately on change, if you don't want to provide a "Save and Close" button
            this.configuration.Save();
        }
    }
}
