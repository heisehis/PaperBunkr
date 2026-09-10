using System;
using System.Linq;
using Paperbunkr.App.Models;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data.Entities;
using Xunit;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Pure-logic coverage for <see cref="SmartListConditionViewModel"/>'s <c>SuggestBox</c> string
/// projections (docs/superpowers/specs/2026-09-10-suggestbox-migration-plan.md). The rule-builder
/// pickers moved off <c>ComboBox</c>; each <c>SelectedXxxText</c> routes through the object
/// property so the existing field/operator cascade still fires.
/// </summary>
public class SmartListConditionViewModelTests
{
    private static SmartListConditionViewModel Create(out SmartListCondition condition)
    {
        condition = new SmartListCondition { Field = SmartListField.Genre, Value = "" };
        var captured = condition;
        return new SmartListConditionViewModel(captured, SmartListTargetKind.Issue, _ => { }, () => { });
    }

    [Fact]
    public void SelectedFieldText_SetToAKnownLabel_ChangesTheField()
    {
        var vm = Create(out var condition);
        var target = vm.FieldOptions.First(o => o.Field != condition.Field);

        vm.SelectedFieldText = target.Label;

        Assert.Equal(target.Field, condition.Field);
        Assert.Equal(target.Label, vm.SelectedFieldText);
        Assert.Equal(vm.FieldOptions.Select(o => o.Label), vm.FieldNames);
    }

    [Fact]
    public void SelectedFieldText_SetToUnknownText_IsANoOp()
    {
        var vm = Create(out var condition);
        var before = condition.Field;

        vm.SelectedFieldText = "Not A Field";

        Assert.Equal(before, condition.Field);
    }

    [Fact]
    public void SelectedOperatorText_RoundTripsWithinTheCurrentField()
    {
        var vm = Create(out var condition);
        var target = vm.OperatorOptions.First(o => o.Operator != condition.Operator);

        vm.SelectedOperatorText = target.Label;

        Assert.Equal(target.Operator, condition.Operator);
        Assert.Equal(target.Label, vm.SelectedOperatorText);
        Assert.Equal(vm.OperatorOptions.Select(o => o.Label), vm.OperatorNames);
    }

    [Fact]
    public void ChangingTheField_RepopulatesOperatorNames()
    {
        var vm = Create(out _);
        var allProperties = vm.FieldOptions.First(o => o.Field == SmartListField.AllProperties);

        vm.SelectedFieldText = allProperties.Label;

        Assert.Equal(vm.OperatorOptions.Select(o => o.Label), vm.OperatorNames);
        Assert.NotEmpty(vm.SearchModeNames);
    }
}
