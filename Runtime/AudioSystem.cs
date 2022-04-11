using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.Audio;

namespace Pastasfuture.Audio.Runtime
{
    // a sound played from the Audio Pool
    [System.Serializable]
    // [ExecuteInEditMode]
    public class AudioPoolElement
    {
        [System.NonSerialized]
        private AudioPool pool;

        public AudioSource source;

        private Transform _location;
        public Transform location
        {
            get { return _location; }
            set { _location = value; isLocationSet = (value != null); }
        }

        public Vector3 offset = Vector3.zero;

        private bool isLocationSet;

        public bool isValid;

        public AudioPoolElement(AudioPool pool, AudioSource source)
        {
            this.pool = pool;
            this.source = source;

            location = null;
            isLocationSet = false;

            isValid = false;
        }

        public void Stop()
        {
            if (isValid)
            {
                source.Stop();
                pool.Recycle(this);
            }
        }

        public void Recycle()
        {
            isValid = false;
            location = null;
            isLocationSet = false;
            source.panStereo = 0.0f;
            source.pitch = 1.0f;
            source.outputAudioMixerGroup = null;
        }

        public bool HasInvalidPosition()
        {
            if (isLocationSet)
            {
                if (location == null)
                {
                    return true;
                }
            }
            return false;
        }
    }

    // pre-allocated pools of Audio Sources - and some information necesssary to position them
    [System.Serializable]
    // [ExecuteInEditMode]
    public class AudioPool
    {
        public GameObject audioSourcePrefab;
        public int poolSize;

        private List<AudioPoolElement> pool;
        private int nextFree;

        private List<AudioPoolElement> recycleQueue;

        public void Initialize(Transform root)
        {
            recycleQueue = new List<AudioPoolElement>();
            pool = new List<AudioPoolElement>(poolSize);
            for (int i = 0; i < poolSize; ++i)
            {
                var sourceGO = GameObject.Instantiate(audioSourcePrefab, root);
                var audioSource = sourceGO.GetComponentInChildren<AudioSource>(true);
                Debug.Assert(audioSource != null, "Missing AudioSource on prefab :" + audioSourcePrefab.name);
                pool.Add(new AudioPoolElement(this, audioSource));
            }
            nextFree = 0;
        }

        public AudioPoolElement Take()
        {
            if (!EnsureCapacity()) { return null; }
            var element = pool[nextFree++];
            element.isValid = true;
            return element;
        }

        public void Recycle(AudioPoolElement element)
        {
            element.Recycle();

            // Element could be waiting to be disposed in the recycle queue.
            // Calling Recycle() directly is treated as "instantly stop this sound and recycle it."
            // Need to remove from the recycle queue to support this behavior.
            int indexRecycleQueue = recycleQueue.IndexOf(element);
            if (indexRecycleQueue >= 0) { recycleQueue.RemoveAt(indexRecycleQueue); }
            
            int index = pool.IndexOf(element);
            Debug.Assert(index >= 0 && index < pool.Count);
            --nextFree;
            pool[index] = pool[nextFree];
            pool[nextFree] = element;
        }

        public void RecycleOnClipComplete(AudioPoolElement element)
        {
            if (element == null) { return; }
            recycleQueue.Add(element);
        }

        private bool EnsureCapacity()
        {
            if (nextFree >= poolSize)
            {
                Debug.Assert(false, "Error: AudioSystem: Requesting too many audio sources. Decrease number of sounds played at once, or increase pre-allocated pool size.");
                return false;
                // Debug.LogWarningFormat("Pool {0} has overgrown its pre-allocated size of {1}, consider expanding the pool.", audioSourcePrefab.name, poolSize);
                // pool.Capacity = pool.Capacity * 2;
                // poolSize = pool.Capacity;
            }
            return true;
        }

        public void UpdateAudioPosition()
        {
            for (int i = 0; i < nextFree; ++i)
            {
                var element = pool[i];
                if (element.HasInvalidPosition())
                {
                    Debug.Assert(false, "Encountered audio source with invalid position");
                    element.source.Stop();
                    element.Recycle();
                    nextFree--;
                    pool[i] = pool[nextFree];
                    pool[nextFree] = element;
                    --i;
                    continue;
                }

                if (element.location != null)
                {
                    element.source.transform.position = element.location.position + element.offset;
                }
            }
        }

