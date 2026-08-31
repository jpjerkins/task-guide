# CONTEXT-INDEX — read this instead of all 122 KB

`CONTEXT.md` is the ubiquitous language for `task-guide`. At ~122 KB it burns a large slice of a
subagent's window before it writes a line, so **read the entries your task actually needs**, by line
range:

```sh
sed -n '894,1032p' CONTEXT.md    # the Firing entry
```

**Decisions** — the things you would otherwise violate — live in [`docs/adr/`](docs/adr/README.md).
Read those first; they are short. This file points at the *vocabulary*.

---

## By what you are building

| Building… | Read these entries | ADR |
|---|---|---|
| **Matching** | Matching rule, Tag, Dimension, Status, Availability Window | 0004, 0007 |
| **Ranking / Scarcity** | Scarcity, Opportunities, Urgency band, Duration as tiebreak, Deadline, Drift | 0004 |
| **Recurrence + DST** | Recurrence, Offset, Availability Window, Day boundary | 0005 |
| **Staleness** | Status, Deadline, Recurrence, Drift | 0007 |
| **Duration snapping** | Dimension, Tag, Task | 0007 |
| **The tick loop** | Firing, Fire record, Day boundary, Snooze, Liveness | 0005 |
| **Notifications / Pushover** | Notification, Receipt, Snooze, Firing, Glance | 0005 |
| **Storage + startup** | Application bootstrap, Task, Override, Event exception, Fire record, Backup, Dimension | 0001, 0008, 0009, 0010 |
| **Schedule authoring** | Availability Window, Day template, Pattern, Override, Event, Event exception | — |
| **Derived obligations** | Derived-obligation rule, Offset, Event, Rules generally | — |
| **Any UI surface** | Glance, Status, Drift + the SPA README | 0006, 0002 |
| **Deployment / ops** | Backup, Liveness | 0003 |

## Every entry, by line range

### Glossary — `45–1709`

| Entry | Lines | Size |
|---|---|---|
| **Task** | `47–66` | 1.5 KB |
| **Status** | `67–140` | 5.2 KB |
| **Deadline** | `141–156` | 0.9 KB |
| **Defer** | `157–204` | 2.5 KB |
| **Postpone** | `205–262` | 4.1 KB |
| **Offset** | `263–280` | 1.0 KB |
| **Recurrence** | `281–338` | 3.1 KB |
| **Dimension** | `339–482` | 9.0 KB |
| **Tag** | `483–564` | 5.5 KB |
| **Drift** | `565–597` | 2.0 KB |
| **Availability Window** | `598–643` | 2.8 KB |
| **Day template** | `644–694` | 3.2 KB |
| **Pattern** | `695–720` | 1.7 KB |
| **Override** | `721–788` | 5.1 KB |
| **Event** | `789–841` | 3.7 KB |
| **Event exception** | `842–872` | 1.8 KB |
| **Day boundary** | `873–893` | 1.3 KB |
| **Firing** | `894–1032` | 7.9 KB |
| **Liveness** | `1033–1107` | 4.8 KB |
| **Backup** | `1108–1192` | 5.3 KB |
| **Fire record** | `1193–1241` | 2.9 KB |
| **Notification** | `1242–1318` | 4.4 KB |
| **Receipt** | `1319–1367` | 3.1 KB |
| **Glance** | `1368–1444` | 4.4 KB |
| **Snooze** | `1445–1574` | 7.6 KB |
| **Matching rule** | `1575–1586` | 0.7 KB |
| **Derived-obligation rule** | `1587–1700` | 6.1 KB |
| **Rules generally** | `1701–1709` | 0.5 KB |

### Ranking — `1710–1870`

| Entry | Lines | Size |
|---|---|---|
| **Urgency band** | `1723–1747` | 1.3 KB |
| **Duration as tiebreak** | `1748–1759` | 0.6 KB |
| **Opportunities** | `1760–1768` | 0.5 KB |
| **Scarcity** | `1769–1870` | 7.0 KB |

### Capture — `1871–1967`

| Entry | Lines | Size |
|---|---|---|
| **Application bootstrap** | `1968–1976` | 0.7 KB |

---

## Vocabulary discipline

Use the term as `CONTEXT.md` defines it. The glossary avoids some synonyms **on purpose**:

- **Opportunities** is the count; **Scarcity** is the rule that ranks on it. One word for both reads
  backwards — *"Scarcity 18"* sounds abundant and means the opposite.
- **Availability** was rejected for the count because it collides with **Availability Window**, which
  is the thing being counted.
- There is no `Overdue` state, no `Deferred` status, and no priority field. If you need one, you are
  contradicting ADR-0004 or ADR-0007.

If a concept you need is not in the glossary, that is a signal: either you are inventing language the
project does not use, or there is a real gap worth a ticket.
