using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using Dalamud.Logging;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using FFXIVClientStructs.FFXIV.Client.UI;

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

    internal unsafe partial class GameFunctions
    {
        private readonly UIModule* pUIModuleInstance;

        private delegate nint EnableCutsceneInputModeDelegate(UIModule* pUIModule, long a2);

        private delegate nint DisableCutsceneInputModeDelegate(UIModule* pUIModule);

        [Signature("48 89 5C 24 ?? 57 48 83 EC 20 48 8D 99 ?? ?? ?? ?? 48 8B F9 80 7B 25 00")]
        private readonly EnableCutsceneInputModeDelegate? enableCutsceneInputMode = null;

        [Signature("48 89 5C 24 ?? 57 48 83 EC 20 48 8B F9 48 81 C1 ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8B 07")]
        private readonly DisableCutsceneInputModeDelegate? disableCutsceneInputMode = null;

        private bool autoAdvanceEnabled = false;

        public bool AutoAdvanceEnabled
        {
            get
            {
                return autoAdvanceEnabled;
            }
            set
            {
                if (autoAdvanceEnabled != value)
                {
                    autoAdvanceEnabled = value;

                    if (value)
                    {
                        this.EnableCutsceneInputMode();
                    }
                    else
                    {
                        this.DisableCutsceneInputMode();
                    }
#if DEBUG
                    PluginLog.LogInformation($"autoAdvanceEnabled set to [{value}]", value);
#endif
                }

                return;
            }
        }

        public GameFunctions()
        {
            SignatureHelper.Initialise(this);

            this.pUIModuleInstance = UIModule.Instance();
        }

        public void EnableCutsceneInputMode()
        {
            if (this.enableCutsceneInputMode == null)
                throw new InvalidOperationException("EnableCutsceneInputMode signature wasn't found!");

            this.enableCutsceneInputMode(this.pUIModuleInstance, 35); // figure out what a2 is

            return;
        }

        public void DisableCutsceneInputMode()
        {
            if (this.disableCutsceneInputMode == null)
                throw new InvalidOperationException("DisableCutsceneInputMode signature wasn't found!");

            this.disableCutsceneInputMode(this.pUIModuleInstance);

            return;
        }
    }

    internal unsafe class Listener : IDisposable
    {
        private static class Signatures
        {
            internal const string PlaySpecificSound = "48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC 20 33 F6 8B DA 48 8B F9 0F BA E2 0F";

            internal const string GetResourceSync = "E8 ?? ?? ?? ?? 48 8D 8F ?? ?? ?? ?? 48 89 87 ?? ?? ?? ?? 48 8D 54 24";
            internal const string GetResourceAsync = "E8 ?? ?? ?? ?? 48 8B D8 EB 07 F0 FF 83";
            internal const string LoadSoundFile = "E8 ?? ?? ?? ?? 48 85 C0 75 04 B0 F6";

            internal const string EnableCutsceneInputMode = "48 89 5C 24 ?? 57 48 83 EC 20 48 8D 99 ?? ?? ?? ?? 48 8B F9 80 7B 25 00";
            internal const string DisableCutsceneInputMode = "48 89 5C 24 ?? 57 48 83 EC 20 48 8B F9 48 81 C1 ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8B 07";
        }

        // Updated: 5.55
        private const int ResourceDataPointerOffset = 0xB0;

        #region Delegates

        private delegate void* PlaySpecificSoundDelegate(long a1, int idx);

        private delegate void* GetResourceSyncPrototype(IntPtr pFileManager, uint* pCategoryId, char* pResourceType, uint* pResourceHash, char* pPath, void* pUnknown);

        private delegate void* GetResourceAsyncPrototype(IntPtr pFileManager, uint* pCategoryId, char* pResourceType, uint* pResourceHash, char* pPath, void* pUnknown, bool isUnknown);

        private delegate IntPtr LoadSoundFileDelegate(IntPtr resourceHandle, uint a2);

        #endregion

        #region Hooks

        private Hook<PlaySpecificSoundDelegate>? PlaySpecificSoundHook { get; set; }

        private Hook<GetResourceSyncPrototype>? GetResourceSyncHook { get; set; }

        private Hook<GetResourceAsyncPrototype>? GetResourceAsyncHook { get; set; }

        private Hook<LoadSoundFileDelegate>? LoadSoundFileHook { get; set; }

        #endregion

        private SmartAutoAdvancePlugin Plugin { get; }
        private bool InNewCutscene { get; set; }

        private ConcurrentDictionary<IntPtr, string> Scds { get; } = new();

        internal ConcurrentQueue<string> Recent { get; } = new();

        private GameFunctions gameFunctions { get; } = new();

        internal Listener(SmartAutoAdvancePlugin plugin)
        {
            this.Plugin = plugin;

            this.InNewCutscene = false;

            // Disabled on game start for consistent results
            this.gameFunctions.AutoAdvanceEnabled = false;
        }

        internal void Enable()
        {
            this.Plugin.Condition.ConditionChange += OnConditionChanged;

            if (this.PlaySpecificSoundHook == null && this.Plugin.SigScanner.TryScanText(Signatures.PlaySpecificSound, out var playPtr))
            {
                this.PlaySpecificSoundHook = new Hook<PlaySpecificSoundDelegate>(playPtr, this.PlaySpecificSoundDetour);
            }

            if (this.GetResourceSyncHook == null && this.Plugin.SigScanner.TryScanText(Signatures.GetResourceSync, out var syncPtr))
            {
                this.GetResourceSyncHook = new Hook<GetResourceSyncPrototype>(syncPtr, this.GetResourceSyncDetour);
            }

            if (this.GetResourceAsyncHook == null && this.Plugin.SigScanner.TryScanText(Signatures.GetResourceAsync, out var asyncPtr))
            {
                this.GetResourceAsyncHook = new Hook<GetResourceAsyncPrototype>(asyncPtr, this.GetResourceAsyncDetour);
            }

            if (this.LoadSoundFileHook == null && this.Plugin.SigScanner.TryScanText(Signatures.LoadSoundFile, out var soundPtr))
            {
                this.LoadSoundFileHook = new Hook<LoadSoundFileDelegate>(soundPtr, this.LoadSoundFileDetour);
            }

            this.PlaySpecificSoundHook?.Enable();
            this.LoadSoundFileHook?.Enable();
            this.GetResourceSyncHook?.Enable();
            this.GetResourceAsyncHook?.Enable();
        }

        internal void OnConditionChanged(ConditionFlag flag, bool value)
        {
#if DEBUG
            PluginLog.LogInformation($"Flag [{flag}] changed to [{value}]", flag, value);
#endif
            if (value != this.InNewCutscene && (
                flag == ConditionFlag.WatchingCutscene ||
                flag == ConditionFlag.WatchingCutscene78 ||
                flag == ConditionFlag.OccupiedInCutSceneEvent
            )) {

                if (value)
                {
                    PluginLog.LogInformation("Cutscene started, disabling auto-advance!");

                    this.gameFunctions.AutoAdvanceEnabled = false;
                }

                this.InNewCutscene = value;
            }
        }

        internal void Disable()
        {
            this.PlaySpecificSoundHook?.Disable();
            this.LoadSoundFileHook?.Disable();
            this.GetResourceSyncHook?.Disable();
            this.GetResourceAsyncHook?.Disable();
        }

        public void Dispose()
        {
            this.Plugin.Condition.ConditionChange -= OnConditionChanged;

            this.PlaySpecificSoundHook?.Dispose();
            this.LoadSoundFileHook?.Dispose();
            this.GetResourceSyncHook?.Dispose();
            this.GetResourceAsyncHook?.Dispose();
        }

        private void* PlaySpecificSoundDetour(long a1, int idx)
        {
            try
            {
                if (this.PlaySpecificSoundDetourInner(a1))
                {
                    PluginLog.LogInformation("Voice line detected, enabling auto-advance!");

                    // Cutscene is still playing, but no longer "new". We don't need to listen for new sounds
                    this.InNewCutscene = false;
                    this.gameFunctions.AutoAdvanceEnabled = true;
                }
            }
            catch (Exception ex)
            {
                PluginLog.LogError(ex, "Error in PlaySpecificSoundDetour");
            }

            return this.PlaySpecificSoundHook!.Original(a1, idx);
        }

        private bool PlaySpecificSoundDetourInner(long a1)
        {
            if (a1 == 0)
            {
                return false;
            }

            //return early if not in cutscene
            if (!this.InNewCutscene)
            {
                return false;
            }

            var scdData = *(byte**)(a1 + 8);
            if (scdData == null)
            {
                return false;
            }

            // check cached scds for path
            if (!this.Scds.TryGetValue((IntPtr)scdData, out var path))
            {
                return false;
            }

            path = path.ToLowerInvariant();

#if DEBUG
            PluginLog.LogInformation($".scd played: {path}", path);
#endif

            // return early if .scd audio isn't cutscene
            if (!path.StartsWith("cut/"))
            {
                return false;
            }

            return VoValidator.IsValid(path);
        }

        private void* GetResourceSyncDetour(IntPtr pFileManager, uint* pCategoryId, char* pResourceType, uint* pResourceHash, char* pPath, void* pUnknown)
        {
            return this.ResourceDetour(true, pFileManager, pCategoryId, pResourceType, pResourceHash, pPath, pUnknown, false);
        }

        private void* GetResourceAsyncDetour(IntPtr pFileManager, uint* pCategoryId, char* pResourceType, uint* pResourceHash, char* pPath, void* pUnknown, bool isUnknown)
        {
            return this.ResourceDetour(false, pFileManager, pCategoryId, pResourceType, pResourceHash, pPath, pUnknown, isUnknown);
        }

        private void* ResourceDetour(bool isSync, IntPtr pFileManager, uint* pCategoryId, char* pResourceType, uint* pResourceHash, char* pPath, void* pUnknown, bool isUnknown)
        {
            var ret = this.CallOriginalResourceHandler(isSync, pFileManager, pCategoryId, pResourceType, pResourceHash, pPath, pUnknown, isUnknown);

            var path = Util.ReadTerminatedString((byte*)pPath);
            if (ret != null && path.EndsWith(".scd"))
            {
                var scdData = Marshal.ReadIntPtr((IntPtr)ret + ResourceDataPointerOffset);
                // if we immediately have the scd data, cache it, otherwise add it to a waiting list to hopefully be picked up at sound play time
                if (scdData != IntPtr.Zero)
                {
                    this.Scds[scdData] = path;
                }
            }

            return ret;
        }

        private void* CallOriginalResourceHandler(bool isSync, IntPtr pFileManager, uint* pCategoryId, char* pResourceType, uint* pResourceHash, char* pPath, void* pUnknown, bool isUnknown)
        {
            return isSync
                ? this.GetResourceSyncHook!.Original(pFileManager, pCategoryId, pResourceType, pResourceHash, pPath, pUnknown)
                : this.GetResourceAsyncHook!.Original(pFileManager, pCategoryId, pResourceType, pResourceHash, pPath, pUnknown, isUnknown);
        }

        private IntPtr LoadSoundFileDetour(IntPtr resourceHandle, uint a2)
        {
            var ret = this.LoadSoundFileHook!.Original(resourceHandle, a2);

            try
            {
                var handle = (ResourceHandle*)resourceHandle;
                var name = handle->FileName.ToString();
                if (name.EndsWith(".scd"))
                {
                    var dataPtr = Marshal.ReadIntPtr(resourceHandle + ResourceDataPointerOffset);
                    this.Scds[dataPtr] = name;
                }
            }
            catch (Exception ex)
            {
                PluginLog.LogError(ex, "Error in LoadSoundFileDetour");
            }

            return ret;
        }
    }
}
