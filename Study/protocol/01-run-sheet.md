# Session run sheet

The step-by-step plan for one session, with what the researchers say. The same for ROVR (voice only) and Manual (controller only), except where marked. Target: 60–75 minutes. Pilot participants follow it once per condition, on two different days.

The exact researcher controls (setting the participant code and condition, starting, skipping and restarting tasks) come from the study build. Check the build's README once the study-ready work lands. So far: **F5**, or `start` on the bridge, starts a task's timer, and `setup` on the bridge sets the participant code and condition.

## Roles

| Role | Does |
|---|---|
| **Facilitator** | Talks to the participant: consent, briefing, task instructions, questionnaires, interview. The only person who speaks to them during tasks |
| **Operator** | Runs the PC: bridge, build, participant setup, starting each task, screen and audio recording. Watches the bridge log for errors |
| **Observer** | Fills in the observation sheet and runs the backup stopwatch. Notes the time of anything worth asking about in the interview |

With four researchers, the fourth can greet the next participant or take over a role between sessions.

## Before the participant arrives (30 min)

- [ ] **Assignment sheet:** this participant's code and condition (P01–P30 in balanced random blocks; pilot: PP1–PP4)
- [ ] **Headset:** charged above 80%, lenses cleaned, fresh face cover, cable ready
- [ ] **PC:** Ollama running with the pinned model; bridge started (Wi-Fi or cable) and showing `Speech to text: whisper.cpp on this PC`; one test command answered in under about 1 s
- [ ] **Build:** the study build on the headset; participant code and condition entered; the practice area loads
- [ ] **ROVR:** microphone unmuted and tested. **Manual:** both controllers paired and charged
- [ ] **Recording:** casting from the Quest to the PC with OBS (or the headset's own recording) tested; audio recorder charged with enough space
- [ ] **Forms:** on the tablet, open with the participant code filled in; printed copies as backup. Consent form printed twice (one copy for the participant)
- [ ] **Room:** swivel chair in the middle of a clear area, nothing within arm's reach; water and wipes ready
- [ ] **Observation sheet** headed with the code, condition and date

## The session

### 1. Arrival, consent and baseline (10–15 min)

1. Welcome them and check the screening answers still hold: *"Are you feeling well today? Have you had any alcohol in the last 24 hours?"* If not, reschedule.
2. Give them the information sheet and consent form. Let them read it, answer questions, collect the signed form, give them a copy.
3. **Briefing (read aloud):**
   > "Today you'll move around three virtual worlds: an open plain, a maze and a house. You'll use [ROVR: only your voice / Manual: only the controllers]. In each world we'll give you a few tasks, like reaching a spot or finding rooms. The headset records how long each task takes; we're testing the navigation method, not you, so there's no need to rush or worry about mistakes.
   > After each world you'll take the headset off and answer a few short questionnaires. That's also your break. At the end we'll have a short conversation about your experience.
   > If you feel unwell at any point, such as dizzy, nauseous or with a headache, tell us straight away and we'll stop. You can stop or withdraw at any time without giving a reason. Any questions?"
4. **Baseline SSQ** on the tablet, before the headset goes on.
5. Seat them on the swivel chair. Fit the headset, adjusting the strap and lens spacing, and check glasses fit if they wear them. Hand over the controllers (Manual), or none (ROVR).
6. Operator: start the screen and audio recording. Observer: write the start time.

### 2. Practice (about 5 min, not timed)

Read the instructions for their condition, then let them try the practice tasks until they're comfortable, up to about 5 minutes.

**ROVR (voice):**
> "You move by speaking. Say what you want to do in your own words, for example 'move forward five meters', 'turn left', or 'go to the door'. You'll see what the system heard at the bottom of your view. Say **'stop'** at any time to stop immediately. If something goes wrong, just say it a different way. You can also ask to go faster or slower. Please don't use your hands."

**Manual (controller):**
> "You move with the controllers. Push the left stick to move, and the right stick to turn; the edges of your view darken a little while you turn, which helps with comfort. You can change speed with the Slow, Normal and Fast buttons on the panel. While you move, please think aloud: say what you're thinking, what you're looking for and what you're about to do, just like talking to yourself."

**Manual only:** do the think-aloud warm-up in [`03-think-aloud.md`](03-think-aloud.md) before the practice.

Questions are fine during practice. From the first world on, the facilitator only gives the task instructions.

### 3. Plain world

1. Operator moves them to the Plain. Facilitator: *"This is the first world. I'll tell you each task; the timer starts when I say 'go'."*
2. For each task: read the instruction from the build's task list (for example *"Move left until you reach the marker"*), say "go", and the operator starts the timer (F5). The task ends by itself when the goal is reached, or at the time limit.
3. After the last task: *"That's the end of this world. You can take the headset off now."*

### Questionnaires and break (about 5 min)

Headset off. On the tablet, in this order: **SSQ → NASA-TLX → SUS → UEQ-S**, with the world set to "Plain". Offer water.

- Check the SSQ: if anything is marked **Severe**, or they say they feel unwell, see "Stopping" below.
- Before the TLX the first time, read: *"For performance, the scale is reversed: the left end means you did perfectly, the right end means you failed."*

### 4. Maze

*"In this world you start inside a maze. Your goal is to find the exit, marked by the yellow pad. There's a small map in the corner of your view that you can use."* Then "go", and the operator starts the timer. Afterwards: the same questionnaires (world = Maze), and the break.

### 5. House

*"This is a house. Please visit these rooms in this order: [room order from the build, e.g. the living room, the bathroom, the kitchen and the bedroom]. I'll tell you when you've reached each one."* Then "go", and the operator starts the timer. Afterwards: the same questionnaires (world = House).

> **To confirm in the build:** which room counts as "the bathroom" (the ensuite off the bedroom, or the powder room by the kitchen), and the room order.

### 6. Interview and debrief (10–15 min)

1. Run the interview from [`../instruments/06-interview-guide.md`](../instruments/06-interview-guide.md), using the observer's notes.
2. **Debrief (read aloud):**
   > "Thank you, that's everything. This study compares voice navigation with a large language model against standard controller movement, across worlds of increasing complexity. Some participants used voice and some used the controllers; each person tries only one. *(Pilot: leave out that sentence.)* Your answers and recordings are stored under a code, not your name. Please don't share the details of the tasks with friends who might take part, so their experience is as fresh as yours. Do you have any questions?"
3. **Pilot only:** the pilot feedback form (Part A; on day 2 also Part B). Confirm the date of their second session.

## After the participant leaves

- [ ] Stop the recordings. Name the files `<code>_<condition>_<what>`, for example `P07_ROVR_screen.mp4`, `P07_ROVR_interview.m4a`, and move them to the study folder
- [ ] Check the task CSV and session transcript exist with the right code and condition. Note anything odd on the observation sheet
- [ ] Copy the bridge logs for this session (`Bridge/logs/`)
- [ ] File the consent form separately from the data
- [ ] Clean the headset and controllers; charge them
- [ ] **Pilot:** fill in Part C of the pilot feedback form

## Stopping

Stop the session (headset off, sit them down, offer water) if:
- they ask to stop, or say they feel unwell
- they mark **Severe** on any SSQ symptom
- you see clear signs of sickness: pale, sweating, holding their head or stomach

Wait at least 10 minutes. Continue only if they want to and feel fine. Otherwise end the session, thank them, and make sure they feel well before they leave. Record what happened and when on the observation sheet. Their data so far is kept unless they withdraw.
