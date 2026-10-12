# 12. Best of Three as a draw format with an on-demand deciding fight

Status: Accepted

## Context

Categories with exactly two athletes are commonly decided by a **Kampfserie** (first to two wins)
instead of a single Kampf. We needed to decide where this lives in the model, when its fights exist,
and how it interacts with scoring, result correction and re-draws (domain grilling on issue #95).

## Decision

- **Best of Three** is a sixth draw format (`BracketFormat`), valid only for a category with exactly
  two athletes in the individual competition mode. It is not a per-fight or per-bracket option.
- The draw creates the 1st and 2nd Kampf (rounds 1 and 2) up front, because both always take place.
  The **Entscheidungskampf** (round 3) is created only when the Serienstand reaches 1:1, on the same
  Tatami as the 2nd Kampf. There is no "skipped" fight status.
- A result correction that turns 1:1 into 2:0 removes the Entscheidungskampf (confirmation required
  and audited if it already started); one that turns 2:0 into 1:1 creates it.
- If an unlocked Best-of-Three category no longer has exactly two athletes, its draw is discarded
  (fights deleted, draw format cleared) instead of being silently kept.
- Every Kampf of a Kampfserie counts individually for Siegquote and Vereinswertung (ADR-0007
  unchanged).

## Considered Options

- Creating all three fights at draw time and marking the third as skipped: rejected, it would need a
  new fight status that queue, overview, rankings and export all have to understand.
- Creating each fight only after the previous one: rejected, the 2nd Kampf would be invisible in
  planning although it always takes place.
- Best of Three as a setting next to every format (or per fight): rejected, it would change bracket
  progression, Siegquote and club scoring across all formats.

## Consequences

- Placement and the "Final" status depend on a fight that may not exist yet; the Entscheidungskampf
  must be created in the same operation that completes the 2nd Kampf.
- The running-score display is a shared UI building block, reused for the Begegnungsstand of team
  encounters.
