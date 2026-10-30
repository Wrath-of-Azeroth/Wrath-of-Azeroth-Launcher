local PREFIX = "KILLSTATSV1"
-- Keep the wire-protocol version at 1.9.0: the server enforces that exact
-- handshake string. The package/UI version is tracked in the TOC and feed.
local ADDON_VERSION = "1.9.0"
local stats = {}
local rows = {}
local panel
local minimapButton
local totalKills = 0
local totalQuests = 0
local serverQuestCount
local lastServerQuestCountIncrease = 0
local localQuestTurnInsPending = 0
local questSyncAttempts = 0
local questSyncElapsed = 0
local killCountText
local questCountText
local characterData
local groupUnitGUIDs = {}
local damagedCreatures = {}

local function getCharacterData(name)
    -- SavedVariables used to include GetRealmName(), so changing the realm's
    -- displayed name made the addon look like a new character. Use a stable
    -- WoA key and fold any earlier realm-name keys into it once.
    local key = "WoA-" .. name
    local data = MobRewardStatsDB[key]
    if type(data) ~= "table" then
        data = {}
    end
    data.stats = type(data.stats) == "table" and data.stats or {}

    local suffix = "-" .. name
    for oldKey, oldData in pairs(MobRewardStatsDB) do
        if oldKey ~= key and type(oldKey) == "string" and type(oldData) == "table"
            and string.sub(oldKey, -#suffix) == suffix then
            data.totalKills = math.max(tonumber(data.totalKills) or 0, tonumber(oldData.totalKills) or 0)
            data.totalQuests = math.max(tonumber(data.totalQuests) or 0, tonumber(oldData.totalQuests) or 0)
            if type(oldData.stats) == "table" then
                for statId, amount in pairs(oldData.stats) do
                    data.stats[statId] = math.max(tonumber(data.stats[statId]) or 0, tonumber(amount) or 0)
                end
            end
        end
    end

    MobRewardStatsDB[key] = data
    return data
end

local displayStats = {
    {0, "Strength"},
    {1, "Agility"},
    {2, "Stamina"},
    {3, "Intellect"},
    {4, "Spirit"},
    {29, "Armor Penetration Rating"},
    {30, "Movement Speed"},
    {32, "Spell Penetration"},
    {33, "Armor"},
    {34, "Block Chance"},
    {35, "Parry Chance"},
    -- Keep showing historic reward data even though Block Value is no longer
    -- available from the current exchanger.
    {31, "Block Value (legacy)"},
}
local statIdsByName = {
    ["Strength"] = 0,
    ["Agility"] = 1,
    ["Stamina"] = 2,
    ["Intellect"] = 3,
    ["Spirit"] = 4,
    ["Armor Penetration Rating"] = 29,
    ["Movement Speed"] = 30,
    ["Block Value"] = 31,
    ["Spell Penetration"] = 32,
    ["Armor"] = 33,
    ["Block Chance"] = 34,
    ["Parry Chance"] = 35,
}

local function requestServerStats()
    if SendAddonMessage and UnitName("player") then
        SendAddonMessage(PREFIX, "HELLO\t" .. ADDON_VERSION, "WHISPER", UnitName("player"))
        SendAddonMessage(PREFIX, "SYNC", "WHISPER", UnitName("player"))
    end
end

local function beginServerSync()
    serverQuestCount = nil
    questSyncAttempts = 0
    questSyncElapsed = 0
    requestServerStats()
    questSyncAttempts = 1
end

local function formatAmount(id)
    local amount = stats[id] or 0
    if id == 30 or id == 34 or id == 35 then
        return string.format("+%.2f%%", amount / 100)
    end
    return string.format("+%.2f", amount / 100)
end

local function refreshRows()
    if not rows[1] then
        return
    end
    for index, entry in ipairs(displayStats) do
        rows[index]:SetText(entry[2] .. ": " .. formatAmount(entry[1]))
    end
end

local function updateKillCount(count)
    totalKills = count
    if characterData then
        characterData.totalKills = totalKills
    end
    if killCountText then
        killCountText:SetText("Total creature kills: " .. totalKills)
    end
end

local function updateQuestCount(count)
    -- The local quest-reward hook is an immediate fallback. Never let a late
    -- server sync overwrite a higher locally recorded total.
    totalQuests = math.max(totalQuests, tonumber(count) or 0)
    if characterData then
        characterData.totalQuests = totalQuests
    end
    if questCountText then
        questCountText:SetText("Quests completed: " .. totalQuests)
    end
end

local function recordQuestTurnIn()
    localQuestTurnInsPending = localQuestTurnInsPending + 1
    updateQuestCount(totalQuests + 1)
end

local function addGroupUnit(unit)
    local guid = UnitGUID(unit)
    if guid then
        groupUnitGUIDs[guid] = true
    end
end

local function refreshGroupUnits()
    wipe(groupUnitGUIDs)
    addGroupUnit("player")
    addGroupUnit("pet")
    for index = 1, 4 do
        addGroupUnit("party" .. index)
        addGroupUnit("partypet" .. index)
    end
    for index = 1, 40 do
        addGroupUnit("raid" .. index)
        addGroupUnit("raidpet" .. index)
    end
end

local function isCreature(flags, guid)
    if type(flags) == "number" and COMBATLOG_OBJECT_TYPE_NPC and bit then
        return bit.band(flags, COMBATLOG_OBJECT_TYPE_NPC) ~= 0
    end
    return type(guid) == "string" and string.sub(guid, 1, 6) == "0xF130"
end

local damageEvents = {
    SWING_DAMAGE = true,
    RANGE_DAMAGE = true,
    SPELL_DAMAGE = true,
    SPELL_PERIODIC_DAMAGE = true,
    DAMAGE_SHIELD = true,
    DAMAGE_SPLIT = true,
    SPELL_BUILDING_DAMAGE = true,
    SPELL_INSTAKILL = true,
    SPELL_PERIODIC_INSTAKILL = true,
}

local function onCombatLog(...)
    local _, event, sourceGUID, _, _, destGUID, _, destFlags = ...
    if not destGUID or not isCreature(destFlags, destGUID) then
        return
    end

    if damageEvents[event] and groupUnitGUIDs[sourceGUID] then
        damagedCreatures[destGUID] = true
    elseif event == "UNIT_DIED" then
        if damagedCreatures[destGUID] then
            updateKillCount(totalKills + 1)
        end
        damagedCreatures[destGUID] = nil
    end
end

local function showStatsPanel()
    if not panel then
        return
    end
    if panel:IsShown() then
        panel:Hide()
    else
        panel:Show()
        refreshRows()
        beginServerSync()
    end
end

local function placeMinimapButton()
    if not minimapButton or not MiniMapTracking then
        return
    end
    minimapButton:ClearAllPoints()
    -- Follow Blizzard's tracking frame itself so this stays aligned if the minimap moves.
    minimapButton:SetPoint("TOPLEFT", MiniMapTracking, "TOPLEFT", 0, -36)
end

local function createMinimapButton()
    if minimapButton or not Minimap then
        return
    end

    minimapButton = CreateFrame("Button", "MobRewardStatsMinimapButton", Minimap)
    minimapButton:SetSize(32, 32)
    minimapButton:SetFrameStrata("MEDIUM")
    minimapButton:SetFrameLevel(Minimap:GetFrameLevel() + 8)
    minimapButton:EnableMouse(true)
    minimapButton:RegisterForClicks("LeftButtonUp")

    local background = minimapButton:CreateTexture(nil, "BACKGROUND")
    background:SetTexture("Interface\\Minimap\\UI-Minimap-Background")
    background:SetSize(25, 25)
    background:SetPoint("TOPLEFT", minimapButton, "TOPLEFT", 2, -4)
    background:SetVertexColor(1, 1, 1, 0.6)

    local icon = minimapButton:CreateTexture(nil, "ARTWORK")
    icon:SetTexture("Interface\\AddOns\\MobRewardStats\\MobRewardStatsIcon.tga")
    icon:SetSize(20, 20)
    icon:SetPoint("TOPLEFT", minimapButton, "TOPLEFT", 6, -6)

    local pressedOverlay = minimapButton:CreateTexture(nil, "OVERLAY")
    pressedOverlay:SetTexture("Interface\\Buttons\\WHITE8X8")
    pressedOverlay:SetAllPoints(icon)
    pressedOverlay:SetVertexColor(0, 0, 0, 0.5)
    pressedOverlay:Hide()

    local border = minimapButton:CreateTexture(nil, "BORDER")
    border:SetTexture("Interface\\Minimap\\MiniMap-TrackingBorder")
    border:SetSize(54, 54)
    border:SetPoint("TOPLEFT", minimapButton, "TOPLEFT")

    minimapButton:SetScript("OnMouseDown", function() pressedOverlay:Show() end)
    minimapButton:SetScript("OnMouseUp", function() pressedOverlay:Hide() end)
    minimapButton:SetHighlightTexture("Interface\\Minimap\\UI-Minimap-ZoomButton-Highlight")
    minimapButton:SetScript("OnClick", showStatsPanel)
    minimapButton:SetScript("OnEnter", function(self)
        GameTooltip:SetOwner(self, "ANCHOR_LEFT")
        GameTooltip:AddLine("Stat Tracker", 1, 0.82, 0)
        GameTooltip:AddLine("Click to view lifetime stats", 1, 1, 1)
        GameTooltip:Show()
    end)
    minimapButton:SetScript("OnLeave", GameTooltip_Hide)
    placeMinimapButton()
end

local function createPanel()
    if panel then
        return
    end

    panel = CreateFrame("Frame", "MobRewardStatsFrame", UIParent)
    panel:SetSize(340, 500)
    panel:SetPoint("CENTER", UIParent, "CENTER", 0, 0)
    panel:SetFrameStrata("DIALOG")
    panel:SetClampedToScreen(true)
    panel:SetMovable(true)
    panel:SetResizable(true)
    panel:SetMinResize(300, 390)
    panel:SetMaxResize(700, 800)
    panel:EnableMouse(true)
    panel:RegisterForDrag("LeftButton")
    panel:SetScript("OnDragStart", function(self) self:StartMoving() end)
    panel:SetScript("OnDragStop", function(self) self:StopMovingOrSizing() end)
    panel:SetBackdrop({
        bgFile = "Interface\\DialogFrame\\UI-DialogBox-Background",
        edgeFile = "Interface\\DialogFrame\\UI-DialogBox-Border",
        tile = true,
        tileSize = 32,
        edgeSize = 32,
        insets = { left = 11, right = 12, top = 12, bottom = 11 },
    })
    panel:SetBackdropColor(0.05, 0.07, 0.10, 0.96)
    panel:Hide()

    local title = panel:CreateFontString(nil, "OVERLAY", "GameFontNormalLarge")
    title:SetPoint("TOPLEFT", panel, "TOPLEFT", 18, -18)
    title:SetText("Stat Tracker")

    local subtitle = panel:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall")
    subtitle:SetPoint("TOPLEFT", title, "BOTTOMLEFT", 0, -5)
    subtitle:SetText("Current reward bonuses from kills and quests — drag to move")

    local close = CreateFrame("Button", nil, panel, "UIPanelCloseButton")
    close:SetPoint("TOPRIGHT", panel, "TOPRIGHT", -7, -7)

    killCountText = panel:CreateFontString(nil, "OVERLAY", "GameFontNormal")
    killCountText:SetPoint("TOPLEFT", subtitle, "BOTTOMLEFT", 0, -12)
    killCountText:SetText("Total creature kills: " .. totalKills)

    questCountText = panel:CreateFontString(nil, "OVERLAY", "GameFontNormalLarge")
    questCountText:SetPoint("TOPLEFT", killCountText, "BOTTOMLEFT", 0, -6)
    questCountText:SetText("Quests completed: " .. totalQuests)

    local scroll = CreateFrame("ScrollFrame", "MobRewardStatsScrollFrame", panel, "UIPanelScrollFrameTemplate")
    scroll:SetPoint("TOPLEFT", panel, "TOPLEFT", 16, -120)
    scroll:SetPoint("BOTTOMRIGHT", panel, "BOTTOMRIGHT", -34, 18)

    local scrollChild = CreateFrame("Frame", nil, scroll)
    scrollChild:SetWidth(270)
    scrollChild:SetHeight(#displayStats * 22)
    scroll:SetScrollChild(scrollChild)

    panel:SetScript("OnSizeChanged", function(_, width)
        local contentWidth = math.max(180, width - 75)
        scrollChild:SetWidth(contentWidth)
        for _, row in ipairs(rows) do
            row:SetWidth(contentWidth - 5)
        end
    end)

    for index, entry in ipairs(displayStats) do
        local row = scrollChild:CreateFontString(nil, "OVERLAY", "GameFontHighlight")
        row:SetPoint("TOPLEFT", 4, -((index - 1) * 22))
        row:SetWidth(265)
        row:SetJustifyH("LEFT")
        rows[index] = row
    end

    local resizeGrip = CreateFrame("Button", nil, panel)
    resizeGrip:SetSize(16, 16)
    resizeGrip:SetPoint("BOTTOMRIGHT", panel, "BOTTOMRIGHT", -12, 12)
    resizeGrip:SetNormalTexture("Interface\\ChatFrame\\UI-ChatIM-SizeGrabber-Up")
    resizeGrip:SetHighlightTexture("Interface\\ChatFrame\\UI-ChatIM-SizeGrabber-Highlight")
    resizeGrip:SetPushedTexture("Interface\\ChatFrame\\UI-ChatIM-SizeGrabber-Down")
    resizeGrip:SetScript("OnMouseDown", function() panel:StartSizing("BOTTOMRIGHT") end)
    resizeGrip:SetScript("OnMouseUp", function() panel:StopMovingOrSizing() end)
    panel:GetScript("OnSizeChanged")(panel, panel:GetWidth(), panel:GetHeight())

    SLASH_STATTRACKER1 = "/stattracker"
    SLASH_STATTRACKER2 = "/killstats"
    SlashCmdList["STATTRACKER"] = showStatsPanel
    createMinimapButton()

    refreshRows()
end

local function onServerMessage(message)
    if type(message) ~= "string" then
        return
    end

    -- LANG_ADDON self-whispers are surfaced by 3.3.5 as CHAT_MSG_ADDON with
    -- the prefix removed from the message body. Accept both that form and
    -- the full wire payload so server sync works with either event path.
    if message == "DONE" or string.match(message, "^KILLS\t%d+$")
        or string.match(message, "^QUESTS\t%d+$") or string.match(message, "^%d+\t%d+$") then
        message = PREFIX .. "\t" .. message
    end

    if message == PREFIX .. "\tDONE" then
        refreshRows()
        return
    end

    local killCount = string.match(message, "^KILLSTATSV1\tKILLS\t(%d+)$")
    if killCount then
        updateKillCount(math.max(totalKills, tonumber(killCount) or totalKills))
        return
    end

    local questCount = string.match(message, "^KILLSTATSV1\tQUESTS\t(%d+)$")
    if questCount then
        questSyncAttempts = 5
        local newServerQuestCount = tonumber(questCount) or serverQuestCount or 0
        if serverQuestCount and newServerQuestCount > serverQuestCount then
            lastServerQuestCountIncrease = GetTime()
        elseif not serverQuestCount and newServerQuestCount > totalQuests then
            -- First message may be the login sync. Suppress a nearby reward
            -- fallback only if the server already raised the visible total.
            lastServerQuestCountIncrease = GetTime()
        end
        serverQuestCount = newServerQuestCount
        updateQuestCount(serverQuestCount)
        return
    end

    local id, amount = string.match(message, "^KILLSTATSV1\t(%d+)\t(%d+)$")
    id = tonumber(id)
    amount = tonumber(amount)
    if id and amount then
        stats[id] = amount
        if characterData then
            characterData.stats = characterData.stats or {}
            characterData.stats[id] = amount
        end
        refreshRows()
    end
end

local function onWhisper(_, message)
    onServerMessage(message)
end

local function onAddonMessage(_, prefix, message)
    if prefix == PREFIX then
        onServerMessage(message)
    end
end

local function onSystemMessage(message)
    if type(message) ~= "string" then
        return
    end

    -- Keep totals updating immediately from the normal reward notice as a
    -- fallback if the server's addon-message sync is unavailable.
    local amount, statName = string.match(message, "^.- reward: %+(%d+%.%d+) permanent (.-)%.$")
    local statId = statName and statIdsByName[statName]
    if not amount or not statId then
        return
    end

    -- The quest reward stat notice is a reliable fallback if the client UI
    -- hook or the server's quest-count whisper is missed. Avoid double counts
    -- when either path already recorded this turn-in.
    if string.sub(message, 1, 14) == "Quest reward: " then
        if localQuestTurnInsPending > 0 then
            localQuestTurnInsPending = localQuestTurnInsPending - 1
            lastServerQuestCountIncrease = 0
        elseif lastServerQuestCountIncrease > 0 and GetTime() - lastServerQuestCountIncrease < 5 then
            lastServerQuestCountIncrease = 0
        else
            updateQuestCount(totalQuests + 1)
        end
    end

    stats[statId] = (stats[statId] or 0) + math.floor((tonumber(amount) or 0) * 100 + 0.5)
    if characterData then
        characterData.stats = characterData.stats or {}
        characterData.stats[statId] = stats[statId]
    end
    refreshRows()
end

local eventFrame = CreateFrame("Frame")
if RegisterAddonMessagePrefix then
    RegisterAddonMessagePrefix(PREFIX)
end
eventFrame:RegisterEvent("PLAYER_LOGIN")
eventFrame:RegisterEvent("PLAYER_ENTERING_WORLD")
eventFrame:RegisterEvent("PARTY_MEMBERS_CHANGED")
eventFrame:RegisterEvent("RAID_ROSTER_UPDATE")
eventFrame:RegisterEvent("CHAT_MSG_WHISPER")
eventFrame:RegisterEvent("CHAT_MSG_WHISPER_INFORM")
eventFrame:RegisterEvent("CHAT_MSG_ADDON")
eventFrame:RegisterEvent("CHAT_MSG_SYSTEM")
eventFrame:RegisterEvent("COMBAT_LOG_EVENT_UNFILTERED")
eventFrame:SetScript("OnEvent", function(self, event, ...)
    if event == "PLAYER_LOGIN" then
        MobRewardStatsDB = MobRewardStatsDB or {}
        characterData = getCharacterData(UnitName("player"))
        totalKills = tonumber(characterData.totalKills) or 0
        totalQuests = tonumber(characterData.totalQuests) or 0
        characterData.stats = characterData.stats or {}
        for _, entry in ipairs(displayStats) do
            stats[entry[1]] = tonumber(characterData.stats[entry[1]]) or 0
        end
        -- In 3.3.5, GetQuestReward is called when the player confirms a quest
        -- turn-in. QUEST_COMPLETE fires earlier, while the reward window is
        -- merely open, so it would count quests that were never turned in.
        if type(GetQuestReward) == "function" and hooksecurefunc then
            hooksecurefunc("GetQuestReward", recordQuestTurnIn)
        end
        refreshGroupUnits()
        createPanel()
    elseif event == "PLAYER_ENTERING_WORLD" or event == "PARTY_MEMBERS_CHANGED" or event == "RAID_ROSTER_UPDATE" then
        refreshGroupUnits()
        if event == "PLAYER_ENTERING_WORLD" then
            beginServerSync()
        end
    elseif event == "COMBAT_LOG_EVENT_UNFILTERED" then
        onCombatLog(...)
    elseif event == "CHAT_MSG_WHISPER" or event == "CHAT_MSG_WHISPER_INFORM" then
        onWhisper(event, ...)
    elseif event == "CHAT_MSG_ADDON" then
        onAddonMessage(event, ...)
    elseif event == "CHAT_MSG_SYSTEM" then
        onSystemMessage(...)
    end
end)
eventFrame:SetScript("OnUpdate", function(self, elapsed)
    if serverQuestCount ~= nil or questSyncAttempts >= 5 then
        return
    end

    questSyncElapsed = questSyncElapsed + elapsed
    if questSyncElapsed >= 3 then
        questSyncElapsed = 0
        requestServerStats()
        questSyncAttempts = questSyncAttempts + 1
    end
end)