        public void ProcessRecycleQueue()
        {
            if (recycleQueue == null) { return; }
            for (int i = 0; i < recycleQueue.Count; )
            {
                if (!recycleQueue[i].source.isPlaying)
                {
                    // Warning: Calls to Recycle removes the element from the recycle queue immediately.
                    Recycle(recycleQueue[i]);
                }
                else
                {
                    ++i;
                }
            }
        }
    }

    // static manager that holds all pooled audio sources
    // [ExecuteInEditMode]
    public class AudioSystem : MonoBehaviour
    {
        public AudioPool localizedAudioPool = new AudioPool();
        public AudioPool audioPool = new AudioPool();

        private static AudioSystem _instance;
        private static AudioSystem Instance
        {
            get
            {
                if (_instance == null)
                {
                    Debug.LogError("Missing Audio System - no sounds will play.");
                }
                return _instance;
            }
        }

        void OnEnable()
        {
            // Debug.Log("AudioSystem.OnEnable()");
            Initialize();
        }

        // after updating all positions, make the audio positions accurate
        void Update()
        {
            localizedAudioPool.UpdateAudioPosition();
            audioPool.ProcessRecycleQueue();
            localizedAudioPool.ProcessRecycleQueue();
        }

        private AudioSystem Initialize()
        {
            if (_instance == null)
            {
                _instance = this;
                SetupPools();
            }
            return _instance;
        }

        private void SetupPools()
        {
            localizedAudioPool.Initialize(this.transform);
            audioPool.Initialize(this.transform);
        }

        // Public static API for playing audio
        // Play and PlayOneShot are for UI sounds, or other non-localized sounds
        // PlayAt and PlayOneShotAt are for positional sounds in the world, either parented to a transform or played at a location
        public static AudioPoolElement Play(AudioClip clip, float volume = 1.0f, bool loop = false, AudioMixerGroup outputAudioMixerGroup = null)
        {
            return AudioSystem.Instance.PlayInternal(clip, volume, loop, outputAudioMixerGroup);
        }

        private AudioPoolElement PlayInternal(AudioClip clip, float volume, bool loop, AudioMixerGroup outputAudioMixerGroup)
        {
            var element = audioPool.Take();
            if (element == null)
            {
                Debug.Assert(false, "Error: AudioSystem: Failed to play clip as there are no free pool elements.");
                return null;
            }
            element.source.clip = clip;
            element.source.volume = volume;
            element.source.loop = loop;
            element.source.panStereo = 0.0f;
            element.source.pitch = 1.0f;
            element.source.outputAudioMixerGroup = null;
            element.source.Play();
            return element;
        }

        public static AudioPoolElement PlayScheduled(AudioClip clip, float volume, bool loop, double startTime, AudioMixerGroup outputAudioMixerGroup = null)
        {
            return AudioSystem.Instance.PlayScheduledInternal(clip, volume, loop, startTime, outputAudioMixerGroup);
        }

        private AudioPoolElement PlayScheduledInternal(AudioClip clip, float volume, bool loop, double startTime, AudioMixerGroup outputAudioMixerGroup)
        {
            var element = audioPool.Take();
            if (element == null)
            {
                Debug.Assert(false, "Error: AudioSystem: Failed to play clip as there are no free pool elements.");
                return null;
            }
            element.source.clip = clip;
            element.source.volume = volume;
            element.source.loop = loop;
            element.source.panStereo = 0.0f;
            element.source.pitch = 1.0f;
            element.source.outputAudioMixerGroup = outputAudioMixerGroup;
            element.source.PlayScheduled(AudioSettings.dspTime + startTime);
            return element;
        }

        public static void PlayOneShot(AudioClip clip, float volumeScale = 1.0f, AudioMixerGroup outputAudioMixerGroup = null)
        {
            // Debug.Log("Requesting to play audio clip: " + clip.name + "with load state: " + clip.loadState);
            AudioSystem.Instance.PlayOneShotInternal(clip, volumeScale, outputAudioMixerGroup);
        }

