# Edits for the proposal and consent forms

Changes to `_HXIL__ROVR_CS_ST_Proposal_Document-5.pdf` so it matches the study design decided on 28 October 2026 and the instruments in this folder. They're in page order. Each gives the section, the page, a short quote of the current wording to find it, and the replacement text.

The decisions behind them: world order Plain → Maze → House; SSQ before the first world, then SSQ, Raw NASA-TLX, SUS and UEQ-S after each world; one set of eligibility criteria; the House names every object in view; no up/down tasks; think-aloud kept for the controller group; smooth turning and adjustable speed in both conditions; the maze minimap shown in both; seated on a swivel chair; a 4-person pilot, each person doing both conditions.

**Tense:** Chapter 7 is written in the past tense ("The study employed…", "Participants were randomly…"), while §4.4 uses the future. The replacement text below uses the future, as a proposal should. Change the rest of Chapter 7 to match.

---

## Main text

### §1.3 Scope and Limitations (p. 3–4)

1. **World order.** Now: *"an open plain, a house, and a maze."*
   → "an open plain, a maze, and a house."
2. **Instruments.** Now: *"…system usability is evaluated using the System Usability Scale (SUS)."*
   → "…system usability is evaluated using the System Usability Scale (SUS), complemented by the short User Experience Questionnaire (UEQ-S). Simulator sickness is monitored with the Simulator Sickness Questionnaire (SSQ), to check that differences in workload and usability aren't caused by discomfort."
3. **New limitation**, at the end of the paragraph:
   → "In addition, participants in the controller condition will think aloud while navigating. This gives a verbal record comparable to the voice condition's, but may slow them slightly and add to their workload."

### §2.1 Locomotion Modalities in VR (p. 7)

4. **Movement model.** Now: *"both modalities are constrained to identical directional controls: continuous linear translation (moving forward, backward, or vertically) and discrete angular rotation (shifting the view vector in fixed 90-degree increments)."*
   → "both modalities are constrained to the same movement model: continuous translation along the ground at one of three fixed speeds (slow, normal and fast), and smooth rotation about the user's head, with a comfort vignette that darkens the edges of the view while turning."
5. **Cybersickness.** Now: *"While cybersickness is not evaluated as a formal dependent variable in our metric set, its underlying physiological triggers…"*
   → "While cybersickness is not a primary dependent variable in our metric set, it is monitored with the Simulator Sickness Questionnaire (SSQ) as a control measure (Section 7.1.3), and its underlying physiological triggers…"
6. **Comfort measures.** Now: *"…enforces constant movement speeds, and utilizes fixed discrete snap-turns rather than unconstrained continuous yaw or pitch rotation."*
   → "…moves at constant speeds that the user can switch between, and turns only about the vertical axis, smoothly and with a comfort vignette, rather than through unconstrained yaw and pitch rotation."

### §2.5 LLMs for VR Navigation (p. 19)

7. **World order.** Now: *"across an open plain, a house, and a maze"* → "across an open plain, a maze, and a house".
8. **Measures.** Now: *"…interface usability via SUS, intentionally excluding cybersickness evaluations due to our restricted translation constraints, while adding…"*
   → "…interface usability via SUS and UEQ-S, and monitor cybersickness with the SSQ as a control measure, while adding…"

### §4.4 Experimental Design (p. 32)

9. **World order.** Now: *"(an open plain, a house, and a maze)"* → "(an open plain, a maze, and a house)".
10. **Participants.** Now: *"A baseline goal of 30 participants will be recruited from a pool of participants above the age of 18."*
    → "Thirty participants will be recruited from undergraduate students of De La Salle University aged 18 or older, and randomly assigned in balanced blocks to the two conditions, 15 per condition."
    Then, after the English-proficiency sentence, add: "Participants must also have no known language-processing difficulty and no severe uncorrected visual impairment (Section 7.1.1)."
11. **Questionnaires.** Now: *"the NASA Task Load Index (NASA-TLX) and the System Usability Scale (SUS) will be administered via digital questionnaires"*
    → "the Simulator Sickness Questionnaire (SSQ) will be administered before the first environment, and the SSQ, the raw NASA Task Load Index (NASA-TLX), the System Usability Scale (SUS) and the short User Experience Questionnaire (UEQ-S) after each environment, via digital questionnaires"

### §6.5 Interaction Walkthrough (p. 62)

12. **World order.** Now: *"an open plain, a house, and a maze (Section 4.4)"* → "an open plain, a maze, and a house (Section 4.4)".
13. **Between subjects.** Now: *"…first under the LLM-driven modality and again under traditional controller-based locomotion…"*. This says every participant uses both, which contradicts the between-subjects design.
    → "…under either the LLM-driven modality or traditional controller-based locomotion, as each participant is assigned to one (Section 7.1)…"

### §7.1 Experimental Protocol (p. 63)

14. **Tense and assignment.** Now: *"The study employed a rigorous between-subjects design… Participants were randomly and equally allocated…"*
    → "The study will employ a between-subjects design… Participants will be randomly assigned, in balanced blocks, to one of two conditions (15 each):"
