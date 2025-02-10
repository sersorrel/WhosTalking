using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace WhosTalking;

public enum NonXivUsersDisplayMode {
    Off = 0,
    BelowPartyList = 1,
    ManuallyPositioned = 2,
}

public enum IndicatorStyle {
    Imgui = 0,
    Atk = 1,
}

[Serializable]
public sealed class Configuration: IPluginConfiguration {
    // the below exist just to make saving less cumbersome
    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    public string? AccessToken { get; set; }
    public List<AssignmentEntry> IndividualAssignments { get; set; } = new();
    public NonXivUsersDisplayMode NonXivUsersDisplayMode { get; set; } = NonXivUsersDisplayMode.BelowPartyList;
    public bool ShowNonXivUsersAlways { get; set; } = false;
    public int NonXivUsersX { get; set; } = 10;
    public int NonXivUsersY { get; set; } = 10;
    public bool ShowIndicators { get; set; } = true;
    public bool ShowUnmatchedUsers { get; set; } = true;
    public IndicatorStyle IndicatorStyle { get; set; } = IndicatorStyle.Imgui;
    public bool UseRoundedCorners { get; set; } = true;
    public int Port { get; set; } = 6463;

    // colours are ABGR
    public uint ColourUnmatched { get; set; } = 0xFF00FFFF; // yellow
    public uint ColourSpeaking { get; set; } = 0xFF00FF00; // green
    public uint ColourMuted { get; set; } = 0xFF808000; // teal
    public uint ColourDeafened { get; set; } = 0xFF0000FF; // red
    public int Version { get; set; } = 0;

    public void Initialize(IDalamudPluginInterface pluginInterface) {
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
