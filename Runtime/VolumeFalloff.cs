using UnityEngine;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

namespace Pastasfuture.Audio.Runtime
{
    public class VolumeFalloff : MonoBehaviour
    {
        public struct OrientedBBox
        {
            // 3 x float4 = 48 bytes.
            // TODO: pack the axes into 16-bit UNORM per channel, and consider a quaternionic representation.
            public float3 right;
            public float   extentX;
            public float3 up;
            public float   extentY;
            public float3 center;
            public float   extentZ;

            public float3 forward { get { return math.cross(up, right); } }
            
            public OrientedBBox(float4x4 trs)
            {
                float3 vecX = trs[0].xyz;
                float3 vecY = trs[1].xyz;
                float3 vecZ = trs[2].xyz;

                center = trs[3].xyz;
                right = math.normalize(vecX);
                up = math.normalize(vecY);

                extentX = 0.5f * math.length(vecX);
                extentY = 0.5f * math.length(vecY);
                extentZ = 0.5f * math.length(vecZ);
            }
        }

        public struct VolumeFalloffEngineData
        {
            public float weight;
            public float3 rcpFaceFadePos;
            public float3 rcpFaceFadeNeg;
            public float rcpDistFadeLen;
            public float endTimesRcpDistFadeLen;
        }

        public float weight = 1.0f;
        public float3 center = new float3(0.0f, 0.0f, 0.0f);
        public float3 size = new float3(1.0f, 1.0f, 1.0f);
        public float3 fadePositive = new float3(0.0f, 0.0f, 0.0f);
        public float3 fadeNegative = new float3(0.0f, 0.0f, 0.0f);
        public float distanceFadeStart = 10000.0f;
        public float distanceFadeEnd = 10000.0f;
        public Color debugColor = Color.white;
        public bool isTransformScaleUsed = true;

        [BurstCompile]
        private static float ComputeVolumeFalloffWeight(
            float3 samplePositionBoxNDC,
            float distanceWS,
            float3 rcpPosFaceFade,
            float3 rcpNegFaceFade,
            float rcpDistFadeLen,
            float endTimesRcpDistFadeLen
        ) {
            // We have to account for handedness.
            samplePositionBoxNDC.z = 1 - samplePositionBoxNDC.z;

            float3 posF = VolumeFalloff.Remap10(samplePositionBoxNDC, rcpPosFaceFade, rcpPosFaceFade);
            float3 negF = VolumeFalloff.Remap01(samplePositionBoxNDC, rcpNegFaceFade, 0);
            float fade = posF.x * posF.y * posF.z * negF.x * negF.y * negF.z;
            float dstF = 1.0f;//VolumeFalloff.Remap10(distanceWS, rcpDistFadeLen, endTimesRcpDistFadeLen);
            return fade * dstF;
        }

        // [start, end] -> [0, 1] : (x - start) / (end - start) = x * rcpLength - (start * rcpLength)
        [BurstCompile]
        private static float3 Remap01(float3 x, float3 rcpLength, float3 startTimesRcpLength)
        {
            return math.saturate(x * rcpLength - startTimesRcpLength);
        }

        // [start, end] -> [1, 0] : (end - x) / (end - start) = (end * rcpLength) - x * rcpLength
        [BurstCompile]
        private static float3 Remap10(float3 x, float3 rcpLength, float3 endTimesRcpLength)
        {
            return math.saturate(endTimesRcpLength - x * rcpLength);
        }

        // [start, end] -> [0, 1] : (x - start) / (end - start) = x * rcpLength - (start * rcpLength)
        [BurstCompile]
        private static float Remap01(float x, float rcpLength, float startTimesRcpLength)
        {
            return math.saturate(x * rcpLength - startTimesRcpLength);
        }

        // [start, end] -> [1, 0] : (end - x) / (end - start) = (end * rcpLength) - x * rcpLength
        [BurstCompile]
        private static float Remap10(float x, float rcpLength, float endTimesRcpLength)
        {
            return math.saturate(endTimesRcpLength - x * rcpLength);
        }

        [BurstCompile]
        public static float Evaluate(ref VolumeFalloffEngineData falloff, ref OrientedBBox obb, float3 positionWS, float distanceWS)
        {
            float3x3 obbFrame = new float3x3(obb.right, obb.up, math.cross(obb.up, obb.right));
            float3 obbExtents = new float3(obb.extentX, obb.extentY, obb.extentZ);

            float3 samplePositionBS = math.mul(obbFrame, positionWS - obb.center);
            float3 samplePositionBCS = samplePositionBS / obbExtents;

            bool isInsideVolume = math.max(math.abs(samplePositionBCS.x), math.max(math.abs(samplePositionBCS.y), math.abs(samplePositionBCS.z))) < 1.0f;
            if (!isInsideVolume) { return 0.0f; }

            float3 samplePositionBNDC = samplePositionBCS * 0.5f + 0.5f;

            float volumeWeight = VolumeFalloff.ComputeVolumeFalloffWeight(
                samplePositionBNDC,
                distanceWS,
                falloff.rcpFaceFadePos,
                falloff.rcpFaceFadeNeg,
                falloff.rcpDistFadeLen,
                falloff.endTimesRcpDistFadeLen
            );
            volumeWeight *= falloff.weight;
            return volumeWeight;
        }

        public static void ComputeOrientedBBoxFromFalloffAndTransform(out OrientedBBox obb, ref VolumeFalloff falloff, Transform t)
        {
            Matrix4x4 m = falloff.isTransformScaleUsed
                ? (t.localToWorldMatrix * Matrix4x4.TRS(falloff.center, Quaternion.identity, falloff.size))
                : Matrix4x4.TRS(t.position + (t.rotation * falloff.center), t.rotation, falloff.size);

            obb = new OrientedBBox(m);
        }

        public void Constrain()
        {
            distanceFadeStart = math.max(0.0f, distanceFadeStart);
            distanceFadeEnd = math.max(distanceFadeStart, distanceFadeEnd);
        }

        public VolumeFalloffEngineData ConvertToEngineData()
        {
            VolumeFalloffEngineData data = new VolumeFalloffEngineData();

            data.weight = weight;

            // Clamp to avoid NaNs.
            float3 fadePositive = this.fadePositive;
            float3 fadeNegative = this.fadeNegative;

            data.rcpFaceFadePos.x = (fadePositive.x < 1e-5f) ? float.MaxValue : (1.0f / fadePositive.x);
            data.rcpFaceFadePos.y = (fadePositive.y < 1e-5f) ? float.MaxValue : (1.0f / fadePositive.y);
            data.rcpFaceFadePos.z = (fadePositive.z < 1e-5f) ? float.MaxValue : (1.0f / fadePositive.z);

            data.rcpFaceFadeNeg.y = (fadeNegative.x < 1e-5f) ? float.MaxValue : (1.0f / fadeNegative.x);
            data.rcpFaceFadeNeg.x = (fadeNegative.y < 1e-5f) ? float.MaxValue : (1.0f / fadeNegative.y);
            data.rcpFaceFadeNeg.z = (fadeNegative.z < 1e-5f) ? float.MaxValue : (1.0f / fadeNegative.z);

            float distFadeLen = Mathf.Max(0.00001526f, distanceFadeEnd - distanceFadeStart);
            data.rcpDistFadeLen = 1.0f / distFadeLen;
            data.endTimesRcpDistFadeLen = distanceFadeEnd * data.rcpDistFadeLen;

            return data;
        }

        private void OnValidate()
        {
            Constrain();
        }
    }
}