15. **Control group.** Now: *"(thumbstick-driven locomotion and snap/smooth turning)"*
    → "(thumbstick-driven continuous movement and smooth turning with a comfort vignette, at the same three selectable speeds as ROVR)"
16. **New sentence** after the two conditions:
    → "In both conditions, participants will be seated on a swivel chair throughout, so they can turn their body without walking."

### §7.1.1 Participant Recruitment (p. 64)

17. **Criteria.** Now: *"…being at least 18 years of age and presenting no known linguistic processing difficulties, or severe uncorrected visual deficits."*
    → "Participants must be at least 18 years old, be undergraduate students of De La Salle University, have used a VR headset before, be comfortable holding a conversation in English, and have no known language-processing difficulty or severe uncorrected visual impairment. For safety, people with a history of seizures, or with a balance, inner-ear or heart condition, will not be included. Eligibility will be checked with a short screening questionnaire before scheduling, which also records VR, gaming and voice-assistant experience so the two groups can be compared."

### §7.1.2 Experimental Design and Tasks (p. 64–65)

18. **New opening sentence:**
    → "Before the first environment, participants will practise their assigned modality in a separate practice area, untimed, for about five minutes."
19. **Plain, no vertical tasks.** Now: *"…executing discrete lateral translations (move left, move right), vertical adjustments (move up, move down), and directional orientation shifts (turn around)."*
    → "…executing discrete lateral translations (move left, move right) and directional orientation shifts (turn around), each ending at a marked target. Vertical movement is not included, in keeping with grounded embodiment (Section 5.5.5)."
20. **Maze minimap**, after the Maze description:
    → "In both conditions, a minimap in the corner of the view shows the maze, the participant's position and the exit."
21. **House metadata.** Now: *"Bounded strictly by core spatial metadata abstractions (doors, walls, and chairs)"*
    → "Bounded by generic labels for the objects in the participant's current view (for example door, chair, sofa or fridge)"
    And for the rooms: "…sequentially visit, in a fixed order, the living area, bathroom, kitchen and bedroom." Name which bathroom: the ensuite off the bedroom, or the powder room by the kitchen.
22. **Speed**, after the rest-period paragraph:
    → "In both conditions, participants can switch between three walking speeds: by voice in ROVR, and with buttons on the control panel in the controller condition. Every speed change is logged."

### §7.1.3 Evaluation Metrics and Instruments (p. 65–66)

23. **NASA-TLX**, add to the end of the bullet:
    → "The raw (unweighted) NASA-TLX will be used: the average of the six subscale ratings, each from 0 to 100, without the pairwise weighting (Hart, 2006). It will be administered after each environment."
24. **SUS.** Now: *"This metric was deployed post-test…"*
    → "It will be administered after each environment. To fit both conditions, 'this system' is worded as 'this navigation method', and item 8 reads 'cumbersome (awkward)' for non-native English speakers (Finstad, 2006)."
25. **New bullet, UEQ-S**, after the SUS:
    → "**User Experience (UEQ-S):** The eight-item short User Experience Questionnaire (Schrepp et al., 2017) will complement the SUS after each environment. It measures pragmatic quality (supportive, easy, efficient, clear) and hedonic quality (exciting, interesting, inventive, leading edge), capturing aspects of the experience the SUS doesn't."
26. **SSQ.** Now: *"…administered before and after each session…"*
    → "…administered before the first environment, as a baseline, and after each environment, and analysed as the change from baseline…"
27. **Task Completion Time**, add to the end of the bullet:
    → "The same logs will record every speed change and, in the ROVR condition, every voice command and the system's response."

### §7.1.4 Think-Aloud Protocol (p. 66)

28. **New closing sentence:**
    → "Because thinking aloud may slow participants and add to their workload, its possible effect on the control group's Task Completion Time and NASA-TLX scores will be discussed as a limitation."

### §7.1.5 Data Analysis Plan (p. 66)

29. Now: *"Quantitative measures (SUS, NASA-TLX subscales, SSQ scores, and TCT) will be compared between the control and experimental groups…"*
    → "Quantitative measures (SUS, UEQ-S pragmatic and hedonic quality, raw NASA-TLX and its subscales, SSQ change from baseline, and TCT) will be compared between the control and experimental groups for each environment…"

### New §7.1.6 Pilot Study (after §7.1.5)

30. Add this subsection. The proposal doesn't describe the pilot in the Gantt chart (Table 4.1) anywhere else:

> **7.1.6 Pilot Study**
>
> Before the user study, a pilot study will be conducted with four participants who meet the same inclusion criteria. Unlike the main study, each pilot participant will complete the full protocol under both conditions, on two different days, with the order counterbalanced: two will start with ROVR and two with the controller. The pilot will check the session length, the clarity of the instructions and questionnaires, whether every task can be completed in both conditions, and the accuracy of the automatic logging against a stopwatch. Pilot participants will also complete a short feedback form about the session. Pilot data will be used only to refine the protocol and will not be included in the main analysis, and pilot participants will not take part in the main study.

---

## Appendices

