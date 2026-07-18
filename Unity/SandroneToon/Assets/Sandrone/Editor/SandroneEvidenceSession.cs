using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace SandroneToon.Editor
{
    /// <summary>
    /// Binds generated reports and captures to one explicitly-started verification run.
    /// The final manifest records content hashes for the complete formal project input set
    /// and for every artifact generated after the session start. It is deliberately kept
    /// independent of individual phase validators so stale phase output cannot satisfy a gate.
    /// </summary>
    public static class SandroneEvidenceSession
    {
        public const string SchemaVersion = "SandroneEvidence_v1";

        [Serializable]
        public sealed class FileRecord
        {
            public string path;
            public long bytes;
            public long lastWriteUtcTicks;
            public string sha256;
        }

        [Serializable]
        public sealed class SessionRecord
        {
            public string schemaVersion = SchemaVersion;
            public string sessionId;
            public string label;
            public string status;
            public string startedUtc;
            public string finalizedUtc;
            public string unityVersion;
            public string editorGraphicsApi;
            public string editorGraphicsDevice;
            public string formalTarget = "Windows PC";
            public string sourceAggregateSha256;
            public int sourceFileCount;
            public int artifactFileCount;
            public List<FileRecord> sources = new();
            public List<FileRecord> artifacts = new();
            public List<string> verificationFailures = new();
        }

        [Serializable]
        private sealed class ReportEnvelope
        {
            public string generatedUtc;
            public string evidenceSessionId;
            public int failureCount;
            public List<string> failures;
        }

        [Serializable]
        public sealed class NegativeTestReport
        {
            public string generatedUtc;
            public string sessionId;
            public bool baselineAccepted;
            public bool artifactTamperRejected;
            public bool staleArtifactRejected;
            public bool sourceFingerprintTamperRejected;
            public List<string> details = new();
        }

        private static readonly string[] SourceRoots =
        {
            "Assets/Sandrone", "Assets/Settings", "Packages", "ProjectSettings"
        };

        private static readonly string[] ArtifactRoots =
        {
            "TestArtifacts/M0", "TestArtifacts/M1", "TestArtifacts/M2", "TestArtifacts/M3",
            "TestArtifacts/M4", "TestArtifacts/M5", "TestArtifacts/M6", "TestArtifacts/M7",
            "TestArtifacts/M8", "TestArtifacts/M9", "TestArtifacts/M9GameViewAudit",
            "TestArtifacts/Audit"
        };

        private static readonly string[] RequiredSuccessfulReports =
        {
            "TestArtifacts/M0/M0ValidationReport.json",
            "TestArtifacts/M1/M1ValidationReport.json",
            "TestArtifacts/M2/M2ValidationReport.json",
            "TestArtifacts/M3/M3ValidationReport.json",
            "TestArtifacts/M4/M4ValidationReport.json",
            "TestArtifacts/M5/M5ValidationReport.json",
            "TestArtifacts/M6/M6ValidationReport.json",
            "TestArtifacts/M7/M7ValidationReport.json",
            "TestArtifacts/M8/M8ValidationReport.json",
            "TestArtifacts/M9/M9ValidationReport.json",
            "TestArtifacts/M9/Api/D3D11/M9ValidationReport.json",
            "TestArtifacts/M9/Api/D3D12/M9ValidationReport.json",
            "TestArtifacts/Audit/M0M4RegressionAudit.json",
            "TestArtifacts/M9GameViewAudit/D3D11/M9GameViewAudit.json",
            "TestArtifacts/M9GameViewAudit/D3D12/M9GameViewAudit.json",
            "TestArtifacts/Audit/CascadeShadowContract/D3D11/CascadeShadowAudit.json",
            "TestArtifacts/Audit/CascadeShadowContract/D3D12/CascadeShadowAudit.json",
            "TestArtifacts/Audit/Lifecycle/MpbLifecycleAudit.json"
        };

        private static readonly string[] RequiredArtifacts =
        {
            "TestArtifacts/M9/M9PlayerBuildReport.json",
            "TestArtifacts/M9/M9PlayerPerformance.json",
            "TestArtifacts/M9/M9ShaderVariantReport.json",
            "TestArtifacts/M9/Player/M9Player_Final.ppm",
            "TestArtifacts/M9GameViewAudit/D3D11/GameView_M9_Final_SMAA_768x1280.png",
            "TestArtifacts/M9GameViewAudit/D3D12/GameView_M9_Final_SMAA_768x1280.png"
        };

        public static string CurrentSessionId
        {
            get
            {
                var record = LoadActive();
                return record?.status == "collecting" ? record.sessionId : string.Empty;
            }
        }

        [MenuItem("Sandrone/Audit Evidence/Begin Fresh Session")]
        public static void BeginFromCommandLine() => BeginFresh("M0-M9 Windows PC verification");

        public static SessionRecord BeginFresh(string label)
        {
            Directory.CreateDirectory(EvidenceRoot());
            var record = new SessionRecord
            {
                sessionId = Guid.NewGuid().ToString("N"),
                label = label,
                status = "collecting",
                startedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                unityVersion = Application.unityVersion,
                editorGraphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                editorGraphicsDevice = SystemInfo.graphicsDeviceName
            };
            WriteJson(ActivePath(), record);
            if (File.Exists(FinalPath())) File.Delete(FinalPath());
            Debug.Log($"[Sandrone Evidence] Began {record.sessionId} at {record.startedUtc}.");
            return record;
        }

        public static SessionRecord EnsureActive(string label)
        {
            var record = LoadActive();
            return record != null && record.status == "collecting" ? record : BeginFresh(label);
        }

        public static bool IsCurrentSuccessfulReport(string projectRelativePath, out string details)
        {
            var session = LoadActive();
            if (session == null || session.status != "collecting")
            {
                details = "no collecting evidence session";
                return false;
            }
            return ValidateReport(ProjectPath(projectRelativePath), ParseUtc(session.startedUtc), session.sessionId, out details);
        }

        [MenuItem("Sandrone/Audit Evidence/Finalize And Verify Session")]
        public static void FinalizeFromCommandLine()
        {
            var record = LoadActive() ?? throw new BuildFailedException("No evidence session is active.");
            if (record.status != "collecting" && record.status != "verified")
                throw new BuildFailedException("Evidence session cannot be finalized from status: " + record.status);
            record.status = "collecting";
            var started = ParseUtc(record.startedUtc);
            record.verificationFailures.Clear();

            foreach (var relative in RequiredSuccessfulReports)
            {
                if (!ValidateReport(ProjectPath(relative), started, record.sessionId, out var details))
                    record.verificationFailures.Add(relative + ": " + details);
            }
            foreach (var relative in RequiredArtifacts)
            {
                var path = ProjectPath(relative);
                if (!File.Exists(path)) record.verificationFailures.Add(relative + ": missing");
                else if (File.GetLastWriteTimeUtc(path) < started) record.verificationFailures.Add(relative + ": stale last-write time");
                else if (new FileInfo(path).Length == 0) record.verificationFailures.Add(relative + ": empty");
            }

            record.sources = EnumerateSourceFiles().Select(ToRecord).ToList();
            record.sourceFileCount = record.sources.Count;
            record.sourceAggregateSha256 = Aggregate(record.sources);
            record.artifacts = EnumerateArtifactFiles(started).Select(ToRecord).ToList();
            record.artifactFileCount = record.artifacts.Count;
            record.finalizedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            record.status = record.verificationFailures.Count == 0 ? "verified" : "failed";
            WriteJson(FinalPath(), record);
            WriteJson(ActivePath(), record);
            if (record.verificationFailures.Count > 0)
                throw new BuildFailedException("Evidence finalization failed:\n" + string.Join("\n", record.verificationFailures));
            Debug.Log($"[Sandrone Evidence] Verified {record.sessionId}: inputs={record.sourceFileCount}, artifacts={record.artifactFileCount}, source={record.sourceAggregateSha256}.");
        }

        [MenuItem("Sandrone/Audit Evidence/Verify Final Manifest")]
        public static void VerifyFinalFromCommandLine()
        {
            if (!VerifyFinal(out var failures)) throw new BuildFailedException("Evidence verification failed:\n" + string.Join("\n", failures));
            Debug.Log("[Sandrone Evidence] Final manifest and all recorded hashes are current.");
        }

        public static bool VerifyFinal(out List<string> failures)
        {
            failures = new List<string>();
            var record = LoadJson<SessionRecord>(FinalPath());
            if (record == null || record.schemaVersion != SchemaVersion || record.status != "verified")
            {
                failures.Add("missing, incompatible or non-verified final manifest");
                return false;
            }
            VerifyRecords(record.sources, failures, "source");
            VerifyRecords(record.artifacts, failures, "artifact");
            var currentSources = EnumerateSourceFiles().Select(ToRecord).ToList();
            var aggregate = Aggregate(currentSources);
            if (currentSources.Count != record.sourceFileCount || aggregate != record.sourceAggregateSha256)
                failures.Add($"source fingerprint mismatch: expected {record.sourceFileCount}/{record.sourceAggregateSha256}, current {currentSources.Count}/{aggregate}");
            return failures.Count == 0;
        }

        [MenuItem("Sandrone/Audit Evidence/Run Negative Tests")]
        public static void RunNegativeTestsFromCommandLine()
        {
            var manifest = LoadJson<SessionRecord>(FinalPath()) ?? throw new BuildFailedException("Final manifest missing.");
            var report = new NegativeTestReport
            {
                generatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                sessionId = manifest.sessionId,
                baselineAccepted = VerifyFinal(out var baselineFailures)
            };
            report.details.Add("baseline=" + string.Join(" | ", baselineFailures));

            var target = manifest.artifacts.FirstOrDefault(x => x.path.EndsWith("M9ValidationReport.json", StringComparison.Ordinal));
            if (target == null) throw new BuildFailedException("No M9 validation artifact in manifest.");
            var targetPath = ProjectPath(target.path);
            var bytes = File.ReadAllBytes(targetPath);
            var writeTime = File.GetLastWriteTimeUtc(targetPath);
            try
            {
                File.WriteAllBytes(targetPath, bytes.Concat(new byte[] { 0x20 }).ToArray());
                report.artifactTamperRejected = !VerifyFinal(out var tamperFailures) && tamperFailures.Any(x => x.Contains(target.path, StringComparison.Ordinal));
                report.details.Add("tamper=" + string.Join(" | ", tamperFailures));
            }
            finally
            {
                File.WriteAllBytes(targetPath, bytes);
                File.SetLastWriteTimeUtc(targetPath, writeTime);
            }

            try
            {
                File.SetLastWriteTimeUtc(targetPath, ParseUtc(manifest.startedUtc).AddMinutes(-1));
                report.staleArtifactRejected = !VerifyFinal(out var staleFailures) && staleFailures.Any(x => x.Contains("last-write", StringComparison.Ordinal));
                report.details.Add("stale=" + string.Join(" | ", staleFailures));
            }
            finally
            {
                File.SetLastWriteTimeUtc(targetPath, writeTime);
            }

            var originalAggregate = manifest.sourceAggregateSha256;
            manifest.sourceAggregateSha256 = new string('0', 64);
            WriteJson(FinalPath(), manifest);
            try
            {
                report.sourceFingerprintTamperRejected = !VerifyFinal(out var sourceFailures) && sourceFailures.Any(x => x.Contains("source fingerprint mismatch", StringComparison.Ordinal));
                report.details.Add("source=" + string.Join(" | ", sourceFailures));
            }
            finally
            {
                manifest.sourceAggregateSha256 = originalAggregate;
                WriteJson(FinalPath(), manifest);
            }

            var passed = report.baselineAccepted && report.artifactTamperRejected && report.staleArtifactRejected && report.sourceFingerprintTamperRejected;
            WriteJson(Path.Combine(EvidenceRoot(), "NegativeTests.json"), report);
            if (!passed) throw new BuildFailedException("One or more evidence negative tests did not reject invalid evidence.");
            Debug.Log("[Sandrone Evidence] Negative tests passed: tamper, stale artifact and source fingerprint drift rejected.");
        }

        private static bool ValidateReport(string path, DateTime started, string sessionId, out string details)
        {
            if (!File.Exists(path)) { details = "missing"; return false; }
            if (File.GetLastWriteTimeUtc(path) < started) { details = "stale last-write time"; return false; }
            ReportEnvelope envelope;
            try { envelope = JsonUtility.FromJson<ReportEnvelope>(File.ReadAllText(path)); }
            catch (Exception exception) { details = "invalid JSON: " + exception.Message; return false; }
            if (envelope == null || !DateTime.TryParse(envelope.generatedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var generated))
            { details = "missing/invalid generatedUtc"; return false; }
            if (generated.ToUniversalTime() < started) { details = "stale generatedUtc"; return false; }
            if (envelope.failureCount != 0 || (envelope.failures != null && envelope.failures.Count != 0))
            { details = $"reported failures: count={envelope.failureCount}, list={envelope.failures?.Count ?? 0}"; return false; }
            if (!string.IsNullOrEmpty(envelope.evidenceSessionId) && envelope.evidenceSessionId != sessionId)
            { details = "session id mismatch"; return false; }
            details = $"current successful report, generated={generated:O}";
            return true;
        }

        private static void VerifyRecords(IEnumerable<FileRecord> records, ICollection<string> failures, string kind)
        {
            foreach (var record in records)
            {
                var path = ProjectPath(record.path);
                if (!File.Exists(path)) { failures.Add($"{kind} missing: {record.path}"); continue; }
                var info = new FileInfo(path);
                if (info.Length != record.bytes) failures.Add($"{kind} size mismatch: {record.path}");
                if (info.LastWriteTimeUtc.Ticks != record.lastWriteUtcTicks) failures.Add($"{kind} last-write mismatch: {record.path}");
                if (Sha256(path) != record.sha256) failures.Add($"{kind} hash mismatch: {record.path}");
            }
        }

        private static IEnumerable<string> EnumerateSourceFiles() => SourceRoots
            .Select(ProjectPath).Where(Directory.Exists)
            .SelectMany(path => Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            .Where(path => !path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .OrderBy(Relative, StringComparer.Ordinal);

        private static IEnumerable<string> EnumerateArtifactFiles(DateTime started) => ArtifactRoots
            .Select(ProjectPath).Where(Directory.Exists)
            .SelectMany(path => Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            .Where(path => File.GetLastWriteTimeUtc(path) >= started)
            .Where(path => !path.StartsWith(EvidenceRoot(), StringComparison.OrdinalIgnoreCase))
            // Unity can append shutdown diagnostics to the command-line log after this method
            // writes the manifest. Such process-owned logs cannot be immutable evidence inputs.
            .Where(path => !path.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
            .OrderBy(Relative, StringComparer.Ordinal);

        private static FileRecord ToRecord(string path)
        {
            var info = new FileInfo(path);
            return new FileRecord { path = Relative(path), bytes = info.Length, lastWriteUtcTicks = info.LastWriteTimeUtc.Ticks, sha256 = Sha256(path) };
        }

        private static string Aggregate(IEnumerable<FileRecord> records)
        {
            using var sha = SHA256.Create();
            var payload = string.Join("\n", records.OrderBy(x => x.path, StringComparer.Ordinal)
                .Select(x => $"{x.path}\t{x.bytes}\t{x.sha256}"));
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(payload))).Replace("-", string.Empty);
        }

        private static string Sha256(string path)
        {
            using var sha = SHA256.Create();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
        }

        private static SessionRecord LoadActive() => LoadJson<SessionRecord>(ActivePath());
        private static T LoadJson<T>(string path) where T : class
        {
            if (!File.Exists(path)) return null;
            try { return JsonUtility.FromJson<T>(File.ReadAllText(path)); }
            catch { return null; }
        }
        private static void WriteJson(string path, object value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? EvidenceRoot());
            File.WriteAllText(path, JsonUtility.ToJson(value, true));
        }
        private static DateTime ParseUtc(string value) => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
        private static string ProjectRoot() => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        private static string ProjectPath(string relative) => Path.GetFullPath(Path.Combine(ProjectRoot(), relative.Replace('/', Path.DirectorySeparatorChar)));
        private static string Relative(string absolute) => Path.GetRelativePath(ProjectRoot(), absolute).Replace('\\', '/');
        private static string EvidenceRoot() => ProjectPath("TestArtifacts/Audit/Evidence");
        private static string ActivePath() => Path.Combine(EvidenceRoot(), "ActiveSession.json");
        private static string FinalPath() => Path.Combine(EvidenceRoot(), "EvidenceManifest.json");
    }
}
