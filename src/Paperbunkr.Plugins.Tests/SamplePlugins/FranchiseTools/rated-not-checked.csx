// Franchise Tools - CreateBookList hook (Plugin API v3: IRulesEngine). Dynamic Smart List entry:
// every book with a rating set that hasn't been marked Checked yet - the "I rated it but forgot to
// tick it off" gap. Runs the app's own Smart List matcher instead of hand-rolling the filter.
var rule = PluginConditionGroup.And(
    new PluginCondition(SmartListField.Rating, SmartListOperator.GreaterThan, "0"),
    new PluginCondition(SmartListField.Checked, SmartListOperator.Is, "false"));

return Environment.Rules.Evaluate(rule).ToList();
