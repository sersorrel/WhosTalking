using System;
using System.Runtime.InteropServices;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game.Group;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using ImGuiNET;

namespace WhosTalking.Windows;

public sealed class MainWindow: Window, IDisposable {
    private readonly Plugin plugin;

    public MainWindow(Plugin plugin): base(
        "Who's Talking debug",
        ImGuiWindowFlags.AlwaysAutoResize
    ) {
        this.plugin = plugin;
    }

    public void Dispose() {}

    private static unsafe string PtrToString(byte* ptr, int maxLen = int.MaxValue) {
        var len = 0;
        while (len < maxLen && ptr[len] != '\0') {
            len++;
        }

        return Marshal.PtrToStringUTF8((nint)ptr, len);
    }

    public override unsafe void Draw() {
        ImGui.TextUnformatted($"Current channel: {this.plugin.Connection.Channel?.Channel}");

        ImGui.Text("GroupManager party");
        foreach (var member in GroupManager.Instance()->PartyMembersSpan) {
            ImGui.TextUnformatted($"{*member.Name} {member.X}");
        }

        ImGui.Text("GroupManager alliance");
        // foreach (var member in GroupManager.Instance()->AllianceMembersSpan) {
        //     // ImGui.TextUnformatted($"{(member.Name != (byte*)nint.Zero ? *member.Name : "(null)")}");
        //     ImGui.TextUnformatted($"{*member.Name} {member.X}");
        // }
        for (var group = 0; group < 6; group++) {
            for (var idx = 0; idx < 8; idx++) {
                var partyMember = GroupManager.Instance()->GetAllianceMemberByGroupAndIndex(group, idx);
                if (partyMember == null) {
                    continue;
                }

                var name = partyMember->Name != null ? PtrToString(partyMember->Name, 20) : "(null)";
                ImGui.TextUnformatted($"[{group}][{idx}] = {name}");
            }
        }

        ImGui.TextUnformatted($"[0] idx {InfoProxyCrossRealm.GetGroupIndex(0)}");
        ImGui.TextUnformatted($"[1] idx {InfoProxyCrossRealm.GetGroupIndex(1)}");
        ImGui.TextUnformatted($"[2] idx {InfoProxyCrossRealm.GetGroupIndex(2)}");
        ImGui.TextUnformatted($"[0] members {InfoProxyCrossRealm.GetGroupMemberCount(0)}");
        ImGui.TextUnformatted($"[1] members {InfoProxyCrossRealm.GetGroupMemberCount(1)}");
        ImGui.TextUnformatted($"[2] members {InfoProxyCrossRealm.GetGroupMemberCount(2)}");
        ImGui.TextUnformatted($"partymembercount {InfoProxyCrossRealm.GetPartyMemberCount()}");
        ImGui.TextUnformatted($"groupcount {InfoProxyCrossRealm.Instance()->GroupCount}");

        ImGui.Text($"Party (size {this.plugin.PartyList.Length}):");
        foreach (var partyMember in this.plugin.PartyList) {
            ImGui.TextUnformatted($"{partyMember.Name.TextValue}");
        }

        ImGui.Text("Discord users:");
        foreach (var user in this.plugin.Connection.AllUsers.Values) {
            var muted = user.Muted.GetValueOrDefault();
            var deafened = user.Deafened.GetValueOrDefault();
            var speaking = user.Speaking.GetValueOrDefault();
            var displayName = user.DisplayName.IsNullOrEmpty() ? user.Username : user.DisplayName;
            ImGui.TextUnformatted(
                $"{displayName}: {(muted ? "" : "un")}muted, {(deafened ? "" : "un")}deafened{(speaking ? ", speaking" : "")}"
            );
            if (ImGui.IsItemHovered()) {
                ImGui.SetTooltip($"{user.Username}#{user.Discriminator}");
            }
        }

        ImGui.TextUnformatted("agentHUD stuff:");
        var agentHud = Framework.Instance()->GetUiModule()->GetAgentModule()->GetAgentHUD();
        var partyMemberCount = agentHud->PartyMemberCount;
        var partyMemberList = (HudPartyMember*)agentHud->PartyMemberList; // length 10
        for (var i = 0; i < partyMemberCount; i++) {
            var partyMember = partyMemberList[i];
            var name = partyMember.Name != null ? PtrToString(partyMember.Name, 20) : "(null)";
            ImGui.TextUnformatted($"    [{i}] = {name}{(partyMember.Object == null ? " (BattleChara is null)" : "")}");
        }

        var module = Framework.Instance()->GetUiModule()->GetInfoModule();
        if (module != null) {
            ImGui.Text("cross-world info proxy stuff:");
            var cwProxy = module->GetInfoProxyById(InfoProxyId.CrossRealmParty);
            var cwMemberCount = InfoProxyCrossRealm.GetPartyMemberCount();
            ImGui.TextUnformatted($"cwMemberCount = {cwMemberCount}");
            for (uint i = 0; i < cwMemberCount; i++) {
                var member = InfoProxyCrossRealm.GetGroupMember(i);
                var name = member->Name != null ? PtrToString(member->Name, 20) : "(null)";
                var idx = member->MemberIndex;
                var groupIdx = member->GroupIndex;
                ImGui.TextUnformatted(
                    $"    [{i}] = {name}, idx {idx} (group idx {groupIdx}, job {member->ClassJobId})"
                );
            }

            ImGui.Text("info proxy stuff:");
            var proxy = module->GetInfoProxyById(InfoProxyId.Party);
            ImGui.TextUnformatted($"EntryCount = {proxy->EntryCount}");
            var proxyList = (InfoProxyCommonList*)proxy;
            ImGui.TextUnformatted($"proxyList DataSize = {proxyList->DataSize}");
            ImGui.TextUnformatted($"proxyList DictSize = {proxyList->DictSize}");
            for (uint i = 0; i < 8 && i < proxy->EntryCount; i++) {
                var entry = proxyList->GetEntry(i);
                if (entry == null) { // shouldn't happen, but just in case
                    ImGui.TextUnformatted("null entry...");
                    break;
                }

                ImGui.TextUnformatted(
                    $"InfoProxyCommonList[{i}] = {(entry->Index == null ? "null" : "not null")}, name {(entry->Name != null ? PtrToString(entry->Name, 20) : "(null)")}"
                );
            }
        }

        if (ImGui.Button("Pop arRPC warning")) {
            this.plugin.Connection.ShowArRpcWarning();
        }

        ImGui.Separator();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Cog)) {
            this.plugin.OpenConfigUi();
        }
    }
}
