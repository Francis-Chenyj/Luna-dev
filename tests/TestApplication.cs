using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;

// Compatibility for existing fault-injection fixtures only. Never part of Luna.exe.
// Ordinary operations use typed services; private-field access is kept here for
// deliberate corrupt-state/clock fixtures while those tests migrate individually.
internal sealed class TestApplication : IDisposable {
    internal readonly Luna.ApplicationRuntime Runtime;
    readonly object[] owners;
    internal TestApplication(string root) {
        Runtime = new Luna.ApplicationRuntime(new Luna.RuntimeOptions(root));
        owners = new object[] { Runtime, Runtime.Options, Runtime.Library, Runtime.Queries, Runtime.Updates,
            Runtime.Storage, Runtime.AppUpdates, Runtime.Preferences, Runtime.Source, Runtime.Releases,
            Runtime.Diagnostics, Runtime.Quarantine, Runtime.Activity };
    }
    const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    internal object Field(string name) {
        if (name == "AppVersion") return Luna.PreferenceStore.AppVersion;
        foreach (var owner in owners) {
            var field = owner.GetType().GetField(name, InstanceFields);
            if (field != null) return field.GetValue(owner);
        }
        throw new MissingFieldException(name);
    }
    internal void Set(string name, object value) {
        foreach (var owner in owners) {
            var field = owner.GetType().GetField(name, InstanceFields);
            if (field != null) { field.SetValue(owner, value); return; }
        }
        throw new MissingFieldException(name);
    }
    internal object Call(string name, params object[] args) {
        if (name == "PrepareMaintenance") { Runtime.AppUpdates.PrepareMaintenance((string)args[0], (string)args[1], Runtime.Updates.CancelLibraryUpdate); return null; }
        foreach (var owner in owners) {
            var method = owner.GetType().GetMethod(name, InstanceFields);
            if (method != null) return Invoke(method, owner, args);
        }
        var helper = typeof(Luna.DataFormat).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
        if (helper != null) return Invoke(helper, null, args);
        var native = typeof(Luna.Program).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
        if (native != null) return Invoke(native, null, args);
        throw new MissingMethodException(name);
    }
    static object Invoke(MethodInfo method, object owner, object[] args) {
        try { return method.Invoke(owner, args); }
        catch (TargetInvocationException ex) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }
    public void Dispose() { Runtime.Dispose(); }
}
