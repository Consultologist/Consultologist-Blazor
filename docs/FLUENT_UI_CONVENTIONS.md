# Fluent UI conventions

How the web client (`src/Consultologist.Web`, and the shared RCL
`src/Consultologist.UI`) uses **Microsoft.FluentUI.AspNetCore.Components**, so new UI
reads as one Microsoft/Fluent system. Part of epic #885; the button taxonomy below was
codified with #887.

## Buttons — `FluentButton` appearance taxonomy

Every actionable control is a `FluentButton` (not a native `<button>`), choosing its
`Appearance` by role:

| Appearance | Role | Examples |
| --- | --- | --- |
| `Accent` | The one primary/commit action of a surface | Create consult, Publish, Connect LinkedIn |
| `Neutral` | Secondary action | Back to run, Retrieve (link fetch) |
| `Outline` | Secondary action sitting beside an Accent one | Cancel, Discard, Revert |
| `Stealth` | Tertiary / in-row / icon-only | row Move ↑/↓ & Remove, Add entry, Run diagram, overlay close, Remove chips |
| `Lightweight` | Very low-emphasis, link-like | rare |

`ConfirmButton` (`Consultologist.UI`) is the shared arm→confirm wrapper (#820); its
`Native="true"` mode renders a plain `<button>` on purpose (e.g. inside a `<details>`
summary where a `fluent-button` would interfere) and is left as-is.

## Icons

Fluent System Icons via the object pattern, `Size20`, `Regular` weight:

```razor
<FluentButton Appearance="Appearance.Stealth" IconStart="@(new Icons.Regular.Size20.Add())"
              OnClick="@(() => field.AddRow())">Add entry</FluentButton>
```

`_Imports.razor` aliases `Icons = Microsoft.FluentUI.AspNetCore.Components.Icons`. Icon-only
buttons carry an `aria-label` (and usually a `Title`).

## Gotchas

- **Keep an element's `Class` when converting** a native button to `FluentButton`: the class
  renders onto the `<fluent-button>`, so scoped CSS and `.class` test selectors keep matching.
  Tests that used `button[title=…]` or bare `FindAll("button")` must move to
  `fluent-button[…]`.
- **`FluentButton.OnClick` is a component callback, not the DOM `@onclick`** — the
  `@onclick:preventDefault`/`@onclick:stopPropagation` modifiers don't apply to it. Inside a
  `<summary>` or a slot `<label>`, wrap the button in a `<span @onclick:preventDefault
  @onclick:stopPropagation>` so the click doesn't toggle the `<details>` or activate the label
  (the label-activation gotcha, #510/#534). bUnit cannot show label activation — verify live.
- **Design tokens / accent** are set once via `<FluentDesignTheme CustomColor="#0078d4">`
  (`Shared/Header.razor`); see the `--consultologist-*` layer in `wwwroot/css/app.css` (#883).
