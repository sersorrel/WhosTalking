using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Party;
using Dalamud.Game.Gui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Logging;
using Dalamud.Plugin;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game.Group;
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
        [RequiredVersion("1.0")] PartyList partyList,
        [RequiredVersion("1.0")] ObjectTable objectTable
    ) {
        this.PluginInterface = pluginInterface;
        this.GameGui = gameGui;
        this.PartyList = partyList;
        this.ObjectTable = objectTable;

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
        // this.MainWindow.IsOpen = true;
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
    internal ObjectTable ObjectTable { get; init; }
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

    private unsafe void DrawIndicatorAlliance(ImDrawListPtr drawList, AtkUnitBase* allianceAddon, int idx, User? user) {
        // if (idx is 0 or 1 or 2) {
        //     PluginLog.Information($"idx {idx} {user?.DisplayName} is speaking: {user?.Speaking}");
        // }
        var atkIdx = 9 - idx;
        var nodePtr = allianceAddon->UldManager.NodeList[atkIdx];
        var comp = ((AtkComponentNode*)nodePtr)->Component;
        // var gridNode = comp->UldManager.NodeList[2]->ChildNode;
        var gridNode = comp->UldManager.SearchNodeById(4);

        var indicatorStart = GetNodePosition(gridNode);
        var scale = allianceAddon->Scale;
        var indicatorSize = new Vector2(gridNode->Width, gridNode->Height) * scale;
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
                    // cross-world party stuff: basically separate from everything else
                    // specifically, here we draw the cross-world stuff just for our own full party
                    // (if we're in alliance raid mode, we need special handling to avoid trying to draw
                    // indicators for the other alliances)
                    // TODO: possibly we can just always do the alliance-mode case? does that work?
                    // also TODO: does this draw the indicators twice if PartyList.Length > 0 (i.e. in the duty)?
                    // I don't remember whether the cross-realm stuff stays set up in the instance, but I think it might
                    var ipcr = InfoProxyCrossRealm.Instance();
                    if (ipcr->IsInAllianceRaid == 1) {
                        var memberCount = InfoProxyCrossRealm.GetGroupMemberCount(ipcr->LocalPlayerGroupIndex);
                        for (var i = 0; i < memberCount; i++) {
                            var member = InfoProxyCrossRealm.GetGroupMember((uint)i);
                            var user = this.XivToDiscord(Marshal.PtrToStringUTF8((nint)member->Name)!, null);
                            this.DrawIndicator(drawList, partyAddon, i, user);
                        }
                    } else {
                        var memberCount = InfoProxyCrossRealm.GetPartyMemberCount();
                        for (var i = 0; i < memberCount; i++) {
                            var member = InfoProxyCrossRealm.GetGroupMember((uint)i);
                            var user = this.XivToDiscord(Marshal.PtrToStringUTF8((nint)member->Name)!, null);
                            this.DrawIndicator(drawList, partyAddon, i, user);
                        }
                    }

                    if (this.PartyList.Length == 0 && !InfoProxyCrossRealm.IsCrossRealmParty()) {
                        // note: GroupCount is *weird*, it never seems to actually decrease??
                        // || (InfoProxyCrossRealm.Instance()->GroupCount == 1
                        //     && InfoProxyCrossRealm.GetPartyMemberCount() == 0))) {
                        // only enter solo mode if not in (or in a PF for) an alliance raid
                        // actually: only enter solo mode if nobody's in *our* full party AND there are no cross-realm shenanigans afoot
                        this.DrawIndicator(drawList, partyAddon, 0, this.Connection.Self);
                    }

                    if (this.PartyList.Length > 0) {
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

                    // do we need to draw indicators for other alliances?
                    var allianceWindow1 = (AtkUnitBase*)this.GameGui.GetAddonByName("_AllianceList1");
                    var allianceWindow2 = (AtkUnitBase*)this.GameGui.GetAddonByName("_AllianceList2");
                    var allianceWindow1Visible =
                        (nint)allianceWindow1 != nint.Zero
                        && allianceWindow1->IsVisible; // these checks don't actually seem to work???
                    var allianceWindow2Visible = (nint)allianceWindow2 != nint.Zero && allianceWindow2->IsVisible;
                    // IsInAllianceRaid is true even in the pre-raid PF state, argh...
                    if (ipcr->IsInAllianceRaid == 1 && allianceWindow1Visible && allianceWindow2Visible) {
                        // PluginLog.Warning("FRAME");
                        var allianceWindowNumber = 1;
                        for (byte group = 0; group < ipcr->GroupCount; group++) {
                            // TODO: is groupIndex actually needed for anything else?
                            // at least when in alliance A, this is just 0:0 1:1 2:2...
                            var groupIndex = InfoProxyCrossRealm.GetGroupIndex(group);
                            if (groupIndex == ipcr->LocalPlayerGroupIndex) {
                                // note: we do not increment allianceWindowNumber here
                                // FIXME: is this actually correct? (I *think* this will skip doing alliance stuff for the alliance we're in)
                                continue;
                            }

                            // i'm in alliance A: groups 0 and 1 are alliance B and C respectively

                            // conclusions!
                            // IPCR just wants the "real" group index (0, 1, or 2), regardless of which group you're in
                            // GroupManager wants the alliance group index (that is, either 0 or 1 in a regular alliance raid)
                            // this game is made out of spaghetti

                            var groupManager = GroupManager.Instance();
                            var groupMemberCount = InfoProxyCrossRealm.GetGroupMemberCount(group);
                            for (var memberIdx = 0; memberIdx < groupMemberCount; memberIdx++) {
                                // PluginLog.Information($"getting member [{group}][{memberIdx}] with index {allianceWindowNumber - 1}");
                                var member = groupManager->GetAllianceMemberByGroupAndIndex(
                                    allianceWindowNumber - 1,
                                    memberIdx
                                );
                                // var member = groupManager->GetAllianceMemberByIndex(
                                //     (group * agentHud->RaidGroupSize) + memberIdx
                                // );
                                // var memberObjectId = agentHud->RaidMemberIds[group * agentHud->RaidGroupSize + memberIdx];
                                // var memberObject = this.ObjectTable.SearchById(memberObjectId);
                                // // TODO FIXME XXX: check memberObject.IsValid()
                                // if (memberObject == null) {
                                //     PluginLog.Error($"object table entry missing for [{group}][{memberIdx}]");
                                //     continue;
                                // }
                                // var user = this.XivToDiscord(memberObject.Name.TextValue, null);
                                // if (member == null) { // this should never happen
                                //     PluginLog.Information($"SKIPPING {group} {memberIdx}");
                                //     continue;
                                // }
                                var name = Marshal.PtrToStringUTF8((nint)member->Name)!;
                                if (name == "") {
                                    // they left the raid, don't draw an indicator for them
                                    // for whatever reason they still show up in the crossworld infoproxy even if they leave
                                    // (this isn't the case for non-alliance content, at least not sastasha)
                                    continue;
                                }

                                var user = this.XivToDiscord(name, null);
                                // PluginLog.Information($"[{group}][{memberIdx}]: {Marshal.PtrToStringUTF8((nint)member->Name)!}");
                                if (allianceWindowNumber == 1) {
                                    // PluginLog.Information($"drawing alliance ONE, {group} {memberIdx} {name} {user?.DisplayName}");
                                    this.DrawIndicatorAlliance(drawList, allianceWindow1, memberIdx, user);
                                } else if (allianceWindowNumber == 2) {
                                    // PluginLog.Information($"drawing alliance TWOOOOOOO, {group} {memberIdx} {name} {user?.DisplayName}");
                                    this.DrawIndicatorAlliance(drawList, allianceWindow2, memberIdx, user);
                                } else {
                                    PluginLog.Error(
                                        $"bad alliance window {allianceWindowNumber}, are you doing DRS or something"
                                    );
                                }
                            }

                            allianceWindowNumber++;
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
            if (split.Length != 2) {
                // e.g. your chocobo (and also just "everything probably" when ClientStructs is out of date post-patch)
                return null;
            }

            discordName = discordName.ToLowerInvariant();
            if (discordName.Contains(split[0].ToLowerInvariant())
                || discordName.Contains(split[1].ToLowerInvariant())) {
                return user;
            }
        }

        return null;
    }
}
