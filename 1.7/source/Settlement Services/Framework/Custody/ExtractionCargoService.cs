using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Settlement_Services.Framework.Workers;

namespace Settlement_Services.Framework.Custody
{
    internal enum ExtractionCargoSlot
    {
        Inventory,
        Worn,
        Equipped,
    }

    internal sealed class ExtractionCargoItem
    {
        public Thing thing;
        public ExtractionCargoSlot slot;
        public bool wasLocked;
        public bool wasForced;
    }

    internal static class ExtractionCargoService
    {
        internal static bool TryBank(ServiceJobContext ctx, Pawn donor, List<ExtractionCargoItem> banked, out string errorKey)
        {
            errorKey = null;

            var pending = new List<ExtractionCargoItem>();
            if (donor.equipment != null)
                pending.AddRange(donor.equipment.AllEquipmentListForReading.Select(e => new ExtractionCargoItem { thing = e, slot = ExtractionCargoSlot.Equipped }));
            if (donor.apparel != null)
            {
                pending.AddRange(donor.apparel.WornApparel.Select(a => new ExtractionCargoItem
                {
                    thing = a,
                    slot = ExtractionCargoSlot.Worn,
                    wasLocked = donor.apparel.IsLocked(a),
                    wasForced = donor.outfits?.forcedHandler?.IsForced(a) ?? false,
                }));
            }
            if (donor.inventory != null)
                pending.AddRange(donor.inventory.innerContainer.Select(i => new ExtractionCargoItem { thing = i, slot = ExtractionCargoSlot.Inventory }));

            foreach (ExtractionCargoItem entry in pending)
            {
                if (ctx.Domain.TryBankHeldItemAsJobResult(ctx.Job.jobId, entry.thing))
                {
                    banked.Add(entry);
                    continue;
                }

                errorKey = "SettlementServices.Error.ExtractionCargoCouldNotBeSecured";
                SupportLog.Error($"Job {ctx.Job.jobId}: could not secure {entry.thing} carried by {donor} before gene extraction.");
                ReapplyApparelState(donor, entry);
                RestoreToDonor(ctx, donor, banked);
                return false;
            }

            return true;
        }

        internal static void Settle(ServiceJobContext ctx, Pawn donor, List<ExtractionCargoItem> banked)
        {
            if (banked.Count == 0) return;

            if (!donor.Dead) RestoreToDonor(ctx, donor, banked);
            else DeliverToWaitingCaravan(ctx, donor, banked);
        }

        internal static void RestoreToDonor(ServiceJobContext ctx, Pawn donor, List<ExtractionCargoItem> banked)
        {
            if (donor.inventory == null) return;

            ThingOwner inventory = donor.inventory.innerContainer;
            foreach (ExtractionCargoItem entry in banked.ToList())
            {
                if (!ctx.Domain.TryReleaseJobResultTo(ctx.Job.jobId, entry.thing, inventory))
                {
                    SupportLog.Error($"Job {ctx.Job.jobId}: could not hand {entry.thing} back to {donor}; it stays collectible from the job.");
                    continue;
                }

                banked.Remove(entry);
                if (entry.slot == ExtractionCargoSlot.Worn && entry.thing is Apparel apparel) TryWearFromInventory(donor, inventory, apparel, entry);
                else if (entry.slot == ExtractionCargoSlot.Equipped && entry.thing is ThingWithComps equipment) TryEquipFromInventory(donor, inventory, equipment);
            }
        }

        private static void TryWearFromInventory(Pawn donor, ThingOwner inventory, Apparel apparel, ExtractionCargoItem entry)
        {
            if (donor.apparel == null || !donor.apparel.CanWearWithoutDroppingAnything(apparel.def)) return;
            if (!inventory.Remove(apparel)) return;

            donor.apparel.Wear(apparel, dropReplacedApparel: false, locked: entry.wasLocked);
            if (donor.apparel.WornApparel.Contains(apparel))
            {
                ReapplyApparelState(donor, entry);
                return;
            }

            if (!inventory.TryAdd(apparel, canMergeWithExistingStacks: false) || !inventory.Contains(apparel))
                SupportLog.Error($"{donor} could not re-wear {apparel} and it could not be put back in their inventory.");
        }

        private static void TryEquipFromInventory(Pawn donor, ThingOwner inventory, ThingWithComps equipment)
        {
            if (donor.equipment == null || equipment.GetComp<CompEquippable>() == null) return;
            if (equipment.def.equipmentType == EquipmentType.Primary && donor.equipment.Primary != null) return;
            if (!inventory.Remove(equipment)) return;

            donor.equipment.AddEquipment(equipment);
            if (donor.equipment.Contains(equipment)) return;

            if (!inventory.TryAdd(equipment, canMergeWithExistingStacks: false) || !inventory.Contains(equipment))
                SupportLog.Error($"{donor} could not re-equip {equipment} and it could not be put back in their inventory.");
        }

        private static void ReapplyApparelState(Pawn donor, ExtractionCargoItem entry)
        {
            if (entry.slot != ExtractionCargoSlot.Worn || !(entry.thing is Apparel apparel) || donor.apparel == null || !donor.apparel.WornApparel.Contains(apparel)) return;

            if (entry.wasLocked) donor.apparel.Lock(apparel);
            if (entry.wasForced) donor.outfits?.forcedHandler?.SetForced(apparel, true);
        }

        private static void DeliverToWaitingCaravan(ServiceJobContext ctx, Pawn donor, List<ExtractionCargoItem> banked)
        {
            Caravan caravan = FindSafeRequesterCaravan(ctx);
            if (caravan == null) return;

            List<Pawn> carriers = caravan.PawnsListForReading
                .Where(p => p != donor && !p.Dead && !p.Destroyed && MassUtility.CanEverCarryAnything(p))
                .ToList();
            if (carriers.Count == 0) return;

            bool movedAny = false;
            foreach (ExtractionCargoItem entry in banked.ToList())
            {
                Pawn carrier = CaravanInventoryUtility.FindPawnToMoveInventoryTo(entry.thing, carriers, null, donor);
                if (carrier == null) break;

                if (ctx.Domain.TryReleaseJobResultTo(ctx.Job.jobId, entry.thing, carrier.inventory.innerContainer))
                {
                    banked.Remove(entry);
                    movedAny = true;
                }
            }

            if (movedAny) caravan.RecacheInventory();
        }

        private static Caravan FindSafeRequesterCaravan(ServiceJobContext ctx)
        {
            int caravanId = ctx.Job.requesterCaravanId;
            if (caravanId < 0) return null;

            Settlement settlement = ctx.ResolveSettlement();
            if (settlement == null) return null;

            Caravan caravan = Find.WorldObjects.Caravans.FirstOrDefault(c => c.ID == caravanId);
            return ServicePawnCaravanReturn.IsWaitingPlayerCaravan(caravan) && caravan.Tile == settlement.Tile ? caravan : null;
        }
    }
}
