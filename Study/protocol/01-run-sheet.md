# Session run sheet

The step-by-step plan for one session, with what the researchers say. The same for ROVR (voice only, plus one button for the microphone) and traditional (controllers only; "Manual" in the build), except where marked. Target: 60–75 minutes. Pilot participants follow it once per condition, on two different days.

**How a session runs:** the participant types their ID in the headset's opening menu. Their ID decides their condition and the order of the three worlds (one of six sequences, [`../assignment-table.csv`](../assignment-table.csv)). The researchers follow along on the bridge's **control page** on the PC, `http://127.0.0.1:8765/control` (see the [README](../../README.md), step 4), which has **Next** (the next world), **Skip objective**, **Restart world** and **Reset** (a new participant). In each world the objectives appear in the headset one after the other, and the clock starts at the participant's first action there. Each world run is saved as its own CSV, for example `Bridge/logs/study/p007-trad-house.csv`.

> **Placeholders:** the build's objectives and practice area are placeholders for now. Replace them, and the world introductions below, before user testing. The placeholder House uses the powder room (by the kitchen) as "the bathroom".

## Roles

| Role | Does |
|---|---|
| **Facilitator** | Talks to the participant: consent, briefing, world introductions, questionnaires, interview. The only person who speaks to them during the worlds |
| **Operator** | Runs the PC: bridge, build, the control page, screen and audio recording. Watches the bridge log for errors |
| **Observer** | Fills in the observation sheet and runs the backup stopwatch. Notes the time of anything worth asking about in the interview |

With four researchers, the fourth can greet the next participant or take over a role between sessions.

## Before the participant arrives (30 min)

