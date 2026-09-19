import type { components } from '../api/schema'

// Ported from schedule-editing.prototype.html's dimPills() (~678): one `.pill.dim` per declared
// dimension, then one `.pill.inert` per loose tag. The prototype renders the dimension's display
// LABEL; the wire only gives the dimension key (no /api/dimensions fetch here), so this renders
// the key lowercased instead — recorded as a gap in tests/TEST-INVENTORY.md.
export function dimPills(tags: components['schemas']['TagSet']) {
  return <>
    {Object.entries(tags.dimensions).map(([key, values]) => {
      const present = values.map(v => v.value).filter((v): v is string => v != null)
      return present.length ? <span key={key} className="pill dim">{key.toLowerCase()}: {present.join(' / ')}</span> : null
    })}
    {tags.looseTags.map((tag, i) => (
      tag.value != null ? <span key={i} className="pill inert">{tag.value} · inert</span> : null
    ))}
  </>
}
