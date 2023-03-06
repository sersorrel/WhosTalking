using System;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace WhosTalking;

[Serializable]
public sealed class Configuration: IPluginConfiguration {
    // the below exist just to make saving less cumbersome
    [NonSerialized]
    private DalamudPluginInterface? pluginInterface;

    public string? AccessToken { get; set; }
    public int Version { get; set; } = 0;

    public void Initialize(DalamudPluginInterface pluginInterface) {
        this.pluginInterface = pluginInterface;
    }

    public void Save() {
        this.pluginInterface!.SavePluginConfig(this);
    }
}
