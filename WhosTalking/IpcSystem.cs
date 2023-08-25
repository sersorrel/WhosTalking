using Dalamud.Logging;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using System;
using WhosTalking.Discord;

namespace WhosTalking;

public class IpcSystem: IDisposable {
    private readonly Plugin plugin;
    private readonly ICallGateProvider<string, int> cgGetUserState;

    public IpcSystem(Plugin plugin, DalamudPluginInterface pluginInterface) {
        this.plugin = plugin;

        this.cgGetUserState = pluginInterface.GetIpcProvider<string, int>("WT.GetUserState");
        this.cgGetUserState.RegisterFunc(GetUserState);

        PluginLog.Verbose("[IPC] Firing WT.Available.");
        ICallGateProvider<bool> cgAvailable = pluginInterface.GetIpcProvider<bool>("WT.Available");
        cgAvailable.SendMessage();
    }

    private int GetUserState(string name) {
        User? user = plugin.XivToDiscord(name);
        UserState state = user?.State ?? UserState.None;

        return (int)state;
    }

    public void Dispose() {
        this.cgGetUserState.UnregisterFunc();
    }
}

