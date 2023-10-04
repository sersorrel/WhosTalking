using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using WhosTalking.Discord;

namespace WhosTalking;

public class IpcSystem: IDisposable {
    private readonly ICallGateProvider<string, int> cgGetUserState;
    private readonly Plugin plugin;

    public IpcSystem(Plugin plugin, DalamudPluginInterface pluginInterface) {
        this.plugin = plugin;

        this.cgGetUserState = pluginInterface.GetIpcProvider<string, int>("WT.GetUserState");
        this.cgGetUserState.RegisterFunc(this.GetUserState);

        plugin.PluginLog.Verbose("[IPC] Firing WT.Available.");
        var cgAvailable = pluginInterface.GetIpcProvider<bool>("WT.Available");
        cgAvailable.SendMessage();
    }

    public void Dispose() {
        this.cgGetUserState.UnregisterFunc();
    }

    private int GetUserState(string name) {
        var user = this.plugin.XivToDiscord(name);
        var state = user?.State ?? UserState.None;

        return (int)state;
    }
}
