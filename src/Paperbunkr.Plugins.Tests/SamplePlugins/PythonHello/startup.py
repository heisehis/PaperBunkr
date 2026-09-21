# Python Hello - Startup hook (docs/superpowers/specs/2026-08-30-python-plugin-scripting-design.md).
# Proves PythonCommand works end-to-end through the real PluginEngine, not just in isolation - the
# Python counterpart of the DuplicateFinder sample plugin's own startup.csx.
#
# Also shows the Plugin API 4.1 Activity reporter (docs/superpowers/specs/2026-09-20-plugin-api-4-1-
# design.md section 4): a job that appears in the Activity Center, attributed to this plugin.
def on_startup(globals):
    job = globals.Environment.Activity.StartJob("Saying hello")
    job.Report("Starting up")
    job.Succeed("Python Hello is ready")
    return "Python Hello is active for this session."
