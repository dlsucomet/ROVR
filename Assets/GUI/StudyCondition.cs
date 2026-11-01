// Assets/GUI/StudyCondition.cs
// The two conditions of the user study. Each participant uses one for the whole session:
//   ROVR   = voice only (the LLM pipeline), the right controller held only for its A button.
//   Manual = controllers only (ManualLocomotion); voice commands are off, as these participants think aloud.
public enum StudyCondition { ROVR, Manual }
