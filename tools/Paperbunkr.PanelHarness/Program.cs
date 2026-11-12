using Paperbunkr.App.Services.Reader.Panels;
using Paperbunkr.PanelHarness.Common;
using Paperbunkr.PanelHarness.Modes;

// Dev-only panel detection harness (docs/superpowers/specs/2026-09-28-guided-view-detection-upgrade-design.md and
// docs/superpowers/specs/2026-11-07-panel-model-retraining-design.md). Modes:
//   --probe MODEL                 print a model's input and output shapes
//   --selftest                    check the scoring rules
//   --prelabel SPLIT_DIR          pre-draw YOLO boxes for X-AnyLabeling
//   --order-sheet SPLIT_DIR       number the labelled boxes for the order review (add --apply afterwards)
//   --score SPLIT_DIR             score the pipeline against labelled pages
//   (anything else)               the comics-folder survey (SurveyMode)
// --model PATH points every mode at a different ONNX file.
var a = new Args(args);

if (a.Has("--probe"))
{
    string? model = a.Required("--probe");
    if (model is null)
    {
        return 2;
    }

    using var probe = new Microsoft.ML.OnnxRuntime.InferenceSession(model);
    foreach (var i in probe.InputMetadata) Console.WriteLine($"in  {i.Key}: {i.Value.ElementDataType} [{string.Join(",", i.Value.Dimensions)}]");
    foreach (var o in probe.OutputMetadata) Console.WriteLine($"out {o.Key}: {o.Value.ElementDataType} [{string.Join(",", o.Value.Dimensions)}]");
    return 0;
}

if (a.Has("--selftest"))
{
    return SelfTest.Run();
}

// Without --model, use the repo's bundled model: the harness's own bin has no Models folder, and silently scoring the heuristic instead would be misleading.
string modelPath = a.Str("--model", "");
if (modelPath.Length == 0)
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
        string candidate = Path.Combine(dir.FullName, "src", "Paperbunkr.App", "PanelModel", "panel-detector.onnx");
        if (File.Exists(candidate))
        {
            modelPath = candidate;
            break;
        }
    }
}

if (modelPath.Length > 0)
{
    PanelDetectionService.ModelPath = modelPath;
}

if (a.Has("--prelabel"))
{
    return PrelabelMode.Run(a);
}

if (a.Has("--order-sheet"))
{
    return OrderSheetMode.Run(a);
}

if (a.Has("--score"))
{
    return ScoreMode.Run(a);
}

return SurveyMode.Run(a);
