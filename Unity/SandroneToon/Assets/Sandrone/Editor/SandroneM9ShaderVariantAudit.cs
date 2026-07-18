using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace SandroneToon.Editor
{
    public sealed class SandroneM9ShaderVariantAudit : IPreprocessShaders
    {
        [Serializable] public sealed class Entry
        {
            public string shader, pass, passType; public int input, removed, retained;
        }
        [Serializable] public sealed class Report
        {
            public string generatedUtc, policy = "SandroneM9ConservativeStrip_v1";
            public string[] rules = {"strip _MAIN_LIGHT_SHADOWS_SCREEN","strip _SHADOWS_SOFT_LOW/_SHADOWS_SOFT_MEDIUM","strip _CASTING_PUNCTUAL_LIGHT_SHADOW"};
            public int callbackCount, inputVariants, removedVariants, retainedVariants;
            public List<Entry> entries = new();
        }

        private static bool active;
        private static readonly Dictionary<string,Entry> Entries = new();
        private static int callbackCount;
        public int callbackOrder => -1000;

        public static void Begin()
        {
            active=true;callbackCount=0;Entries.Clear();
        }

        public static Report Finish(string path)
        {
            var report=new Report{generatedUtc=DateTime.UtcNow.ToString("O"),callbackCount=callbackCount,entries=Entries.Values.OrderBy(x=>x.shader).ThenBy(x=>x.pass).ToList()};
            report.inputVariants=report.entries.Sum(x=>x.input);report.removedVariants=report.entries.Sum(x=>x.removed);report.retainedVariants=report.entries.Sum(x=>x.retained);
            Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(report,true));active=false;return report;
        }

        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if(!active||shader==null||!shader.name.StartsWith("Sandrone/",StringComparison.Ordinal))return;
            callbackCount++;var input=data.Count;var removed=0;
            for(var i=data.Count-1;i>=0;i--)if(ShouldStrip(shader,data[i])){data.RemoveAt(i);removed++;}
            var key=$"{shader.name}|{snippet.passName}|{snippet.passType}";
            if(!Entries.TryGetValue(key,out var entry))Entries[key]=entry=new Entry{shader=shader.name,pass=snippet.passName,passType=snippet.passType.ToString()};
            entry.input+=input;entry.removed+=removed;entry.retained+=data.Count;
        }

        private static bool ShouldStrip(Shader shader,ShaderCompilerData data)
        {
            return Enabled(shader,data,"_MAIN_LIGHT_SHADOWS_SCREEN")||Enabled(shader,data,"_SHADOWS_SOFT_LOW")||
                Enabled(shader,data,"_SHADOWS_SOFT_MEDIUM")||Enabled(shader,data,"_CASTING_PUNCTUAL_LIGHT_SHADOW");
        }

        private static bool Enabled(Shader shader,ShaderCompilerData data,string keyword)=>data.shaderKeywordSet.IsEnabled(new ShaderKeyword(shader,keyword));
    }
}
