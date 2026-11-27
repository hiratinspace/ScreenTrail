// Turns tokens.json into what each UI consumes (ST-016): WPF resource dictionaries and web CSS variables.
// Same single-source pattern as the session schema: generated files are committed and CI fails if they
// drift from tokens.json (`npm run check`).

import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { dirname, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const repoRoot = resolve(here, '../../..');
const tokensFile = resolve(here, '../tokens.json');
const targets = {
  wpfDark: resolve(repoRoot, 'client/ScreenTrail.UI/Theme/Tokens.Dark.g.xaml'),
  wpfLight: resolve(repoRoot, 'client/ScreenTrail.UI/Theme/Tokens.Light.g.xaml'),
  wpfCommon: resolve(repoRoot, 'client/ScreenTrail.UI/Theme/Tokens.Common.g.xaml'),
  css: resolve(repoRoot, 'web/src/styles/tokens.g.css'),
  // The public site (site/) reads the same variables, so its Iris and state colours never drift from the app's.
  siteCss: resolve(repoRoot, 'site/src/styles/tokens.g.css'),
};
const banner = 'Generated from shared/design/tokens.json by shared/design/codegen/generate.mjs. Do not edit by hand.';

const tokens = JSON.parse(await readFile(tokensFile, 'utf8'));
await emit(targets.wpfDark, wpfColors(tokens, 'dark'));
await emit(targets.wpfLight, wpfColors(tokens, 'light'));
await emit(targets.wpfCommon, wpfCommon(tokens));
await emit(targets.css, css(tokens));
await emit(targets.siteCss, css(tokens));

async function emit(file, text) {
  await mkdir(dirname(file), { recursive: true });
  await writeFile(file, text.endsWith('\n') ? text : `${text}\n`);
  console.log(`wrote ${relative(repoRoot, file)}`);
}

// Resource keys: "Token.bg.surface" (Color) and "Brush.bg.surface" (SolidColorBrush).
function wpfColors(t, theme) {
  const lines = [
    `<!-- ${banner} Theme: ${theme}. -->`,
    '<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"',
    '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"',
    '                    xmlns:sys="clr-namespace:System;assembly=System.Runtime">',
  ];
  for (const [name, def] of Object.entries(t.color)) {
    lines.push(`    <!-- ${def.use} -->`);
    lines.push(`    <Color x:Key="Token.${name}">${def[theme].toUpperCase()}</Color>`);
    lines.push(`    <SolidColorBrush x:Key="Brush.${name}" Color="${def[theme].toUpperCase()}" />`);
  }
  lines.push(`    <sys:String x:Key="Token.theme">${theme}</sys:String>`);
  // Design system 6: a shadow only for what overlaps other content, as a DropShadowEffect per theme. Put on an
  // empty Border behind the content, never on the container holding text, which would lose ClearType.
  for (const [name, e] of Object.entries(t.elevation)) {
    lines.push(
      `    <DropShadowEffect x:Key="Elevation.${name}" Color="${e[theme].color.toUpperCase()}" BlurRadius="${e.blur}" ShadowDepth="${e.depth}" Direction="270" Opacity="${e[theme].opacity}" />`,
    );
  }
  // A text field's keyboard ring (design system 10.2, review finding B-09): none in light and dark, where the
  // field's own 2 px accent edge shows focus; the high contrast dictionary draws one, because there the edge
  // alone was too faint to see on a scaled screen (ST-084).
  lines.push('    <Style x:Key="FocusVisual.input" />');
  // A chosen segment's edge (design system 10.3, review finding B-21): none in light and dark, where its tint
  // says it; 2 px Highlight in high contrast, where the tint is the window colour.
  lines.push('    <Thickness x:Key="Thickness.selected-edge">0</Thickness>');
  // A floating surface's edge, the pill and its card (design system 10.10, review finding C-H6): 1 px
  // border.strong in light and dark; 2 px WindowFrame in high contrast, where there is no shadow to lift it.
  lines.push('    <Thickness x:Key="Thickness.float-edge">1</Thickness>');
  // A selected row's edge (design system 12, review findings A-35, E-28): clear in light and dark, where the
  // fill and the bar say it; Highlight in high contrast, where both are the window colour. A token and not
  // the system flag, so a render with the high-contrast dictionary draws what high contrast does.
  lines.push('    <Color x:Key="Token.selection.edge">#00000000</Color>');
  lines.push('    <SolidColorBrush x:Key="Brush.selection.edge" Color="Transparent" />');
  // A skeleton block's edge (design system 10.8): none in light and dark, where its bg.hover fill shows it;
  // GrayText in high contrast, where that fill is the window colour and the block was invisible.
  lines.push('    <Color x:Key="Token.skeleton.edge">#00000000</Color>');
  lines.push('    <SolidColorBrush x:Key="Brush.skeleton.edge" Color="Transparent" />');
  // A scroll bar's thumb at rest (design system 10.8, review findings A-33, B-29): 4 wide, 8 while the pointer
  // is over its track; high contrast keeps it 8 at all times.
  lines.push('    <sys:Double x:Key="ScrollThumb.rest">4</sys:Double>');
  lines.push('</ResourceDictionary>');
  return lines.join('\n');
}

// The client's own values (tokens.json "client"), over the shared ones, for the WPF dictionaries only.
function forClient(t) {
  const merged = structuredClone(t);
  for (const [key, value] of Object.entries(t.client ?? {})) {
    if (key.startsWith('_')) continue;
    const dot = key.indexOf('.');
    merged[key.slice(0, dot)][key.slice(dot + 1)] = value;
  }
  return merged;
}

function seconds(ms) {
  return `0:0:${(ms / 1000).toFixed(3)}`;
}

function wpfCommon(shared) {
  const t = forClient(shared);
  const lines = [
    `<!-- ${banner} Theme-independent values. -->`,
    '<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"',
    '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"',
    '                    xmlns:sys="clr-namespace:System;assembly=System.Runtime">',
  ];
  // WPF reads a comma-separated FontFamily as a fallback list, so the CSS stack carries over without its quotes.
  for (const [k, v] of Object.entries(t.font)) lines.push(`    <FontFamily x:Key="Font.${k}">${xmlEscape(v.replaceAll('"', ''))}</FontFamily>`);
  for (const [k, v] of Object.entries(t.size)) lines.push(`    <sys:Double x:Key="Size.${k}">${v}</sys:Double>`);
  for (const [k, v] of Object.entries(t.weight)) lines.push(`    <FontWeight x:Key="Weight.${k}">${weightName(v)}</FontWeight>`);
  for (const [k, v] of Object.entries(t.lineheight)) lines.push(`    <sys:Double x:Key="LineHeight.${k}">${v}</sys:Double>`);
  // Line height per text role, in pixels, set with LineStackingStrategy="BlockLineHeight" (design system 3.2).
  for (const [k, v] of Object.entries(t.leading)) lines.push(`    <sys:Double x:Key="Leading.${k}">${v}</sys:Double>`);
  for (const [k, v] of Object.entries(t.space)) lines.push(`    <sys:Double x:Key="Space.${k}">${v}</sys:Double>`);
  for (const [k, v] of Object.entries(t.layout)) lines.push(`    <sys:Double x:Key="Layout.${k}">${v}</sys:Double>`);
  lines.push(
    `    <Thickness x:Key="Padding.pane">${t.layout['pane-padding']}</Thickness>`,
    `    <Thickness x:Key="Padding.card">${t.layout['card-padding']}</Thickness>`,
    `    <Thickness x:Key="Padding.control">${t.layout['control-padding-x']},${t.layout['control-padding-y']}</Thickness>`,
    `    <Thickness x:Key="Padding.control-compact">${t.layout['control-padding-x-compact']},${t.layout['control-padding-y']}</Thickness>`,
    `    <Thickness x:Key="Padding.pane-narrow">${t.layout['pane-padding-narrow']}</Thickness>`,
    `    <Thickness x:Key="Padding.rail-row">${t.layout['rail-row-padding-x']},${t.layout['rail-row-padding-y']}</Thickness>`,
    // A search field: its text after the 16 px glyph 12 in and an 8 gap (design system 10.2).
    `    <Thickness x:Key="Padding.search">${t.layout['search-text-inset']},0,${t.layout['control-padding-x']},0</Thickness>`,
  );
  // "pill" is 999: fully round at any height. A Border scales radii that overrun its sides down to fit, so a
  // 20 px badge and the 40 px pill both get true half circles from the one value (design system 5).
  for (const [k, v] of Object.entries(t.radius)) lines.push(`    <CornerRadius x:Key="Radius.${k}">${v}</CornerRadius>`);
  for (const [k, v] of Object.entries(t.motion)) {
    if (k.endsWith('-ms')) lines.push(`    <Duration x:Key="Motion.${k.replace('-ms', '')}">${seconds(v)}</Duration>`);
    else if (k.startsWith('ease-')) lines.push(`    <KeySpline x:Key="Motion.${k}">${v}</KeySpline>`);
  }
  for (const [k, v] of Object.entries(t.icon)) lines.push(`    <sys:Double x:Key="Icon.${k}">${v}</sys:Double>`);
  lines.push(`    <sys:Double x:Key="Focus.width">${t.focus.width}</sys:Double>`);
  lines.push(`    <sys:Double x:Key="Focus.offset">${t.focus.offset}</sys:Double>`);
  lines.push(`    <sys:Double x:Key="Border.width">${t.border.width}</sys:Double>`);
  lines.push(`    <Thickness x:Key="Thickness.border">${t.border.width}</Thickness>`);
  lines.push(`    <Thickness x:Key="Thickness.focus">${t.focus.width}</Thickness>`);
  lines.push('</ResourceDictionary>');
  return lines.join('\n');
}

// Light is the web default (Spec §5 S9); dark applies with [data-theme="dark"] or the OS preference.
function css(t) {
  const colorVars = (theme) => Object.entries(t.color).map(([n, d]) => `  --${cssName(n)}: ${d[theme]};`);
  const shadowVars = (theme) =>
    Object.entries(t.elevation).map(([n, e]) => `  --shadow-${n}: 0 ${e.depth}px ${e.blur}px ${rgba(e[theme].color, e[theme].opacity)};`);
  const lines = [
    `/* ${banner} */`,
    ':root {',
    ...colorVars('light'),
    ...shadowVars('light'),
    ...Object.entries(t.font).map(([k, v]) => `  --font-${k}: ${v};`),
    ...Object.entries(t.size).map(([k, v]) => `  --size-${k}: ${v}px;`),
    ...Object.entries(t.weight).map(([k, v]) => `  --weight-${k}: ${v};`),
    ...Object.entries(t.lineheight).map(([k, v]) => `  --lineheight-${k}: ${v};`),
    ...Object.entries(t.leading).map(([k, v]) => `  --leading-${k}: ${v}px;`),
    ...Object.entries(t.space).map(([k, v]) => `  --space-${k}: ${v}px;`),
    ...Object.entries(t.layout).map(([k, v]) => `  --layout-${k}: ${v}px;`),
    ...Object.entries(t.radius).map(([k, v]) => `  --radius-${k}: ${v}px;`),
    ...Object.entries(t.motion).map(([k, v]) => `  --motion-${k.replace('-ms', '')}: ${motionValue(k, v)};`),
    ...Object.entries(t.icon).map(([k, v]) => `  --icon-${k}: ${v}px;`),
    `  --focus-ring: ${t.focus.width}px solid var(--${cssName(t.focus.color)});`,
    `  --focus-offset: ${t.focus.offset}px;`,
    `  --border-width: ${t.border.width}px;`,
    '  color-scheme: light;',
    '}',
    ':root[data-theme="dark"] {',
    ...colorVars('dark'),
    ...shadowVars('dark'),
    '  color-scheme: dark;',
    '}',
    '@media (prefers-color-scheme: dark) {',
    '  :root:not([data-theme="light"]) {',
    ...colorVars('dark').map((l) => `  ${l}`),
    ...shadowVars('dark').map((l) => `  ${l}`),
    '    color-scheme: dark;',
    '  }',
    '}',
    '@media (prefers-reduced-motion: reduce) {',
    '  :root { --motion-state: 0ms; --motion-pane: 0ms; }',
    '}',
  ];
  return lines.join('\n');
}

function motionValue(key, value) {
  if (typeof value === 'number') return `${value}ms`;
  return key.startsWith('ease-') ? `cubic-bezier(${value.replace(' ', ',')})` : value;
}

function rgba(hex, opacity) {
  const [r, g, b] = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16));
  return `rgba(${r},${g},${b},${opacity})`;
}

function cssName(token) {
  return token.replaceAll('.', '-');
}

function weightName(v) {
  return { 400: 'Normal', 500: 'Medium', 600: 'SemiBold', 700: 'Bold' }[v] ?? String(v);
}

function xmlEscape(s) {
  return s.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;');
}
