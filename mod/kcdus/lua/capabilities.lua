-- SPDX-License-Identifier: GPL-3.0-only
-- Read-only candidate discovery. A callable binding is NOT proof that it works.
local K = KCDUS
local candidates = {
    soul = { 'GetStatLevel', 'SetStatLevel', 'SetStatLevelDebug', 'GetSkillLevel', 'SetSkillLevel', 'SetSkillLevelDebug',
             'GetSkillXP', 'GetPerks', 'HasPerk', 'AddPerk', 'RemovePerk', 'GetState', 'SetState', 'DealDamage' },
    actor = { 'GetCurrentAnimationState', 'ChangeAnimGraph', 'SetAnimationInput', 'EquipInventoryItem', 'UnequipInventoryItem', 'GetEquippedItems' },
    human = { 'PlayAnim', 'SetAnimMotionParam', 'DrawWeapon' },
    inventory = { 'GetInventoryTable', 'GetCountOfClass', 'RemoveAllItems', 'RemoveItem', 'AddItem' },
}
function K.capabilities()
    K.out('CAPABILITY', 'begin', 'unproven', K.VERSION or 'unknown')
    if not player then K.out('CAPABILITY', 'player', 'unproven', 'not-loaded'); return end
    for _, group in ipairs({ 'soul', 'actor', 'human', 'inventory' }) do
        for _, method in ipairs(candidates[group]) do
            local ok, value = pcall(function() return player[group] and player[group][method] end)
            local state = (ok and type(value) == 'function') and 'candidate-present' or 'candidate-unavailable'
            K.out('CAPABILITY', group .. '.' .. method, 'unproven', state)
        end
    end
    K.out('CAPABILITY', 'end', 'unproven', 'runtime-effects-and-persistence-not-tested')
end
K.handlers['CAPABILITIES'] = function() K.capabilities() end
