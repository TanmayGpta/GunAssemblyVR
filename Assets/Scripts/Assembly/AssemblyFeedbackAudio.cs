using UnityEngine;

namespace VRArmory
{
    // Sound for assembly results: a metallic clack when a part seats, a low buzz when a placement is rejected.
    // Played at the socket's position so in VR the sound comes from the gun. Works in desktop and VR mode.
    [RequireComponent(typeof(AudioSource))]
    public class AssemblyFeedbackAudio : MonoBehaviour
    {
        public GunAssemblyManager manager;
        [Range(0f, 1f)] public float seatVolume = 0.9f;
        [Range(0f, 1f)] public float rejectVolume = 0.5f;

        private AudioSource source;
        private AudioClip seatClip, rejectClip;

        void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.minDistance = 0.5f;
            seatClip = ProceduralAudio.MakeClick("Part_Seated", 0.06f, 1600f, 1f);
            rejectClip = ProceduralAudio.MakeBuzz("Part_Rejected", 0.18f, 140f, 0.35f);
        }

        void Start()
        {
            foreach (AssemblyStep step in manager.steps)
            {
                if (step.socket == null) continue;
                step.socket.Seated += OnSeated;
                step.socket.Rejected += OnRejected;
            }
        }

        void OnDestroy()
        {
            if (manager == null) return;
            foreach (AssemblyStep step in manager.steps)
            {
                if (step.socket == null) continue;
                step.socket.Seated -= OnSeated;
                step.socket.Rejected -= OnRejected;
            }
        }

        private void OnSeated(SnapSocket socket) => PlayAt(socket.transform.position, seatClip, seatVolume);

        private void OnRejected(SnapSocket socket, AssemblyPart part, string reason) => PlayAt(part.transform.position, rejectClip, rejectVolume);

        private void PlayAt(Vector3 position, AudioClip clip, float volume)
        {
            transform.position = position;
            source.PlayOneShot(clip, volume);
        }
    }
}
