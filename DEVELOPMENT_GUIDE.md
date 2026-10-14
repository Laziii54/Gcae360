# OBD360 development guide

Short, project-level rules for everyone (and every tool) that changes the OBD360 app. Rules that already live elsewhere are
linked, not repeated: `PRODUCTION_CHECKLIST.md`, `BUILD_IOS_ON_MAC.md`, `OBD360_PROGRESS.md`.

---

## Icon policy

> **NO EMOJI-STYLE ICONS IN THE OBD360 USER INTERFACE.**

This is a global, permanent design rule. It applies to every page, shared component, dialog, notification, report, e-mail
and web page that a person can see, in every language.

### What is forbidden

* Emoji of any kind (fuel pump, lightning, car, flag, magnifier, wrench, charts, check-mark boxes, ...).
* Unicode symbols that stand in for an icon: check marks and crosses (`✓ ✕ ✗`), stars (`★ ☆`), bullets and dots used as status
  (`● ○ ◐`), triangles and play/pause glyphs (`▲ ▼ ▶ ⏸`), warning/info signs, hamburger `☰`, refresh `↻`, flags, and a lone `›`,
  `‹` or `×` used as a chevron or close button.
* Emoji written as HTML entities (`&#9889;`) or escapes.
* Coloured pictograms and flag images/emoji for languages (a two-letter badge such as `KA`, `EN`, `DE` is used instead).
* Replacing one emoji with another, or with a coloured pictogram.

### What to do instead

1. **Prefer text.** Most elements need no icon. A label or a status word ("Ready", "Not reported by this car", "Needs
   attention", "Engine running") is clearer than a symbol. Remove decorative icons rather than replacing them.
2. **Use the shared icon set when an icon really helps**: close, chevrons, play/pause/stop, record, marker flag, expand, menu,
   refresh, favourite star, send, and the status icons check / warning / info / minus / circle / approx.
3. **Never carry meaning by an icon or a colour alone.** Keep the text label or status word next to a status icon, and give every
   icon-only button a `SemanticProperties.Description` (these English descriptions are also translated through the language
   packs - see `LanguagePacks.Extra28.cs`).

### The icon system

| Piece | File | Purpose |
|---|---|---|
| `IconCatalog` | `Controls/IconCatalog.cs` | The only list of icons: name -> path data on a 24 x 24 grid. |
| `Icon` | `Controls/Icon.cs` | Draws one catalog icon. Theme text colour by default; `Tint`, `Filled`, `StrokeThickness`. Decorative (hidden from screen readers). |
| `IconButton` | `Controls/IconButton.cs` | Tappable outlined (or `Plain`) button around an `Icon`: `Command` / `Clicked`, `Kind`, `Tint`, `Filled`, `IconSize`. |

Style: **monochrome outline**, round caps and joins, one stroke weight (1.7 at 20 px, the same recipe as `RowIconStyle`), no
fills except an on/off state (favourite star, active record button). Colour is the text colour (so it follows light and dark)
or a single status colour from the existing palette (`OkText`, `WarnTitle`, `ErrText`, `Primary`).

XAML:

```xml
xmlns:controls="clr-namespace:CarScanner.Controls"

<controls:Icon Kind="chevron-right" Tint="{DynamicResource SubTextColor}" WidthRequest="14" HeightRequest="14" />
<controls:IconButton Kind="close" Plain="True" Command="{Binding CloseCommand}" SemanticProperties.Description="Close" />
<controls:Icon Kind="{Binding Mark}" Tint="{Binding MarkColor}" />      <!-- Mark is an icon kind string from the view model -->
```

View models that need to show a status expose an **icon kind string** (`"check"`, `"warning"`, ...), never a glyph character.
Backend pages use an inline monochrome SVG with `stroke="currentColor"` (see `WebPages.StatusSvg`,
`VerificationEmailTemplate.MarkSvg`); e-mails contain no icon image or emoji.

### Adding an icon

1. Do you really need it? If a word works, use the word.
2. Add one entry to `IconCatalog` (outline, 24 grid, absolute commands `M L C A Z`, no coordinate outside 0-24).
3. Use it by name. Add it to the "Icon direction" section of `OBD360_VISUAL_PROPOSALS.html` if it appears in a proposal.
4. Run the tests; `IconPolicyTests` checks the catalog and every `Kind="..."` in XAML.

### What is kept (not covered by the rule)

* The OBD360 logo, manufacturer/brand logos and vehicle photos (images, not icons).
* Meaningful diagrams and real visualisations: graphs, gauges, the power bar, the ring gauge, the bus tree (drawn with box-drawing
  characters), QR codes, charts drawn on canvases.
* Existing vector `Path` icons in rows (`RowIconStyle`), maintenance-part icons, the camera icon.
* Typography that is not an icon: arrows inside technical or explanatory text (`->` as `→ ← ↑ ↓`), math and units
  (`≈ ≤ ≥ × − ± ° ·`), currency (`₾ € $`), `•` and `·` as list or separator marks, `…`, dashes, quotation marks, and the letters of
  every language.
* Legal marks. The allowed exceptions inside the otherwise forbidden Unicode blocks are exactly: `U+00A9` (c), `U+00AE` (R),
  `U+2122` (TM) and `U+2194` (two-way arrow, used for ranges such as "A <-> B"). Anything else must be removed or added to this list
  together with a reason.
* Developer-only files that never reach a user: unit-test source, scratch notes, internal audit documents (the policy applies to
  the product, its store listings and the proposals page).

### Automated check

`Tests/CarScanner.Tests/IconPolicyTests.cs` (detector in `IconPolicy.cs`) runs with the normal test suite:

* every `.xaml` file: no banned character anywhere except in comments, and no label/button whose whole text is a lone `›`, `‹`, `×`
  (and so on);
* every C# string literal in the product (including language packs and the backend): no banned character, no lone icon
  punctuation, no numeric HTML entity that decodes to one;
* `OBD360_VISUAL_PROPOSALS.html` and the store listings;
* the catalog: valid outline path data on the grid, every `Kind="..."` used in XAML exists, view-model icon kinds exist;
* this guide states the policy and lists the allowed exceptions.

The detector looks at characters, not meaning, and is intentionally narrow (emoji planes, dingbats, miscellaneous symbols and
technical symbols, geometric shapes, regional-indicator flags, variation selectors, enclosed alphanumerics, a few arrows used as
refresh buttons). Letters of any script, digits, currency, math operators, plain arrows, box-drawing characters and ordinary
punctuation pass. If a legitimate string is ever flagged, change the string - or, for a genuine exception, extend
`IconPolicy.Allowed` and the list above in the same change.

### Review checklist for a UI change

- [ ] No emoji or symbol glyph in any new string, XAML attribute, notification, dialog, report or web page.
- [ ] Each icon comes from `IconCatalog`; each icon-only button has a `SemanticProperties.Description` and a language-pack row.
- [ ] Status has a word next to it; light and dark themes both read well.
- [ ] No logo, diagram or graph was replaced by an icon.
