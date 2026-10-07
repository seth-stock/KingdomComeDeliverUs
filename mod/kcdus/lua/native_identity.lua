-- Read-only persistent RPG identities from the supported startup adapter.
-- Entity IDs and soul WUIDs are handles; neither is safe to send as world identity.
local K=KCDUS
K.NativeIdentity={ actors={} }
local N=K.NativeIdentity
local function valid(id)
    return type(id)=='string' and #id==32 and not string.find(id,'[^0-9a-f]')
        and id~='00000000000000000000000000000000'
end
function N.soul(entity)
    if not entity or not entity.soul then return nil end
    local ok,value=pcall(function() return ItemManager.GetItem(entity.soul:GetId()) end)
    if ok and type(value)=='table' and value.kcdusSoul==1 and valid(value.kcdusPersistent) then
        return value.kcdusPersistent
    end
    return nil
end
function N.item(handle)
    local ok,value=pcall(ItemManager.GetItem,handle)
    if ok and type(value)=='table' and value.kcdusBridge==1 and valid(value.kcdusPersistent) then
        return value.kcdusPersistent,value
    end
    return nil
end
function N.clear() N.actors={} end
function N.findActor(identity)
    if not valid(identity) then return nil end
    local cached=N.actors[identity]
    local entity=cached and System.GetEntity(cached)
    -- Even a valid cached handle must not hide a newly duplicated identity.
    if entity and N.soul(entity)~=identity then entity=nil end
    N.actors[identity]=nil
    local ok,entities=pcall(System.GetEntitiesByClass,'NPC')
    if not ok or type(entities)~='table' then return nil end
    local found
    for _,e in pairs(entities) do
        if N.soul(e)==identity then
            if found then return nil end -- duplicated identities cannot be mutated safely
            found=e
        end
    end
    if found then N.actors[identity]=found.id end
    return found
end
function N.findItem(inventory,identity)
    if not valid(identity) or not inventory then return nil end
    local ok,items=pcall(function() return inventory:GetInventoryTable() end)
    if not ok or type(items)~='table' then return nil end
    local found
    for _,handle in pairs(items) do
        if N.item(handle)==identity then
            if found then return nil end
            found=handle
        end
    end
    return found
end
