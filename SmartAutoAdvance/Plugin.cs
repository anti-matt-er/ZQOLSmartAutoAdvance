using Dalamud.Game;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using SmartAutoAdvance.Windows;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Party;

namespace SmartAutoAdvance
{
    public sealed class SmartAutoAdvancePlugin : IDalamudPlugin
    {
        public string Name => "Smart text auto-advance";
        private const string ShortCommandName = "/staa";
        private const string LongCommandName = "/smarttextautoadvance";

        private DalamudPluginInterface PluginInterface { get; init; }
        private CommandManager CommandManager { get; init; }
        private CommandInfo CommandInfo { get; init; }
        public Configuration Configuration { get; init; }
        public WindowSystem WindowSystem = new("SmartAutoAdvancePlugin");

        private ConfigWindow ConfigWindow { get; init; }

        [PluginService]
        public SigScanner SigScanner { get; init; } = null!;

        [PluginService]
        public Framework Framework { get; init; } = null!;

        [PluginService]
        public Condition Condition { get; init; } = null!;

        [PluginService]
        public PartyList PartyList { get; init; } = null!;

        public Listener Listener { get; }

        public SmartAutoAdvancePlugin(
            [RequiredVersion("1.0")] DalamudPluginInterface pluginInterface,
            [RequiredVersion("1.0")] CommandManager commandManager,
            [RequiredVersion("1.0")] SigScanner sigScanner,
            [RequiredVersion("1.0")] Condition condition,
            [RequiredVersion("1.0")] PartyList partyList)
        {
            this.PluginInterface = pluginInterface;
            this.CommandManager = commandManager;
            this.SigScanner = sigScanner;
            this.Condition = condition;
            this.PartyList = partyList;

            // common CommandInfo for all aliases
            this.CommandInfo = new CommandInfo(OnCommand)
            {
                HelpMessage = "Opens the Smart Text Auto-Advance config window.\nUse /staa toggle to manually toggle Auto-Advance"
            };

            this.Configuration = this.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            this.Configuration.Initialize(this.PluginInterface);

            ConfigWindow = new ConfigWindow(this);
            
            WindowSystem.AddWindow(ConfigWindow);

            this.Listener = new Listener(this);
            if (this.Configuration.Enabled)
            {
                this.Listener.Enable();
            }

            // command aliases
            this.CommandManager.AddHandler(ShortCommandName, this.CommandInfo);
            this.CommandManager.AddHandler(LongCommandName, this.CommandInfo);

            this.PluginInterface.UiBuilder.Draw += DrawUI;
            this.PluginInterface.UiBuilder.OpenConfigUi += DrawConfigUI;
        }

        public void Dispose()
        {
            this.WindowSystem.RemoveAllWindows();
            
            ConfigWindow.Dispose();
            
            this.CommandManager.RemoveHandler(ShortCommandName);
            this.CommandManager.RemoveHandler(LongCommandName);

            this.PluginInterface.UiBuilder.Draw -= DrawUI;
            this.PluginInterface.UiBuilder.OpenConfigUi -= DrawConfigUI;

            this.Listener.Dispose();
        }

        private void OnCommand(string command, string args)
        {
            // Toggle config window when no args are used
            if (args == "")
            {
                ConfigWindow.IsOpen = !ConfigWindow.IsOpen;
            }

            if (args.ToLower() == "toggle")
            {
                this.Listener.ToggleAutoAdvance();
            }
        }

        private void DrawUI()
        {
            this.WindowSystem.Draw();
        }

        public void DrawConfigUI()
        {
            ConfigWindow.IsOpen = true;
        }
    }
}
