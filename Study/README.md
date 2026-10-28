# ROVR study instruments

Drafts of the forms for the pilot (December 2026) and the user study (December 2026 – January 2027). They follow the proposal's method (§4.4, §7.1) and the team's decisions of 28 October 2026. Each file has the participant-facing wording, notes for building it as an online form, and how to score it.

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
| [`protocol/03-think-aloud.md`](protocol/03-think-aloud.md) | Think-aloud instructions, warm-up and reminders for the Manual group |
| [`proposal-edits.md`](proposal-edits.md) | 46 edits that bring the proposal and consent forms in line with these decisions, in page order |

## One session

The same for both conditions, ROVR (voice only) and Manual (controller only). The target is 60–75 minutes.

| Step | Time | What happens |
|---|---|---|
| Before the day | — | Screening form online. If eligible: give a participant code (P01–P30, pilot PP1–PP4) and a condition from the assignment sheet, then schedule |
| 1. Arrival | 10–15 min | Consent form, briefing, headset fit, seated on a swivel chair. **SSQ** (baseline, before putting the headset on) |
| 2. Practice | about 5 min | The practice area: learn the assigned condition. Not timed |
| 3. Plain | | Tasks, timed by Unity |
| Questionnaires | about 5 min | Headset off: **SSQ → NASA-TLX → SUS → UEQ-S** (40 items). This doubles as the break |
| 4. Maze | | Tasks, timed by Unity |
| Questionnaires | about 5 min | SSQ → NASA-TLX → SUS → UEQ-S |
| 5. House | | Tasks, timed by Unity |
| Questionnaires | about 5 min | SSQ → NASA-TLX → SUS → UEQ-S |
| 6. Interview | 10–15 min | Interview guide, audio recorded. Then the debrief |

The SSQ comes first in each round because symptoms fade within minutes of taking the headset off. The Manual group thinks aloud during the tasks (§7.1.4), and that audio is recorded too.

In total a participant answers 136 questionnaire items: 16 at the start and 40 after each world. The pilot should time this.

**Pilot:** each pilot participant does the whole session twice, once per condition, on two different days. PP1 and PP3 start with ROVR, PP2 and PP4 with Manual. At the end of each day they also fill in the pilot feedback form (Part A on day 1, Parts A and B on day 2). Pilot participants don't take part in the main study.

## Building the forms

- Every form starts with the **participant code**, filled in by the researcher, plus the **world** for the forms given after each world. Never ask for a name on these forms.
- Contact details from the screening form go in a separate sheet, kept apart from the responses (§4.5).
- Make every item required, so nothing is skipped by accident.
- Keep the item order exactly as written: the SUS and the SSQ rely on it for scoring.
- Have printed copies ready in case the tablet or the network fails.
