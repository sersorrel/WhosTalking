using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Command;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Dalamud.Utility.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game.Group;
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
    internal IpcSystem IpcSystem;
    public WindowSystem WindowSystem = new("WhosTalking");

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        IGameGui gameGui,
        IPartyList partyList,
        IObjectTable objectTable,
        IClientState clientState,
        ICommandManager commandManager,
        IPluginLog pluginLog,
        INotificationManager notificationManager,
        ITextureProvider textureProvider,
        IAddonLifecycle addonLifecycle
    ) {
        this.PluginInterface = pluginInterface;
        this.GameGui = gameGui;
        this.PartyList = partyList;
        this.ObjectTable = objectTable;
        this.ClientState = clientState;
        this.CommandManager = commandManager;
        this.PluginLog = pluginLog;
        this.NotificationManager = notificationManager;
        this.TextureProvider = textureProvider;
        this.AddonLifecycle = addonLifecycle;

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

        this.IpcSystem = new IpcSystem(this, pluginInterface);
        this.disposeActions.Push(() => this.IpcSystem.Dispose());

        this.CommandManager.AddHandler("/whostalking", new CommandInfo(this.OnCommand));
        this.disposeActions.Push(() => this.CommandManager.RemoveHandler("/whostalking"));

        this.AddonLifecycle.RegisterListener(AddonEvent.PreDraw, "_PartyList", this.AtkDrawPartyList);
        this.disposeActions.Push(() => this.AddonLifecycle.UnregisterListener(this.AtkDrawPartyList));

        this.AddonLifecycle.RegisterListener(AddonEvent.PreDraw, ["_AllianceList1", "_AllianceList2"], this.AtkDrawAllianceList);
        this.disposeActions.Push(() => this.AddonLifecycle.UnregisterListener(this.AtkDrawAllianceList));

#if DEBUG
        if (pluginInterface.Reason == PluginLoadReason.Reload) {
            this.MainWindow.IsOpen = true;
        }
        // this.ConfigWindow.IsOpen = true;
#endif
        if (pluginInterface.Reason == PluginLoadReason.Installer) {
            this.ConfigWindow.IsOpen = true;
        }

        this.PluginLog.Information("Who's Talking is ready for action!");
    }

    public Configuration Configuration { get; init; }
    internal ConfigWindow ConfigWindow { get; init; }
    internal IGameGui GameGui { get; init; }
    internal MainWindow MainWindow { get; init; }
    internal IPartyList PartyList { get; init; }
    internal IObjectTable ObjectTable { get; init; }
    internal IDalamudPluginInterface PluginInterface { get; init; }
    internal IClientState ClientState { get; init; }
    internal ICommandManager CommandManager { get; init; }
    internal IPluginLog PluginLog { get; init; }
    internal INotificationManager NotificationManager { get; init; }
    internal ITextureProvider TextureProvider { get; init; }
    internal IAddonLifecycle AddonLifecycle { get; init; }

    private int validSlots;

    public void Dispose() {
        foreach (var action in this.disposeActions) {
            action.Invoke();
        }
    }

    private void OnCommand(string command, string args) {
        this.ConfigWindow.IsOpen = !this.ConfigWindow.IsOpen;
    }

    public void OpenConfigUi() {
        this.ConfigWindow.IsOpen = true;
    }

    private uint GetColour(User? user) {
        if (user == null) {
            return this.Configuration.ShowUnmatchedUsers ? this.Configuration.ColourUnmatched : 0;
        }

        if (user.Speaking.GetValueOrDefault()) {
            return this.Configuration.ColourSpeaking;
        }

        if (user.Deafened.GetValueOrDefault()) {
            return this.Configuration.ColourDeafened;
        }

        if (user.Muted.GetValueOrDefault()) {
            return this.Configuration.ColourMuted;
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

    private unsafe void AtkDrawPartyList(AddonEvent evt, AddonArgs args) {
        if (this.Configuration.IndicatorStyle != IndicatorStyle.Atk) {
            return;
        }

        var partyList = (AddonPartyList*)args.Addon;
        if (partyList == null) {
            return;
        }

        for (var i = 0; i < 8; i++) {
            var partyMemberComponent = partyList->PartyMembers[i].PartyMemberComponent;
            if (partyMemberComponent == null) continue;
            if (partyMemberComponent->OwnerNode == null) continue;
            if (!partyMemberComponent->OwnerNode->IsVisible()) continue;
            var jobIconGlow = partyMemberComponent->GetImageNodeById(19);
            if (jobIconGlow == null) continue;
            if ((this.validSlots & (1 << i)) != 0) {
                jobIconGlow->ToggleVisibility(jobIconGlow->Color.RGBA != 0);
            } else { // reset the colour to normal
                jobIconGlow->Color.RGBA = 0xffffffff;
            }
        }
    }

    private unsafe void AtkDrawAllianceList(AddonEvent evt, AddonArgs args) {
        if (this.Configuration.IndicatorStyle != IndicatorStyle.Atk) {
            return;
        }

        var allianceList = (AtkUnitBase*)args.Addon;
        if (allianceList == null) {
            return;
        }

        var offset = allianceList->NameString.EndsWith('1') ? 8 : 16;
        for (var i = 0; i < 8; i++) {
            var memberNode = allianceList->UldManager.NodeList[9 - i]; // 9 to 2
            if (memberNode == null) continue;
            if (!memberNode->IsVisible()) continue;
            var componentNode = memberNode->GetComponent();
            if (componentNode == null) continue;
            var jobIconGlow = componentNode->GetImageNodeById(9);
            if (jobIconGlow == null) continue;
            if ((this.validSlots & (1 << (i + offset))) != 0) {
                jobIconGlow->ToggleVisibility(jobIconGlow->Color.RGBA != 0);
            } else { // reset the colour to normal
                jobIconGlow->Color.RGBA = 0xffffffff;
            }
        }
    }

    private void Draw() {
        this.WindowSystem.Draw();
        if (this.Connection?.Self != null || this.ConfigWindow.IsOpen) {
            this.DrawOverlay();
        } else {
            this.validSlots = 0;
        }
    }

    private unsafe void DrawIndicator(ImDrawListPtr drawList, AddonPartyList* partyAddon, int idx, User? user) {
        if (!this.Configuration.ShowIndicators) {
            return;
        }

        var classJobIcon = partyAddon->PartyMembers[idx].ClassJobIcon;
        if (classJobIcon == null) { // this seems like it's null sometimes? set up cwp via pf, exception on join
            return;
        }
        var colNode = &classJobIcon->AtkResNode;

        if (this.Configuration.IndicatorStyle == IndicatorStyle.Imgui) {
            var indicatorStart = GetNodePosition(colNode);
            var scale = partyAddon->AtkUnitBase.Scale;
            var indicatorSize = new Vector2(colNode->Width, colNode->Height) * scale;
            var indicatorMin = indicatorStart + ImGui.GetMainViewport().Pos;
            var indicatorMax = indicatorStart + indicatorSize + ImGui.GetMainViewport().Pos;
            var cornerStyle = (this.Configuration.UseRoundedCorners == true) ?
                ImDrawFlags.RoundCornersAll :
                ImDrawFlags.RoundCornersNone;
            drawList.AddRect(
                indicatorMin,
                indicatorMax,
                this.GetColour(user),
                7 * scale,
                cornerStyle,
                (3 * scale) - 1
            );
        } else if (this.Configuration.IndicatorStyle == IndicatorStyle.Atk) {
            var colour = this.GetColour(user);
            var jobIconGlowNode = partyAddon->PartyMembers[idx].PartyMemberComponent->GetImageNodeById(19);
            jobIconGlowNode->Color.RGBA = colour;
            // visibility will be handled in predraw
        }
    }

    private unsafe void DrawIndicatorAlliance(ImDrawListPtr drawList, AtkUnitBase* allianceAddon, int idx, User? user) {
        if (!this.Configuration.ShowIndicators) {
            return;
        }

        // if (idx is 0 or 1 or 2) {
        //     PluginLog.Information($"idx {idx} {user?.DisplayName} is speaking: {user?.Speaking}");
        // }
        var atkIdx = 9 - idx;
        var nodePtr = allianceAddon->UldManager.NodeList[atkIdx];
        var comp = ((AtkComponentNode*)nodePtr)->Component;
        // var gridNode = comp->UldManager.NodeList[2]->ChildNode;
        var gridNode = comp->UldManager.SearchNodeById(4);

        if (this.Configuration.IndicatorStyle == IndicatorStyle.Imgui) {
            var indicatorStart = GetNodePosition(gridNode);
            var scale = allianceAddon->Scale;
            var indicatorSize = new Vector2(gridNode->Width, gridNode->Height) * scale;
            var indicatorMin = indicatorStart + ImGui.GetMainViewport().Pos;
            var indicatorMax = indicatorStart + indicatorSize + ImGui.GetMainViewport().Pos;
            var cornerStyle = (this.Configuration.UseRoundedCorners == true) ?
                ImDrawFlags.RoundCornersAll :
                ImDrawFlags.RoundCornersNone;
            drawList.AddRect(
                indicatorMin,
                indicatorMax,
                this.GetColour(user),
                7 * scale,
                cornerStyle,
                (3 * scale) - 1
            );
        } else if (this.Configuration.IndicatorStyle == IndicatorStyle.Atk) {
            var colour = this.GetColour(user);
            var jobIconGlowNode = comp->UldManager.SearchNodeById(9);
            jobIconGlowNode->Color.RGBA = colour;
            // visibility will be handled in predraw
        }
    }

    private unsafe void DrawOverlay() {
        ImGuiHelpers.ForceNextWindowMainViewport();
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().Pos);
        ImGui.SetNextWindowSize(ImGui.GetMainViewport().Size);
        var validSlots = 0;
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
                var knownUsers = new HashSet<User>();
                ImGui.PushClipRect(
                    ImGui.GetMainViewport().Pos,
                    ImGui.GetMainViewport().Pos + ImGui.GetMainViewport().Size,
                    false
                );
                var drawList = ImGui.GetWindowDrawList();
                var partyAddon = (AddonPartyList*)this.GameGui.GetAddonByName("_PartyList");
                // TODO: check AddonPartyList.HideWhenSolo?
                // FIXME: this is mostly broken lmao
                // the IsVisible test prevents us from drawing if the party list is hidden because "hide party list when solo" is enabled,
                // but it doesn't help at all for the case where the user hid their party list via HUD Layout.
                // (this seems like a common behaviour across the whole of HUD Layout, frustratingly)
                var shouldDrawParty = (nint)partyAddon != nint.Zero
                    && partyAddon->AtkUnitBase.IsVisible // false only if hidden by being in solo
                    && (partyAddon->AtkUnitBase.VisibilityFlags & 1) == 0 // hidden by user (HUD Layout)
                    && (partyAddon->AtkUnitBase.VisibilityFlags & 4) == 0; // hidden by game
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
                            var user = this.XivToDiscord(member->NameString);
                            this.DrawIndicator(drawList, partyAddon, i, user);
                            validSlots |= 1 << i;
                            knownUsers.Add(user!);
                        }
                    } else {
                        var memberCount = InfoProxyCrossRealm.GetPartyMemberCount();
                        for (var i = 0; i < memberCount; i++) {
                            var member = InfoProxyCrossRealm.GetGroupMember((uint)i);
                            var user = this.XivToDiscord(member->NameString);
                            this.DrawIndicator(drawList, partyAddon, i, user);
                            validSlots |= 1 << i;
                            knownUsers.Add(user!);
                        }
                    }

                    if (this.PartyList.Length == 0 && !InfoProxyCrossRealm.IsCrossRealmParty()) {
                        // note: GroupCount is *weird*, it never seems to actually decrease??
                        // || (InfoProxyCrossRealm.Instance()->GroupCount == 1
                        //     && InfoProxyCrossRealm.GetPartyMemberCount() == 0))) {
                        // only enter solo mode if not in (or in a PF for) an alliance raid
                        // actually: only enter solo mode if nobody's in *our* full party AND there are no cross-realm shenanigans afoot
                        // additionally, don't enter solo mode if our own party list entry is hidden
                        // ("hide party list when solo", under Character Configuration > UI Settings > Party List)
                        // because that would draw the indicator on top of e.g. our chocobo
                        var node = partyAddon->AtkUnitBase.UldManager.SearchNodeById(10);
                        if (node != null && node->IsVisible()) {
                            this.DrawIndicator(drawList, partyAddon, 0, this.Connection.Self);
                            validSlots |= 1;
                            knownUsers.Add(this.Connection.Self!);
                        }
                    }

                    if (this.PartyList.Length > 0) {
                        // regular party (or cross-world party in an instance, which works out the same)
                        var agentHud = AgentHUD.Instance();
                        // take the lower of these two; we don't want to index off the end of PartyMemberList,
                        // but PartyList seems to have a better idea of how many *people* are in the party
                        // (as opposed to e.g. a chocobo)
                        var partyMemberCount = Math.Min(this.PartyList.Length, agentHud->PartyMemberCount);
                        var partyMemberList = agentHud->PartyMembers; // length 10
                        for (var i = 0; i < partyMemberCount; i++) {
                            var partyMember = partyMemberList[i];
                            // TODO: look at partyMember.Object (and this.PartyList)
                            var user = this.XivToDiscord(Marshal.PtrToStringUTF8((nint)partyMember.Name)!);
                            this.DrawIndicator(drawList, partyAddon, i, user);
                            validSlots |= 1 << i;
                            knownUsers.Add(user!);
                        }
                    }

                    // do we need to draw indicators for other alliances?
                    var allianceWindow1 = (AtkUnitBase*)this.GameGui.GetAddonByName("_AllianceList1");
                    var allianceWindow2 = (AtkUnitBase*)this.GameGui.GetAddonByName("_AllianceList2");
                    var allianceWindow1Visible =
                        (nint)allianceWindow1 != nint.Zero
                        && allianceWindow1->IsVisible
                        && (allianceWindow1->VisibilityFlags & 1) == 0
                        && (allianceWindow1->VisibilityFlags & 4) == 0;
                    var allianceWindow2Visible =
                        (nint)allianceWindow2 != nint.Zero
                        && allianceWindow2->IsVisible
                        && (allianceWindow2->VisibilityFlags & 1) == 0
                        && (allianceWindow2->VisibilityFlags & 4) == 0;
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

                            // a year later:
                            // my hypothesis is that GroupManager handles exclusively "normal" alliance raids (with the two bonus party list hud elements),
                            // and does not handle anything like DRS (or BA??) where you have more than two other groups (that aren't shown on-screen).
                            // the questions are:
                            // - is IPCR the source of truth for "weird" raids like those? (if you change party comp in DSR, does IPCR reflect that?)
                            // - is IPCR populated for "normal" alliance raids if you didn't enter via PF?
                            // if so: I should drop GroupManager entirely and use IPCR for all >8man raids
                            // otherwise: death
                            // maybe "use GroupManager if GroupCount is 3, else use IPCR"??
                            // how does the *game* tell if GroupManager has valid data?
                            // help i need @someone (to reverse more stuff for me)

                            // ...actually this is all kinda moot, because other groups aren't visible in DRS...

                            var groupManager = GroupManager.Instance();
                            var groupMemberCount = InfoProxyCrossRealm.GetGroupMemberCount(group);
                            for (var memberIdx = 0; memberIdx < groupMemberCount; memberIdx++) {
                                // PluginLog.Information($"getting member [{group}][{memberIdx}] with index {allianceWindowNumber - 1}");
                                var member = groupManager->MainGroup.GetAllianceMemberByGroupAndIndex(
                                    allianceWindowNumber - 1,
                                    memberIdx
                                );
                                if (member is null) {
                                    // this is quite bad: we thought there was a party member here, but GroupManager has no knowledge of them
                                    // this can happen in e.g. PF state for DRS (have someone join alliance F)
                                    // generally speaking, passing group indices from IPCR into GroupManager seems like a bad idea...
                                    // probably should instead use InfoProxyCrossRealm.GetGroupMember() here, iff it would work strictly better
                                    // see above comments for more details
                                    // for the time being, to avoid crashing: just continue
                                    continue;
                                }

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
                                var name = member->NameString;
                                if (name == "") {
                                    // they left the raid, don't draw an indicator for them
                                    // for whatever reason they still show up in the crossworld infoproxy even if they leave
                                    // (this isn't the case for non-alliance content, at least not sastasha)
                                    continue;
                                }

                                var user = this.XivToDiscord(name);
                                // PluginLog.Information($"[{group}][{memberIdx}]: {Marshal.PtrToStringUTF8((nint)member->Name)!}");
                                validSlots |= 1 << (memberIdx + (8 * allianceWindowNumber));
                                if (allianceWindowNumber == 1) {
                                    // PluginLog.Information($"drawing alliance ONE, {group} {memberIdx} {name} {user?.DisplayName}");
                                    this.DrawIndicatorAlliance(drawList, allianceWindow1, memberIdx, user);
                                    knownUsers.Add(user!);
                                } else if (allianceWindowNumber == 2) {
                                    // PluginLog.Information($"drawing alliance TWOOOOOOO, {group} {memberIdx} {name} {user?.DisplayName}");
                                    this.DrawIndicatorAlliance(drawList, allianceWindow2, memberIdx, user);
                                    knownUsers.Add(user!);
                                } else {
                                    this.PluginLog.Error(
                                        $"bad alliance window {allianceWindowNumber}, are you doing DRS or something"
                                    );
                                    // yes they are doing DRS or something
                                    // (but we never actually get this far in that case)
                                }
                            }

                            allianceWindowNumber++;
                        }
                    }

                    // who else is talking?
                    if (this.Configuration.NonXivUsersDisplayMode != NonXivUsersDisplayMode.Off) {
                        var pos = this.GetNonXivUsersPos(partyAddon);

                        if (pos != null) {
                            var position = pos.Value;
                            var leftColor = ImGui.GetColorU32(new Vector4(0, 0, 0, 0.75f));
                            var rightColor = ImGui.GetColorU32(new Vector4(0, 0, 0, 0));
                            var textColor = ImGui.GetColorU32(new Vector4(1, 1, 1, 1));
                            var textPadding = new Vector2(8, 2);

                            foreach (var user in this.Connection.AllUsers.Values) {
                                if (user.Speaking.GetValueOrDefault(false) && !knownUsers.Contains(user)) {
                                    var size = ImGui.CalcTextSize(user.DisplayName);
                                    var midPoint = position.WithX(position.X + (170 * partyAddon->AtkUnitBase.Scale));
                                    var rightEdge = midPoint.WithX(midPoint.X + (80 * partyAddon->AtkUnitBase.Scale));
                                    drawList.AddRectFilled(
                                        position,
                                        midPoint.WithY(midPoint.Y + size.Y + 4),
                                        leftColor
                                    );
                                    drawList.AddRectFilledMultiColor(
                                        midPoint,
                                        rightEdge.WithY(rightEdge.Y + size.Y + 4),
                                        leftColor,
                                        rightColor,
                                        rightColor,
                                        leftColor
                                    );
                                    drawList.AddText(position + textPadding, textColor, user.DisplayName);
                                    position.Y += size.Y + 5;
                                }
                            }

                            if (this.ConfigWindow.IsOpen) {
                                foreach (var s in new[]
                                    { "additional names will appear here...", "...when other people speak" }) {
                                    var size = ImGui.CalcTextSize(s);
                                    var midPoint = position.WithX(position.X + (170 * partyAddon->AtkUnitBase.Scale));
                                    var rightEdge = midPoint.WithX(midPoint.X + (80 * partyAddon->AtkUnitBase.Scale));
                                    drawList.AddRectFilled(
                                        position,
                                        midPoint.WithY(midPoint.Y + size.Y + 4),
                                        leftColor
                                    );
                                    drawList.AddRectFilledMultiColor(
                                        midPoint,
                                        rightEdge.WithY(rightEdge.Y + size.Y + 4),
                                        leftColor,
                                        rightColor,
                                        rightColor,
                                        leftColor
                                    );
                                    drawList.AddText(position + textPadding, textColor, s);
                                    position.Y += size.Y + 5;
                                }
                            }
                        }
                    }
                }
            } finally {
                this.validSlots = validSlots;
                ImGui.PopClipRect();
                ImGui.End();
            }
        }
    }

    public User? XivToDiscord(string name, string? world = null) {
        if (name == this.ClientState.LocalPlayer?.Name.ToString()) {
            return this.Connection.Self;
        }

        // TODO: this is hilariously quadratic, i should make it not be
        foreach (var user in this.Connection.AllUsers.Values) {
            var discordId = user.UserId;

            foreach (var individualEntry in this.Configuration.IndividualAssignments) {
                if (individualEntry.CharacterName == name && individualEntry.DiscordId == discordId) {
                    return user;
                }
            }
        }

        foreach (var user in this.Connection.AllUsers.Values) {
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

    private unsafe Vector2? GetNonXivUsersPos(AddonPartyList* partyAddon) {
        switch (this.Configuration.NonXivUsersDisplayMode) {
            case NonXivUsersDisplayMode.Off:
                return null;
            case NonXivUsersDisplayMode.BelowPartyList: {
                Vector2? pos = null;
                var node = (AtkResNode*)null; // lol lmao
                var lastGoodNode = (AtkResNode*)null;

                for (uint id = 10; id <= 19; id++) {
                    node = partyAddon->AtkUnitBase.UldManager.SearchNodeById(id);
                    if (node == null || !node->IsVisible()) {
                        continue;
                    }

                    lastGoodNode = node;
                    var nodePos = GetNodePosition(node);
                    if (pos == null || nodePos.Y > pos.Value.Y) {
                        pos = nodePos;
                    }
                }

                // chocobo (etc?)
                for (uint id = 180001; id <= 180007; id++) {
                    node = partyAddon->AtkUnitBase.UldManager.SearchNodeById(id);
                    if (node == null || !node->IsVisible()) {
                        continue;
                    }

                    lastGoodNode = node;
                    var nodePos = GetNodePosition(node);
                    if (pos == null || nodePos.Y > pos.Value.Y) {
                        pos = nodePos;
                    }
                }

                if (lastGoodNode != null) {
                    var position = pos ?? new Vector2(0, 0);
                    position.X += 27 * partyAddon->AtkUnitBase.Scale;
                    // all these nodes are the same height, so it doesn't matter which one we have here
                    position.Y += (lastGoodNode->Height - 10) * partyAddon->AtkUnitBase.Scale;
                    return position;
                }

                return null;
            }
            case NonXivUsersDisplayMode.ManuallyPositioned:
                return new Vector2(this.Configuration.NonXivUsersX, this.Configuration.NonXivUsersY);
            default:
                throw new ArgumentOutOfRangeException(
                    null,
                    $"unknown config value for NonXivUsersDisplayMode: {this.Configuration.NonXivUsersDisplayMode}"
                );
        }
    }
}
