import { readFileSync } from 'node:fs';

function styleFrom(path) {
  const source = readFileSync(path, 'utf8');
  const match = source.match(/<style>([\s\S]*?)<\/style>/);
  if (!match) throw new Error(`No stylesheet found in ${path}`);
  return match[1].trim();
}

function blocks(css) {
  const result = [];
  let start = 0;
  let depth = 0;
  for (let index = 0; index < css.length; index += 1) {
    if (css.startsWith('/*', index)) {
      index = css.indexOf('*/', index + 2);
      if (index < 0) throw new Error('Unterminated CSS comment');
      continue;
    }
    if (css[index] === '{') depth += 1;
    if (css[index] !== '}') continue;
    depth -= 1;
    if (depth !== 0) continue;
    result.push(css.slice(start, index + 1).trim());
    start = index + 1;
  }
  if (css.slice(start).trim()) throw new Error('Unparsed CSS');
  return result;
}

function normalizedHeader(block) {
  return block
    .slice(0, block.indexOf('{'))
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(/\s+/g, ' ')
    .replace(/"/g, "'")
    .trim();
}

function normalizedChunk(text) {
  return text
    .replace(/"/g, "'")
    .replace(/\s+/g, ' ')
    .replace(/\s*([:;,])\s*/g, '$1')
    .replace(/(^|[^\d.])\.(\d)/g, '$10.$2')
    .trim();
}

// Splits `text` on ';' at depth 0, treating both `{}` and `()` as nesting, so
// calc()/env() args and a nested block (e.g. @keyframes steps) survive as one
// piece.
function splitTopLevel(text) {
  const parts = [];
  let start = 0;
  let depth = 0;
  for (let index = 0; index < text.length; index += 1) {
    const char = text[index];
    if (char === '{' || char === '(') depth += 1;
    else if (char === '}' || char === ')') depth -= 1;
    else if (char === ';' && depth === 0) {
      parts.push(text.slice(start, index));
      start = index + 1;
    }
  }
  parts.push(text.slice(start));
  return parts;
}

// Splits a rule body into declarations, keyed by property name - except a
// chunk with a nested block (e.g. an @keyframes stop), keyed by its whole
// text since it has no single property. A repeated key deletes then re-sets,
// so the later declaration also wins the cascade position.
function declarations(body, into) {
  // Comments come out before the split, not after: an unbalanced bracket
  // inside one (`/* see (#125 */`) would corrupt splitTopLevel's depth.
  for (const raw of splitTopLevel(body.replace(/\/\*[\s\S]*?\*\//g, ''))) {
    const decl = normalizedChunk(raw);
    if (!decl) continue;
    const key = decl.includes('{') ? decl : decl.slice(0, decl.indexOf(':'));
    into.delete(key);
    into.set(key, decl);
  }
}

function serialize(declMap) {
  return [...declMap.values()].join(';');
}

// header (media-query-aware) -> Map<prop, declaration>; a header repeated
// within one file layers into the same inner map instead of replacing it,
// unless `duplicates` is given, in which case the repeat is reported there
// instead of layered - see the `actual` parse below, where a split rule
// would otherwise pass silently at its first-seen position.
function rules(css, prefix = '', into = new Map(), duplicates = null) {
  for (const block of blocks(css)) {
    const header = normalizedHeader(block);
    const body = block.slice(block.indexOf('{') + 1, -1);
    if (header.startsWith('@media')) {
      rules(body, `${prefix}${header}|`, into, duplicates);
      continue;
    }
    const key = `${prefix}${header}`;
    if (!into.has(key)) into.set(key, new Map());
    else if (duplicates) duplicates.push(key);
    declarations(body, into.get(key));
  }
  return into;
}

const selectorAliases = new Map([
  ['.nav button.icon', '.icon'],
]);
const retainedSelectors = new Set([
  'html, body, #root',
  '[hidden]',
  '.btn:disabled, .chipset button:disabled',
  '.range.unset',
]);
// schedule-editing is later and overrides ui-screens rule-for-rule on every
// shared header (not merged with it - some of its omissions, like dropping
// .blk.event's dashed border, are deliberate redesigns, not accidental
// drops). Map-spread replaces a duplicate key's whole value while keeping
// the first-seen insertion position, which gives exactly that override. That
// first-seen position is also the order-check tiebreak for the 13 (of 96
// shared) headers the two prototypes order differently: ui-screens' position
// wins, even though the declaration values come from schedule-editing.
const source = new Map([
  ...rules(styleFrom('docs/prototypes/ui-screens.prototype.html')),
  ...rules(styleFrom('docs/prototypes/schedule-editing.prototype.html')),
]);
// tag-entry is a third prototype, but it can't join `source` the way
// schedule-editing does: 27 of the 46 selectors it shares with the existing
// union have a different body there, and those differences are the settled
// ui-screens/schedule-editing contract, not drift to inherit. So tag-entry
// contributes only the selectors it alone defines - its other 75 selectors,
// filtered here to those `source` doesn't already have. Most of those 75
// describe screens the SPA hasn't built, so they're permitted in index.css
// and drift-checked when present, but never required - `missing` below stays
// scoped to `source`. See #177.
const optional = new Map(
  [...rules(styleFrom('docs/prototypes/tag-entry.prototype.html'))]
    .filter(([selector]) => !source.has(selector)),
);
const duplicateSelectors = [];
const actual = rules(readFileSync('src/TaskGuide.Web/src/index.css', 'utf8'), '', new Map(), duplicateSelectors);
const alias = (selector) => selectorAliases.get(selector) ?? selector;
const missing = [...source.keys()].filter((selector) => !actual.has(alias(selector)));
const drifted = [...source, ...optional].filter(([selector, declMap]) =>
  actual.has(alias(selector)) && serialize(actual.get(alias(selector))) !== serialize(declMap),
).map(([selector, declMap]) =>
  `${alias(selector)}\n  prototype: ${serialize(declMap)}\n  index.css: ${serialize(actual.get(alias(selector)))}`);
const allowedSelectors = new Set([...[...source.keys()].map(alias), ...[...optional.keys()].map(alias), ...retainedSelectors]);
const unexpected = [...actual.keys()].filter((selector) => !allowedSelectors.has(selector));

// Equal-specificity rules resolve by source order, so a rule moved past
// another changes rendering even though the set-based checks above pass.
// Media-query-prefixed keys participate in this same flat sequence, since
// @media bodies are recursed into the same map in place.
const expected = [...new Set([...source.keys()].map(alias))].filter((selector) => actual.has(selector));
const expectedSet = new Set(expected);
const found = [...actual.keys()].filter((selector) => expectedSet.has(selector));
const outOfOrder = expected.findIndex((selector, index) => found[index] !== selector);

if (missing.length > 0 || unexpected.length > 0 || drifted.length > 0 || outOfOrder >= 0 || duplicateSelectors.length > 0) {
  const errors = [];
  if (missing.length > 0) errors.push(`missing prototype selectors:\n${missing.join('\n')}`);
  if (drifted.length > 0) errors.push(`declarations differ from prototype:\n${drifted.join('\n')}`);
  if (unexpected.length > 0) errors.push(`unexpected selectors:\n${unexpected.join('\n')}`);
  if (duplicateSelectors.length > 0) {
    errors.push(`repeated selectors (index.css must declare each once, so its rule order is well-defined):\n${duplicateSelectors.join('\n')}`);
  }
  if (outOfOrder >= 0) {
    errors.push(`rule order differs at position ${outOfOrder}: prototype has '${expected[outOfOrder]}', index.css has '${found[outOfOrder]}'`);
  }
  throw new Error(`index.css ${errors.join('\n\n')}`);
}

console.log('index.css covers the ui-screens and schedule-editing prototype union, declarations included, plus tag-entry\'s selectors where present.');
