using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.Party;
using Dalamud.Game.Gui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using JetBrains.Annotations;
using WhosTalking.Discord;
using WhosTalking.Windows;

namespace WhosTalking;

[PublicAPI]
public sealed class Plugin: IDalamudPlugin {
    internal DiscordConnection Connection;
    private Stack<Action> disposeActions = new();
    public WindowSystem WindowSystem = new("WhosTalking");

    public Plugin(
        [RequiredVersion("1.0")] DalamudPluginInterface pluginInterface,
        [RequiredVersion("1.0")] GameGui gameGui,
        [RequiredVersion("1.0")] PartyList partyList
    ) {
        this.PluginInterface = pluginInterface;
        this.GameGui = gameGui;
        this.PartyList = partyList;

        this.Configuration = this.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        this.Configuration.Initialize(this.PluginInterface);

        this.ConfigWindow = new ConfigWindow(this);
        this.disposeActions.Push(() => this.ConfigWindow.Dispose());
        this.MainWindow = new MainWindow(this);
        this.disposeActions.Push(() => this.MainWindow.Dispose());

        this.WindowSystem.AddWindow(this.ConfigWindow);
        this.WindowSystem.AddWindow(this.MainWindow);
        this.disposeActions.Push(() => this.WindowSystem.RemoveAllWindows());

        this.PluginInterface.UiBuilder.Draw += this.Draw;
        this.disposeActions.Push(() => this.PluginInterface.UiBuilder.Draw -= this.Draw);
        this.PluginInterface.UiBuilder.OpenConfigUi += this.OpenConfigUi;
        this.disposeActions.Push(() => this.PluginInterface.UiBuilder.OpenConfigUi -= this.OpenConfigUi);

        this.Connection = new DiscordConnection(this);
        this.disposeActions.Push(() => this.Connection.Dispose());

#if DEBUG
        this.MainWindow.IsOpen = true;
#endif
        if (pluginInterface.Reason == PluginLoadReason.Installer) {
            this.ConfigWindow.IsOpen = true;
        }
    }

    public Configuration Configuration { get; init; }
    internal ConfigWindow ConfigWindow { get; init; }
    internal GameGui GameGui { get; init; }
    internal MainWindow MainWindow { get; init; }
    internal PartyList PartyList { get; init; }
    internal DalamudPluginInterface PluginInterface { get; init; }
    public string Name => "Who's Talking";

    public void Dispose() {
        foreach (var action in this.disposeActions) {
            action.Invoke();
        }
    }

    public void OpenConfigUi() {
        this.ConfigWindow.IsOpen = true;
    }

    private static uint GetColour(User? user) {
        // colours are ABGR
        if (user == null) {
            return 0xFF00FFFF; // yellow
        }

        if (user.Speaking.GetValueOrDefault()) {
            return 0xFF00FF00; // green
        }

        if (user.Deafened.GetValueOrDefault()) {
            return 0xFF0000FF; // red
        }

        if (user.Muted.GetValueOrDefault()) {
            return 0xFF808000; // blue
        }

        return 0;
    }

    private static unsafe Vector2 GetNodePosition(AtkResNode* node) {
        var pos = new Vector2(node->X, node->Y);
        var par = node->ParentNode;
        while (par != null) {
            pos *= new Vector2(par->ScaleX, par->ScaleY);
            pos += new Vector2(par->X, par->Y);
            par = par->ParentNode;
        }

        return pos;
    }

    private void Draw() {
        this.WindowSystem.Draw();
        if (this.Connection?.Self != null) {
            this.DrawOverlay();
        }
    }

    private unsafe void DrawIndicator(ImDrawListPtr drawList, AddonPartyList* partyAddon, int idx, User? user) {
        var colNode = &partyAddon->PartyMember[idx].ClassJobIcon->AtkResNode;
        if ((nint)colNode == nint.Zero) { // this seems like it's null sometimes? set up cwp via pf, exception on join
            return;
        }

        var indicatorStart = GetNodePosition(colNode);
        var scale = partyAddon->AtkUnitBase.Scale;
        var indicatorSize = new Vector2(colNode->Width, colNode->Height) * scale;
        var indicatorMin = indicatorStart + ImGui.GetMainViewport().Pos;
        var indicatorMax = indicatorStart + indicatorSize + ImGui.GetMainViewport().Pos;
        drawList.AddRect(
            indicatorMin,
            indicatorMax,
            GetColour(user),
            7 * scale,
            ImDrawFlags.RoundCornersAll,
            (3 * scale) - 1
        );
    }

