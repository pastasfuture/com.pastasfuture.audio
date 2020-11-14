using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Pastasfuture.Audio.Runtime
{
    public class AmbientVolumeController : MonoBehaviour
    {
        [System.Serializable]
        public class AmbientSound
        {
            public AudioClip clip;
            public bool isVolumeSpiceDriven;
            public float volumeMin;
            public float volumeMax;
            private AudioPoolElement element;

            private float ComputeVolumeFromSpice(float spice)
            {
                return ComputeVolumeFromNormalizedLoudness(
                    isVolumeSpiceDriven
                        ? Mathf.Lerp(volumeMin, volumeMax, spice)
                        : volumeMax
                );
            }

            private static float ComputeVolumeFromNormalizedLoudness(float loudness)
            {
                return Mathf.Pow(Mathf.Clamp01(loudness), Mathf.Log(10.0f, 4.0f));
            }

            public void Update(float spice, float fadeInNormalized)
            {
                // Lazy initialization of AudioPoolElement, as AudioSystem is Initialized OnStart(), so we cannot guarentee that our OnStart() will get hit after.
                if (element == null)
                {
                    Play();
                }
                Debug.Assert(element != null);

                element.source.volume = ComputeVolumeFromSpice(spice) * fadeInNormalized;
            }

            private void Play()
            {
                Debug.Assert(clip != null);
                element = AudioSystem.Play(clip);
                element.source.loop = true;
                element.source.volume = 0.0f;
            }

            public void Dispose()
            {
                if (element != null) { element.Stop(); element = null; }
            }
        }

        // Assign ambient sounds statically in the inspector.
        public List<AmbientSound> ambientSounds = new List<AmbientSound>();
        public float fadeInDuration = 40.0f;
        private float fadeInTimer = 0.0f;

        void OnDestroy()
        {
            for (int i = 0, iLen = ambientSounds.Count; i < iLen; ++i)
            {
                AmbientSound ambientSound = ambientSounds[i];
                ambientSound.Dispose();
            }
        }

        // Update is called once per frame
        void Update()
        {
            // if (PauseManager.GetIsPaused()) { return; }

            float spice = 1.0f;//Mathf.Min(1, GameplayState.instance.currentSpiciness);

            float fadeInNormalized = Mathf.Clamp(fadeInTimer / fadeInDuration, 0.0f, 1.0f);
            fadeInTimer += Time.deltaTime;

            for (int i = 0, iLen = ambientSounds.Count; i < iLen; ++i)
            {
                AmbientSound ambientSound = ambientSounds[i];

                ambientSound.Update(spice, fadeInNormalized);
            }
        }
    }
}