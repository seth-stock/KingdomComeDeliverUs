// SPDX-License-Identifier: GPL-3.0-only
namespace KcdUs.Agent.Worlds;

/// <summary>Logical archive identities prevent a reused physical slot from
/// becoming somebody else's home world. This migration is additive for old registries.</summary>
public static class SlotBindings
{
    public static void Archive(WorldRegistry registry, SlotLeaseManager.Lease lease)
    {
        foreach (var world in registry.Worlds)
        {
            if (world.Playline == lease.Playline)
            {
                world.ArchivedLeaseId = lease.Id; world.ArchivedWasSlot = world.Slot; world.Playline = -1; world.Slot = false;
                if (registry.Active == world.Id) registry.Active = "";
            }
            if (world.HomePlayline == lease.Playline) { world.HomeLeaseId = lease.Id; world.HomePlayline = -1; }
        }
        if (registry.HomePlayline == lease.Playline) { registry.HomeLeaseId = lease.Id; registry.HomePlayline = -1; }
    }
    public static void Restore(WorldRegistry registry, SlotLeaseManager.Lease lease)
    {
        foreach (var world in registry.Worlds)
        {
            if (world.ArchivedLeaseId == lease.Id)
            { world.Playline = lease.Playline; world.ArchivedLeaseId = ""; world.Slot = world.ArchivedWasSlot; }
            if (world.HomeLeaseId == lease.Id) { world.HomePlayline = lease.Playline; world.HomeLeaseId = ""; }
        }
        if (registry.HomeLeaseId == lease.Id) { registry.HomePlayline = lease.Playline; registry.HomeLeaseId = ""; }
    }
}
