# Surrogacy service (RimWorld 1.7 only)

`SettlementService_Surrogacy` takes one fertilized human embryo to a newborn through an anonymous, off-map surrogate. It exists only under `1.7/` (def in `1.7/Biotech/Defs/SettlementServiceDefs/GeneticServices.xml`, code in `Services/Genetics/Surrogacy*.cs`). Its translation keys live in `1.7/Biotech/Languages/English/Keyed/surrogacy.xml`, not in the shared root `Languages/` folder, which 1.6 also loads.

## Rules

- **Gate**: Biotech active, children allowed in the difficulty settings, a non-hostile settlement whose faction is at least `TechLevel.Industrial`. No `AdvancedGenetics` capability or specialty is required. `requiredTechLevel` is a minimum, so Spacer and higher factions qualify too.
- **Input**: exactly one whole `HumanEmbryo` with a gene set and no pending `implantTarget`, still in the caravan at quote, acceptance and start. In person only; `allowRemoteRequest` is off, so a remote order can never consume an embryo from home storage. `GrowthVatEmbryoServiceWorker` shares these checks through `EmbryoEligibility`.
- **Price**: `minimumCost` 500 silver, `wealthScale` 0.015. These are starting values for playtesting. There is no food, medicine or surrogate input and no separate stock requirement.
- **Duration**: the race's `gestationPeriodDays` (18 days for a human) times `GenDate.TicksPerDay`, supplied through `ExpectedDurationTicksFor`. That hook bypasses specialty and tier duration discounts, so biological gestation is never shortened. The value is frozen in the accepted quote and `SettlementServiceJobScheduler` completes the job at `expectedCompletionTick`. No pregnancy is simulated while it runs.
- **Endpoint**: birth. There are no age milestones; use the growth vat service to raise the child afterwards.

## Birth

No implantation roll is made, because an anonymous surrogate has no fertility stat. At the due tick `Complete` generates the birth through `EmbryoBirthGeneration`, the core shared with the growth vat. It selects the vanilla `ChildBirth` outcome at a fixed quality of 0.7, so the `babiesAreHealthy` difficulty setting always yields the healthy outcome. It then generates a newborn from the embryo's exact gene set, applies the genetic parent relations and the inherited xenotype, and adds infant illness or the stillborn hediff as needed. No `ParentBirth` relation is added.

Possible outcomes:

- **Healthy baby**: a player-faction newborn.
- **Infant illness**: the newborn carries the `InfantIllness` hediff.
- **Stillbirth**: the corpse is kept as the recoverable target.

A baby-naming letter that says surrogacy is sent once. The Quest manager is notified with the baby as its own birther.

## Saved state and commit

`SurrogacyJobState` is saved as the optional `ServiceJobRecord.surrogacy`. It holds the start and due ticks, `birthCommitted`, `stillborn`, `birthNotificationSent`, `completed` and `refundIssued`. Old saves load it as null, so `SchemaVersion` is unchanged.

`SettlementServicesWorldComponent.CommitSurrogacyBirth` is the only place the birth is applied. In order, it:

1. Checks the embryo is still a job target.
2. Secures the replacement. A live newborn is parked in the world through `TargetCustodyService.ParkPawnInWorld`; a corpse goes into item custody.
3. Swaps the job target from the embryo to the newborn or corpse.
4. Releases the embryo from custody.
5. Sets `birthCommitted`.
6. Destroys the embryo.

If anything fails before the swap, the temporary pawn is discarded and the embryo stays in custody. After the commit, a repeated `Complete` never rolls another outcome. The letter is guarded by `birthNotificationSent`.

## Returning the result

The newborn replaces the embryo as a pawn target, so `ServicePawnCaravanReturn` merges it into a waiting caravan, or makes a recovery caravan, with no custom delivery. If the return cannot be verified, the job stays `AwaitingCollection` and home delivery covers a missing settlement. A stillborn corpse is an item target and always goes through collection.

## Refunds and cancellation

| Situation | Fee |
| --- | --- |
| Job cancelled while still Reserved (never started) | Full |
| Player cancels before birth | Unused part, from elapsed and quoted ticks |
| Failure before birth (hostility, missing embryo, missing state, birth error, settlement destroyed, reconciliation) | Full, through `TerminationRefundFor` |
| After a birth commits | None; the fee is spent |

`TakeSurrogacyRefund` pays at most once. The embryo goes back through the normal custody return, collection or home delivery path.

## Tick safety

`Tick` fails the job with a full refund if its state or embryo disappears, rather than leaving a corrupted job active. `IsReadyToComplete` and `CompletesAtExpectedTick` keep the framework defaults.
