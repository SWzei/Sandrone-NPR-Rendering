using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SandroneToon
{
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class SandroneM9FinalController : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        [SerializeField] private Volume gradingVolume;
        [SerializeField] private SandroneM9FinalProfile profile;
        [SerializeField] private bool gradingEnabled = true;
        [SerializeField] private bool antiAliasingEnabled = true;
        [SerializeField] private bool mobileQuality;
        private bool snapshotCaptured,cacheValid,snapshotCameraHdr,snapshotPostProcessing;
        private float snapshotVolumeWeight;
        private AntialiasingMode snapshotAntialiasing;
        private AntialiasingQuality snapshotAntialiasingQuality;
        private bool cachedGrading,cachedAntiAliasing,cachedMobileQuality;

        public Camera TargetCamera => targetCamera;
        public Volume GradingVolume => gradingVolume;
        public SandroneM9FinalProfile Profile => profile;
        public bool GradingEnabled { get => gradingEnabled; set { gradingEnabled = value; if(isActiveAndEnabled)Apply(); } }
        public bool AntiAliasingEnabled { get => antiAliasingEnabled; set { antiAliasingEnabled = value; if(isActiveAndEnabled)Apply(); } }
        public bool MobileQuality { get => mobileQuality; set { mobileQuality = value; if(isActiveAndEnabled)Apply(); } }

        public void Configure(Camera camera, Volume volume, SandroneM9FinalProfile finalProfile)
        {
            RestoreSnapshot();
            targetCamera = camera;
            gradingVolume = volume;
            profile = finalProfile;
            if(isActiveAndEnabled){CaptureSnapshot();Apply(true);}
        }

        public void Apply(bool force=false)
        {
            var desiredWeight=gradingEnabled?1f:0f;var desiredAa=!antiAliasingEnabled?AntialiasingMode.None:mobileQuality?AntialiasingMode.FastApproximateAntialiasing:AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var cameraData=targetCamera!=null?targetCamera.GetUniversalAdditionalCameraData():null;
            var directStateMatches=(gradingVolume==null||Mathf.Approximately(gradingVolume.weight,desiredWeight))&&
                                   (targetCamera==null||(targetCamera.allowHDR&&cameraData!=null&&cameraData.renderPostProcessing&&cameraData.antialiasing==desiredAa&&cameraData.antialiasingQuality==AntialiasingQuality.High));
            if(!force&&cacheValid&&directStateMatches&&gradingEnabled==cachedGrading&&antiAliasingEnabled==cachedAntiAliasing&&mobileQuality==cachedMobileQuality)return;
            if (gradingVolume != null) gradingVolume.weight = desiredWeight;
            if (targetCamera == null) return;
            targetCamera.allowHDR = true;
            var data = cameraData;
            data.renderPostProcessing = true;
            data.antialiasing = desiredAa;
            data.antialiasingQuality = AntialiasingQuality.High;
            cacheValid=true;cachedGrading=gradingEnabled;cachedAntiAliasing=antiAliasingEnabled;cachedMobileQuality=mobileQuality;
        }

        private void CaptureSnapshot()
        {
            if(snapshotCaptured)return;if(gradingVolume!=null)snapshotVolumeWeight=gradingVolume.weight;
            if(targetCamera!=null){var data=targetCamera.GetUniversalAdditionalCameraData();snapshotCameraHdr=targetCamera.allowHDR;snapshotPostProcessing=data.renderPostProcessing;snapshotAntialiasing=data.antialiasing;snapshotAntialiasingQuality=data.antialiasingQuality;}
            snapshotCaptured=true;
        }
        private void RestoreSnapshot()
        {
            if(!snapshotCaptured)return;if(gradingVolume!=null)gradingVolume.weight=snapshotVolumeWeight;
            if(targetCamera!=null){targetCamera.allowHDR=snapshotCameraHdr;var data=targetCamera.GetUniversalAdditionalCameraData();data.renderPostProcessing=snapshotPostProcessing;data.antialiasing=snapshotAntialiasing;data.antialiasingQuality=snapshotAntialiasingQuality;}
            snapshotCaptured=false;cacheValid=false;
        }
        private void OnEnable(){CaptureSnapshot();Apply(true);}
        private void OnDisable()=>RestoreSnapshot();
        private void OnDestroy()=>RestoreSnapshot();
        private void OnValidate(){if(isActiveAndEnabled){CaptureSnapshot();Apply(true);}}
        private void LateUpdate() => Apply();
    }
}
