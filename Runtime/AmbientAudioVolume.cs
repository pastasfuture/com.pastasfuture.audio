using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;

namespace Pastasfuture.Audio.Runtime
{
    // [ExecuteInEditMode]
    public class AmbientAudioVolume : MonoBehaviour
    {
        public struct AmbientAudioVolumeEngineData
        {
            public int audioIndex;
            public float audioVolume;
            public float3 audioPositionWS;
        }

        [Tooltip("Warning: Audio Source can only be modified during content creation time. Audio Source is assumed to stay constant throughout lifetime of AmbientAudioVolume")]
        public AudioClip audioClip = null;
        public Transform audioTransform = null;
        public float audioVolume = 1.0f;
        public VolumeFalloff volumeFalloff = null;
        private int audioIndex = -1;

        public AmbientAudioVolumeEngineData ConvertToEngineData()
        {
            AmbientAudioVolumeEngineData data = new AmbientAudioVolumeEngineData();

            data.audioIndex = audioIndex;
            data.audioVolume = audioVolume;
            data.audioPositionWS = audioTransform.position;
            return data;
        }
        
        public static List<AmbientAudioVolume> instances = new List<AmbientAudioVolume>();
        public static List<AudioClip> audioClips = new List<AudioClip>();

        private static int RegisterAudioClip(AudioClip audioClip)
        {
            for (int i = 0, iLen = audioClips.Count; i < iLen; ++i)
            {
                if (audioClips[i] == audioClip)
                {
                    // Debug.Log("AmbientAudioVolume: Found pre-existing audio clip.");
                    return i;
                }
            }
            
            // Debug.Log("AmbientAudioVolume: Adding a new audio clip: " + audioClip.name);
            audioClips.Add(audioClip);
            return audioClips.Count - 1;
        }

        void OnEnable()
        {
            // Debug.Log("AmbientAudioVolume.OnEnable()");
            audioIndex = AmbientAudioVolume.RegisterAudioClip(audioClip);
            AmbientAudioVolume.instances.Add(this);
        }

        void OnDisable()
        {
            AmbientAudioVolume.instances.Remove(this);
        }
    }
}
