using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace WhosTalking;

public enum NonXivUsersDisplayMode {
    Off = 0,
    BelowPartyList = 1,
}

[Serializable]
public sealed class Configuration: IPluginConfiguration {
    // the below exist just to make saving less cumbersome
    [NonSerialized]
    private DalamudPluginInterface? pluginInterface;

    public string? AccessToken { get; set; }
    public List<AssignmentEntry> IndividualAssignments { get; set; } = new();
    public NonXivUsersDisplayMode NonXivUsersDisplayMode { get; set; } = NonXivUsersDisplayMode.BelowPartyList;
    public bool ShowIndicators { get; set; } = true;
    public bool ShowUnmatchedUsers { get; set; } = true;
    public int Version { get; set; } = 0;

    // colours are ABGR
    public uint ColourUnmatched { get; set; } = 0xFF00FFFF; // yellow
    public uint ColourSpeaking { get; set; } = 0xFF00FF00; // green
    public uint ColourMuted { get; set; } = 0xFF808000; // teal
    public uint ColourDeafened { get; set; } = 0xFF0000FF; // red

    public void Initialize(DalamudPluginInterface pluginInterface) {
        this.pluginInterface = pluginInterface;
    }

    public void Save() {
        this.pluginInterface!.SavePluginConfig(this);
    }
}

public sealed class AssignmentEntry {
    public string CharacterName { get; set; } = string.Empty;
    public string DiscordId { get; set; } = string.Empty;
}
