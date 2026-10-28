# Simulator Sickness Questionnaire (SSQ)

**Source:** Kennedy, R. S., Lane, N. E., Berbaum, K. S., & Lilienthal, M. G. (1993). Simulator Sickness Questionnaire: An enhanced method for quantifying simulator sickness. *The International Journal of Aviation Psychology, 3*(3), 203–220.

**When:** 4 times per session. Once before the headset goes on (baseline), and right after each world (Plain, Maze, House), before the other questionnaires. **Time:** about 1 minute.

Give it straight after the headset comes off: symptoms fade within minutes.

---

## Form text

**Participant code:** ______ (filled in by the researcher)
**When:** ○ Before starting ○ After Plain ○ After Maze ○ After House

**How are you feeling right now?**
For each symptom, choose how much it affects you **at this moment**.

| # | Symptom | None | Slight | Moderate | Severe |
|---|---|:-:|:-:|:-:|:-:|
| 1 | General discomfort | ○ | ○ | ○ | ○ |
| 2 | Fatigue (tiredness) | ○ | ○ | ○ | ○ |
| 3 | Headache | ○ | ○ | ○ | ○ |
| 4 | Eye strain | ○ | ○ | ○ | ○ |
| 5 | Difficulty focusing (your eyes) | ○ | ○ | ○ | ○ |
| 6 | Increased salivation (more saliva than usual) | ○ | ○ | ○ | ○ |
| 7 | Sweating | ○ | ○ | ○ | ○ |
| 8 | Nausea (feeling like vomiting) | ○ | ○ | ○ | ○ |
| 9 | Difficulty concentrating | ○ | ○ | ○ | ○ |
| 10 | Fullness of head (heavy or pressured head) | ○ | ○ | ○ | ○ |
| 11 | Blurred vision | ○ | ○ | ○ | ○ |
| 12 | Dizziness with eyes open | ○ | ○ | ○ | ○ |
| 13 | Dizziness with eyes closed | ○ | ○ | ○ | ○ |
| 14 | Vertigo (the room seems to spin) | ○ | ○ | ○ | ○ |
| 15 | Stomach awareness (an uneasy feeling in your stomach) | ○ | ○ | ○ | ○ |
| 16 | Burping | ○ | ○ | ○ | ○ |

The short explanations in brackets aren't part of the original wording. They're there because participants answer in their second language. Mention them in the paper's methods.

**Building it:** Google Forms → *Multiple-choice grid*, rows = the 16 symptoms in this order, columns = None, Slight, Moderate, Severe, "Require a response in each row" on.

---

## Scoring

Code each answer: None = 0, Slight = 1, Moderate = 2, Severe = 3.

Add up the raw scores of the symptoms in each subscale. Some symptoms count in two subscales.

| Subscale | Symptoms (by #) | Weight |
|---|---|---|
| Nausea (N) | 1, 6, 7, 8, 9, 15, 16 | × 9.54 |
| Oculomotor (O) | 1, 2, 3, 4, 5, 9, 11 | × 7.58 |
| Disorientation (D) | 5, 8, 10, 11, 12, 13, 14 | × 13.92 |
| Total Score (TS) | sum of the three raw subscale sums | × 3.74 |

For example, N = (raw sum of items 1, 6, 7, 8, 9, 15, 16) × 9.54, and TS = (raw N + raw O + raw D) × 3.74.

**What to analyse:** the change from baseline (each world's score minus the "Before starting" score), compared between ROVR and Manual. That separates sickness caused by the condition from how people felt when they arrived.

**Safety rule for the session:** if anyone marks **Severe** on any symptom, or says they feel unwell, stop and offer a longer break or to end the session. Write it on the observation sheet.
