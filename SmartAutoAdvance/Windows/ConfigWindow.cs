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
        "Smart Text Auto-Advance Configuration",
        ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.Plugin = plugin;

        this.Size = new Vector2(390, 85);
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = (Vector2)this.Size
        };
        this.SizeCondition = ImGuiCond.Always;

        this.configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var enabled = this.configuration.Enabled;
        if (ImGui.Checkbox("Plugin enabled", ref enabled))
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
        if (ImGui.Checkbox("Enable Auto-Advance for unvoiced cutscenes when in a party", ref enabledInParty))
        {
            this.configuration.ForceEnableInParty = enabledInParty;
            // can save immediately on change, if you don't want to provide a "Save and Close" button
            this.configuration.Save();
        }
    }
}
