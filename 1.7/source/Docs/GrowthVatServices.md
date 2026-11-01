# Growth vat services (RimWorld 1.7 only)

Two Biotech services, `SettlementService_GrowthVatEmbryo` and `SettlementService_GrowthVatChild`, grow an embryo or a young colonist inside the settlement's own vats. They exist only under `1.7/` (defs in `1.7/Biotech/Defs/SettlementServiceDefs/GeneticServices.xml`, code in `Services/Genetics/GrowthVat*.cs`). Translation keys live in the shared `Languages/English/Keyed/genetics.xml`.

This note records the framework exceptions these workers rely on, so other workers do not copy them by accident.

## Timing exception: biologically driven completion

Most workers complete when the frozen quote ETA is reached. Growth vat jobs do not.

- `SettlementServiceWorker.CompletesAtExpectedTick` returns `false` for both workers, so `SettlementServiceJobScheduler` never completes them from `expectedCompletionTick`.
- `SettlementServiceWorker.IsReadyToComplete(ctx)` gates every completion entry point (`TickAll`, `TryCompleteActiveJobNow`). Both workers return `true` only after `GrowthVatJobState.targetReached` is set. A duration-changing event or the dev "Complete now" button therefore cannot finish a job early.
- `expectedCompletionTick` is informational. `GrowthVatSimulation` refreshes it on every tick through `SettlementServicesWorldComponent.SetJobExpectedCompletionTick` from the occupant's current vat speed.
- `Complete` is idempotent: it does nothing once `GrowthVatJobState.completed` is set, and it fails (never silently finishes) if the target was not reached.
- Both defs set `eventChancePct` to 0 so generic medical events cannot alter vat timing or the newborn.

Progress is computed from `GrowthVatJobState.lastProcessedTick`, not from the scheduler's nominal 250-tick step, so a missed or delayed scheduler call is caught up (up to four days per call).

## Simulation

`GrowthVatSimulation.Advance` reproduces `Building_GrowthVat.Tick` without a spawned building:

- Embryo gestation consumes `Building_GrowthVat.EmbryoGestationTicks` (540000) at 6 nutrition/day. The embryo stays an item in item custody until birth.
- Growth then calls `Pawn_AgeTracker.Notify_TickedInGrowthVat(RoundToInt(20 * GrowthVatOccupantSpeed) * gameTicks)` and adds `gameTicks - 1` to `vatGrowTicks` so the per-tick counter matches vanilla. Birthdays, growth moments, life stage changes and aging injuries therefore run through vanilla code.
- The step is sized so the pawn stops on the first game tick that reaches the chosen age. It never ages to a scheduler boundary and truncates back.
- Growth points (2 per day, from the real `GrowthPointsPerDay`) and the `VatLearning` hediff are advanced by hand because the pawn does not tick in custody.
- Nutrition used is `consumedNutrition` in the job state: 6/day during gestation, 3/day afterwards.

## Virtual vat custody

Vanilla checks `pawn.ParentHolder is Building_GrowthVat` for the biological-age multiplier (1 instead of storyteller child aging), growth points and the `VatLearning`/`VatGrowing` hediffs. To keep those checks true without a map, the world component owns a `ThingOwner<Thing> growthVatCustody` holding unspawned `Building_GrowthVat` instances, one per active growth job.

- The vat is never spawned, powered or ticked. It only holds the occupant in its `innerContainer`.
- `TryEnterGrowthVat` removes the pawn from `WorldPawns`, places it in the vat, and adds both vat hediffs. `ReleaseGrowthVat` reverses this: it strips the hediffs, parks the pawn back in `WorldPawns` (`TargetCustodyService.ParkPawnInWorld`), banks any non-pawn contents in item custody, and destroys the empty vat. It is safe to call repeatedly.
- Everything is mutated through `SettlementServicesWorldComponent` accessors (`SettlementServicesWorldComponent.GrowthVat.cs`). The vat and its occupant are saved by the world component and exposed in `GetChildHolders`. On load, `ReconcileGrowthVats` delivers the contents of any vat no job references back home.
- The job's `GrowthVatJobState` (target age, gestation ticks left, last processed tick, nutrition ledger, birth/stillbirth/reached/completed/refund flags, vat reference) is saved on `ServiceJobRecord.growthVat`.

