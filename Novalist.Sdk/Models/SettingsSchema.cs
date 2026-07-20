using System.Collections.Generic;

namespace Novalist.Sdk.Models;

/// <summary>
/// A declarative, host-renderable description of an extension's advanced
/// settings. A schema is pure data: the host renders a form from
/// <see cref="Fields"/> and hands the edited values back to the extension.
/// Implement <see cref="Hooks.ISettingsSchemaContributor"/> to contribute one.
/// </summary>
public sealed class SettingsSchema
{
    /// <summary>Heading shown above the generated form.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>The fields, in display order.</summary>
    public IReadOnlyList<SettingsField> Fields { get; init; } = [];
}

/// <summary>Editor kind for a <see cref="SettingsField"/>.</summary>
public enum SettingsFieldType
{
    /// <summary>Single-line text input.</summary>
    Text,
    /// <summary>Masked single-line text input (tokens, keys).</summary>
    Password,
    /// <summary>Boolean checkbox.</summary>
    Bool,
    /// <summary>Numeric input (integer or decimal).</summary>
    Number,
    /// <summary>Single-select from <see cref="SettingsField.Options"/>.</summary>
    Select,
    /// <summary>Multi-line text area.</summary>
    Multiline,
    /// <summary>A button that invokes
    /// <see cref="Hooks.ISettingsSchemaContributor.ExecuteSchemaActionAsync"/>
    /// with the field's <see cref="SettingsField.Key"/> as the action id. The
    /// extension can return a refreshed schema (e.g. to populate a field's
    /// <see cref="SettingsField.Suggestions"/> after fetching data).</summary>
    Action
}

/// <summary>
/// One field in a <see cref="SettingsSchema"/>. Values cross the host boundary
/// as strings (bool as "true"/"false", numbers as their invariant string form).
/// </summary>
public sealed class SettingsField
{
    /// <summary>Stable key used when reading the edited value back.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Localized label rendered next to the input.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>The editor kind.</summary>
    public SettingsFieldType Type { get; init; } = SettingsFieldType.Text;

    /// <summary>Current value as a string.</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>Choices for <see cref="SettingsFieldType.Select"/>.</summary>
    public IReadOnlyList<string>? Options { get; init; }

    /// <summary>Optional minimum for <see cref="SettingsFieldType.Number"/>.</summary>
    public double? Min { get; init; }

    /// <summary>Optional maximum for <see cref="SettingsFieldType.Number"/>.</summary>
    public double? Max { get; init; }

    /// <summary>Optional group heading; consecutive fields sharing a group are
    /// rendered together under it.</summary>
    public string? Group { get; init; }

    /// <summary>Optional helper text shown beneath the input.</summary>
    public string? Help { get; init; }

    /// <summary>Optional conditional visibility: the key of another field in the
    /// same schema that this field depends on. When set, the host shows this
    /// field only while that field's current value is one of
    /// <see cref="VisibleWhenValues"/>. Null means the field is always visible.
    /// Useful for provider-specific settings (e.g. show the LM Studio fields only
    /// when the "provider" field is "lmstudio").</summary>
    public string? VisibleWhenKey { get; init; }

    /// <summary>The values of the <see cref="VisibleWhenKey"/> field that make
    /// this field visible. Ignored when <see cref="VisibleWhenKey"/> is null.</summary>
    public IReadOnlyList<string>? VisibleWhenValues { get; init; }

    /// <summary>Optional autocomplete suggestions for a text or password field.
    /// The host renders the input as free-text but offers these as a dropdown of
    /// suggestions (an HTML datalist). Unlike <see cref="Options"/> (which
    /// restricts a <see cref="SettingsFieldType.Select"/> to those choices), a
    /// field with suggestions still accepts any typed value. Typically populated
    /// dynamically by an <see cref="SettingsFieldType.Action"/> — e.g. a
    /// "Refresh models" button that fills a model field's suggestions.</summary>
    public IReadOnlyList<string>? Suggestions { get; init; }
}
