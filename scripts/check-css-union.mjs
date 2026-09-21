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
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(/"/g, "'")
    .replace(/\s+/g, ' ')
    .replace(/\s*([:;,])\s*/g, '$1')
    .replace(/(^|[^\d.])\.(\d)/g, '$10.$2')
    .trim();
}

// Splits `text` on `separator` at depth 0, treating both `{}` and `()` as
// nesting, so calc()/env() args and a nested block (e.g. @keyframes steps)
// survive as one piece.
function splitTopLevel(text, separator) {
  const parts = [];
  let start = 0;
  let depth = 0;
  for (let index = 0; index < text.length; index += 1) {
    const char = text[index];
    if (char === '{' || char === '(') depth += 1;
    else if (char === '}' || char === ')') depth -= 1;
    else if (char === separator && depth === 0) {
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
function declarations(body, into = new Map()) {
  for (const raw of splitTopLevel(body, ';')) {
    const decl = normalizedChunk(raw);
    if (!decl) continue;
    const key = decl.includes('{') ? decl : decl.slice(0, decl.indexOf(':'));
    into.delete(key);
    into.set(key, decl);
  }
  return into;
}

function serialize(declMap) {
  return [...declMap.values()].join(';');
}

// header (media-query-aware) -> Map<prop, declaration>; a header repeated
// within one file layers into the same inner map instead of replacing it.
function rules(css, prefix = '', into = new Map()) {
  for (const block of blocks(css)) {
    const header = normalizedHeader(block);
    const body = block.slice(block.indexOf('{') + 1, -1);
    if (header.startsWith('@media')) {
      rules(body, `${prefix}${header}|`, into);
      continue;
    }
    const key = `${prefix}${header}`;
    if (!into.has(key)) into.set(key, new Map());
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
  '.range',
  '.range.unset',
  '.ticks',
  '.hint',
  // tag-entry.prototype.html:148 — a third prototype this guard does not source; see #177
  'input.field.date',
]);
// schedule-editing is later and overrides ui-screens rule-for-rule on every
// shared header (not merged with it - some of its omissions, like dropping
// .blk.event's dashed border, are deliberate redesigns, not accidental
// drops). Map-spread replaces a duplicate key's whole value while keeping
// the first-seen insertion position, which gives exactly that override.
const source = new Map([
  ...rules(styleFrom('docs/prototypes/ui-screens.prototype.html')),
  ...rules(styleFrom('docs/prototypes/schedule-editing.prototype.html')),
]);
const actual = rules(readFileSync('src/TaskGuide.Web/src/index.css', 'utf8'));
const alias = (selector) => selectorAliases.get(selector) ?? selector;
const missing = [...source.keys()].filter((selector) => !actual.has(alias(selector)));
const drifted = [...source].filter(([selector, declMap]) =>
  actual.has(alias(selector)) && serialize(actual.get(alias(selector))) !== serialize(declMap),
).map(([selector, declMap]) =>
  `${alias(selector)}\n  prototype: ${serialize(declMap)}\n  index.css: ${serialize(actual.get(alias(selector)))}`);
const allowedSelectors = new Set([...[...source.keys()].map(alias), ...retainedSelectors]);
const unexpected = [...actual.keys()].filter((selector) => !allowedSelectors.has(selector));

// Equal-specificity rules resolve by source order, so a rule moved past
// another changes rendering even though the set-based checks above pass.
// Media-query-prefixed keys participate in this same flat sequence, since
// @media bodies are recursed into the same map in place.
const expected = [...source.keys()].map(alias).filter((selector) => actual.has(selector));
const expectedSet = new Set(expected);
const found = [...actual.keys()].filter((selector) => expectedSet.has(selector));
const outOfOrder = expected.findIndex((selector, index) => found[index] !== selector);

if (missing.length > 0 || unexpected.length > 0 || drifted.length > 0 || outOfOrder >= 0) {
  const errors = [];
  if (missing.length > 0) errors.push(`missing prototype selectors:\n${missing.join('\n')}`);
  if (drifted.length > 0) errors.push(`declarations differ from prototype:\n${drifted.join('\n')}`);
  if (unexpected.length > 0) errors.push(`unexpected selectors:\n${unexpected.join('\n')}`);
  if (outOfOrder >= 0) {
    errors.push(`rule order differs at position ${outOfOrder}: prototype has '${expected[outOfOrder]}', index.css has '${found[outOfOrder]}'`);
  }
  throw new Error(`index.css ${errors.join('\n\n')}`);
}

console.log('index.css covers the ui-screens and schedule-editing prototype union, declarations included.');
