-- SPDX-License-Identifier: GPL-3.0-only
-- Visual equipment snapshots. Mutations target disposable peer NPCs only.
local K=KCDUS
K.Outfits={pending={},maxItems=32,maxBytes=1600}
local O=K.Outfits
local function guid(s)
    return type(s)=='string' and #s==36 and string.match(s,'^%x%x%x%x%x%x%x%x%-%x%x%x%x%-%x%x%x%x%-%x%x%x%x%-%x%x%x%x%x%x%x%x%x%x%x%x$')~=nil
end
function O.parse(s)
    if type(s)~='string' or #s>O.maxBytes then return nil end
    local f=K.split(s,';');if f[1]~='1' or #f>O.maxItems+1 then return nil end
    local items,seen={},{}
    for i=2,#f do
        local class,health=string.match(f[i],'^([%x%-]+),([%d%.]+)$');health=tonumber(health)
        if not guid(class) or not health or health<0 or health>1 or seen[class] then return nil end
        seen[class]=true;items[#items+1]={class=class,health=health}
    end
    return items
end
function O.capture(ent)
    local parts={}
    for _,id in pairs(ent.inventory:GetInventoryTable()) do
        local it=ItemManager.GetItem(id)
        if not it or it.kcdusBridge~=1 then return nil end
        if it.kcdusEquipped==1 then
            if not guid(it.class) or type(it.health)~='number' or it.health<0 or it.health>1 then return nil end
            parts[#parts+1]=it.class..','..string.format('%.5f',it.health)
        end
    end
    table.sort(parts);local snapshot='1'..(#parts>0 and ';'..table.concat(parts,';') or '')
    return O.parse(snapshot) and snapshot or nil
end
local function clean_created(ids)
    for _,id in ipairs(ids) do pcall(ItemManager.RemoveItem,id) end
end
function O.apply(ent,snapshot)
    local wanted=O.parse(snapshot);if not wanted or O.capture(ent)==nil then return false end
    if O.capture(ent)==snapshot then return true end
    local staged={}
    -- Check item classes/creation before removing the proxy's default clothing.
    for _,w in ipairs(wanted) do
        local ok,id=pcall(ItemManager.CreateItem,w.class,w.health,1)
        if not ok or not id then clean_created(staged);return false end
        local readable,item=pcall(ItemManager.GetItem,id)
        if readable and item then staged[#staged+1]=id end
        if not readable or not item or item.class~=w.class or item.amount~=1 then clean_created(staged);return false end
    end
    local attached={}
    local ok=pcall(function()
        ent.inventory:RemoveAllItems()
        if next(ent.inventory:GetInventoryTable())~=nil then error('Proxy inventory did not clear') end
        for _,id in ipairs(staged) do
            ent.inventory:AddItem(id);attached[id]=true;ent.actor:EquipInventoryItem(id)
        end
    end)
    if not ok then
        local unowned={};for _,id in ipairs(staged) do if not attached[id] then unowned[#unowned+1]=id end end
        clean_created(unowned);return false
    end
    return O.capture(ent)==snapshot
end
function O.update(ent,g,now)
    local desired=O.pending[g.key]
    if not desired or desired==g.appliedOutfit or now<(g.outfitRetry or 0) or now<(g.animationReadyAt or 0) then return end
    local ok,applied=pcall(O.apply,ent,desired)
    if ok and applied then
        g.appliedOutfit=desired;g.clip=nil;g.animationReadyAt=now+0.5
        K.out('OUTFIT','applied',g.key)
    else
        g.outfitRetry=now+5
        K.out('OUTFIT','failed',g.key)
    end
end
K.handlers.OUT=function(f)
    local key=f[2];if not key or not string.match(key,'^%d+$') or #key>3 or not O.parse(f[3]) then return end
    local n=0;for _ in pairs(O.pending) do n=n+1 end
    if n>=16 and not O.pending[key] then return end
    O.pending[key]=f[3]
end
function O.emit(now)
    local ok,snapshot=pcall(O.capture,player)
    if not ok or not snapshot then return end
    if snapshot~=O.last or now-(O.sentAt or 0)>=5 then
        O.last=snapshot;O.sentAt=now;K.out('OUT',snapshot)
    end
end
K.every(1,'outfits',O.emit)
