// ConfigScript hook - the Editor Probe command's paired config script (same Key in plugin.xml).
// A real config script would show/persist its own settings; this just proves invocation happened.
Environment.App.AskQuestion("Configured!", "OK", string.Empty);
return null;
