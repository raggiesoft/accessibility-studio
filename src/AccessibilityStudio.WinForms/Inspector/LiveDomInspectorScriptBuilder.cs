// ================================================================================
// FILE: Inspector/LiveDomInspectorScriptBuilder.cs
// PROJECT: AccessibilityStudio.WinForms
// LICENSE: MIT License
// ================================================================================
// PLAIN-ENGLISH OVERVIEW (100-LEVEL LIVE CHROMIUM VISUAL INSPECTOR GUIDE):
//   When an engineer selects a WCAG violation in the WinForms Error List or clicks
//   an Emulation Mode button (`Forced-Colors High Contrast`, `400% Reflow Zoom`,
//   or `Show Keyboard Tab-Order Badges`), C# injects small, deterministic JavaScript
//   helpers into the Live Preview `WebView2` pane via `ExecuteScriptAsync`:
//     1. `BuildHighlightSelectorScript(selector)`: Draws a high-visibility crimson/gold
//        bounding outline around the matching DOM node and scrolls it smoothly into view.
//     2. `BuildTabOrderOverlayScript(enable)`: Walks all keyboard-focusable elements
//        in actual `Tab` sequence and pins numbered `[1]`, `[2]`, `[3]` pills on top!
//     3. `BuildForcedColorsSimulationScript(enable)`: Simulates Windows High-Contrast
//        `Canvas` / `CanvasText` / `LinkText` / `ButtonFace` surfaces for instant preview.
// ================================================================================

using System.Text.Json;

namespace AccessibilityStudio.WinForms.Inspector;

public static class LiveDomInspectorScriptBuilder
{
    public static string BuildHighlightSelectorScript(string? cssSelector)
    {
        string safeSelectorJson = JsonSerializer.Serialize(cssSelector ?? string.Empty);
        return $$"""
        (function() {
          document.querySelectorAll('[data-a11y-studio-highlight]').forEach(el => {
            el.style.outline = el.getAttribute('data-a11y-orig-outline') || '';
            el.style.outlineOffset = '';
            el.removeAttribute('data-a11y-studio-highlight');
          });
          const sel = {{safeSelectorJson}};
          if (!sel) return;
          try {
            const target = document.querySelector(sel);
            if (target) {
              target.setAttribute('data-a11y-orig-outline', target.style.outline || '');
              target.setAttribute('data-a11y-studio-highlight', 'true');
              target.style.outline = '4px solid #da1e28';
              target.style.outlineOffset = '3px';
              target.scrollIntoView({ behavior: 'smooth', block: 'center' });
            }
          } catch (e) {}
        })();
        """;
    }

    public static string BuildTabOrderOverlayScript(bool enable)
    {
        return enable
            ? """
              (function() {
                document.querySelectorAll('.a11y-studio-tab-badge').forEach(b => b.remove());
                const focusables = Array.from(document.querySelectorAll(
                  'a[href], button:not([disabled]), input:not([disabled]):not([type="hidden"]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'
                )).filter(el => el.offsetParent !== null);
                focusables.forEach((el, idx) => {
                  const rect = el.getBoundingClientRect();
                  const badge = document.createElement('span');
                  badge.className = 'a11y-studio-tab-badge';
                  badge.setAttribute('aria-hidden', 'true');
                  badge.textContent = String(idx + 1);
                  badge.style.cssText = 'position:absolute;z-index:2147483647;background:#0f62fe;color:#ffffff;border:2px solid #ffffff;border-radius:999px;padding:1px 7px;font:700 11px/1.3 system-ui,sans-serif;pointer-events:none;box-shadow:0 2px 6px rgba(0,0,0,.45);' +
                    'top:' + Math.max(0, window.scrollY + rect.top - 10) + 'px;' +
                    'left:' + Math.max(0, window.scrollX + rect.left - 8) + 'px;';
                  document.body.appendChild(badge);
                });
              })();
              """
            : "document.querySelectorAll('.a11y-studio-tab-badge').forEach(b => b.remove());";
    }

    public static string BuildForcedColorsSimulationScript(bool enable)
    {
        return enable
            ? """
              (function() {
                if (document.getElementById('a11y-studio-hc-sim')) return;
                const st = document.createElement('style');
                st.id = 'a11y-studio-hc-sim';
                st.textContent = 'html, body, main, header, footer, section, article, div, nav, aside, table, tr, td, th { background: #000000 !important; background-image: none !important; color: #ffffff !important; border-color: #ffffff !important; box-shadow: none !important; } a, a * { color: #ffff00 !important; text-decoration: underline !important; } button, input, select, textarea { background: #000000 !important; color: #3ff23f !important; border: 2px solid #ffffff !important; }';
                document.head.appendChild(st);
              })();
              """
            : "document.getElementById('a11y-studio-hc-sim')?.remove();";
    }
}
