# Built-In WCAG 2.2 & Revised Section 508 Rule Catalog (25 Rules)

Every rule below is evaluated deterministically by `AccessibilityStudio.Core` with exact 1-based line and column coordinates. Press **`Shift+F1`** on any finding inside the IDE Error List to open its full Before/After code examples in the offline Help Viewer.

## HTML5 & WAI-ARIA Semantic Analyzer (`17` Rules)

| Rule ID | Severity | WCAG 2.2 Criterion | Guided Fix | Summary |
| :--- | :--- | :--- | :--- | :--- |
| `WCAG-HTML-LANG` | Serious | 3.1.1 Language of Page (A) | Yes (`Ctrl+D`) | Root `<html>` element missing valid BCP 47 `lang` attribute. |
| `WCAG-PAGE-TITLE` | Serious | 2.4.2 Page Titled (A) | Yes (`Ctrl+D`) | Document `<head>` missing non-empty `<title>`. |
| `WCAG-MAIN-LANDMARK` | Moderate | 1.3.1 / 2.4.1 Landmarks (A) | Manual | Missing `<main>` landmark or multiple non-inert `<main>` elements. |
| `WCAG-MULTI-NAV-LABEL` | Moderate | 1.3.1 Info & Relationships (A) | Manual | Multiple `<nav>` landmarks missing unique `aria-label` or `aria-labelledby`. |
| `WCAG-HEADING-SKIP` | Serious | 1.3.1 / 2.4.6 Headings | Yes (`Ctrl+D`) | Skipped heading hierarchy level (e.g., `<h1>` directly to `<h4>`). |
| `WCAG-TABLE-FAKE-HEADER` | Serious | 1.3.1 Info & Relationships (A) | Yes (`Ctrl+D`) | First table row uses bold `<td>` cells instead of semantic `<th scope="col">`. |
| `WCAG-TH-SCOPE` | Moderate | 1.3.1 Info & Relationships (A) | Yes (`Ctrl+D`) | Table header `<th>` missing explicit `scope="col"` or `scope="row"`. |
| `WCAG-IMG-ALT` | Critical | 1.1.1 Non-text Content (A) | Yes (`Ctrl+D`) | `<img>` element missing `alt` attribute (`alt="..."` or `alt=""`). |
| `WCAG-IMG-REDUNDANT-ALT` | Advisory | 1.1.1 Non-text Content (A) | Manual | Redundant `"image of"` / `"photo of"` prefix or raw filename in `alt`. |
| `WCAG-SVG-ACCESSIBLE` | Serious | 1.1.1 / 4.1.2 Inline SVG (A) | Yes (`Ctrl+D`) | Inline `<svg>` missing `aria-hidden="true"` (decorative) or `role="img"` + title. |
| `WCAG-FORM-LABEL` | Critical | 1.3.1 / 3.3.2 Form Labels (A) | Yes (`Ctrl+D`) | `<input>`, `<select>`, or `<textarea>` missing programmatic `<label>` or name. |
| `WCAG-AUTOCOMPLETE` | Moderate | 1.3.5 Identify Input Purpose (AA) | Yes (`Ctrl+D`) | Personal user input (email, name, tel) missing `autocomplete` token. |
| `WCAG-IFRAME-TITLE` | Serious | 4.1.2 / 2.4.1 Frame Title (A) | Yes (`Ctrl+D`) | `<iframe>` missing descriptive `title` attribute. |
| `WCAG-POSITIVE-TABINDEX` | Serious | 2.4.3 Focus Order (A) | Yes (`Ctrl+D`) | Positive `tabindex` (`> 0`) overrides natural sequential focus order. |
| `WCAG-ARIA-HIDDEN-FOCUS` | Critical | 4.1.2 Name, Role, Value (A) | Yes (`Ctrl+D`) | Focusable element marked `aria-hidden="true"`, creating a ghost focus trap. |
| `WCAG-AMBIGUOUS-LINK` | Moderate | 2.4.4 Link Purpose (A) | Manual | Ambiguous link phrase (`"click here"`, `"read more"`, `"here"`). |
| `WCAG-MEDIA-CAPTIONS` | Critical | 1.2.2 Captions (Prerecorded) (A) | Yes (`Ctrl+D`) | `<video>` element missing `<track kind="captions">`. |

## CSS Contrast, Focus & Motion Analyzer (`3` Rules)

| Rule ID | Severity | WCAG 2.2 Criterion | Guided Fix | Summary |
| :--- | :--- | :--- | :--- | :--- |
| `WCAG-CSS-CONTRAST` | Serious | 1.4.3 Contrast (Minimum) (AA) | Yes (`Ctrl+D`) | Paired CSS `color` / `background-color` luminance ratio below `4.5:1`. |
| `WCAG-CSS-FOCUS-SUPPRESSED` | Critical | 2.4.7 Focus Visible (AA) | Yes (`Ctrl+D`) | `:focus` / `:focus-visible` suppresses outline (`outline: none` / `0`) without replacement. |
| `WCAG-CSS-REDUCED-MOTION` | Advisory | 2.3.3 Animation from Interactions | Manual | `@keyframes` animation defined without `@media (prefers-reduced-motion)` guard. |

## WebVTT Closed-Caption Linter (`5` Rules)

| Rule ID | Severity | WCAG 2.2 Criterion | Guided Fix | Summary |
| :--- | :--- | :--- | :--- | :--- |
| `WCAG-VTT-HEADER` | Critical | 1.2.2 Captions (A) | Manual | Missing mandatory uppercase `WEBVTT` header on line 1. |
| `WCAG-VTT-TIMING` | Critical | 1.2.2 Captions (A) | Manual | Invalid or reversed cue timestamps (`end <= start`). |
| `WCAG-VTT-OVERLAP` | Moderate | 1.2.2 Captions (A) | Manual | Cue begins before preceding cue ends. |
| `WCAG-VTT-EMPTY` | Moderate | 1.2.2 Captions (A) | Manual | Timestamp block contains no spoken text or sound cue. |
| `WCAG-VTT-CPS` | Advisory | 1.2.2 Caption Readability | Manual | Cue reading rate exceeds `22` characters per second. |
