using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using UnityEditorInternal;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor.Rendering; // For Core Volume System.
using Pastasfuture.Audio.Runtime;

namespace Pastasfuture.Audio.Editor
{
    class VolumeFalloffSerialized
    {
        SerializedObject serializedObject;

        internal SerializedProperty isTransformScaleUsed;
        internal SerializedProperty weight;
        internal SerializedProperty center;
        internal SerializedProperty size;
        internal SerializedProperty fadePositive;
        internal SerializedProperty fadeNegative;
        internal SerializedProperty distanceFadeStart;
        internal SerializedProperty distanceFadeEnd;
        internal SerializedProperty debugColor;
        

        internal VolumeFalloffSerialized(SerializedObject s)
        {
            serializedObject = s;

            isTransformScaleUsed = serializedObject.FindProperty("isTransformScaleUsed");
            weight = serializedObject.FindProperty("weight");
            center = serializedObject.FindProperty("center");
            size = serializedObject.FindProperty("size");
            fadePositive = serializedObject.FindProperty("fadePositive");
            fadeNegative = serializedObject.FindProperty("fadeNegative");
            distanceFadeStart = serializedObject.FindProperty("distanceFadeStart");
            distanceFadeEnd = serializedObject.FindProperty("distanceFadeEnd");
            debugColor = serializedObject.FindProperty("debugColor");
        }

        internal void Update()
        {
            serializedObject.Update();
        }

        internal void ApplyModifiedProperties()
        {
            serializedObject.ApplyModifiedProperties();
        }
    }
}
