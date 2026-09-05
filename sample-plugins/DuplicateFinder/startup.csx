// Duplicate Finder - Startup hook (docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md §7).
// Every Startup command just needs to prove it ran once per session - real plugins would use this
// hook for one-time setup (registering state, warming a cache, etc).
return "Duplicate Finder is active for this session.";