        public static void PlayOneShotRandom(ref List<AudioClip> clips, float volumeScale = 1.0f, AudioMixerGroup outputAudioMixerGroup = null)
        {
            if (clips.Count > 0)
            {
                int clipIndex = Mathf.FloorToInt(UnityEngine.Random.value * (float)(clips.Count - 1) + 0.5f);
                Debug.Assert(clips[clipIndex] != null);
                AudioSystem.PlayOneShot(clips[clipIndex], volumeScale, outputAudioMixerGroup);
            }
        }

        private void PlayOneShotInternal(AudioClip clip, float volumeScale, AudioMixerGroup outputAudioMixerGroup)
        {
            var element = audioPool.Take();
            if (element == null)
            {
                Debug.Assert(false, "Error: AudioSystem: Failed to play clip as there are no free pool elements."); 
                return; 
            }
            // element.source.PlayOneShot(clip, volumeScale);
            element.source.clip = clip;
            element.source.volume = volumeScale;
            element.source.loop = false;
            element.source.panStereo = 0.0f;
            element.source.pitch = 1.0f;
            element.source.outputAudioMixerGroup = outputAudioMixerGroup;
            element.source.Play();
            audioPool.RecycleOnClipComplete(element);
        }

        public static AudioPoolElement PlayAt(AudioClip clip, Transform parent, Vector3 offset, float volume = 1.0f, bool loop = false, AudioMixerGroup outputAudioMixerGroup = null)
        {
            return AudioSystem.Instance.PlayAtInternal(clip, parent, offset, volume, loop, outputAudioMixerGroup);
        }

        private AudioPoolElement PlayAtInternal(AudioClip clip, Transform parent, Vector3 offset, float volume, bool loop, AudioMixerGroup outputAudioMixerGroup)
        {
            var element = localizedAudioPool.Take();
            if (element == null)
            {
                Debug.Assert(false, "Error: AudioSystem: Failed to play clip as there are no free pool elements.");
                return null;
            }
            var transform = element.source.transform;

            if (parent != null)
            {
                element.location = parent;
                element.offset = offset;
            }
            else
            {
                transform.position = offset;
            }

            element.source.clip = clip;
            element.source.volume = volume;
            element.source.loop = loop;
            element.source.panStereo = 0.0f;
            element.source.pitch = 1.0f;
            element.source.outputAudioMixerGroup = outputAudioMixerGroup;
            element.source.Play();
            return element;
        }

        public static void PlayOneShotAt(AudioClip clip, Transform parent, Vector3 offset, float volumeScale = 1.0f, AudioMixerGroup outputAudioMixerGroup = null)
        {
            AudioSystem.Instance.PlayOneShotAtInternal(clip, parent, offset, volumeScale, outputAudioMixerGroup);
        }

        public static void PlayOneShotAtRandom(ref List<AudioClip> clips, Transform parent, Vector3 offset, float volumeScale = 1.0f, AudioMixerGroup outputAudioMixerGroup = null)
        {
            if (clips.Count > 0)
            {
                int clipIndex = Mathf.FloorToInt(UnityEngine.Random.value * (float)(clips.Count - 1) + 0.5f);
                Debug.Assert(clips[clipIndex] != null);
                AudioSystem.PlayOneShotAt(clips[clipIndex], parent, offset, volumeScale, outputAudioMixerGroup);
            }
        }

        private void PlayOneShotAtInternal(AudioClip clip, Transform parent, Vector3 offset, float volumeScale, AudioMixerGroup outputAudioMixerGroup)
        {
            var element = localizedAudioPool.Take();
            if (element == null)
            {
                Debug.Assert(false, "Error: AudioSystem: Failed to play clip as there are no free pool elements.");
                return;
            }

            if (parent != null)
            {
                element.source.transform.position = parent.position + offset;
            }
            else
            {
                element.source.transform.position = offset;
            }

            element.source.clip = clip;
            element.source.volume = volumeScale;
            element.source.loop = false;
            element.source.panStereo = 0.0f;
            element.source.pitch = 1.0f;
            element.source.outputAudioMixerGroup = outputAudioMixerGroup;
            element.source.Play();
            localizedAudioPool.RecycleOnClipComplete(element);
        }
    }
}