using Dalamud.Hooking;
using Dalamud.Logging;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using FFXIVClientStructs.FFXIV.Client.UI;
using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace SmartAutoAdvance
{
    public class PlaySpecificSoundEventArgs : EventArgs
    {
        public PlaySpecificSoundEventArgs(string path, int index)
        {
            Path = path;
            Index = index;
        }

        public string Path { get; private set; }

        public int Index { get; private set; }
    }

    internal unsafe class ClientFunctions : IDisposable
    {
        private readonly UIModule* pUIModuleInstance;

        private const int ResourceDataPointerOffset = 0xB0;

        private delegate void* PlaySpecificSoundDelegate(long a1, int idx);

        private delegate void* GetResourceSyncPrototype(IntPtr pFileManager, uint* pCategoryId, char* pResourceType, uint* pResourceHash, char* pPath, void* pUnknown);

        private delegate void* GetResourceAsyncPrototype(IntPtr pFileManager, uint* pCategoryId, char* pResourceType, uint* pResourceHash, char* pPath, void* pUnknown, bool isUnknown);

        private delegate IntPtr LoadSoundFileDelegate(IntPtr resourceHandle, uint a2);

        private delegate nint EnableCutsceneInputModeDelegate(UIModule* pUIModule, nint a2);

        private delegate nint DisableCutsceneInputModeDelegate(UIModule* pUIModule);

        [Signature("48 89 5C 24 ?? 57 48 83 EC 20 48 8D 99 ?? ?? ?? ?? 48 8B F9 80 7B 25 00")]
        private readonly EnableCutsceneInputModeDelegate? enableCutsceneInputMode = null;

        [Signature("48 89 5C 24 ?? 57 48 83 EC 20 48 8B F9 48 81 C1 ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8B 07")]
        private readonly DisableCutsceneInputModeDelegate? disableCutsceneInputMode = null;

        [Signature("48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC 20 33 F6 8B DA 48 8B F9 0F BA E2 0F")]
        private readonly Hook<PlaySpecificSoundDelegate>? playSpecificSoundHook = null;

        [Signature("E8 ?? ?? ?? ?? 48 8D 8F ?? ?? ?? ?? 48 89 87 ?? ?? ?? ?? 48 8D 54 24")]
        private readonly Hook<GetResourceSyncPrototype>? getResourceSyncHook = null;

        [Signature("E8 ?? ?? ?? ?? 48 8B D8 EB 07 F0 FF 83")]
        private readonly Hook<GetResourceAsyncPrototype>? getResourceAsyncHook = null;

        [Signature("E8 ?? ?? ?? ?? 48 85 C0 75 04 B0 F6")]
        private readonly Hook<LoadSoundFileDelegate>? loadSoundFileHook = null;

        private ConcurrentDictionary<IntPtr, string> Scds { get; } = new();

        public EventHandler<PlaySpecificSoundEventArgs> PlaySpecificSoundEvent = null!;

        private void OnPlaySpecificSoundEvent(PlaySpecificSoundEventArgs e)
        {
            PlaySpecificSoundEvent.Invoke(this, e);
        }

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

        public ClientFunctions()
        {
            SignatureHelper.Initialise(this);

            this.pUIModuleInstance = UIModule.Instance();

            this.playSpecificSoundHook?.Enable();
            this.loadSoundFileHook?.Enable();
            this.getResourceSyncHook?.Enable();
            this.getResourceAsyncHook?.Enable();

        }

        public void Dispose()
        {
            this.playSpecificSoundHook?.Dispose();
            this.loadSoundFileHook?.Dispose();
            this.getResourceSyncHook?.Dispose();
            this.getResourceAsyncHook?.Dispose();
        }

        internal void Disable()
        {
            this.playSpecificSoundHook?.Disable();
            this.loadSoundFileHook?.Disable();
            this.getResourceSyncHook?.Disable();
            this.getResourceAsyncHook?.Disable();
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

        private void* PlaySpecificSoundDetour(long a1, int idx)
        {
            try
            {
                if (a1 == 0)
                {
                    return null;
                }

                var scdData = *(byte**)(a1 + 8);
                if (scdData == null)
                {
                    return null;
                }

                // check cached scds for path
                if (!this.Scds.TryGetValue((IntPtr)scdData, out var path))
                {
                    return null;
                }

                path = path.ToLowerInvariant();
#if DEBUG
                PluginLog.LogInformation($".scd played: {path}", path);
#endif
                OnPlaySpecificSoundEvent(new PlaySpecificSoundEventArgs(path, idx));
            }
            catch (Exception ex)
            {
                PluginLog.LogError(ex, "Error in PlaySpecificSoundDetour!");
            }

            return this.playSpecificSoundHook!.Original(a1, idx);
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
                ? this.getResourceSyncHook!.Original(pFileManager, pCategoryId, pResourceType, pResourceHash, pPath, pUnknown)
                : this.getResourceAsyncHook!.Original(pFileManager, pCategoryId, pResourceType, pResourceHash, pPath, pUnknown, isUnknown);
        }

        private IntPtr LoadSoundFileDetour(IntPtr resourceHandle, uint a2)
        {
            var ret = this.loadSoundFileHook!.Original(resourceHandle, a2);

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
