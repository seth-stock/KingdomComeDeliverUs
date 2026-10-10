-- Actual native stat XP application/readback in an owned disposable world.
if not KCDUS.inWorldRaw() then error('Loaded private world required') end
local function latest()
    local r=player.human:PlayAnim('@kcdus/xp-last','')
    if type(r)~='table' then error('Private XP observer unavailable') end
    return r
end
local function capture()
    player.human:PlayAnim('@kcdus/state-capture','')
    local s=ItemManager.GetItem(player.soul:GetId())
    if type(s)~='table' or not s.kcdusStatProgressRaw or not s.kcdusSkillProgressRaw then error('Native progression read unavailable') end
    return s
end
local before=capture();local previous=latest()
local kind=KCDUS_XP_PROBE_SKILL and 1 or 0
if kind==1 then player.soul:AddSkillXP('reading',1) else player.soul:AddStatXP('str',1) end
local attempt=0
local function verify()
    attempt=attempt+1
    local r=latest()
    if r.sequence<=previous.sequence then
        if attempt>=20 then error('XP effect never observed') end
        Script.SetTimer(100,verify);return
    end
    local after=capture();local offset=r.stat*16+1
    local field=kind==1 and 'kcdusSkillProgressRaw' or 'kcdusStatProgressRaw'
    if r.sequence~=previous.sequence+1 or r.kind~=kind or r.applied~=1 or r.verified~=1 or
       r.soulIdentity~=before.kcdusPersistent or
       r.before~=before[field]:sub(offset,offset+15) or
       r.after~=after[field]:sub(offset,offset+15) or r.before==r.after then
        error('Applied XP disagrees with native progression')
    end
    System.LogAlways('KCDUS|S8|xp-apply|PASS|kind='..kind..'|stat='..r.stat..'|source='..string.format('%x',r.sourceVtableRva)..'|cause='..string.format('%x',r.causeVtableRva)..
        '|before='..r.before..'|after='..r.after..'|requested='..r.requestedRaw..'|actual='..r.actualRaw)
    -- A progression query is not an XP award and must not create a receipt.
    player.soul:GetStatLevel('str');capture()
    if latest().sequence~=r.sequence then error('Read-only query fabricated an XP event') end
    System.LogAlways('KCDUS|S8|xp-query|PASS')
end
Script.SetTimer(100,verify)
