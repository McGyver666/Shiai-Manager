# 13. Freikampf as a fight kind without a category

Status: Accepted

## Context

A **Freikampf** is a spontaneous Kampf added at the Mattentisch outside the official schedule
(issue #96). It must run through the existing match control (clock, Osae-komi, scoring, display)
but must never count towards Vereinswertung, rankings, medals or brackets, nor affect any category.
Until now every Kampf belonged to a category, which also carried its match and Golden Score
durations, and participants and winner were athlete references only.

## Decision

- A Kampf has a **fight kind**: tournament fight or Freikampf. A Freikampf has **no category**; the
  category reference becomes optional for Freikämpfe only.
- A Freikampf carries its own match duration and Golden Score setting (enabled + duration).
- Each side is either a free-text name or an athlete of the Turnier; the winner of a Freikampf is
  recorded per side (White/Blue), not as an athlete reference. Hikiwake is allowed and is the
  operator's decision: since a Freikampf never counts, the server does not check clock or score
  before accepting it.
- A Freikampf belongs to the tatami it was created on: it cannot be moved to another tatami, and
  deleting the tatami deletes its waiting Freikämpfe.
- Every consumer that evaluates fights (rankings, Vereinswertung, medals, Kampfübersicht, match
  lists, guest share, overview statistics, bracket progression, category lock) excludes Freikämpfe;
  queries over official results use one shared filter for this.

## Considered Options

- A hidden technical category per Freikampf (as the Team-Matchday weight-class categories do):
  rejected, every category consumer (category management, draw, registrations, rankings, medals,
  match lists) would have to filter it out, and a forgotten filter silently pollutes official
  results.
- A separate Freikampf table: rejected, match control (clock, Osae-komi, scoring, display, SignalR)
  would have to be duplicated or re-abstracted.

## Consequences

- Making the category optional lets the compiler flag every place that assumes a category, which is
  the main guard against Freikämpfe leaking into official results.
- The match clock reads durations from the Freikampf itself when it has no category.
- Backup/restore must round-trip Freikämpfe, including their free-text names and per-side winner.