    private unsafe void DrawOverlay() {
        ImGuiHelpers.ForceNextWindowMainViewport();
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().Pos);
        ImGui.SetNextWindowSize(ImGui.GetMainViewport().Size);
        if (ImGui.Begin(
                "##WhosTalkingOverlay",
                ImGuiWindowFlags.NoDecoration
                | ImGuiWindowFlags.NoSavedSettings
                | ImGuiWindowFlags.NoMove
                | ImGuiWindowFlags.NoMouseInputs
                | ImGuiWindowFlags.NoFocusOnAppearing
                | ImGuiWindowFlags.NoBackground
                | ImGuiWindowFlags.NoNav
            )) {
            try {
                ImGui.PushClipRect(
                    ImGui.GetMainViewport().Pos,
                    ImGui.GetMainViewport().Pos + ImGui.GetMainViewport().Size,
                    false
                );
                var drawList = ImGui.GetWindowDrawList();
                var partyAddon = (AddonPartyList*)this.GameGui.GetAddonByName("_PartyList");
                // TODO: check AddonPartyList.HideWhenSolo?
                var shouldDrawParty = (nint)partyAddon != nint.Zero && partyAddon->AtkUnitBase.IsVisible;
                if (shouldDrawParty) {
                    if (this.PartyList.Length == 0) {
                        var memberCount = InfoProxyCrossRealm.GetPartyMemberCount();
                        if (memberCount == 0) {
                            // solo
                            var user = this.Connection.Self;
                            this.DrawIndicator(drawList, partyAddon, 0, user);
                        } else {
                            // cross-world party
                            for (var i = 0; i < memberCount; i++) {
                                var member = InfoProxyCrossRealm.GetGroupMember((uint)i);
                                var user = this.XivToDiscord(Marshal.PtrToStringUTF8((nint)member->Name)!, null);
                                this.DrawIndicator(drawList, partyAddon, i, user);
                            }
                        }
                    } else {
                        // regular party (or cross-world party in an instance, which works out the same)
                        var agentHud = Framework.Instance()->GetUiModule()->GetAgentModule()->GetAgentHUD();
                        var partyMemberCount = agentHud->PartyMemberCount;
                        var partyMemberList = (HudPartyMember*)agentHud->PartyMemberList; // length 10
                        for (var i = 0; i < partyMemberCount; i++) {
                            var partyMember = partyMemberList[i];
                            // TODO: look at partyMember.Object (and this.PartyList)
                            var user = this.XivToDiscord(Marshal.PtrToStringUTF8((nint)partyMember.Name)!, null);
                            this.DrawIndicator(drawList, partyAddon, i, user);
                        }
                    }
                }
            } finally {
                ImGui.PopClipRect();
                ImGui.End();
            }
        }
    }

    private User? XivToDiscord(string name, string? world) {
        foreach (var user in this.Connection.AllUsers.Values) {
            var discordId = user.UserId;

            foreach (var individualEntry in this.Configuration.IndividualAssignments) {
                if (individualEntry.CharacterName == name && individualEntry.DiscordId == discordId) {
                    return user;
                }
            }

            var discordName = user.DisplayName.IsNullOrEmpty() ? user.Username : user.DisplayName;
            if (discordName == null) {
                continue;
            }

            if (discordName == name || discordName.ToLowerInvariant().Contains(name.ToLowerInvariant())) {
                return user;
            }

            var split = name.Split(' ');
            discordName = discordName.ToLowerInvariant();
            if (discordName.Contains(split[0].ToLowerInvariant())
                || discordName.Contains(split[1].ToLowerInvariant())) {
                return user;
            }
        }

        return null;
    }
}
