-- Exact native ownership transitions; disposable no-save inventories only.
if not KCDUS.inWorldRaw() then error('Loaded private world required') end
local p=player:GetWorldPos();local bodies={}
local function spawn(n)
    local name='kcdus_owner_probe_'..n
    if System.GetEntityByName(name) then error('Previous owner probe exists') end
    local e=System.SpawnEntity({class='NPC',name=name,position={x=p.x+2+n,y=p.y,z=p.z}})
    if not e or not e.inventory then error('Disposable inventory spawn failed') end
    bodies[#bodies+1]=e;e:SetFlags(ENTITY_FLAG_NO_SAVE,0);e:DisableBehaviorTreeEvaluation();e:Event_MakeInvulnerable()
    return e
end
local function present(inv,id)
    for _,x in pairs(inv:GetInventoryTable() or {}) do if x==id then return true end end
    return false
end
local a,b=spawn(1),spawn(2)
local first
for _,id in pairs(player.inventory:GetInventoryTable()) do first=ItemManager.GetItem(id);break end
if not first then error('Need a read-only item definition from the private character') end
local id=ItemManager.CreateItem(first.class,0.73,1)
local initial=ItemManager.GetItem(id)
if not initial or not initial.kcdusOwner then error('Native item owner missing') end
a.inventory:AddItem(id)
local inA=ItemManager.GetItem(id)
if not inA or not present(a.inventory,id) then error('First native add did not retain the instance') end
if not KCDUS.NativeIdentity.transferInstance then error('Load the current source native_identity.lua into the private probe first') end
local moved,reason=KCDUS.NativeIdentity.transferInstance(a.inventory,b.inventory,inA.kcdusPersistent)
if not moved then error('Whole-instance primitive refused: '..tostring(reason)) end
local inB=ItemManager.GetItem(id)
if not inB or not present(b.inventory,id) or present(a.inventory,id) then error('Native ownership transition was not exclusive') end
if inA.kcdusPersistent~=inB.kcdusPersistent or inA.health~=inB.health or inA.amount~=inB.amount then error('Native transfer changed instance/condition/count') end
if inA.kcdusOwner==inB.kcdusOwner then error('Native owner readback did not change') end
System.LogAlways('KCDUS|S7|owner-transfer|PASS|exclusive=true|same-instance=true|metadata=true')
for _,e in ipairs(bodies) do System.RemoveEntity(e.id) end
Script.SetTimer(300,function()
    for i=1,2 do if System.GetEntityByName('kcdus_owner_probe_'..i) then error('Disposable holder removal failed') end end
    System.LogAlways('KCDUS|S7|owner-transfer|cleanup|PASS')
end)
