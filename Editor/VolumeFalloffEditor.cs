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
    [CanEditMultipleObjects]
    [CustomEditor(typeof(VolumeFalloff))]
    class VolumeFalloffEditor : UnityEditor.Editor
    {
        static HierarchicalBox s_ShapeBox;
        internal static HierarchicalBox s_BlendBox;

        private enum VolumeFalloffEditMode
        {
            Bounds = 0,
            Falloff = 1
        };

        private static readonly string[] VOLUME_FALLOFF_EDIT_MODE_NAMES =
        {
            "Bounds",
            "Falloff"
        };

        // TODO: Create inspector for edit mode.
        private static VolumeFalloffEditMode editMode = VolumeFalloffEditMode.Bounds;
        
        void OnEnable()
        {
            var volumeFalloff = target as VolumeFalloff;

            if (s_ShapeBox == null || s_ShapeBox.Equals(null))
            {
                s_ShapeBox = new HierarchicalBox(volumeFalloff.debugColor, null);
                s_ShapeBox.monoHandle = false;
            }
            if (s_BlendBox == null || s_BlendBox.Equals(null))
            {
                s_BlendBox = new HierarchicalBox(volumeFalloff.debugColor, null, parent: s_ShapeBox);
            }
        }

        public override void OnInspectorGUI()
        {
            var volumeFalloff = target as VolumeFalloff;

            EditorGUILayout.BeginVertical();

            EditorGUI.BeginChangeCheck();
            volumeFalloff.isTransformScaleUsed = EditorGUILayout.Toggle("Is Transform Scale Used", volumeFalloff.isTransformScaleUsed);
            if (EditorGUI.EndChangeCheck())
            {
                volumeFalloff.Constrain();
                Undo.RecordObject(volumeFalloff, "Change Volume Falloff Is Transform Scale Used");
            }

            EditorGUI.BeginChangeCheck();
            volumeFalloff.weight = EditorGUILayout.FloatField("Weight", volumeFalloff.weight);
            if (EditorGUI.EndChangeCheck())
            {
                volumeFalloff.Constrain();
                Undo.RecordObject(volumeFalloff, "Change Volume Falloff Weight");
            }

            EditorGUI.BeginChangeCheck();
            volumeFalloff.center = EditorGUILayout.Vector3Field("Center", volumeFalloff.center);
            if (EditorGUI.EndChangeCheck())
            {
                volumeFalloff.Constrain();
                Undo.RecordObject(volumeFalloff, "Change Volume Falloff Center");
            }

            EditorGUI.BeginChangeCheck();
            volumeFalloff.size = EditorGUILayout.Vector3Field("Size", volumeFalloff.size);
            if (EditorGUI.EndChangeCheck())
            {
                volumeFalloff.Constrain();
                Undo.RecordObject(volumeFalloff, "Change Volume Falloff Size");
            }

            EditorGUI.BeginChangeCheck();
            volumeFalloff.fadePositive = EditorGUILayout.Vector3Field("Fade Positive", volumeFalloff.fadePositive);
            if (EditorGUI.EndChangeCheck())
            {
                volumeFalloff.Constrain();
                Undo.RecordObject(volumeFalloff, "Change Volume Falloff Fade Positive");
            }

            EditorGUI.BeginChangeCheck();
            volumeFalloff.fadeNegative = EditorGUILayout.Vector3Field("Fade Negative", volumeFalloff.fadeNegative);
            if (EditorGUI.EndChangeCheck())
            {
                volumeFalloff.Constrain();
                Undo.RecordObject(volumeFalloff, "Change Volume Falloff Fade Negative");
            }

            EditorGUI.BeginChangeCheck();
            volumeFalloff.distanceFadeStart = EditorGUILayout.FloatField("Fade Distance Start", volumeFalloff.distanceFadeStart);
            if (EditorGUI.EndChangeCheck())
            {
                volumeFalloff.Constrain();
                Undo.RecordObject(volumeFalloff, "Change Volume Falloff Fade Distance Start");
            }

            EditorGUI.BeginChangeCheck();
            volumeFalloff.distanceFadeEnd = EditorGUILayout.FloatField("Fade Distance End", volumeFalloff.distanceFadeEnd);
            if (EditorGUI.EndChangeCheck())
            {
                volumeFalloff.Constrain();
                Undo.RecordObject(volumeFalloff, "Change Volume Falloff Fade Distance End");
            }

            EditorGUI.BeginChangeCheck();
            volumeFalloff.debugColor = EditorGUILayout.ColorField("Debug Color", volumeFalloff.debugColor);
            if (EditorGUI.EndChangeCheck())
            {
                volumeFalloff.Constrain();
                Undo.RecordObject(volumeFalloff, "Change Volume Falloff Debug Color");
            }

            editMode = (VolumeFalloffEditMode)EditorGUILayout.Popup("Edit Mode", (int)editMode, VOLUME_FALLOFF_EDIT_MODE_NAMES);

            EditorGUILayout.EndVertical();
        }

        static float3 ComputeCenterBlendLocalPosition(VolumeFalloff volumeFalloff)
        {
            float3 size = volumeFalloff.size;
            float3 posBlend = volumeFalloff.fadePositive;
            posBlend.x *= size.x;
            posBlend.y *= size.y;
            posBlend.z *= size.z;
            float3 negBlend = volumeFalloff.fadeNegative;
            negBlend.x *= size.x;
            negBlend.y *= size.y;
            negBlend.z *= size.z;
            float3 localPosition = (negBlend - posBlend) * 0.5f;
            return localPosition;
        }

        static float3 ComputeBlendSize(VolumeFalloff volumeFalloff)
        {
            float3 size = volumeFalloff.size;
            float3 blendSize = (new float3(1.0f, 1.0f, 1.0f) - volumeFalloff.fadePositive - volumeFalloff.fadeNegative);
            blendSize.x *= size.x;
            blendSize.y *= size.y;
            blendSize.z *= size.z;
            return blendSize;
        }
        
        [DrawGizmo(GizmoType.Selected|GizmoType.Active)]
        static void DrawGizmosSelected(VolumeFalloff volumeFalloff, GizmoType gizmoType)
        {
            Matrix4x4 m = volumeFalloff.isTransformScaleUsed
                ? volumeFalloff.transform.localToWorldMatrix
                : Matrix4x4.TRS(volumeFalloff.transform.position, volumeFalloff.transform.rotation, Vector3.one);
            m = m * Matrix4x4.TRS(volumeFalloff.center, Quaternion.identity, Vector3.one);
            using (new Handles.DrawingScope(m))
            {
                // Blend box
                s_BlendBox.center = ComputeCenterBlendLocalPosition(volumeFalloff);
                s_BlendBox.size = ComputeBlendSize(volumeFalloff);
                Color baseColor = volumeFalloff.debugColor;
                baseColor.a = 8.0f / 255.0f;
                s_BlendBox.baseColor = baseColor;
                s_BlendBox.DrawHull(editMode == VolumeFalloffEditMode.Falloff);
                
                // Bounding box.
                s_ShapeBox.center = new float3(0.0f, 0.0f, 0.0f);
                s_ShapeBox.size = volumeFalloff.size;
                s_ShapeBox.DrawHull(editMode == VolumeFalloffEditMode.Bounds);
            }
        }

        void OnSceneGUI()
        {
            //Note: for each handle to be independent when multi-selecting VolumeFalloff,
            //We cannot rely  hereon VolumeFalloff which is the collection of
            //selected VolumeFalloff. Thus code is almost the same of the UI.

            VolumeFalloff volumeFalloff = target as VolumeFalloff;

            Matrix4x4 m = volumeFalloff.isTransformScaleUsed
                ? volumeFalloff.transform.localToWorldMatrix
                : Matrix4x4.TRS(volumeFalloff.transform.position, volumeFalloff.transform.rotation, Vector3.one);
            m = m * Matrix4x4.TRS(volumeFalloff.center, Quaternion.identity, Vector3.one);

            switch (editMode)
            {
                case VolumeFalloffEditMode.Falloff:
                    using (new Handles.DrawingScope(m))
                    {
                        //contained must be initialized in all case
                        s_ShapeBox.center = float3.zero;
                        s_ShapeBox.size = volumeFalloff.size;

                        Color baseColor = volumeFalloff.debugColor;
                        baseColor.a = 8.0f / 255.0f;
                        s_BlendBox.baseColor = baseColor;
                        s_BlendBox.monoHandle = false;//!volumeFalloff.m_EditorAdvancedFade;
                        s_BlendBox.center = ComputeCenterBlendLocalPosition(volumeFalloff);
                        s_BlendBox.size = ComputeBlendSize(volumeFalloff);
                        EditorGUI.BeginChangeCheck();
                        s_BlendBox.DrawHandle();
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(volumeFalloff, "Change Volume Falloff Falloff Region");

                            //work in local space to compute the change on fadePositive and fadeNegative
                            float3 newCenterBlendLocalPosition = s_BlendBox.center;
                            float3 halfSize = s_BlendBox.size * 0.5f;
                            float3 size = volumeFalloff.size;
                            float3 posFade = newCenterBlendLocalPosition + halfSize;
                            posFade.x = 0.5f - posFade.x / size.x;
                            posFade.y = 0.5f - posFade.y / size.y;
                            posFade.z = 0.5f - posFade.z / size.z;
                            float3 negFade = newCenterBlendLocalPosition - halfSize;
                            negFade.x = 0.5f + negFade.x / size.x;
                            negFade.y = 0.5f + negFade.y / size.y;
                            negFade.z = 0.5f + negFade.z / size.z;
                            volumeFalloff.fadePositive = posFade;
                            volumeFalloff.fadeNegative = negFade;
                        }
                    }
                    break;
                case VolumeFalloffEditMode.Bounds:
                    //important: if the origin of the handle's space move along the handle,
                    //handles displacement will appears as moving two time faster.
                    using (new Handles.DrawingScope(m))
                    {
                        //contained must be initialized in all case
                        s_ShapeBox.center = float3.zero;
                        s_ShapeBox.size = volumeFalloff.size;

                        float3 previousSize = volumeFalloff.size;
                        float3 previousPositiveFade = volumeFalloff.fadePositive;
                        float3 previousNegativeFade = volumeFalloff.fadeNegative;
                        
                        EditorGUI.BeginChangeCheck();
                        s_ShapeBox.DrawHandle();
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObjects(new Object[] { volumeFalloff, volumeFalloff.transform }, "Change Volume Falloff Bounds");
                            
                            float3 newSize = s_ShapeBox.size;
                            volumeFalloff.size = newSize;
                            
                            float3 newPositiveFade = new float3(
                                newSize.x < 0.00001 ? 0.0f : previousPositiveFade.x * previousSize.x / newSize.x,
                                newSize.y < 0.00001 ? 0.0f : previousPositiveFade.y * previousSize.y / newSize.y,
                                newSize.z < 0.00001 ? 0.0f : previousPositiveFade.z * previousSize.z / newSize.z
                                );
                            float3 newNegativeFade = new float3(
                                newSize.x < 0.00001 ? 0.0f : previousNegativeFade.x * previousSize.x / newSize.x,
                                newSize.y < 0.00001 ? 0.0f : previousNegativeFade.y * previousSize.y / newSize.y,
                                newSize.z < 0.00001 ? 0.0f : previousNegativeFade.z * previousSize.z / newSize.z
                                );
                            for (int axeIndex = 0; axeIndex < 3; ++axeIndex)
                            {
                                if (newPositiveFade[axeIndex] + newNegativeFade[axeIndex] > 1)
                                {
                                    float overValue = (newPositiveFade[axeIndex] + newNegativeFade[axeIndex] - 1f) * 0.5f;
                                    newPositiveFade[axeIndex] -= overValue;
                                    newNegativeFade[axeIndex] -= overValue;

                                    if (newPositiveFade[axeIndex] < 0)
                                    {
                                        newNegativeFade[axeIndex] += newPositiveFade[axeIndex];
                                        newPositiveFade[axeIndex] = 0f;
                                    }
                                    if (newNegativeFade[axeIndex] < 0)
                                    {
                                        newPositiveFade[axeIndex] += newNegativeFade[axeIndex];
                                        newNegativeFade[axeIndex] = 0f;
                                    }
                                }
                            }
                            volumeFalloff.fadePositive = newPositiveFade;
                            volumeFalloff.fadeNegative = newNegativeFade;

                            volumeFalloff.center += (float3)s_ShapeBox.center;
                        }
                    }
                    break;

                default: Debug.Assert(false, "VolumeFalloffEditor: Encountered unsupported edit mode."); break;
            }
        }
    }
}
