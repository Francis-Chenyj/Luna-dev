using System;
using System.IO;
using System.Linq;
using Microsoft.Win32;

// Historical 1.5.2 -> 1.6.0 verification harness. For current releases use
// run-upgrade-compatibility.ps1 with an unmodified complete predecessor runtime.
internal static class DeploymentIntegration {
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Predecessor(string root, string oldExe) {
        File.Copy(oldExe, Path.Combine(root, "Luna.exe"), true);
        var manifest = Luna.Deployment.Manifest(root, false); manifest["version"] = "1.5.2";
        var row = Luna.Deployment.Array(manifest, "files").Select(Luna.Deployment.Map).Single(r => Luna.Deployment.Text(r, "path") == "Luna.exe");
        row["sha256"] = Luna.Deployment.HashFile(Path.Combine(root, "Luna.exe")); row["bytes"] = new FileInfo(Path.Combine(root, "Luna.exe")).Length;
        Luna.Deployment.Write(Path.Combine(root, "installed-files.json"), manifest);
    }
    static void Stage(string root, string archive, string envelope) {
        string area = Path.Combine(root, "cache/app-update"); Directory.CreateDirectory(area);
        File.Copy(archive, Path.Combine(area, "package.zip")); File.Copy(envelope, Path.Combine(area, "release.json"));
        Luna.Deployment.ExtractVerified(archive, Path.Combine(area, "stage"), Luna.Deployment.VerifyEnvelope(File.ReadAllText(envelope)));
    }
    static int Main(string[] args) {
        try {
            if (args.Length == 1) return Luna.Deployment.HealthCheck(args[0]) ? 0 : 1;
            if (args.Length == 2 && args[0] == "--cleanup-fixture") {
                if (!Path.GetFullPath(args[1]).Contains("\\verification\\deployment-integration\\installed-runtime")) throw new IOException("Not the known failed fixture");
                Luna.Deployment.Uninstall(args[1], true); return 0;
            }
            // runtime, old exe, signed ZIP, envelope, new empty test output.
            string source = args[0], output = args[4]; if (Directory.Exists(output)) throw new IOException("Use a new integration directory");
            Directory.CreateDirectory(output); string root = Path.Combine(output, "installed-runtime");
            Luna.Deployment.Install(source, root);
            string registry;
            const string uninstall = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
            using (var entries = Registry.CurrentUser.OpenSubKey(uninstall)) {
                Check(entries != null, "Uninstall registration container missing");
                var matches = entries.GetSubKeyNames().Where(name => {
                    using (var entry = entries.OpenSubKey(name)) return entry != null && (string)entry.GetValue("InstallLocation") == root;
                }).ToArray();
                Check(matches.Length == 1, "Expected one installation registration for the fixture directory");
                registry = uninstall + @"\" + matches[0];
            }
            using (var key = Registry.CurrentUser.OpenSubKey(registry)) Check(key != null && (string)key.GetValue("InstallLocation") == root, "Install registration missing");
            const string preferences = "{\"schema\":1,\"language\":\"en\",\"theme\":\"dark\",\"autoUpdate\":false,\"appAutoCheck\":false,\"appAutoDownload\":false,\"closeAction\":\"ask\"}";
            File.WriteAllText(Path.Combine(root, "cache/settings.json"), preferences); File.WriteAllText(Path.Combine(root, "my-notes.txt"), "personal sentinel");
            Predecessor(root, args[1]); Stage(root, args[2], args[3]);
            string area = Path.Combine(root, "cache/app-update");
            Luna.Deployment.Apply(root, Path.Combine(area, "stage"), Path.Combine(area, "release.json"), Luna.Deployment.HealthCheck);
            Check(Luna.Deployment.Text(Luna.Deployment.Manifest(root, true), "version") == "1.6.0", "Real update failed");
            Check(File.ReadAllText(Path.Combine(root, "cache/settings.json")) == preferences, "Update changed preferences");
            Check(Luna.Deployment.Text(Luna.Deployment.Manifest(Path.Combine(area, "rollback"), true), "version") == "1.5.2", "Old executable backup missing");
            Check(File.Exists(Path.Combine(root, "my-notes.txt")), "Update removed personal file");
            Console.WriteLine("PASS: real 1.5.2 executable -> signed 1.6.0 package; new process, actual catalog + matchup health probe; settings and user files retained");
            Luna.Deployment.Uninstall(root, false);
            Check(!File.Exists(Path.Combine(root, "Luna.exe")) && File.ReadAllText(Path.Combine(root, "cache/settings.json")) == preferences && File.Exists(Path.Combine(root, "my-notes.txt")), "Keep-data uninstall failed");
            using (var key = Registry.CurrentUser.OpenSubKey(registry)) Check(key == null, "Uninstall registration was left behind");
            Console.WriteLine("PASS: per-user install registration and keep-data uninstall of a full runtime");
            string failRoot = Path.Combine(output, "rollback-runtime"); Luna.Deployment.Install(source, failRoot); Predecessor(failRoot, args[1]); Stage(failRoot, args[2], args[3]);
            string failArea = Path.Combine(failRoot, "cache/app-update"); bool rejected = false;
            try { Luna.Deployment.Apply(failRoot, Path.Combine(failArea, "stage"), Path.Combine(failArea, "release.json"), r => false); } catch (IOException) { rejected = true; }
            Check(rejected && Luna.Deployment.HashFile(Path.Combine(failRoot, "Luna.exe")) == Luna.Deployment.HashFile(args[1]), "Real executable rollback failed");
            Check(Luna.Deployment.HealthCheck(failRoot), "Restored old executable cannot query local data");
            Luna.Deployment.Uninstall(failRoot, true);
            Console.WriteLine("PASS: failed new-version check restores real old executable; old catalog + matchup check succeeds; full data removal stays in fixture");
            Luna.Deployment.Write(Path.Combine(output, "result.json"), new { passed = true, realExecutableUpgrade = true, realRollbackQuery = true, installationRegistration = true, retainedPreferences = true, explicitDataRemoval = true });
            return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
