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
    public class AmbientAudioVolumeManager : MonoBehaviour
    {
        private static readonly int INITIAL_CAPACITY = 128;
        private static readonly int INITIAL_CAPACITY_AUDIO_VOLUME = 32;
        [System.NonSerialized] private NativeArray<VolumeFalloff.OrientedBBox> bounds;
        [System.NonSerialized] private NativeArray<VolumeFalloff.VolumeFalloffEngineData> falloffData;
        [System.NonSerialized] private NativeArray<AmbientAudioVolume.AmbientAudioVolumeEngineData> audioData;
        [System.NonSerialized] private NativeArray<float2> audioVolume;
        [System.NonSerialized] private int count = 0;
        [System.NonSerialized] private int audioVolumeCount = 0;
        [System.NonSerialized] private AudioPoolElement[] audioPoolElements;
        // public AudioPoolElement[] audioPoolElements;

        public Transform targetTransform;

        private void Dispose()
        {
            if (bounds != null && bounds.Length > 0) { bounds.Dispose(); }
            if (falloffData != null && falloffData.Length > 0) { falloffData.Dispose(); }
            if (audioData != null && audioData.Length > 0) { audioData.Dispose(); }
        }

        private void EnsureCapacity(int capacity, Unity.Collections.Allocator allocator)
        {
            Debug.Assert(capacity > 0);

            if (bounds != null && bounds.Length > capacity)
            {
                return;
            }

            if (bounds != null)
            {
                Dispose();
            }

            bounds = new NativeArray<VolumeFalloff.OrientedBBox>(capacity, allocator);
            falloffData = new NativeArray<VolumeFalloff.VolumeFalloffEngineData>(capacity, allocator);
            audioData = new NativeArray<AmbientAudioVolume.AmbientAudioVolumeEngineData>(capacity, allocator);
        }

        private void DisposeAudioVolume()
        {
            if (audioVolume != null && audioVolume.Length > 0) { audioVolume.Dispose(); audioPoolElements = null; }
        }

        private void EnsureCapacityAudioVolume(int capacity, Unity.Collections.Allocator allocator)
        {
            Debug.Assert(capacity > 0);

            if (audioVolume != null && audioVolume.Length > capacity)
            {
                return;
            }

            // Need to preserve any prexisting values, so that we don't cut out / restart audio on reallocs.
            AudioPoolElement[] audioPoolElementsNext = new AudioPoolElement[capacity];
            if (audioVolume != null)
            {
                for (int i = 0; i < audioVolumeCount; ++i)
                {
                    audioPoolElementsNext[i] = audioPoolElements[i];
                }

                DisposeAudioVolume();
            }

            audioVolume = new NativeArray<float2>(capacity, allocator);
            audioPoolElements = audioPoolElementsNext;
        }

        void Start()
        {
            EnsureCapacity(AmbientAudioVolumeManager.INITIAL_CAPACITY, Allocator.Persistent);
            EnsureCapacityAudioVolume(AmbientAudioVolumeManager.INITIAL_CAPACITY_AUDIO_VOLUME, Allocator.Persistent);
        }

        void OnDestroy()
        {
            Dispose();
            DisposeAudioVolume();
        }


        void Update()
        {
            UnityEngine.Profiling.Profiler.BeginSample("Ambient Audio Volume Manager::Update");
            // Ambient audio should always play. Ignoring pause.
            // if (PauseManager.GetIsPaused()) { return; }

            UpdateInternal(Time.deltaTime);

            UnityEngine.Profiling.Profiler.EndSample();
        }

        private void UpdateInternal(float deltaTime)
        {
            if (AmbientAudioVolume.instances.Count == 0) { return; }
            if (targetTransform == null) { return; }

            EnsureCapacity(AmbientAudioVolume.instances.Count, Allocator.Persistent);
            count = AmbientAudioVolume.instances.Count;
            
            EnsureCapacityAudioVolume(AmbientAudioVolume.audioClips.Count, Allocator.Persistent);
            audioVolumeCount = AmbientAudioVolume.audioClips.Count;

            ComputeEngineData(ref AmbientAudioVolume.instances);

            float3 targetPositionWS = targetTransform.position;
            float3 targetTangentDirectionWS = targetTransform.right;
            ComputeAudioVolumeAtPositionAndDirectionFromEngineData(targetPositionWS, targetTangentDirectionWS);
            
            PlayAudioSourcesAtVolume();
        }

        private void ComputeEngineData(ref List<AmbientAudioVolume> instances)
        {
            for (int i = 0, iLen = instances.Count; i < iLen; ++i)
            {
                AmbientAudioVolume ambientAudioVolume = instances[i];
                VolumeFalloff volumeFalloff = ambientAudioVolume.volumeFalloff;

                VolumeFalloff.ComputeOrientedBBoxFromFalloffAndTransform(out VolumeFalloff.OrientedBBox obb, ref volumeFalloff, ambientAudioVolume.transform);
                bounds[i] = obb;
                falloffData[i] = volumeFalloff.ConvertToEngineData();
                audioData[i] = ambientAudioVolume.ConvertToEngineData();
            }
        }

        [BurstCompile]
        private void ComputeAudioVolumeAtPositionAndDirectionFromEngineData(float3 targetPositionWS, float3 targetTangentDirectionWS)
        {
            for (int i = 0; i < audioVolumeCount; ++i)
            {
                audioVolume[i] = float2.zero;
            }

            for (int i = 0; i < count; ++i)
            {
                VolumeFalloff.OrientedBBox obb = bounds[i];
                AmbientAudioVolume.AmbientAudioVolumeEngineData audioEngineData = audioData[i];
                VolumeFalloff.VolumeFalloffEngineData falloffEngineData = falloffData[i];

                float distanceWS = math.length(obb.center - targetPositionWS);
                float weight = VolumeFalloff.Evaluate(ref falloffEngineData, ref obb, targetPositionWS, distanceWS);
                if (weight < 1e-5f) { continue; }

                // TODO: Figure out how to modulate left and right ear independantly.
                float3 audioPositionOffsetWS = audioEngineData.audioPositionWS - targetPositionWS;
                float spatialAttenuation = 1.0f / math.max(1.0f, math.dot(audioPositionOffsetWS, audioPositionOffsetWS));

                float audioVolumeIsotropic = audioEngineData.audioVolume * weight * spatialAttenuation;

                float3 targetToAudioDirectionWS = math.normalize(audioPositionOffsetWS);

                // Debug.Log("spatialAttenuation = " + spatialAttenuation);

                // Perform simple wrap lighting model for ear direction attenuation.
                // TODO: Research realistic attenuation models.
                float2 earWeights = new float2(
                    math.saturate(math.dot(targetTangentDirectionWS, targetToAudioDirectionWS) * -0.5f + 0.5f),
                    math.saturate(math.dot(targetTangentDirectionWS, targetToAudioDirectionWS) * 0.5f + 0.5f)
                );

                audioVolume[audioEngineData.audioIndex] = earWeights * audioVolumeIsotropic + audioVolume[audioEngineData.audioIndex];
            }
        }

        private void PlayAudioSourcesAtVolume()
        {
            for (int i = 0; i < audioVolumeCount; ++i)
            {
                if (math.max(audioVolume[i].x, audioVolume[i].y) < 1e-5f)
                {
                    if (audioPoolElements[i] == null) { continue; }

                    // Debug.Log("Returning audio pool element at index[" + i + "] to the pool");
                    audioPoolElements[i].Stop();
                    audioPoolElements[i] = null;
                }
                else
                {
                    if (audioPoolElements[i] == null)
                    {
                        // Debug.Log("Audio pool element at index[" + i + "] is null, instantiating");
                        bool loop = true;
                        audioPoolElements[i] = AudioSystem.Play(AmbientAudioVolume.audioClips[i], 0.0f, loop);

                        // Debug.Log("Audio pool element at index[" +i + "] now equals + " + audioPoolElements[i]);
                    }

                    float volumeIsotropic = math.max(audioVolume[i].x, audioVolume[i].y); // TODO: This?
                    float panStereo = (audioVolume[i].y - audioVolume[i].x) / math.max(audioVolume[i].x, audioVolume[i].y);

                    // TODO: Investigate how we should actually tonemap audio.
                    float volumeIsotropicTonemapped = math.saturate(volumeIsotropic / (volumeIsotropic + 1.0f));
                    // Debug.Log("audioPoolElements = " + audioPoolElements);
                    audioPoolElements[i].source.volume = volumeIsotropicTonemapped;
                    audioPoolElements[i].source.panStereo = panStereo;        
                }

            }
        }
    }
}