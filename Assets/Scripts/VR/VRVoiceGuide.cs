using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRArmory
{
    // Every spoken instruction. The clip for a line is Assets/Audio/Voice/VO_<name>.wav and its words are
    // VRTrainingText.Spoken(line). Lines are grouped by where they are used: Intro (panel), Asm (assembly steps),
    // FC (function check), Sgt (the soldier's briefing in the Intro scene). A line ending in _A / _X names the safety button of the hand holding the pistol.
    public enum VoiceLine
    {
        None,
        Intro_ChooseModule, Intro_Briefing,
        Common_WellDone, Common_Good,
        Asm_Pick_Barrel, Asm_Place_Barrel, Asm_Pick_Spring, Asm_Place_Spring,
        Asm_Pick_Slide, Asm_Place_Slide, Asm_Pick_Magazine, Asm_Place_Magazine,
        Asm_TryAgain, Asm_Complete,
        FC_Intro, FC_PickUpAgain,
        FC_SafetyOn_A, FC_SafetyOn_X,
        FC_TriggerDemo_Safe, FC_YourTurn_Trigger, FC_SafeOk, FC_LetGo,
        FC_SafetyOff_A, FC_SafetyOff_X,
        FC_TriggerDemo_Fire,
        FC_HammerDropped, FC_KeepHolding, FC_SlideDemo, FC_YourTurn_Rack, FC_HoldWhileRacking, FC_ReleasedTooEarly,
        FC_ReleaseDemo, FC_YourTurn_Release,
        FC_ResetClick, FC_TriggerDemo_Last, FC_YourTurn_Last,
        FC_Passed, Training_Complete,
        // Intro scene: Sgt. Forrest Gump's briefing (appended last: the values are saved as numbers in the scenes).
        Sgt_Welcome, Sgt_WhyItMatters, Sgt_Simulator, Sgt_ControlsIntro,
        Sgt_Grip, Sgt_Trigger, Sgt_Thumbsticks, Sgt_PrimaryButton, Sgt_SecondaryButton, Sgt_Ready,
        // Fire range: the sergeant's live-fire training.
        Rng_Offer, Rng_NotReady, Rng_Handover, Rng_Magazine, Rng_Load, Rng_FireWhenReady, Rng_CeaseFire, Rng_Declined,
        // Unlimited mode, first empty magazine: _B / _Y names the release button of the hand holding the pistol.
        Rng_Reload_B, Rng_Reload_Y,
        // Weapon cards, and the TEC-9's own assembly steps and function check.
        Intro_ChooseWeapon,
        Tec9_Pick_Bolt, Tec9_Place_Bolt, Tec9_Pick_Spring, Tec9_Place_Spring, Tec9_Pick_Upper, Tec9_Place_Upper,
        Tec9_Pick_Pin, Tec9_Place_Pin, Tec9_Pick_Magazine, Tec9_Place_Magazine,
        Tec9_FC_TriggerDemo_First, Tec9_FC_BoltDemo, Tec9_FC_YourTurn_Rack, Tec9_FC_HoldWhileRacking, Tec9_FC_ReleasedTooEarly,
        Rng_Tec9_Magazine, Rng_Tec9_Load, Rng_Tec9_FireWhenReady, Rng_Tec9_Reload_B, Rng_Tec9_Reload_Y,
        Tec9_Briefing,
    }

    [Serializable]
    public class VoiceClip
    {
        public VoiceLine line;
        public AudioClip clip;
    }

    // The VR instructor's voice: plays recorded lines one at a time from its own AudioSource, so two lines can never
    // overlap and the other sounds of the project (clicks, chimes, the gun) are not touched. Play() replaces the
    // instructions still to come; an instruction that is cut short fades out instead of clicking. Short confirmations
    // ("Well done.") are always heard whole, so praise is never chopped off by the next instruction.
    // A line without a clip (not recorded yet, or muted) is "spoken" silently for about as long as it would take,
    // with a warning, so the training and the ghost demonstrations keep the same rhythm.
    [RequireComponent(typeof(AudioSource))]
    public class VRVoiceGuide : MonoBehaviour
    {
        public VoiceClip[] clips = new VoiceClip[0];
        [Range(0f, 1f)] public float volume = 1f;
        [Tooltip("Pause between two lines of one instruction, so each sentence has time to sink in.")]
        public float gap = 0.55f;
        [Tooltip("A line cut short by a newer one fades out over this time.")]
        public float fadeOutTime = 0.15f;
        public bool muted;

        // The line being spoken right now (None between lines).
        public VoiceLine Current { get; private set; }
        public bool IsBusy => Current != VoiceLine.None || queue.Count > 0;
        public event Action<VoiceLine> LineStarted;

        private AudioSource source;
        private readonly List<VoiceLine> queue = new List<VoiceLine>();
        private float silentUntil = -1f; // end of a line spoken without a clip
        private float nextLineTime;
        private bool fading;
        private readonly HashSet<VoiceLine> warnedMissing = new HashSet<VoiceLine>();

        void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;     // the instructor is "in your ear", not somewhere in the room
            source.priority = 0;          // never culled for other sounds
            source.bypassReverbZones = true;
        }

        // Speaks these lines in order. Instructions still waiting are out of date and dropped; confirmations still
        // waiting are kept (and not doubled), so the trainee always hears "Well done." before the next instruction.
        public void Play(params VoiceLine[] lines)
        {
            ClearQueue();
            foreach (VoiceLine l in lines)
            {
                if (l == VoiceLine.None || (LetsFinish(l) && queue.Contains(l))) continue;
                queue.Add(l);
            }
            if (Current != VoiceLine.None && !LetsFinish(Current)) CutCurrent();
        }

        // Lets the current line finish and drops the instructions still waiting (confirmations are still heard).
        public void ClearQueue()
        {
            for (int i = queue.Count - 1; i >= 0; i--)
            {
                if (!LetsFinish(queue[i])) queue.RemoveAt(i);
            }
        }

        // Silence now (fades the current line out).
        public void Stop()
        {
            queue.Clear();
            if (Current != VoiceLine.None) CutCurrent();
        }

        public AudioClip ClipFor(VoiceLine line)
        {
            foreach (VoiceClip c in clips)
            {
                if (c != null && c.line == line) return c.clip;
            }
            return null;
        }

        // Confirmations are short and should be heard whole; instructions may be replaced by a newer one.
        public static bool LetsFinish(VoiceLine line)
        {
            switch (line)
            {
                case VoiceLine.Common_WellDone:
                case VoiceLine.Common_Good:
                case VoiceLine.Asm_Complete:
                case VoiceLine.FC_SafeOk:
                case VoiceLine.FC_HammerDropped:
                case VoiceLine.FC_ResetClick:
                case VoiceLine.FC_Passed:
                    return true;
                default:
                    return false;
            }
        }

        void Update()
        {
            if (fading)
            {
                source.volume = Mathf.MoveTowards(source.volume, 0f, volume * Time.deltaTime / fadeOutTime);
                if (source.volume > 0f) return;
                source.Stop();
                fading = false;
                EndCurrent(0.25f);
            }

            if (Current != VoiceLine.None)
            {
                bool speaking = silentUntil >= 0f ? Time.time < silentUntil : source.isPlaying;
                if (speaking) return;
                EndCurrent(gap);
            }

            if (queue.Count > 0 && Time.time >= nextLineTime) StartLine();
        }

        private void StartLine()
        {
            VoiceLine line = queue[0];
            queue.RemoveAt(0);
            Current = line;
            AudioClip clip = ClipFor(line);
            if (clip != null && !muted)
            {
                silentUntil = -1f;
                source.clip = clip;
                source.volume = volume;
                source.Play();
            }
            else
            {
                if (clip == null && warnedMissing.Add(line))
                    Debug.LogWarning($"[VR Armory] Voice line {line} has no clip (Assets/Audio/Voice/VO_{line}.wav); continuing silently.");
                silentUntil = Time.time + (clip != null ? clip.length : EstimatedSeconds(line));
            }
            LineStarted?.Invoke(line);
        }

        private void CutCurrent()
        {
            if (silentUntil >= 0f) EndCurrent(0.25f);
            else fading = true;
        }

        private void EndCurrent(float pause)
        {
            Current = VoiceLine.None;
            silentUntil = -1f;
            nextLineTime = Time.time + pause;
        }

        // About 150 words a minute, the calm pace of the recordings.
        private static float EstimatedSeconds(VoiceLine line)
        {
            string words = VRTrainingText.Spoken(line);
            return 0.6f + (string.IsNullOrEmpty(words) ? 0 : words.Split(' ').Length) * 0.4f;
        }
    }
}