### Exits

`SettlementServiceWorker.OnJobTerminating` runs before `FailJob` and `HandleSettlementDestroyed` handle an Active job. Both workers use it (and their own `Cancel`) to release the vat first, so normal collection, home delivery and recovery code never sees a pawn still held by an unspawned building.

- Before birth: the embryo is still the job target in item custody, so every exit returns it.
- Birth: `CommitGrowthVatBirth` runs once, atomically. It places the newborn in the vat, replaces the job's target with the newborn (a pawn target, not an item), clears the embryo from item custody and destroys it. `birthCommitted` blocks a second birth. If anything fails before the commit, the embryo is untouched and the job fails with the embryo returned.
- Stillbirth: the corpse becomes the job target (an item), growth ends and the unused post-birth nutrition is refunded.
- Because the newborn replaces the embryo target, the existing pawn return path (`ServicePawnCaravanReturn`) delivers it, and load reconciliation never mistakes a consumed embryo for a lost target.

## Birth

`GrowthVatBirthService` follows `PregnancyUtility.ApplyBirthOutcome` for a vat (birth quality 0.7, `babiesAreHealthy` honored through the ChildBirth outcome worker) but does not call it: that method needs a spawned or caravan-held birther and would discard the newborn from off-map custody. Newborn generation (outcome selection, genes, xenotype, parent relations, infant illness or stillborn hediff) lives in `EmbryoBirthGeneration`, which the surrogacy service also uses (see `SurrogacyService.md`). The vat code then commits the birth and sends the vanilla vat birth letter. Parents that no longer exist are ignored and a last name is generated.

## Staged nutrition accounting

- `GrowthVatInputPlanning` builds a `SettlementStock_Food` requirement for the projected nutrition and allocates whole items. The same cached allocation feeds the quote line item (`BuildPassThroughLineItems`) and `PlanInputs`.
- `SettlementServiceWorker.BuildPassThroughLineItems` is a new pricing hook: its items are added to the quote after the negotiator and compatibility discounts, so food is always paid in full. The facility fee stays discountable.
- At `Start` the paid nutrition (from the job's reservations) and its value (the quote's nutrition line) are stored as `prepaidNutrition` and `prepaidValue`, with `facilityFee` as the remainder.
- Each simulation step compares consumed plus projected remaining nutrition with what was prepaid. If a changed occupant speed makes the prepaid amount insufficient, the job fails safely: growth stops, the occupant is returned at its current age and the unused part is refunded. If less nutrition is needed (or the job ends early), the difference is refunded.
- `ServiceCompletionResult.WithRefund` lets a worker ask for a refund on completion. `SettlementServiceWorker.TerminationRefundFor` replaces the default whole-quote refund for failures and settlement destruction, and the workers' `Cancel` returns an explicit refund line. On early termination the refund is the unused nutrition value plus the facility fee scaled by unfinished progress. A `refundIssued` flag keeps it idempotent.
- `CaravanSilverPaymentProvider.Refund` now falls back to the home colony when the paying caravan no longer exists, instead of dropping the silver.

## Item targets

`TargetCustodyService.ValidateTargets` now checks that a non-pawn target is in the caravan's actual inventory (`CaravanInventoryUtility.AllInventoryItems`), matching the target picker, instead of assuming the caravan directly owns it. `TryDetachItem` removes the exact `Thing` from its real holder and restores it if item custody refuses it. `SettlementServiceWorker.ShouldListTarget` lets a worker keep irrelevant inventory items out of the picker.
