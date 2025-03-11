using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;
using UnityEngine.Serialization;

namespace StormBreakers
{
    [ExecuteAlways]
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class OceanPlanarReflection : MonoBehaviour
    {
        private static readonly int availableId = Shader.PropertyToID("_PlanarAvailable");
        private static readonly int leftTextureId = Shader.PropertyToID("_PlanarLeftTexture");
        private static readonly int rightTextureId = Shader.PropertyToID("_PlanarRightTexture");
        private static readonly int leftMatrixId = Shader.PropertyToID("_PlanarLeftVP");
        private static readonly int rightMatrixId = Shader.PropertyToID("_PlanarRightVP");
        private static readonly int leftEyeId = Shader.PropertyToID("_PlanarLeftEye");
        private static readonly int rightEyeId = Shader.PropertyToID("_PlanarRightEye");
        private static bool isRendering;

        [FormerlySerializedAs("_resolutionScale")]
        [SerializeField, Range(0.2f, 1f)] private float resolutionScale = 0.5f;

        [FormerlySerializedAs("_maximumResolution")]
        [SerializeField, Range(256, 2048)] private int maximumResolution = 1024;

        [FormerlySerializedAs("_clipOffset")]
        [SerializeField, Min(0.001f)] private float clipOffset = 0.04f;

        [FormerlySerializedAs("_reflectionDistance")]
        [SerializeField, Min(10f)] private float reflectionDistance = 120f;

        [FormerlySerializedAs("_reflectionLayers")]
        [SerializeField] private LayerMask reflectionLayers = -1;

        [FormerlySerializedAs("_renderInSceneView")]
        [SerializeField] private bool renderInSceneView = true;

        [FormerlySerializedAs("_reflectShadows")]
        [SerializeField] private bool reflectShadows;

        private readonly Dictionary<Camera, CameraResources> cameraResources = new Dictionary<Camera, CameraResources>();
        private readonly List<Camera> removedCameras = new List<Camera>();
        private MeshRenderer targetRenderer;
        private double nextCleanupTime;

        private void OnEnable()
        {
            targetRenderer = GetComponent<MeshRenderer>();

            if (targetRenderer.sharedMaterial != null && targetRenderer.sharedMaterial.HasProperty(availableId))
            {
                targetRenderer.sharedMaterial.SetFloat(availableId, 0f);
            }

            RenderPipelineManager.beginCameraRendering += RenderReflection;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= RenderReflection;

            if (targetRenderer != null &&
                targetRenderer.sharedMaterial != null &&
                targetRenderer.sharedMaterial.HasProperty(availableId))
            {
                targetRenderer.sharedMaterial.SetFloat(availableId, 0f);
            }

            foreach (var resources in cameraResources.Values)
            {
                ReleaseResources(resources);
            }

            cameraResources.Clear();
        }

        private static void DestroyResource(Object resource)
        {
            if (resource == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(resource);
            }
            else
            {
                DestroyImmediate(resource);
            }
        }

        private static RenderTexture CreateTexture(int width, int height, bool hdr, string name)
        {
            var texture = new RenderTexture(width, height, 24, hdr ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = true,
                autoGenerateMips = false,
                antiAliasing = 1
            };

            texture.Create();

            return texture;
        }

        private static void ReleaseResources(CameraResources resources)
        {
            if (resources.LeftTexture != null)
            {
                resources.LeftTexture.Release();
            }

            if (resources.RightTexture != null)
            {
                resources.RightTexture.Release();
            }

            DestroyResource(resources.LeftTexture);
            DestroyResource(resources.RightTexture);

            if (resources.ReflectionCamera != null)
            {
                DestroyResource(resources.ReflectionCamera.gameObject);
            }
        }

        private void CleanupCameras()
        {
            if (Time.realtimeSinceStartupAsDouble < nextCleanupTime)
            {
                return;
            }

            nextCleanupTime = Time.realtimeSinceStartupAsDouble + 5.0;
            removedCameras.Clear();

            foreach (var entry in cameraResources)
            {
                if (entry.Key == null)
                {
                    ReleaseResources(entry.Value);
                    removedCameras.Add(entry.Key);
                }
            }

            foreach (var camera in removedCameras)
            {
                cameraResources.Remove(camera);
            }
        }

        private CameraResources GetResources(Camera source, bool stereo)
        {
            var sourceWidth = stereo ? XRSettings.eyeTextureWidth : source.pixelWidth;
            var sourceHeight = stereo ? XRSettings.eyeTextureHeight : source.pixelHeight;
            var longestSide = Mathf.Max(Mathf.Max(sourceWidth, sourceHeight), 1);
            var scale = Mathf.Min(resolutionScale, (float)maximumResolution / longestSide);
            var width = Mathf.Max(128, Mathf.RoundToInt(sourceWidth * scale));
            var height = Mathf.Max(128, Mathf.RoundToInt(sourceHeight * scale));

            if (cameraResources.TryGetValue(source, out var resources))
            {
                if (resources.LeftTexture.width == width &&
                    resources.LeftTexture.height == height &&
                    resources.Stereo == stereo &&
                    resources.Hdr == source.allowHDR)
                {
                    return resources;
                }

                ReleaseResources(resources);
            }

            var cameraObject = new GameObject("Ocean reflection camera " + source.GetInstanceID())
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            var reflectionCamera = cameraObject.AddComponent<Camera>();
            reflectionCamera.enabled = false;

            var data = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            data.allowXRRendering = false;
            data.renderPostProcessing = false;
            data.requiresColorTexture = false;
            data.requiresDepthTexture = false;

            resources = new CameraResources
            {
                ReflectionCamera = reflectionCamera,
                LeftTexture = CreateTexture(width, height, source.allowHDR, "Ocean reflection left"),
                RightTexture = stereo ? CreateTexture(width, height, source.allowHDR, "Ocean reflection right") : null,
                Stereo = stereo,
                Hdr = source.allowHDR
            };

            cameraResources[source] = resources;

            return resources;
        }

        private Matrix4x4 RenderEye(
            ScriptableRenderContext context,
            Camera source,
            CameraResources resources,
            Matrix4x4 sourceView,
            Matrix4x4 sourceProjection,
            RenderTexture target)
        {
            var waterHeight = transform.position.y;
            var mirror = Matrix4x4.identity;
            mirror.m11 = -1f;
            mirror.m13 = 2f * waterHeight;

            var reflectedView = sourceView * mirror;
            var sourcePosition = (Vector3)sourceView.inverse.GetColumn(3);
            var reflectedPosition = mirror.MultiplyPoint(sourcePosition);
            var camera = resources.ReflectionCamera;

            camera.CopyFrom(source);
            camera.enabled = false;
            camera.cameraType = CameraType.Reflection;
            camera.useOcclusionCulling = false;
            camera.allowMSAA = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.targetTexture = target;
            camera.rect = new Rect(0f, 0f, 1f, 1f);

            if (source.scene.IsValid())
            {
                camera.scene = source.scene;
            }

            var waterLayer = LayerMask.NameToLayer("Water");
            var waterMask = waterLayer >= 0 ? 1 << waterLayer : 0;
            camera.cullingMask = source.cullingMask & reflectionLayers.value & ~waterMask;
            camera.farClipPlane = Mathf.Min(source.farClipPlane, reflectionDistance);
            var reflectedForward = mirror.MultiplyVector(source.transform.forward);
            var reflectedUp = mirror.MultiplyVector(source.transform.up);
            var reflectedRotation = Quaternion.LookRotation(reflectedForward, reflectedUp);

            camera.transform.SetPositionAndRotation(reflectedPosition, reflectedRotation);
            camera.worldToCameraMatrix = reflectedView;

            var near = camera.nearClipPlane;
            var far = Mathf.Max(near + 0.1f, camera.farClipPlane);

            if (camera.orthographic)
            {
                sourceProjection.m22 = -2f / (far - near);
                sourceProjection.m23 = -(far + near) / (far - near);
            }
            else
            {
                sourceProjection.m22 = -(far + near) / (far - near);
                sourceProjection.m23 = -2f * far * near / (far - near);
            }

            camera.projectionMatrix = sourceProjection;

            var clipPosition = reflectedView.MultiplyPoint(new Vector3(0f, waterHeight + clipOffset, 0f));
            var clipNormal = reflectedView.MultiplyVector(Vector3.up).normalized;
            var clipPlane = new Vector4(clipNormal.x, clipNormal.y, clipNormal.z, -Vector3.Dot(clipPosition, clipNormal));
            var projection = camera.CalculateObliqueMatrix(clipPlane);

            camera.projectionMatrix = projection;
            camera.cullingMatrix = projection * reflectedView;
            camera.GetUniversalAdditionalCameraData().renderShadows = reflectShadows;
#pragma warning disable CS0618
            UniversalRenderPipeline.RenderSingleCamera(context, camera);
#pragma warning restore CS0618
            target.GenerateMips();

            return GL.GetGPUProjectionMatrix(projection, true) * reflectedView;
        }

        private void RenderReflection(ScriptableRenderContext context, Camera source)
        {
            if (isRendering ||
                !isActiveAndEnabled ||
                targetRenderer == null ||
                !targetRenderer.enabled ||
                source == null ||
                source.cameraType == CameraType.Reflection ||
                source.cameraType == CameraType.Preview)
            {
                return;
            }

            if (source.cameraType == CameraType.SceneView && !renderInSceneView)
            {
                return;
            }
#if UNITY_EDITOR

            if (UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(source.gameObject.scene) &&
                source.gameObject.scene != gameObject.scene)
            {
                return;
            }
#endif

            if (source.TryGetComponent<UniversalAdditionalCameraData>(out var sourceData) &&
                sourceData.renderType == CameraRenderType.Overlay)
            {
                return;
            }

            var material = targetRenderer.sharedMaterial;

            if (material == null ||
                !material.HasProperty(availableId) ||
                !(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset))
            {
                return;
            }

            CleanupCameras();

            var stereo = source.cameraType == CameraType.Game && source.stereoEnabled && XRSettings.enabled;
            var resources = GetResources(source, stereo);
            var previousCulling = GL.invertCulling;
            isRendering = true;

            try
            {
                GL.invertCulling = !previousCulling;

                var leftView = stereo ? source.GetStereoViewMatrix(Camera.StereoscopicEye.Left) : source.worldToCameraMatrix;
                var leftProjection = stereo ? source.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left) : source.projectionMatrix;
                var leftVp = RenderEye(context, source, resources, leftView, leftProjection, resources.LeftTexture);
                var rightVp = leftVp;
                var leftEye = (Vector3)leftView.inverse.GetColumn(3);
                var rightEye = leftEye;

                if (stereo)
                {
                    var rightView = source.GetStereoViewMatrix(Camera.StereoscopicEye.Right);
                    rightEye = rightView.inverse.GetColumn(3);
                    rightVp = RenderEye(context, source, resources, rightView, source.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right), resources.RightTexture);
                }

                material.SetTexture(leftTextureId, resources.LeftTexture);
                material.SetTexture(rightTextureId, stereo ? resources.RightTexture : resources.LeftTexture);
                material.SetMatrix(leftMatrixId, leftVp);
                material.SetMatrix(rightMatrixId, rightVp);
                material.SetVector(leftEyeId, leftEye);
                material.SetVector(rightEyeId, rightEye);
                material.SetFloat(availableId, 1f);
            }
            finally
            {
                GL.invertCulling = previousCulling;
                isRendering = false;
            }
        }

        private sealed class CameraResources
        {
            public Camera ReflectionCamera;
            public RenderTexture LeftTexture;
            public RenderTexture RightTexture;
            public bool Stereo;
            public bool Hdr;
        }
    }
}
