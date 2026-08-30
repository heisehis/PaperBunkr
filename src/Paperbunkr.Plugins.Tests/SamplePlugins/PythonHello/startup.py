# Python Hello - Startup hook (docs/superpowers/specs/2026-08-30-python-plugin-scripting-design.md).
# Proves PythonCommand works end-to-end through the real PluginEngine, not just in isolation - the
# Python counterpart of the DuplicateFinder sample plugin's own startup.csx.
def on_startup(globals):
    return "Python Hello is active for this session."