- [ ] **ID:** this participant's ID (p001–p030, in order of arrival; pilot: p901–p904). The table gives their condition and world order: note both on the observation sheet. For a pilot, decide today's condition and sequence
- [ ] **Headset:** charged above 80%, lenses cleaned, fresh face cover, cable ready
- [ ] **PC:** Ollama running with the pinned model; bridge started (Wi-Fi or cable) and showing `Speech to text: whisper.cpp on this PC`; one test command answered in under about 1 s
- [ ] **Build:** the study build on the headset, showing the ID menu; the control page open on the PC, showing the headset connected
- [ ] **Test run:** put the headset on yourself and type the participant's ID. In the practice area (nothing is logged there): **ROVR:** press **A** to unmute, say one command, check the caption strip shows it. **Traditional:** move and turn with the sticks. Then press **Reset** on the control page, so the ID menu is back for the participant
- [ ] **ROVR:** the right controller charged. **Traditional:** both controllers paired and charged
- [ ] **Recording:** casting from the Quest to the PC with OBS (or the headset's own recording) tested; audio recorder charged with enough space
- [ ] **Forms:** on the tablet, open with the participant ID filled in; printed copies as backup. Consent form printed twice (one copy for the participant)
- [ ] **Room:** swivel chair in the middle of a clear area, nothing within arm's reach; water and wipes ready
- [ ] **Observation sheet** headed with the ID, condition, sequence and date

## The session

### 1. Arrival, consent and baseline (10–15 min)

1. Welcome them and check the screening answers still hold: *"Are you feeling well today? Have you had any alcohol in the last 24 hours?"* If not, reschedule.
2. Give them the information sheet and consent form. Let them read it, answer questions, collect the signed form, give them a copy.
3. **Briefing (read aloud):**
   > "Today you'll move around three virtual worlds: an open plain, a maze and a house. You'll use [ROVR: only your voice / traditional: only the controllers]. In each world you'll see a few objectives at the top of your view, like reaching a spot or finding rooms. The headset records how long they take; we're testing the navigation method, not you, so there's no need to rush or worry about mistakes.
   > After each world you'll take the headset off and answer a few short questionnaires. That's also your break. At the end we'll have a short conversation about your experience.
   > If you feel unwell at any point, such as dizzy, nauseous or with a headache, tell us straight away and we'll stop. You can stop or withdraw at any time without giving a reason. Any questions?"
4. **Baseline SSQ** on the tablet, before the headset goes on.
5. Seat them on the swivel chair. Fit the headset, adjusting the strap and lens spacing, and check glasses fit if they wear them. Hand over both controllers (traditional), or only the right one (ROVR: its **A** button turns the microphone on and off).
6. **ID:** *"In front of you is a number pad. Point at it with the controller and press the trigger to type your ID: [0, 0, 7], then OK, then Yes."* The headset moves them to the practice area and shows the tutorial card for their condition. The control page shows their ID, condition and world order: check it matches the observation sheet.
   - **Pilot ID (p901–p904):** the headset says the researcher will finish setting up. Operator: in the pilot box on the control page, pick today's condition and sequence and press **Assign**.
   - **"ID not found":** they typed a wrong number. They can press **Back** and type it again.
7. Operator: start the screen and audio recording. Observer: write the start time.

### 2. Practice (about 5 min, not logged)

The tutorial card for their condition is in view (a placeholder for now). Read the instructions for their condition, then ask them to press **A** to close the card. Let them try the practice area (a tree, a rock, a chair and a plant) until they're comfortable, up to about 5 minutes.

**ROVR (voice):**
> "You move by speaking. Say what you want to do in your own words, for example 'move forward five meters', 'turn left', or 'go to the tree'. The microphone starts off: press **A** on the controller to turn it on, and again to turn it off. It also turns off during each break, so press **A** again at the start of each world. The microphone icon at the bottom of your view has a slash through it while it's off, and next to it you'll see what the system heard. Say **'stop'** at any time to stop immediately, or **'go back'** to undo your last move. If something goes wrong, just say it a different way. You can also ask to go faster or slower. Apart from the A button, please don't use the controller."

**Traditional (controller):**
> "You move with the controllers. Push the left stick to move: the further you push it, the faster you go. The right stick turns; the edges of your view darken a little while you turn, which helps with comfort. While you move, please think aloud: say what you're thinking, what you're looking for and what you're about to do, just like talking to yourself."

Holding **A** for just over half a second shows the panel, and holding it again hides it. Participants don't need it; its world buttons don't work during a session. Its **?** button shows the tutorial card again.

**Traditional only:** do the think-aloud warm-up in [`03-think-aloud.md`](03-think-aloud.md) before the practice.

Questions are fine during practice. When they're comfortable, the operator presses **Next**: the first world in their sequence loads.

### 3. Each world, in their order

The control page shows which world is next. Read that world's introduction:

- **Plains:** *"This is an open plain. Your objectives appear at the top of your view. Take your time to read each one."*
- **Maze:** *"In this world you start inside a maze. Your goal is to find the exit, marked by the yellow pad. There's a small map in the corner of your view that you can use."*
- **House:** *"This is a house. Your objectives ask you to visit its rooms in order. They appear at the top of your view."*

Then:

1. **The clock starts by itself** at their first action in that world: their first push on a stick, or when they start speaking. Reading the objective first doesn't count. The control page shows "waiting for first action", then "running" and the time. Nothing before their first action is logged. **ROVR:** they press **A** to unmute first; that press doesn't start the clock.
2. **Objectives follow each other by themselves.** When they reach one (a yellow pad, a room, the maze exit), the banner flashes green and shows the next one, while the clock keeps running. Don't read them out: they're in the headset. There's no time limit.
3. **If they're stuck** or something goes wrong: **Skip objective** moves on to the next one (logged as skipped), and **Restart world** starts the world again from its beginning as a new run (its own CSV, `...-run2.csv`; the first run is kept, marked incomplete). Note either on the observation sheet.
4. **After the last objective** the headset says the world is complete and asks them to take it off. The world's CSV is saved and sent to the PC: it appears under "Saved world logs" on the control page.

### Questionnaires and break (about 5 min)

Headset off. On the tablet, in this order: **SSQ → NASA-TLX → SUS → UEQ-S**, with the world set to the one they just finished. Offer water.

- Check the SSQ: if anything is marked **Severe**, or they say they feel unwell, see "Stopping" below.
- Before the TLX the first time, read: *"For performance, the scale is reversed: the left end means you did perfectly, the right end means you failed."*
- When they're ready and the headset is back on, the operator presses **Next**: the next world in their sequence loads. If the headset was asleep, Next takes effect when it wakes.

After the third world the headset says **All done**.

### 4. Interview and debrief (10–15 min)

1. Run the interview from [`../instruments/06-interview-guide.md`](../instruments/06-interview-guide.md), using the observer's notes.
2. **Debrief (read aloud):**
   > "Thank you, that's everything. This study compares voice navigation with a large language model against standard controller movement, in three different worlds. Some participants used voice and some used the controllers; each person tries only one. *(Pilot: leave out that sentence.)* Your answers and recordings are stored under a code, not your name. Please don't share the details of the tasks with friends who might take part, so their experience is as fresh as yours. Do you have any questions?"
3. **Pilot only:** the pilot feedback form (Part A; on day 2 also Part B). Confirm the date of their second session.

## After the participant leaves

- [ ] Stop the recordings. Name the files `<id>_<condition>_<what>`, for example `p007_trad_screen.mp4`, `p007_trad_interview.m4a`, and move them to the study folder
- [ ] On the control page, check **Saved world logs** lists all three worlds (for example `p007-trad-plains.csv`, `p007-trad-maze.csv`, `p007-trad-house.csv`) and that no files are "still waiting to be sent". They're in `Bridge/logs/study/`; the headset keeps its own copies in `Android/data/com.hxil.ROVR/files/StudyData/`
- [ ] Copy the bridge logs for this session (`Bridge/logs/`), including the session transcript
- [ ] Press **Reset** on the control page: the ID menu is back for the next participant
- [ ] File the consent form separately from the data
- [ ] Clean the headset and controllers; charge them
- [ ] **Pilot:** fill in Part C of the pilot feedback form

If the app crashed or a world was restarted, the participant can type their ID again: they get the same condition and order, and worlds already completed on this headset are skipped.

## Stopping

Stop the session (headset off, sit them down, offer water) if:
- they ask to stop, or say they feel unwell
- they mark **Severe** on any SSQ symptom
- you see clear signs of sickness: pale, sweating, holding their head or stomach

Wait at least 10 minutes. Continue only if they want to and feel fine. Otherwise end the session, thank them, and make sure they feel well before they leave. Record what happened and when on the observation sheet. If they stop mid-world, press **Reset**: that world's CSV is kept, marked incomplete. Their data so far is kept unless they withdraw.
