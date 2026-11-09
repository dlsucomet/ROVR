# ROVR study instruments

Drafts of the forms for the pilot (December 2026) and the user study (December 2026 – January 2027). They follow the proposal's method (§4.4, §7.1), the team's decisions of 28 October 2026, and the thesis adviser's November changes: counterbalanced world order and a CSV log per world run. Each file has the participant-facing wording, notes for building it as an online form, and how to score it.

| File | What it is | Who fills it in |
|---|---|---|
| [`instruments/01-screening.md`](instruments/01-screening.md) | Screening and demographics: eligibility, VR and voice-assistant experience, health | Participant, before scheduling |
| [`instruments/02-ssq.md`](instruments/02-ssq.md) | Simulator Sickness Questionnaire (16 symptoms) | Participant, before the first world and after each world |
| [`instruments/03-nasa-tlx.md`](instruments/03-nasa-tlx.md) | Raw NASA-TLX (6 workload scales) | Participant, after each world |
| [`instruments/04-sus.md`](instruments/04-sus.md) | System Usability Scale (10 items) | Participant, after each world |
| [`instruments/05-ueq-s.md`](instruments/05-ueq-s.md) | User Experience Questionnaire, short version (8 items) | Participant, after each world |
| [`instruments/06-interview-guide.md`](instruments/06-interview-guide.md) | Semi-structured interview | Researcher, after the last world |
| [`instruments/07-pilot-feedback.md`](instruments/07-pilot-feedback.md) | Feedback on the session itself, plus a researcher checklist | Pilot participants (parts A, B) and researchers (part C) |

For the researchers running the sessions:

| File | What it is |
|---|---|
| [`protocol/01-run-sheet.md`](protocol/01-run-sheet.md) | Roles, setup checklist, the step-by-step session with the words to say, what to do afterwards, and when to stop |
| [`protocol/02-observation-sheet.md`](protocol/02-observation-sheet.md) | Filled in by the observer during each session: outcomes, stopwatch times, hesitations, errors, discomfort, interview follow-ups |
| [`protocol/03-think-aloud.md`](protocol/03-think-aloud.md) | Think-aloud instructions, warm-up and reminders for the traditional (controller) group |
| [`assignment-table.csv`](assignment-table.csv) | Each participant ID's condition and world sequence (the same table the headset uses) |
| [`proposal-edits.md`](proposal-edits.md) | Edits that bring the proposal and consent forms in line with these decisions, in page order, plus the November update for the world sequences and logging |

## One session

The same for both conditions, ROVR (voice only) and traditional (controller only; "Manual" in the build). The target is 60–75 minutes. The three worlds come in one of six orders, set by the participant's ID:

| Sequence | Order |
|---|---|
| seq1 | plains → maze → house |
| seq2 | plains → house → maze |
| seq3 | maze → plains → house |
| seq4 | maze → house → plains |
| seq5 | house → maze → plains |
| seq6 | house → plains → maze |

| Step | Time | What happens |
|---|---|---|
| Before the day | — | Screening form online. If eligible: give the next participant ID (p001–p030, in order; pilot p901–p904), then schedule. The ID decides the condition and sequence ([`assignment-table.csv`](assignment-table.csv)) |
| 1. Arrival | 10–15 min | Consent form, briefing, **SSQ** (baseline, before putting the headset on), headset fit, seated on a swivel chair. The participant types their ID in the headset's menu |
| 2. Practice | about 5 min | The practice area: learn the assigned condition. Not logged |
| 3. World 1 | | Objectives back to back in the headset; the clock and the world's CSV start at the first action |
| Questionnaires | about 5 min | Headset off: **SSQ → NASA-TLX → SUS → UEQ-S** (40 items). This doubles as the break |
| 4. World 2 | | The same |
| Questionnaires | about 5 min | SSQ → NASA-TLX → SUS → UEQ-S |
| 5. World 3 | | The same |
| Questionnaires | about 5 min | SSQ → NASA-TLX → SUS → UEQ-S |
| 6. Interview | 10–15 min | Interview guide, audio recorded. Then the debrief |

The SSQ comes first in each round because symptoms fade within minutes of taking the headset off. The traditional group thinks aloud during the worlds (§7.1.4), and that audio is recorded too. Each world run is saved as its own CSV, for example `p007-trad-house.csv` (see the run sheet).

In total a participant answers 136 questionnaire items: 16 at the start and 40 after each world. The pilot should time this.

**Pilot:** each pilot participant does the whole session twice, once per condition, on two different days, with the same ID both days (p901–p904). Their ID isn't in the table: the researcher picks the condition and sequence on the control page each day. p901 and p903 start with ROVR, p902 and p904 with traditional. At the end of each day they also fill in the pilot feedback form (Part A on day 1, Parts A and B on day 2). Pilot participants don't take part in the main study.

## Building the forms

- Every form starts with the **participant ID** (p007), filled in by the researcher, plus the **world** for the forms given after each world. Never ask for a name on these forms.
- Contact details from the screening form go in a separate sheet, kept apart from the responses (§4.5).
- Make every item required, so nothing is skipped by accident.
- Keep the item order exactly as written: the SUS and the SSQ rely on it for scoring.
- Have printed copies ready in case the tablet or the network fails.
