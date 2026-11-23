using System.Text.Json.Nodes;

namespace Capacitor.Cli.Core.Harness.Antigravity;

/// <summary>
/// Builder + detection for kcap's hook block inside Antigravity's <c>hooks.json</c>.
/// Antigravity's config nests differently from Gemini's shared
/// <c>settings.json</c>: <c>hooks.json</c> is a map of user-named blocks →
/// <c>{ event → entries }</c>, so kcap owns exactly one block (<see cref="BlockName"/>)
/// and never disturbs user blocks.
///
/// Two entry shapes (verified against Antigravity 2.2.1, spike): tool events
/// (<c>PreToolUse</c>/<c>PostToolUse</c>) use <c>[{ matcher, hooks:[…] }]</c>; lifecycle
/// events (<c>PreInvocation</c>/<c>PostInvocation</c>/<c>Stop</c>) use a DIRECT handler
/// list <c>[{ type, command, timeout }]</c> (a matcher there is silently ignored / can
/// suppress the hook). Antigravity's <c>timeout</c> is in <b>seconds</b> (default 30).
/// Only <c>PreInvocation</c> is registered because the dispatcher is a no-op for every
/// other event (the watcher owns transcript tailing and idle session-end); registering
/// <c>PostInvocation</c>/<c>Stop</c>/<c>PreToolUse</c>/<c>PostToolUse</c> would spawn
/// dead hook processes on every tool call and turn. <see cref="BlockReferencesKcap"/>
/// still inspects nested <c>hooks[]</c> entries so older 5-event installs remain
/// detectable for upgrade and uninstall.
/// </summary>
public static class AntigravityHooks {
    public const string BlockName = "kcap";

    /// <summary>Lifecycle events — direct handler list (no matcher). Only <c>PreInvocation</c>
    /// is actionable; the watcher owns transcript capture and session-end.</summary>
    public static readonly string[] LifecycleEvents = ["PreInvocation"];

    /// <summary>Tool events — none registered; the watcher tails the transcript directly.</summary>
    public static readonly string[] ToolEvents = [];

    /// <summary>Antigravity's hook <c>timeout</c> is in seconds (default 30).</summary>
    const int TimeoutSeconds = 15;

    public static string HookCommand(string @event) => $"kcap hook --antigravity {@event}";

    static JsonObject Handler(string @event) =>
        new() {
            ["type"]    = "command",
            ["command"] = HookCommand(@event),
            ["timeout"] = TimeoutSeconds
        };

    /// <summary>Semver of the kcap plugin's manifest/hook layout (NOT the kcap build version —
    /// that's tracked by the sibling <c>.kcap-hooks-version</c> marker). Kept a plain dotted
    /// triple: the GUI was only verified to accept a simple <c>x.y.z</c> (GUI re-test),
    /// so we avoid prerelease/build-metadata suffixes that a stricter loader might reject.</summary>
    public const string PluginVersion = "1.0.0";

    /// <summary>
    /// The plugin manifest (<c>plugin.json</c>) the Antigravity GUI REQUIRES to recognize a
    /// directory under <c>config/plugins/</c> as a loadable plugin. Without this marker the GUI
    /// silently ignores the dir and never loads the sibling <c>hooks.json</c>, so hooks installed
    /// there are invisible to the running IDE.
    /// <c>name</c> is optional (the GUI defaults it to the dir name) but we set it explicitly.
    /// </summary>
    public static JsonObject BuildPluginManifest() =>
        new() {
            ["name"]        = BlockName,
            ["version"]     = PluginVersion,
            ["description"] = "kcap — session capture for Antigravity"
        };

    /// <summary>Builds kcap's block: every event mapped to its (shape-correct) entries.</summary>
    public static JsonObject BuildKcapBlock() {
        var block = new JsonObject();

        foreach (var e in LifecycleEvents)
            block[e] = new JsonArray(Handler(e)); // direct handler list

        foreach (var e in ToolEvents) {
            block[e] = new JsonArray(
                new JsonObject {
                    ["matcher"] = "*",
                    ["hooks"]   = new JsonArray(Handler(e))
                });
        }

        return block;
    }

    /// <summary>
    /// True when the named block (an <c>{ event → entries }</c> object) references a kcap
    /// antigravity command in either entry shape. Also matches the pre-rename
    /// <c>kapacitor hook --antigravity</c> so uninstall/refresh clean up older entries.
    /// </summary>
    public static bool BlockReferencesKcap(JsonNode? block) {
        if (block is not JsonObject events) return false;

        foreach (var (_, entries) in events) {
            if (entries is not JsonArray arr) continue;
            foreach (var entry in arr) {
                if (CommandReferencesKcap(entry?["command"])) return true;                       // lifecycle handler
                if (entry?["hooks"] is JsonArray inner && inner.Any(h => CommandReferencesKcap(h?["command"]))) return true; // tool
            }
        }
        return false;
    }

    static bool CommandReferencesKcap(JsonNode? cmdNode) =>
        cmdNode is JsonValue jv
     && jv.TryGetValue<string>(out var cmd)
     && (cmd.Contains("kcap hook --antigravity",      StringComparison.Ordinal)
      || cmd.Contains("kapacitor hook --antigravity", StringComparison.Ordinal));
}
