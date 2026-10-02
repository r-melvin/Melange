using System.Collections.Generic;
using S1API.Console;

namespace Melange.Smuggling
{
    /// <summary>
    /// The in-game probes (TESTING.md): one console command, <c>smuggling &lt;what&gt;</c>, so each check can be typed or
    /// scripted (S1API's ConsoleHelper.Submit, probe-cmds.txt) instead of waiting days for the boat. S1API finds and
    /// registers it. Every subcommand logs its result to the Melange_Smuggling logger; nothing here is needed for play.
    /// Host only.
    /// </summary>
    public sealed class SmugglingCommand : BaseConsoleCommand
    {
        public override string CommandWord => "smuggling";
        public override string CommandDescription => "Melange Smuggling probes: " + Smuggling.ProbeUsage;
        public override string ExampleUsage => "smuggling status";

        public override void ExecuteCommand(List<string> args)
        {
            var words = new List<string>();
            if (args != null) foreach (var a in args) if (!string.IsNullOrWhiteSpace(a)) words.Add(a.Trim());
            string what = words.Count > 0 ? words[0].ToLowerInvariant() : "status";
            if (words.Count > 0) words.RemoveAt(0);
            string result;
            try { result = Smuggling.Probe(what, words); }
            catch (System.Exception e) { result = "threw: " + e; }
            Mod.Log.Msg($"PROBE {what}{(words.Count > 0 ? " " + string.Join(" ", words) : "")}: {result}");
        }
    }
}
