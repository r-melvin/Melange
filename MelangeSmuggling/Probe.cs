using System.Collections.Generic;
using S1API.Console;

namespace Melange.Smuggling
{
    /// <summary>
    /// The in-game probes (TESTING.md): one console command, <c>smuggling &lt;what&gt;</c>, so each check can be typed or
    /// scripted (S1API's ConsoleHelper.Submit) instead of waiting days for the boat. S1API finds and registers it. Every
    /// subcommand logs its result to the Melange_Smuggling logger; nothing here is needed for play. Host only.
    /// </summary>
    public sealed class SmugglingCommand : BaseConsoleCommand
    {
        public override string CommandWord => "smuggling";
        public override string CommandDescription => "Melange Smuggling probes: status, oscar <spend>, unlock, order, accept, decline, load, sail, return, collect, route [known], tanker, boat";
        public override string ExampleUsage => "smuggling status";

        public override void ExecuteCommand(List<string> args)
        {
            string what = args != null && args.Count > 0 ? args[0] : "status";
            string arg = args != null && args.Count > 1 ? args[1] : null;
            string result;
            try { result = Smuggling.Probe(what, arg); }
            catch (System.Exception e) { result = "threw: " + e; }
            Mod.Log.Msg($"PROBE {what}: {result}");
        }
    }
}