### A.1 Ethics Review Form (p. 69–70)

31. **Data collection** (p. 69). Now: *"…participants complete the NASA Task Load Index (NASA-TLX), the System Usability Scale (SUS), and the Simulator Sickness Questionnaire (SSQ) via digital questionnaires administered after each of the three virtual environments."*
    → "…participants complete the Simulator Sickness Questionnaire (SSQ) before the first environment, and the SSQ, the raw NASA Task Load Index (NASA-TLX), the System Usability Scale (SUS) and the short User Experience Questionnaire (UEQ-S) via digital questionnaires after each of the three virtual environments. A screening questionnaire before scheduling checks eligibility."
32. **Procedure** (p. 70). Change the world order to *"(an open plain, a maze, and a house)"*, and *"the NASA-TLX, SUS, and SSQ questionnaires after each environment"* to "the SSQ, NASA-TLX, SUS and UEQ-S after each environment". Then add:
    → "Participants will be seated on a swivel chair throughout. A pilot study with four participants, each completing both conditions on two different days, will precede the user study to test the procedure."
33. **Potential risks** (p. 70). Now: *"There are no reasonably foreseeable risks or adverse effects associated with participation in the WoZ workshop and User study."* The next sentences go on to describe motion sickness, eye strain and disorientation, and so do §4.5 and the consent forms.
    → "Participation involves minimal risk."
    Then add, after the existing mitigation sentences: "People with a history of seizures, or with a balance, inner-ear or heart condition, are screened out before scheduling, and a session stops if a participant reports a severe symptom on the SSQ or asks to stop."

### A.2.1 Checklist A (p. 83)

34. The participant information here repeats the consent form's sections A and B. Apply edits 40 and 41 here too.

### A.3.1 User Study Protocol (p. 88–92)

35. **Eligibility** (p. 88). Now: *"…because you meet the eligibility criteria of being 18 years or above."*
    → "…because you meet the eligibility criteria: you are at least 18 years old, an undergraduate student of De La Salle University, have used a VR headset before, are comfortable holding a conversation in English, and have no known language-processing difficulty or severe uncorrected visual impairment."
36. **Briefing and setup** (1.2, p. 88), add:
    → "Before putting on the headset, you will answer a short questionnaire about how you feel (the Simulator Sickness Questionnaire). You will stay seated on a swivel chair throughout."
37. **World order** (2.3, p. 89). Swap the House and Maze bullets so the list reads Open Plain, Maze, House.
38. **Questionnaires** (2.5, p. 89). Now: *"User Experience Questionnaire – to assess perceived usability of your assigned navigation modality."*
    → "System Usability Scale (SUS) and short User Experience Questionnaire (UEQ-S): to assess the usability and experience of your navigation method." Also put the SSQ first in the list, since it's given first.
    Also update **Processing and Storing Your Data** (p. 90): *"NASA-TLX and SUS questionnaire answers, SSQ responses"* → "screening answers, SSQ, NASA-TLX, SUS and UEQ-S responses".
    And add a **pilot paragraph** to the Procedure: "If you take part in the pilot study, you will do this procedure twice, once with each navigation method, on two different days, and will fill in a short feedback form about the session at the end of each day."
39. **Certificate of consent** (p. 92). It says *"the ten statements below"* but lists eleven: the statement about personal data being kept securely appears twice, worded slightly differently. Delete the second one. Change *"I confirm that I meet the eligibility criteria (18 or above)."* to "I confirm that I meet the eligibility criteria described above."

### A.4 Informed Consent Form (p. 101–105)

40. **A. Introduction** (p. 102). World order → "(an open plain, a maze, and a house)".
41. **B. Participant selection** (p. 102). Replace the two criteria with the full list from edit 35. Then add:
    → "The session takes about 60 to 75 minutes. You will be randomly assigned to one of two navigation methods, voice or controller, and will use only that one. If you take part in the pilot study, you will use both methods, in two sessions on different days."
42. **D. Risks** (p. 102), add:
    → "You will be seated on a swivel chair throughout, and we will stop the session if you report feeling unwell."
43. **E. Processing and storing data** (p. 103). Now: *"Your responses to the NASA-TLX and SUS questionnaires, along with video recordings of your navigation tasks, will be collected…"*
    → "Your screening answers; your responses to the SSQ, NASA-TLX, SUS and UEQ-S questionnaires; the task times and interaction logs recorded by the system, including what you say to it; video recordings of your navigation tasks; and audio recordings of your voice commands or think-aloud and of the interview will be collected…"
44. **Section letters** (p. 103). After E, four sections are all lettered **F** (Data breach, Your rights, Usage of your data, Contact information). Re-letter them F, G, H and I.
45. **Certificate** (p. 104). Now: *"I agree to take part in the discussion which includes audio and video recording."*
    → "I agree to audio and video recording of the navigation tasks, of my voice commands or think-aloud, and of the interview."
46. **Two different consent certificates.** A.3.1 (eleven statements, including the copyright assignment) and A.4 (eight statements) don't match. Check with your adviser which one the ethics committee expects, and use that one for every participant, pilot included.
