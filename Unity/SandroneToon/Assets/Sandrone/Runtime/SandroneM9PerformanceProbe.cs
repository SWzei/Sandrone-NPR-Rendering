using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SandroneToon
{
    [DisallowMultipleComponent]
    public sealed class SandroneM9PerformanceProbe : MonoBehaviour
    {
        [Serializable] public sealed class CounterRecord
        {
            public string name, category, unit;
            public long meanRaw, maxRaw;
        }

        [Serializable] public sealed class Report
        {
            public string generatedUtc, evidenceSessionId, unityVersion, productName, graphicsApi, graphicsDevice, operatingSystem, quality, renderPipeline, antiAliasing;
            public int width, height, warmupFrames, sampledFrames, frameTimingSamples, cpuFrameSamples, gpuFrameSamples, shadowCascadeCount;
            public float renderScale;
            public bool softShadows;
            public double frameDurationMeanMs, frameDurationMedianMs, frameDurationP95Ms;
            public double cpuFrameMeanMs, cpuFrameMedianMs, cpuFrameP95Ms, cpuFrameMaxMs;
            public double gpuFrameMeanMs, gpuFrameMedianMs, gpuFrameP95Ms, gpuFrameMaxMs;
            public string bandwidthScope = "GPUUploadAndAllocationCountersOnly_ExternalMemoryBandwidthRequiresPIXOrRenderDoc";
            public List<CounterRecord> counters = new();
        }

        private sealed class CounterState : IDisposable
        {
            public string name, category, unit; public ProfilerRecorder recorder; public long sum, max; public int samples;
            public void Sample(){if(!recorder.Valid)return;var value=recorder.LastValue;sum+=value;max=Math.Max(max,value);samples++;}
            public CounterRecord Record()=>new(){name=name,category=category,unit=unit,meanRaw=samples==0?0:sum/samples,maxRaw=max};
            public void Dispose(){if(recorder.Valid)recorder.Dispose();}
        }

        private const int Warmup = 120, Samples = 240;

        private void Start()
        {
            var output = Environment.GetEnvironmentVariable("SANDRONE_M9_REPORT");
            if (string.IsNullOrWhiteSpace(output)) { enabled = false; return; }
            StartCoroutine(Run(output, Environment.GetEnvironmentVariable("SANDRONE_M9_SCREENSHOT")));
        }

        private IEnumerator Run(string output, string screenshot)
        {
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
            for (var i=0;i<Warmup;i++){FrameTimingManager.CaptureFrameTimings();yield return null;}
            var counters=CreateCounters();var durations=new List<double>(Samples);var cpuDurations=new List<double>(Samples);var gpuDurations=new List<double>(Samples);
            double cpuSum=0,gpuSum=0,cpuMax=0,gpuMax=0;var timingCount=0;
            var timings=new FrameTiming[1];
            for(var i=0;i<Samples;i++)
            {
                FrameTimingManager.CaptureFrameTimings();yield return null;durations.Add(Time.unscaledDeltaTime*1000.0);
                if(FrameTimingManager.GetLatestTimings(1,timings)>0)
                {
                    var cpu=timings[0].cpuFrameTime;var gpu=timings[0].gpuFrameTime;
                    if(cpu>0){cpuSum+=cpu;cpuMax=Math.Max(cpuMax,cpu);cpuDurations.Add(cpu);}if(gpu>0){gpuSum+=gpu;gpuMax=Math.Max(gpuMax,gpu);gpuDurations.Add(gpu);}timingCount++;
                }
                foreach(var counter in counters)counter.Sample();
            }
            durations.Sort();cpuDurations.Sort();gpuDurations.Sort();var camera=FindAnyObjectByType<Camera>();var data=camera!=null?camera.GetUniversalAdditionalCameraData():null;
            var pipeline=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            var report=new Report{generatedUtc=DateTime.UtcNow.ToString("O"),evidenceSessionId=Environment.GetEnvironmentVariable("SANDRONE_EVIDENCE_SESSION"),unityVersion=Application.unityVersion,productName=Application.productName,
                graphicsApi=SystemInfo.graphicsDeviceType.ToString(),graphicsDevice=SystemInfo.graphicsDeviceName,operatingSystem=SystemInfo.operatingSystem,
                quality=QualitySettings.names[QualitySettings.GetQualityLevel()],renderPipeline=pipeline==null?"missing":pipeline.name,antiAliasing=data==null?"missing":data.antialiasing.ToString(),width=Screen.width,height=Screen.height,
                warmupFrames=Warmup,sampledFrames=Samples,frameTimingSamples=timingCount,cpuFrameSamples=cpuDurations.Count,gpuFrameSamples=gpuDurations.Count,
                shadowCascadeCount=pipeline==null?0:pipeline.shadowCascadeCount,renderScale=pipeline==null?0:pipeline.renderScale,softShadows=pipeline!=null&&pipeline.supportsSoftShadows,
                frameDurationMeanMs=Mean(durations),frameDurationMedianMs=Percentile(durations,.5),frameDurationP95Ms=Percentile(durations,.95),
                cpuFrameMeanMs=cpuDurations.Count==0?0:cpuSum/cpuDurations.Count,cpuFrameMedianMs=Percentile(cpuDurations,.5),cpuFrameP95Ms=Percentile(cpuDurations,.95),cpuFrameMaxMs=cpuMax,
                gpuFrameMeanMs=gpuDurations.Count==0?0:gpuSum/gpuDurations.Count,gpuFrameMedianMs=Percentile(gpuDurations,.5),gpuFrameP95Ms=Percentile(gpuDurations,.95),gpuFrameMaxMs=gpuMax};
            foreach(var counter in counters){report.counters.Add(counter.Record());counter.Dispose();}
            if(report.cpuFrameMeanMs<=0){var main=report.counters.Find(x=>x.name=="Main Thread");if(main!=null){report.cpuFrameMeanMs=main.meanRaw/1_000_000.0;report.cpuFrameMaxMs=main.maxRaw/1_000_000.0;}}
            if(report.gpuFrameMeanMs<=0){var gpu=report.counters.Find(x=>x.name.IndexOf("GPU Frame",StringComparison.OrdinalIgnoreCase)>=0);if(gpu!=null){report.gpuFrameMeanMs=gpu.meanRaw/1_000_000.0;report.gpuFrameMaxMs=gpu.maxRaw/1_000_000.0;}}
            Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,JsonUtility.ToJson(report,true));
            if(!string.IsNullOrWhiteSpace(screenshot)&&camera!=null)CaptureCamera(camera,screenshot,Screen.width,Screen.height);
            Application.Quit(0);
        }

        private static List<CounterState> CreateCounters()
        {
            var values=new List<CounterState>();
            Add(values,ProfilerCategory.Render,"Batches Count");Add(values,ProfilerCategory.Render,"SetPass Calls Count");Add(values,ProfilerCategory.Render,"Draw Calls Count");
            Add(values,ProfilerCategory.Render,"Triangles Count");Add(values,ProfilerCategory.Render,"Vertices Count");Add(values,ProfilerCategory.Render,"GPU Upload In Frame Bytes");
            Add(values,ProfilerCategory.Memory,"Gfx Used Memory");Add(values,ProfilerCategory.Memory,"Texture Memory");Add(values,ProfilerCategory.Memory,"Render Texture Memory");Add(values,ProfilerCategory.Memory,"Mesh Memory");
            Add(values,ProfilerCategory.Internal,"Main Thread");Add(values,ProfilerCategory.Internal,"Render Thread");Add(values,ProfilerCategory.Render,"GPU Frame Time");
            var handles=new List<ProfilerRecorderHandle>();ProfilerRecorderHandle.GetAvailable(handles);var names=new HashSet<string>(values.ConvertAll(x=>x.name));
            foreach(var handle in handles)
            {
                var description=ProfilerRecorderHandle.GetDescription(handle);var name=description.Name;
                if(names.Contains(name)||!Relevant(name))continue;
                try{var recorder=ProfilerRecorder.StartNew(description.Category,name,300);if(recorder.Valid){values.Add(new CounterState{name=name,category=description.Category.Name,unit=description.UnitType.ToString(),recorder=recorder});names.Add(name);}else recorder.Dispose();}catch(Exception){}
            }
            return values;
        }

        private static void Add(List<CounterState> values,ProfilerCategory category,string name)
        {
            try{var recorder=ProfilerRecorder.StartNew(category,name,300);if(recorder.Valid)values.Add(new CounterState{name=name,category=category.Name,unit="RuntimeDescriptionUnavailable",recorder=recorder});else recorder.Dispose();}catch(Exception){}
        }

        private static bool Relevant(string name)=>name.IndexOf("Batch",StringComparison.OrdinalIgnoreCase)>=0||name.IndexOf("Draw Call",StringComparison.OrdinalIgnoreCase)>=0||
            name.IndexOf("SetPass",StringComparison.OrdinalIgnoreCase)>=0||name.IndexOf("GPU Frame",StringComparison.OrdinalIgnoreCase)>=0||name.IndexOf("GPU Upload",StringComparison.OrdinalIgnoreCase)>=0||
            name.IndexOf("Texture Memory",StringComparison.OrdinalIgnoreCase)>=0||name.IndexOf("Gfx Used",StringComparison.OrdinalIgnoreCase)>=0||name=="Render Thread";

        private static double Mean(List<double> values){double sum=0;foreach(var value in values)sum+=value;return values.Count==0?0:sum/values.Count;}

        private static double Percentile(List<double> sortedValues,double percentile)
        {
            if(sortedValues.Count==0)return 0;
            var index=Mathf.Clamp(Mathf.CeilToInt((float)(sortedValues.Count*percentile))-1,0,sortedValues.Count-1);
            return sortedValues[index];
        }

        private static void CaptureCamera(Camera camera,string path,int width,int height)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));var active=RenderTexture.active;var oldTarget=camera.targetTexture;
            var target=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);var image=new Texture2D(width,height,TextureFormat.RGB24,false);
            try{camera.targetTexture=target;target.Create();camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();WritePpm(path,image.GetPixels32(),width,height);}
            finally{camera.targetTexture=oldTarget;RenderTexture.active=active;Destroy(image);target.Release();Destroy(target);}
        }

        private static void WritePpm(string path,Color32[] pixels,int width,int height)
        {
            using var stream=new FileStream(path,FileMode.Create,FileAccess.Write);using var writer=new BinaryWriter(stream);
            writer.Write(System.Text.Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n"));
            for(var y=height-1;y>=0;y--)for(var x=0;x<width;x++){var p=pixels[y*width+x];writer.Write(p.r);writer.Write(p.g);writer.Write(p.b);}
        }
    }
}
