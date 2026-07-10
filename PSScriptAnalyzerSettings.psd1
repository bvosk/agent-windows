@{
    # Analyze with the complete built-in rule set, but gate only findings that
    # represent likely defects or material maintainability problems.
    IncludeDefaultRules = $true
    Severity = @("Error", "Warning")
}
