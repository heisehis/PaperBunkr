using System;
using System.Globalization;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Paperbunkr.App.Controls.EventMap;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.EventMap;

namespace Paperbunkr.App.Views;

/// <summary>
/// Save as image (docs/superpowers/specs/2026-11-11-continuity-map-polish-design.md §4): renders a fresh off-screen map (or Matrix)
/// bound to the same layout, filters and settings, with a title band and a footer mark, through <see cref="ControlImageExporter"/>.
/// No selection, hover or inspector; the "You are here" ring is drawn still. The card look steps down until the image fits 16,000 px.
/// </summary>
public partial class EventMapView
{
    private const double TitleBand = 96, Footer = 40;

    private async void OnSaveImageRequested()
    {
        if (_vm is null || !_vm.CanSaveImage)
        {
            return;
        }

        string name = string.IsNullOrWhiteSpace(_vm.EventName) ? "Map" : _vm.EventName;
        string? path = await new FilePickerService().PickSaveFileAsync("Save map as image", SafeFileName($"{name} {(_vm.IsMatrixMode ? "matrix" : "map")}.png"),
            "png", "PNG image");
        if (path is null || _vm is null)
        {
            return;
        }

        try
        {
            if (BuildExport(name) is not { } export)
            {
                Alert(ActivityAlertSeverity.Warning, "This map is too big to save as one image",
                    "Even at the smallest card size it would be more than 16,000 pixels on a side. Hide some events or series and try again.");
                return;
            }

            ControlImageExporter.RenderToPng(ExportHost, export.Content, export.Size, path);
            Alert(ActivityAlertSeverity.Info, "Map saved as an image", path);
        }
        catch (Exception ex)
        {
            Alert(ActivityAlertSeverity.Error, "Couldn't save the map as an image", ex.Message);
        }
    }

    private (Control Content, Size Size)? BuildExport(string name)
    {
        var vm = _vm!;
        Control body;
        Size bodySize;
        if (vm.IsMatrixMode)
        {
            var matrix = new EventMatrixCanvas { Matrix = vm.Matrix, Cards = vm.Cards, Covers = vm.MatrixCovers };
            matrix.UseSynchronousCovers();
            bodySize = matrix.ContentSize;
            if (bodySize.Width > EventMapExportScale.MaxPixels || bodySize.Height + TitleBand + Footer > EventMapExportScale.MaxPixels)
            {
                return null;
            }

            body = matrix;
        }
        else
        {
            const double rail = 180;
            if (EventMapExportScale.Choose(vm.Layout, vm.Scale, rail, vm.RulerHeight + TitleBand + Footer) is not double scale)
            {
                return null;
            }

            var canvas = new EventMapCanvas
            {
                Layout = vm.Layout,
                Cards = vm.Cards,
                LaneHeaders = vm.LaneHeaders,
                Connectors = vm.Connectors,
                Scale = scale,
                IsLaneRailWide = true,
                RulerHeight = vm.RulerHeight,
                ShowProgressLine = vm.ShowProgressLine,
                Focusable = false,
            };
            canvas.UseSynchronousCovers();
            var total = vm.Layout.TotalSizeFor(EventMapMetrics.ForScale(scale));
            bodySize = new Size(rail + total.Width, vm.RulerHeight + total.Height);
            body = canvas;
        }

        double width = Math.Max(640, bodySize.Width);
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions($"{TitleBand.ToString(CultureInfo.InvariantCulture)},*,{Footer.ToString(CultureInfo.InvariantCulture)}"),
            Background = this.FindResource("PbBgBrush") as IBrush,
            Width = width,
        };

        var title = new StackPanel { Margin = new Thickness(24, 16, 24, 8), Spacing = 4 };
        title.Children.Add(new TextBlock { Text = name, Classes = { "pbTextHeading" }, TextTrimming = TextTrimming.CharacterEllipsis });
        title.Children.Add(new TextBlock { Text = $"{vm.ProgressText} · {vm.StatusText}", Classes = { "pbTextMeta" }, TextTrimming = TextTrimming.CharacterEllipsis });
        title.Children.Add(new TextBlock { Text = $"Saved from Paperbunkr on {DateTime.Now:d MMMM yyyy}", Classes = { "pbTextCaption" } });
        root.Children.Add(title);

        body.Width = bodySize.Width;
        body.Height = bodySize.Height;
        body.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(24, 8), Spacing = 8 };
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://Paperbunkr.App/Assets/paperbunkr-logo-rail.png"));
            footer.Children.Add(new Image { Source = new Bitmap(stream), Height = 20, VerticalAlignment = VerticalAlignment.Center });
        }
        catch (Exception ex) when (ex is IOException or FileNotFoundException or ArgumentException)
        {
            // The mark is decoration; the text says where the image came from.
        }

        footer.Children.Add(new TextBlock { Text = "Paperbunkr", Classes = { "pbTextCaption" }, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        return (root, new Size(width, TitleBand + bodySize.Height + Footer));
    }

    private void Alert(ActivityAlertSeverity severity, string title, string? detail) => _vm?.RaiseActivityAlert(new ActivityAlert
    {
        Severity = severity,
        Title = title,
        Detail = detail,
        DedupeKey = "event-map-export",
    });

    private static string SafeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '-');
        }

        return name;
    }
}
